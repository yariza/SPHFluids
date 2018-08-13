using UnityEngine;

// [ExecuteInEditMode]
// [RequireComponent(typeof(Camera))]
public class SPHRenderer : MonoBehaviour
{
    [SerializeField]
    SPHSimulationBehaviour _simulation;

    [SerializeField]
    Material _material;

    [SerializeField]
    Material _bucketMaterial;
    
    [SerializeField]
    Shader _raymarchShader;

    [SerializeField]
    Mesh _cubeMesh;

    [SerializeField, Range(1,100)]
    int _maxVoxels = 20;

    [SerializeField]
    bool _imageEffect = true;

    [SerializeField, Range(0,1)]
    float _metaballThreshold = 0.2f;

    [SerializeField, Range(2, 7f)]
    float _precision = 2;

    ComputeBuffer _voxelIndirectArgsBuffer;

    int _idPositionBuffer;
    // int _idZIndexBuffer;
    int _idIdZIndexBuffer;
    int _idBucketOffsetBuffer;
    int _idMinBuffer;
    int _idMaxBuffer;
    int _idVoxelDilateBuffer;
    int _idBucketBuffer;
    int _idVoxelDilateCounterBuffer;

    int _idGridSize;
    int _idMaxVoxels;
    int _idMetaballThreshold;
    int _idPrecision;

    int _idCameraInverseView;

    Material _raymarchMaterial;

    private void Awake()
    {
        _idPositionBuffer = Shader.PropertyToID("_positionBuffer");
        // _idZIndexBuffer = Shader.PropertyToID("_zIndexBuffer");
        _idIdZIndexBuffer = Shader.PropertyToID("_idZIndexBuffer");
        _idBucketOffsetBuffer = Shader.PropertyToID("_bucketOffsetBuffer");
        _idMinBuffer = Shader.PropertyToID("_minBuffer");
        _idMaxBuffer = Shader.PropertyToID("_maxBuffer");
        _idVoxelDilateBuffer = Shader.PropertyToID("_voxelDilateBuffer");
        _idBucketBuffer = Shader.PropertyToID("_bucketBuffer");
        _idVoxelDilateCounterBuffer = Shader.PropertyToID("_voxelDilateCounterBuffer");

        _idGridSize = Shader.PropertyToID("_gridSize");
        _idMaxVoxels = Shader.PropertyToID("_maxVoxels");
        _idMetaballThreshold = Shader.PropertyToID("_metaballThreshold");
        _idPrecision = Shader.PropertyToID("_precision");

        _idCameraInverseView = Shader.PropertyToID("_cameraInverseView");
    }

    private void Start()
    {
        _voxelIndirectArgsBuffer = new ComputeBuffer(5, sizeof(uint), ComputeBufferType.IndirectArguments);
        _voxelIndirectArgsBuffer.SetData(new uint[]{
            _cubeMesh.GetIndexCount(0),
            0, // instance count
            _cubeMesh.GetIndexStart(0),
            _cubeMesh.GetBaseVertex(0)
        });
    }

    private void DrawBuckets()
    {
        _bucketMaterial.SetBuffer(_idVoxelDilateCounterBuffer, _simulation.voxelDilateCounterBuffer);
        _bucketMaterial.SetBuffer(_idPositionBuffer, _simulation.inputPositionBuffer);
        _bucketMaterial.SetBuffer(_idBucketBuffer, _simulation.bucketBuffer);
        _bucketMaterial.SetBuffer(_idBucketOffsetBuffer, _simulation.bucketOffsetBuffer);
        _bucketMaterial.SetBuffer(_idMinBuffer, _simulation.minBuffer);

        _bucketMaterial.SetFloat(_idGridSize, _simulation.gridSize);
        _bucketMaterial.SetFloat(_idMetaballThreshold, _metaballThreshold);
        _bucketMaterial.SetFloat(_idPrecision, Mathf.Pow(10, -1.0f * _precision));

        Bounds bounds = new Bounds(Vector3.zero, Vector3.one * 10000f);
        Graphics.DrawMeshInstancedIndirect(_cubeMesh, 0, _bucketMaterial, bounds, _voxelIndirectArgsBuffer);
    }

    private void DrawRaymarch()
    {

        // _raymarchMaterial.SetBuffer(_idMinBuffer, _simulation.minBuffer);
        // _raymarchMaterial.SetBuffer(_idMaxBuffer, _simulation.maxBuffer);

        // Graphics.DrawMesh(_cubeMesh, transform.position, transform.rotation, _raymarchMaterial, 0);
        

    }

    private void Update()
    {
        ComputeBuffer.CopyCount(_simulation.voxelDilateCounterBuffer, _voxelIndirectArgsBuffer, sizeof(uint));
        
    }

    private void OnRenderObject()
    {
        // if (!Application.isPlaying) return;

        DrawBuckets();

        // _material.SetBuffer(_idPositionBuffer, _simulation.inputPositionBuffer);
        // _material.SetBuffer(_idZIndexBuffer, _simulation.zIndexBuffer);
        // _material.SetBuffer(_idIdZIndexBuffer, _simulation.idZIndexBuffer);
        // _material.SetBuffer(_idBucketCountBuffer, _simulation.bucketCountBuffer);
        // _material.SetPass(0);
        // Graphics.DrawProcedural(MeshTopology.Points,  1, _simulation.numParticles);
    }

    // [ImageEffectOpaque]
    // private void OnRenderImage(RenderTexture src, RenderTexture dest)
    // {
    //     if (!Application.isPlaying || !_imageEffect)
    //     {
    //         Graphics.Blit(src, dest);
    //         return;
    //     }

    //     if (_raymarchMaterial == null)
    //     {
    //         _raymarchMaterial = new Material(_raymarchShader);
    //     }

    //     Camera camera = GetCamera();;

    //     _raymarchMaterial.SetMatrix(_idCameraInverseView, camera.cameraToWorldMatrix);
    //     _raymarchMaterial.SetBuffer(_idPositionBuffer, _simulation.inputPositionBuffer);
    //     _raymarchMaterial.SetBuffer(_idMinBuffer, _simulation.minBuffer);
    //     _raymarchMaterial.SetBuffer(_idMaxBuffer, _simulation.maxBuffer);
    //     _raymarchMaterial.SetBuffer(_idBucketOffsetBuffer, _simulation.bucketOffsetBuffer);
    //     _raymarchMaterial.SetBuffer(_idVoxelDilateBuffer, _simulation.voxelDilateBuffer);
    //     _raymarchMaterial.SetBuffer(_idBucketBuffer, _simulation.bucketBuffer);
    //     _raymarchMaterial.SetFloat(_idGridSize, _simulation.gridSize);
    //     _raymarchMaterial.SetFloat(_idMetaballThreshold, _metaballThreshold);
    //     _raymarchMaterial.SetInt(_idMaxVoxels, _maxVoxels);
    //     _raymarchMaterial.SetFloat(_idPrecision, Mathf.Pow(10, -1.0f * _precision));
        
    //     Graphics.Blit(src, dest, _raymarchMaterial);
    // }

    private void OnDestroy()
    {
        if (_voxelIndirectArgsBuffer != null)
        {
            _voxelIndirectArgsBuffer.Release();
        }
    }
}
