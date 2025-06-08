using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

//[ExecuteInEditMode]
public class Grass : MonoBehaviour
{
    [Header("Linked objects")]
    [SerializeField]
    ComputeShader computeShader;
    [SerializeField]
    Material material;
    List<Terrain> terrainPatches = new List<Terrain>();
    public Camera cam;
    public Mesh originalMesh;
 
    [Header("Grass placement")]
    [SerializeField, Range(10, 4000)]
    int resolution = 10;
    public float jitterStrength;
    [Header("Terrain splatmap settings")]
    public List<int> grassSplatmapIndices = new List<int>();
    [Range(0,1)]
    public float splatCutoff = 0.5f;

    [Header("Wind")]
    public Texture WindTex;
    public float _GlobalWindFacingContribution;
    public float _GlobalWindFacingAngle;
    [SerializeField, Range(0, 1)]
    public float _WindControl;
    public float _BigWindSpeed;
    public float _BigWindScale;
    public float _BigWindRotateAmount;
    public float _WindTexContrast = 1;



    [Header("Grass shape")]
    public float _VertexPlacementPower;

    [Header("Culling")]
    public float _DistanceCullStartDist;
    public float _DistanceCullEndDist;
    public float _DistanceCullMinimumGrassAmount;
    public bool DISTANCE_CULL_ENABLED;
    public float _FrustumCullNearOffset;
    public float _FrustumCullEdgeOffset;
    int numInstances;


    ComputeBuffer grassBladesBuffer;
    ComputeBuffer meshTriangles;
    ComputeBuffer meshPositions;
    ComputeBuffer meshColors;
    ComputeBuffer meshUvs;

    List<Texture2D> splatMasks = new List<Texture2D>();

    ComputeBuffer argsBuffer;

    ComputeBuffer clumpParametersBuffer;

    private const int ARGS_STRIDE = sizeof(int) * 4;

    //[Header("(Dev)Testing Variables")]
    //public float _Test;
    //public float _Test2;
    //public float _Test3;
    //public float _Test4;
    [Header("Clumping")]
    public int clumpTexHeight;
    public int clumpTexWidth;
    public Material clumpingVoronoiMat;

    public float ClumpScale;

    public List<ClumpParametersStruct> clumpParameters;


    [Header("Clump gradient map")]
    public bool enableClumpColoring;
    public float _CentreColorSmoothStepLower;
    public float _CentreColorSmoothStepUpper;
    public float _ClumpColorUniformity;
    public Vector2Int gradientMapDimensions = new Vector2Int(128, 32);
    public Gradient gradientClump;
    //public Gradient gradientBlade;
    public bool testing = false;
    public Texture2D texture;
    //public Texture2D texture2;


    [Serializable]
    public struct ClumpParametersStruct
    {
        public float pullToCentre;
        public float pointInSameDirection;
        public float baseHeight;
        public float heightRandom;
        public float baseWidth;
        public float widthRandom;
        public float baseTilt;
        public float tiltRandom;
        public float baseBend;
        public float bendRandom;

    };


    static readonly int
        grassBladesBufferID = Shader.PropertyToID("_GrassBlades"),
        resolutionId = Shader.PropertyToID("_Resolution"),
        grassSpacingId = Shader.PropertyToID("_GrassSpacing"),
        planeCentreId = Shader.PropertyToID("_PlaneCentre"),
        jitterStrengthId = Shader.PropertyToID("_JitterStrength"),
        heightMapId = Shader.PropertyToID("HeightMap"),
        splatMaskId = Shader.PropertyToID("SplatMask"),
        splatCutoffId = Shader.PropertyToID("_SplatCutoff"),

        distanceCullStartDistId = Shader.PropertyToID("_DistanceCullStartDist"),
        distanceCullEndDistId = Shader.PropertyToID("_DistanceCullEndDist"),


        worldSpaceCameraPositionId = Shader.PropertyToID("_WSpaceCameraPos"),

        clumpParametersId = Shader.PropertyToID("_ClumpParameters"),
        clumpScaleId = Shader.PropertyToID("_ClumpScale"),

        windTexID = Shader.PropertyToID("WindTex"),
        clumpTexID = Shader.PropertyToID("ClumpTex"),
        ClumpGradientMapId = Shader.PropertyToID("ClumpGradientMap"),
        //BladeGradientMapId = Shader.PropertyToID("_GradientMap"),
        vpMatrixID = Shader.PropertyToID("_VP_MATRIX"),
        FrustumCullNearOffsetId = Shader.PropertyToID("_FrustumCullNearOffset"),
        FrustumCullEdgeOffsetId = Shader.PropertyToID("_FrustumCullEdgeOffset"),
        ClumpColorUniformityId = Shader.PropertyToID("_ClumpColorUniformity"),
        CentreColorSmoothStepLowerId = Shader.PropertyToID("_CentreColorSmoothStepLower"),
        CentreColorSmoothStepUpperId = Shader.PropertyToID("_CentreColorSmoothStepUpper"),
        BigWindSpeedID = Shader.PropertyToID("_BigWindSpeed"),
        BigWindScaleID = Shader.PropertyToID("_BigWindScale"),
        BigWindRotateAmountID = Shader.PropertyToID("_BigWindRotateAmount"),
        GlobalWindFacingAngleID = Shader.PropertyToID("_GlobalWindFacingAngle"),
        GlobalWindFacingContributionID = Shader.PropertyToID("_GlobalWindFacingContribution"),
        WindControlID = Shader.PropertyToID("_WindControl"),
        DistanceCullMinimumGrassAmountlID = Shader.PropertyToID("_DistanceCullMinimumGrassAmount"),
        TimeID = Shader.PropertyToID("_Time"),
        WindTexContrastID = Shader.PropertyToID("_WindTexContrast"),

        ClumpScaleID = Shader.PropertyToID("_ClumpScale"),
        WSpaceCameraPosID = Shader.PropertyToID("_WSpaceCameraPos");



    
    

    

    
    MeshFilter meshFilter;
    Mesh clonedMesh;
    
    ClumpParametersStruct[] clumpParametersArray;

    Bounds bounds;
    void UpdateGPUParams()
    {

        grassBladesBuffer.SetCounterValue(0);

        computeShader.SetFloat(distanceCullStartDistId, _DistanceCullStartDist);
        computeShader.SetFloat(distanceCullEndDistId, _DistanceCullEndDist);


        computeShader.SetVector(worldSpaceCameraPositionId, cam.transform.position);

        //computeShader.SetFloat("_Test", _Test);
        //computeShader.SetFloat("_Test2", _Test2);
        //computeShader.SetFloat("_Test3", _Test3);
        //computeShader.SetFloat("_Test4", _Test4);
        computeShader.SetFloat(DistanceCullMinimumGrassAmountlID, _DistanceCullMinimumGrassAmount);

        // Terrain specific parameters will be set per patch


        computeShader.SetVector(TimeID, Shader.GetGlobalVector("_Time"));
        //computeShader.SetVector("_ProjectionParams", new Vector4(1, cam.nearClipPlane, cam.farClipPlane,1/cam.farClipPlane));
        computeShader.SetFloat(WindTexContrastID, _WindTexContrast);

        //computeShader.SetTextureFromGlobal(0, "DepthTexture", "_CameraDepthTexture");

        computeShader.SetFloat(FrustumCullNearOffsetId, _FrustumCullNearOffset);
        computeShader.SetFloat(FrustumCullEdgeOffsetId, _FrustumCullEdgeOffset);
        computeShader.SetFloat(ClumpColorUniformityId, _ClumpColorUniformity);
        computeShader.SetFloat(CentreColorSmoothStepLowerId, _CentreColorSmoothStepLower);
        computeShader.SetFloat(CentreColorSmoothStepUpperId, _CentreColorSmoothStepUpper);

        computeShader.SetFloat(BigWindSpeedID, _BigWindSpeed);
        computeShader.SetFloat(BigWindScaleID, _BigWindScale);
        computeShader.SetFloat(BigWindRotateAmountID, _BigWindRotateAmount);


        computeShader.SetFloat(GlobalWindFacingAngleID, _GlobalWindFacingAngle);
        computeShader.SetFloat(GlobalWindFacingContributionID, _GlobalWindFacingContribution);
        computeShader.SetFloat(WindControlID, _WindControl);

        Matrix4x4 projMat = GL.GetGPUProjectionMatrix(cam.projectionMatrix, false);

        Matrix4x4 VP = projMat * cam.worldToCameraMatrix;

        computeShader.SetMatrix(vpMatrixID, VP);
        computeShader.SetFloat(ClumpScaleID, ClumpScale);


        foreach (var (terrain, index) in terrainPatches.Select((t, i) => (t, i)))
        {
            if (terrain == null) continue;

            Vector3 planeDims = terrain.terrainData.size;
            Texture heightTex = terrain.terrainData.heightmapTexture;

            float spacing = planeDims.x / resolution;
            Vector3 centre = planeDims / 2f - new Vector3(terrain.transform.position.x, 0, terrain.transform.position.z);

            computeShader.SetInt(resolutionId, resolution);
            computeShader.SetFloat(grassSpacingId, spacing);
            computeShader.SetFloat(jitterStrengthId, jitterStrength);
            computeShader.SetVector(planeCentreId, centre);
            computeShader.SetTexture(0, heightMapId, heightTex);
            computeShader.SetFloat("_HeightMapScale", planeDims.x);
            computeShader.SetFloat("_HeightMapMultiplier", planeDims.y);

            if (index < splatMasks.Count && splatMasks[index] != null)
            {
                computeShader.SetTexture(0, splatMaskId, splatMasks[index]);
                computeShader.SetFloat(splatCutoffId, splatCutoff);
            }
            else
            {
                computeShader.SetTexture(0, splatMaskId, null);
            }

            int groups = Mathf.CeilToInt(resolution / 8f);
            computeShader.Dispatch(0, groups, groups, 1);
        }

        

        ComputeBuffer.CopyCount(grassBladesBuffer, argsBuffer, sizeof(int));


        material.SetBuffer(grassBladesBufferID, grassBladesBuffer);
        material.SetVector(worldSpaceCameraPositionId, cam.transform.position);
        material.SetFloat(WindControlID,_WindControl);

    }

    void Awake()
    {
        //cam.depthTextureMode = DepthTextureMode.Depth;
        terrainPatches = FindObjectsOfType<Terrain>().ToList();
        numInstances = resolution * resolution;
        grassBladesBuffer = new ComputeBuffer(resolution * resolution, sizeof(float) * 18, ComputeBufferType.Append);
        grassBladesBuffer.SetCounterValue(0);

    }

    public void updateGrassArtistParameters() {

        //clumpParametersBuffer.Dispose();

        Debug.Log(clumpParameters.Count);

        clumpParametersArray = new ClumpParametersStruct[clumpParameters.Count];

        for (int i = 0; i < clumpParameters.Count; i++)
        {

            clumpParametersArray[i] = clumpParameters[i];


        }       
        clumpParametersBuffer.SetData(clumpParametersArray);
        computeShader.SetBuffer(0, clumpParametersId, clumpParametersBuffer);

    }

    // Start is called before the first frame update
    void Start()
    {


        if (DISTANCE_CULL_ENABLED)
        {

           computeShader.EnableKeyword("DISTANCE_CULL_ENABLED");

        }
        else
        {
            computeShader.DisableKeyword("DISTANCE_CULL_ENABLED");
            
        }

        if (enableClumpColoring)
        {

            material.EnableKeyword("USE_CLUMP_COLORS");

        }
        else
        {
            material.DisableKeyword("USE_CLUMP_COLORS");

        }


        clumpingVoronoiMat.SetFloat("_NumClumps", clumpParameters.Count);
        Texture2D startTex = new Texture2D(clumpTexWidth, clumpTexHeight, TextureFormat.RGBAFloat, false, true);
        RenderTexture clumpVoronoiTex = RenderTexture.GetTemporary(clumpTexWidth, clumpTexHeight, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)   ;
        Graphics.Blit(startTex, clumpVoronoiTex, clumpingVoronoiMat, 0);


        RenderTexture.active = clumpVoronoiTex;
        Texture2D clumpTex = new Texture2D(clumpTexWidth, clumpTexHeight, TextureFormat.RGBAFloat, false,true);

        clumpTex.filterMode = FilterMode.Point;
        Debug.Log(clumpTex.filterMode);

        clumpTex.ReadPixels(new Rect(0, 0, clumpTexWidth, clumpTexHeight), 0, 0, true);
        clumpTex.Apply();
        RenderTexture.active = null;

        computeShader.SetTexture(0, clumpTexID, clumpTex);

        RenderTexture.ReleaseTemporary(clumpVoronoiTex);

        clonedMesh = new Mesh(); //2

        clonedMesh.name = "clone";
        clonedMesh.vertices = originalMesh.vertices;
        clonedMesh.triangles = originalMesh.triangles;
        clonedMesh.normals = originalMesh.normals;
        clonedMesh.uv = originalMesh.uv;

        Color[] newColors = new Color[originalMesh.colors.Length];

        for (int i = 0; i < originalMesh.colors.Length; i++)
        {
            Color col = originalMesh.colors[i];
            float r = Mathf.Pow(col.r, _VertexPlacementPower);

            col.r = r;

            newColors[i] = col;
        }

        clonedMesh.colors = newColors;


        //gpu buffers for the mesh
        int[] triangles = clonedMesh.triangles;
        meshTriangles = new ComputeBuffer(triangles.Length, sizeof(int));
        meshTriangles.SetData(triangles);
        //It doesnt actually matter what the vertices are. This can be removed in theory
        Vector3[] positions = clonedMesh.vertices;
        meshPositions = new ComputeBuffer(positions.Length, sizeof(float) * 3);
        meshPositions.SetData(positions);

        Color[] colors = clonedMesh.colors;
        meshColors = new ComputeBuffer(colors.Length, sizeof(float) * 4);
        meshColors.SetData(colors);

        Vector2[] uvs = clonedMesh.uv;
        meshUvs = new ComputeBuffer(uvs.Length, sizeof(float) * 2);
        meshUvs.SetData(uvs);


        material.SetBuffer("Triangles", meshTriangles);
        material.SetBuffer("Positions", meshPositions);
        material.SetBuffer("Colors", meshColors);
        material.SetBuffer("Uvs", meshUvs);

        bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);


        argsBuffer = new ComputeBuffer(1, ARGS_STRIDE, ComputeBufferType.IndirectArguments);

        Debug.Log(numInstances);

        argsBuffer.SetData(new int[] { meshTriangles.count, 0, 0,0});


    

        foreach (var t in terrainPatches)
        {
            if (t != null)
            {
                splatMasks.Add(CreateSplatMask(t.terrainData));
            }
        }

        computeShader.SetTexture(0, windTexID, WindTex);

        computeShader.SetTexture(0, ClumpGradientMapId, texture);
       
        clumpParametersBuffer = new ComputeBuffer(clumpParameters.Count, sizeof(float) * 10);
        computeShader.SetFloat("_NumClumpParameters", clumpParameters.Count);
        updateGrassArtistParameters();

        UpdateGPUParams();
    }

    // Update is called once per frame
    void Update()
    {
        if (testing)
        {
            texture = new Texture2D(gradientMapDimensions.x, gradientMapDimensions.y);
            texture.wrapMode = TextureWrapMode.Clamp;
            for (int x = 0; x < gradientMapDimensions.x; x++)
            {
                Color color = gradientClump.Evaluate((float)x / (float)gradientMapDimensions.x);
                for (int y = 0; y < gradientMapDimensions.y; y++)
                {
                    texture.SetPixel(x, y, color);
                }
            }
            texture.Apply();
            computeShader.SetTexture(0, ClumpGradientMapId, texture);

            //texture2 = new Texture2D(gradientMapDimensions.x, gradientMapDimensions.y);
            //texture2.wrapMode = TextureWrapMode.Clamp;
            //for (int x = 0; x < gradientMapDimensions.x; x++)
            //{
            //    Color color = gradientBlade.Evaluate((float)x / (float)gradientMapDimensions.x);
            //    for (int y = 0; y < gradientMapDimensions.y; y++)
            //    {
            //        texture2.SetPixel(x, y, color);
            //    }
            //}
            //texture2.Apply();
            //material.SetTexture(BladeGradientMapId, texture2) ;



            //if (material.HasProperty("_GradientMap"))
            //{
            //    material.SetTexture("_GradientMap", texture);
            //}
        }
    



    UpdateGPUParams();

        //Graphics.DrawProcedural(material, bounds, MeshTopology.Triangles, meshTriangles.count, numInstances);
        Graphics.DrawProceduralIndirect(material, bounds, MeshTopology.Triangles, argsBuffer,
            0, null, null, UnityEngine.Rendering.ShadowCastingMode.Off, true, gameObject.layer);
    }

    Texture2D CreateSplatMask(TerrainData data)
    {
        if (grassSplatmapIndices == null || grassSplatmapIndices.Count == 0)
            return null;

        int w = data.alphamapWidth;
        int h = data.alphamapHeight;
        float[,,] maps = data.GetAlphamaps(0, 0, w, h);
        Texture2D mask = new Texture2D(w, h, TextureFormat.RFloat, false, true);
        mask.wrapMode = TextureWrapMode.Clamp;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float sum = 0f;
                foreach (int idx in grassSplatmapIndices)
                {
                    if (idx < maps.GetLength(2))
                        sum += maps[y, x, idx];
                }
                mask.SetPixel(x, y, new Color(sum, 0, 0, 1));
            }
        }
        mask.Apply();
        return mask;
    }

    void OnDestroy()
    {

        grassBladesBuffer.Dispose();
        clumpParametersBuffer.Dispose();
        meshTriangles.Dispose();
        meshPositions.Dispose();
        meshColors.Dispose();
        meshUvs.Dispose();
        argsBuffer.Dispose();
        foreach (var mask in splatMasks)
        {
            if (mask != null)
            {
                Destroy(mask);
            }
        }
    }
}


