using System.Collections;
using System.Collections.Generic;
using UnityEngine.EventSystems;
using UnityEngine;

public struct HairNode 
{
    public float x, y;
    public float vx, vy;
    public int ax, ay;
    public float dummy1, dummy2;
}

public struct ColliderNode
{
    public float x, y, r;
    public int ax, ay;
    public int dummy1, dummy2, dummy3;
}

public class CPU: MonoBehaviour
{
    public ComputeShader shader;    
    public GameObject hairPrefab, colliderPrefab;
    public Camera mainCamera;
    GameObject currentSelectedCollider;

    static public int res;
    static public int simulationSteps;
    static float kernelSize; // TODO: delete this, we don't need this

    public static int nHairs, nNodesPerHair;
    static HairNode[] hairNodesArray;    
    static GameObject[] hairGeos;

    static public int nColliders;
    static public float colliderRadius;
    static ColliderNode[] colliderNodeArrays;
    static public GameObject[] colliderGeos;

    public static float nodeDistance;
    static float dPosition;             // speed ratio for euler's method
    static float dVelocity;             // force ratio for euler's method
    public static float forceDecay;
    public static float velocityDecay;
    public static float gravity;
    public static float stiffness;             // used for Hooke's law spring coefficient.
    public static float maxTravelDistance;     // the maximum distance node apart.
    public static float bendingStiffness;      // coefficient for bending forces

    // kernel
    int forceKernel, velocityKernel, collisionKernel, eulerKernel;

    // buffer is an array
    ComputeBuffer hairNodeBuffer, colliderBuffer;

    // Start is called before the first frame update
    private void Awake()
    {
        initData();
    }

    void Start()
    {
        Debug.Assert(shader);
        Debug.Assert(hairPrefab);
        Debug.Assert(colliderPrefab);
        Debug.Assert(mainCamera);

        //initData();
        initGeo();
        initBuffer();
        initShader();
    }

    // Update is called once per frame
    void Update()
    {
        simulationOnGPU();
        updateHairGeoPositions();
        updateDataFromCollider();
        moveCollider();
    }

    void initData()
    {
        // kernel data
        res = 32;
        simulationSteps = 40;
        kernelSize = res * 1.0f;

        // hair date
        nHairs = 32;
        nNodesPerHair = 32;
        hairNodesArray = new HairNode[ nHairs * nNodesPerHair ];

        // simulation variables
        nodeDistance = 0.5f;    // Initial Node distance apart.
        dPosition = 0.0004f;    // Euler method integration ratio for speed
        dVelocity = 1.0f;       // Euler method integration ratio for acceleration
        forceDecay = 0.0000f;
        velocityDecay = 0.999f; 
        gravity = 0.1f;
        stiffness = 6.0f;
        maxTravelDistance = 5.0f;
        bendingStiffness = 0.1f;

        for (int i = 0; i < nHairs; i++)
        {
            for (int j = 0; j < nNodesPerHair; j++)
            {
                int nodeIndex = i * nNodesPerHair + j;
                hairNodesArray[nodeIndex].x = nodeDistance * (i - nHairs/2);
                hairNodesArray[nodeIndex].y = - nodeDistance * (j - nNodesPerHair/2);
                hairNodesArray[nodeIndex].vx = 0.0f;
                hairNodesArray[nodeIndex].vy = 0.0f;
                hairNodesArray[nodeIndex].ax = 0;
                hairNodesArray[nodeIndex].ay = 0;
            }                
        }

        // collider data
        nColliders = 1;
        colliderRadius = 2.5f;
        colliderNodeArrays = new ColliderNode[nColliders];
        for (int i = 0; i < nColliders; i++)
        {
            colliderNodeArrays[i].x = colliderRadius * (i - nColliders / 2); ;
            colliderNodeArrays[i].y = -20.0f;
            colliderNodeArrays[i].r = colliderRadius;
            colliderNodeArrays[i].ax = 0;
            colliderNodeArrays[i].ay = 0;
        }        
    }

    void initGeo()
    {
        // instantiate hair objects
        hairGeos = new GameObject[nHairs * nNodesPerHair];
        for (int i = 0; i < hairGeos.Length; i++)
        {
            Vector3 location = new Vector3(hairNodesArray[i].x, hairNodesArray[i].y, 0.0f);
            var newitem = Instantiate(hairPrefab, location, Quaternion.identity);            
            hairGeos[i] = newitem;
        }

        //  instantiate collider
        colliderGeos = new GameObject[nColliders];
        for (int i = 0; i < nColliders; i++)
        {
            Vector3 location = new Vector3(colliderNodeArrays[i].x, colliderNodeArrays[i].y, 0.0f); 
            var newitem = Instantiate(colliderPrefab, location, Quaternion.identity);
            newitem.transform.localScale = new Vector3(1, 1, 1) * 2 * colliderNodeArrays[i].r;
            colliderGeos[i] = newitem;
        }
    }

    void initBuffer()
    {
        // prepare buffer output
        hairNodeBuffer = new ComputeBuffer(hairNodesArray.Length, 4 * 8);
        hairNodeBuffer.SetData(hairNodesArray);
        colliderBuffer = new ComputeBuffer(nColliders, 4 * 8);
        colliderBuffer.SetData(colliderNodeArrays);
    }

    public void updateNodeDistance(float dis) {
        shader.SetFloat("nodeDistance", dis);
        //shader.SetFloat("dPosition", dPosition);
        //shader.SetFloat("dVelocity", dVelocity);
        //shader.SetFloat("forceDecay", forceDecay);
        //shader.SetFloat("velocityDecay", velocityDecay);
        //shader.SetFloat("gravity", gravity);
        //shader.SetFloat("stiffness", stiffness);
        //shader.SetFloat("maxTravelDistance", maxTravelDistance);
        //shader.SetFloat("bendingStiffness", bendingStiffness);
    }

    public void updateStiffness(float stiff)
    {
        shader.SetFloat("stiffness", stiff);
    }

    public void updateGravity(float grav)
    {
        shader.SetFloat("gravity", grav);
    }

    public void updateMaxTravel(float maxTrav)
    {
        shader.SetFloat("maxTravelDistance", maxTrav);
    }

    public void updateBendStiff(float bendStiff)
    {
        shader.SetFloat("bendingStiffness", bendStiff);
    }

    public void updateFriction(float friction)
    {
        shader.SetFloat("velocityDecay", friction);
    }

    void initShader() 
    {
        shader.SetInt("nNodesPerHair", nNodesPerHair);
        shader.SetInt("nHairs", nHairs);
        shader.SetInt("nColliders", nColliders);
        shader.SetFloat("nodeDistance", nodeDistance);
        shader.SetFloat("dPosition", dPosition);
        shader.SetFloat("dVelocity", dVelocity);
        shader.SetFloat("forceDecay", forceDecay);
        shader.SetFloat("velocityDecay", velocityDecay);
        shader.SetFloat("gravity", gravity);
        shader.SetFloat("stiffness", stiffness); 
        shader.SetFloat("maxTravelDistance", maxTravelDistance);
        shader.SetFloat("bendingStiffness", bendingStiffness);

        shader.SetInt("floatToInt", 2 << 17);
        shader.SetFloat("intToFloat", 1f / (2 << 17));

        velocityKernel = shader.FindKernel("VelocityKernel");
        shader.SetBuffer(velocityKernel, "hairNodeBuffer", hairNodeBuffer);

        forceKernel = shader.FindKernel("ForceKernel");
        shader.SetBuffer(forceKernel, "hairNodeBuffer", hairNodeBuffer);

        collisionKernel = shader.FindKernel("CollisionKernel");
        shader.SetBuffer(collisionKernel, "hairNodeBuffer", hairNodeBuffer);
        shader.SetBuffer(collisionKernel, "colliderBuffer", colliderBuffer);

        eulerKernel = shader.FindKernel("EulerKernel");
        shader.SetBuffer(eulerKernel, "hairNodeBuffer", hairNodeBuffer);
    }

    void simulationOnGPU() 
    {
        int nThreadGrpsX = 1;
        int nThreadGrpsY = 1;

        // set buffer data from collider
        colliderBuffer.SetData(colliderNodeArrays);

        // update node positions in GPU
        for (int i = 0; i < simulationSteps; i++)
        {
            shader.Dispatch(velocityKernel, nThreadGrpsX, nThreadGrpsY, 1); // exchange velocity among nodes
            shader.Dispatch(forceKernel, nThreadGrpsX, nThreadGrpsY, 1);    // update force from position ( hooke's law )
            shader.Dispatch(collisionKernel, nThreadGrpsX, nThreadGrpsY, 1);// update position, velocity and force upon collision
            shader.Dispatch(eulerKernel, nThreadGrpsX, nThreadGrpsY, 1);    // Euler's method to accumulate to new position.
        }

        // set data to collider
        colliderBuffer.GetData(colliderNodeArrays);
        //Debug.Log("{" + colliderNodeArrays[0].x + colliderNodeArrays[0].y + "}");

        // set data to hairNode
        hairNodeBuffer.GetData(hairNodesArray);
        // Debug.Log("[" + hairNodesArray[1].x + " / " + hairNodesArray[1].y + "], [" + hairNodesArray[1].vx + " / " + hairNodesArray[1].vy + "], [" + hairNodesArray[1].ax + " / " + hairNodesArray[1].ay + "]");
        // Debug.Log("[" + hairNodesArray[6].dummy1+ "/" + hairNodesArray[6].dummy2 + "]");
    }

    void updateHairGeoPositions()
    {
        for (int i = 0; i < hairGeos.Length; i++)
        {
            Vector3 location = new Vector3(hairNodesArray[i].x, hairNodesArray[i].y, 0.0f);
            hairGeos[i].transform.position = location;
        }
    }

    void updateDataFromCollider()
    {
        for (int i = 0; i < nColliders; i++)
        {
            colliderNodeArrays[i].x = colliderGeos[i].transform.position.x;
            colliderNodeArrays[i].y = colliderGeos[i].transform.position.y;
            colliderNodeArrays[i].ax = 0;
            colliderNodeArrays[i].ay = 0;
        }
    }

    void moveCollider()
    {
        if (Input.GetMouseButton(0)){
            RaycastHit hit;
            var ray = Camera.main.ScreenPointToRay(Input.mousePosition);

            if (Physics.Raycast(ray, out hit))
            {
                if (hit.collider.gameObject.tag == "Collider")
                {
                    currentSelectedCollider = hit.collider.gameObject;
                }

                if (!currentSelectedCollider)
                {
                    return;
                }

                float moveRatio = 0.075f;

                float x = (float)((Input.mousePosition.x - Screen.width / 2.0) * moveRatio);
                float y = (float)((Input.mousePosition.y - Screen.height / 2.0) * moveRatio);

                currentSelectedCollider.transform.position = new Vector3(x, y, 0);
            }
        }else {
            currentSelectedCollider = null;
        }
    }

    void OnDestroy()
    {                  
        hairNodeBuffer.Release();
        colliderBuffer.Release();
    }
}
