using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SPHSimulationBehaviour : MonoBehaviour
{
    #region Serialized Fields

    [SerializeField]
    SPHSimulationProfile _profile;
    public SPHSimulationProfile profile
    {
        get { return _profile; }
    }

    [Header("Shaders")]
    [SerializeField]
    ComputeShader _spawnShader;

    [SerializeField]
    ComputeShader _minMaxShader;

    [SerializeField]
    ComputeShader _minShader;

    [SerializeField]
    ComputeShader _maxShader;

    [SerializeField]
    ComputeShader _sortShader;

    [SerializeField]
    ComputeShader _bucketInitShader;

    [SerializeField]
    ComputeShader _bucketCountShader;

    [SerializeField]
    ComputeShader _bucketGroupInitShader;

    [SerializeField]
    ComputeShader _voxelDilateShader;

    [SerializeField]
    SPHScanProfile _scanProfile;

    [SerializeField]
    float _noiseFrequency;

    #endregion

    #region Private fields

    int _numParticles;
    public int numParticles
    {
        get { return _numParticles; }
    }
    float _gridSize;
    public float gridSize
    {
        get { return _gridSize; }
    }

    SPHSpawner[] _spawners;

    const int NUM_POSITION_BUFFERS = 1;
    int _bufferIndex;
    ComputeBuffer[] _positionBuffers;
    public ComputeBuffer inputPositionBuffer
    {
        get { return _positionBuffers[_bufferIndex]; }
    }
    public ComputeBuffer sortedPositionBuffer
    {
        get { return _positionBuffers[(_bufferIndex + 1) % NUM_POSITION_BUFFERS]; }
    }
    ComputeBuffer _minBuffer;
    public ComputeBuffer minBuffer
    {
        get { return _minBuffer; }
    }
    ComputeBuffer _maxBuffer;
    public ComputeBuffer maxBuffer
    {
        get { return _maxBuffer; }
    }

    ComputeBuffer _zIndexBuffer;
    ComputeBuffer _bucketBuffer;
    public ComputeBuffer bucketBuffer
    {
        get { return _bucketBuffer; }
    }
    ComputeBuffer _bucketOffsetBuffer;
    public ComputeBuffer bucketOffsetBuffer
    {
        get { return _bucketOffsetBuffer; }
    }
    ComputeBuffer _bucketGroupOffsetBuffer;

    ComputeBuffer _voxelDilateBuffer;
    public ComputeBuffer voxelDilateBuffer
    {
        get { return _voxelDilateBuffer; }
    }
    ComputeBuffer _voxelFacesCountBuffer;
    ComputeBuffer _voxelFacesOffsetBuffer;
    ComputeBuffer _voxelFaceBuffer;

    #endregion

    #region Shader IDs

    int _idTransform;
    int _idBoundsMin;
    int _idBoundsSize;
    int _idDimensions;
    int _idOffset;
    int _idNumParticles;
    int _idGridSize;
    int _idDispatchDim;

    int _idZIndexBuffer;
    int _idPositionBuffer;
    int _idInputBuffer;
    int _idOutputBuffer;
    int _idMinBuffer;
    int _idMaxBuffer;
    int _idBucketBuffer;
    int _idBucketOffsetBuffer;
    int _idBucketGroupOffsetBuffer;

    int _idVoxelDilateBuffer;

    int _idNoiseFrequency;
    int _idTime;

    #endregion

    #region Constants

    const int MAX_PARTICLES = 1 << 19;
    const int MAX_BUCKETS = 1 << 21; // 128x128x128 support
    const int MAX_BUCKET_GROUPS = MAX_BUCKETS / BUCKET_GROUP_SIZE;
    const int BUCKET_SIZE = 8;
    const int BUCKET_GROUP_SIZE = 8; // 2x2x2 buckets

    #endregion

    #region Private functions

    void InitializeResources()
    {
        _idTransform = Shader.PropertyToID("_transform");
        _idBoundsMin = Shader.PropertyToID("_boundsMin");
        _idBoundsSize = Shader.PropertyToID("_boundsSize");
        _idDimensions = Shader.PropertyToID("_dimensions");
        _idOffset = Shader.PropertyToID("_offset");
        _idNumParticles = Shader.PropertyToID("_numParticles");
        _idGridSize = Shader.PropertyToID("_gridSize");
        _idDispatchDim = Shader.PropertyToID("_dispatchDim");

        _idZIndexBuffer = Shader.PropertyToID("_zIndexBuffer");
        _idPositionBuffer = Shader.PropertyToID("_positionBuffer");
        _idInputBuffer = Shader.PropertyToID("_inputBuffer");
        _idOutputBuffer = Shader.PropertyToID("_outputBuffer");
        _idMinBuffer = Shader.PropertyToID("_minBuffer");
        _idMaxBuffer = Shader.PropertyToID("_maxBuffer");
        _idBucketBuffer = Shader.PropertyToID("_bucketBuffer");
        _idBucketOffsetBuffer = Shader.PropertyToID("_bucketOffsetBuffer");
        _idBucketGroupOffsetBuffer = Shader.PropertyToID("_bucketGroupOffsetBuffer");

        _idVoxelDilateBuffer = Shader.PropertyToID("_voxelDilateBuffer");

        _idNoiseFrequency = Shader.PropertyToID("_noiseFrequency");
        _idTime = Shader.PropertyToID("_time");

        // set grid size based on rest density and bucket size
        float idealVolume = BUCKET_SIZE / _profile.restDensity / 2; // magic number
        // grid size should be cube root of volume
        _gridSize = Mathf.Pow(idealVolume, 1f/3f);

        _positionBuffers = new ComputeBuffer[NUM_POSITION_BUFFERS];
        for (int i = 0; i < NUM_POSITION_BUFFERS; i++)
        {
            _positionBuffers[i] = new ComputeBuffer(MAX_PARTICLES, sizeof(float) * 4);
        }
        _minBuffer = new ComputeBuffer(512, sizeof(float) * 4);
        _maxBuffer = new ComputeBuffer(512, sizeof(float) * 4);

        _zIndexBuffer = new ComputeBuffer(MAX_PARTICLES, sizeof(uint));
        // approx 67 MB
        _bucketBuffer = new ComputeBuffer(MAX_BUCKETS * BUCKET_SIZE, sizeof(uint));
        // 8 MB
        _bucketOffsetBuffer = new ComputeBuffer(MAX_BUCKETS, sizeof(uint));
        // 2 MB
        // _bucketGroupOffsetBuffer = new ComputeBuffer(MAX_BUCKET_GROUPS, sizeof(uint));

        // 8 MB
        _voxelDilateBuffer = new ComputeBuffer(MAX_BUCKETS, sizeof(uint));
        // 8 MB
        // _voxelFacesCountBuffer = new ComputeBuffer(MAX_BUCKETS, sizeof(uint));
        // 8 MB
        // _voxelFacesOffsetBuffer = new ComputeBuffer(MAX_BUCKETS, sizeof(uint));
        // 8 MB
        // _voxelFaceBuffer = new ComputeBuffer(MAX_BUCKETS, sizeof(uint));


        _scanProfile.Initialize(MAX_BUCKET_GROUPS);
    }

    void SpawnParticles(SPHSpawner spawner, int offset)
    {
        const int kThreadsPerGroupDim = 8;
        var dimension = spawner.particleDimensions;

        _spawnShader.SetMatrix(_idTransform, spawner.transform.localToWorldMatrix);
        _spawnShader.SetVector(_idBoundsMin, spawner.bounds.min);
        _spawnShader.SetVector(_idBoundsSize, spawner.bounds.size);
        _spawnShader.SetInts(_idDimensions,
                             new int[] { dimension.x, dimension.y, dimension.z });
        _spawnShader.SetInt(_idOffset, offset);
        _spawnShader.SetBuffer(0, _idPositionBuffer, inputPositionBuffer);

        _spawnShader.SetFloat(_idNoiseFrequency, _noiseFrequency);
        _spawnShader.SetFloat(_idTime, Time.time);

        int groupsX = Mathf.CeilToInt((float)dimension.x / kThreadsPerGroupDim);
        int groupsY = Mathf.CeilToInt((float)dimension.y / kThreadsPerGroupDim);
        int groupsZ = Mathf.CeilToInt((float)dimension.z / kThreadsPerGroupDim);

        _spawnShader.Dispatch(0, groupsX, groupsY, groupsZ);
    }

    void SpawnAllParticles()
    {
        int numParticles = 0;
        for (int i = 0; i < _spawners.Length; i++)
        {
            var spawner = _spawners[i];
            SpawnParticles(spawner, numParticles);
            numParticles += spawner.particleCount;
        }
        _numParticles = numParticles;
    }

    void ComputeMinMax()
    {
        const int kThreadsPerGroup = 512;
        const int kMaxGroups = 512;

        // reduce from _numParticles to 512 entries per min/max buffer
        int numThreads = _numParticles / 2;
        int numGroups = Mathf.CeilToInt((float)numThreads / kThreadsPerGroup);
        numGroups = (kMaxGroups < numGroups) ? kMaxGroups : numGroups;

        _minMaxShader.SetInt(_idNumParticles, _numParticles);
        _minMaxShader.SetInt(_idDispatchDim, numGroups);
        _minMaxShader.SetBuffer(0, _idInputBuffer, inputPositionBuffer);
        _minMaxShader.SetBuffer(0, _idMinBuffer, _minBuffer);
        _minMaxShader.SetBuffer(0, _idMaxBuffer, _maxBuffer);
        _minMaxShader.Dispatch(0, numGroups, 1, 1);

        // min: reduce from 512 to 1
        _minShader.SetInt(_idNumParticles, 512);
        _minShader.SetInt(_idDispatchDim, 1);
        _minShader.SetBuffer(0, _idInputBuffer, _minBuffer);
        _minShader.SetBuffer(0, _idOutputBuffer, _minBuffer);
        _minShader.Dispatch(0, 1, 1, 1);

        // max: reduce from 512 to 1
        _maxShader.SetInt(_idNumParticles, 512);
        _maxShader.SetInt(_idDispatchDim, 1);
        _maxShader.SetBuffer(0, _idInputBuffer, _maxBuffer);
        _maxShader.SetBuffer(0, _idOutputBuffer, _maxBuffer);
        _maxShader.Dispatch(0, 1, 1, 1);
    }

    // clear the bucket offset buffer with 0 to prepare for fill
    // and subsequent scan operation
    void ClearBucketOffsetBuffer()
    {
        const int kThreadsPerGroup = 512;

        int groups = MAX_BUCKETS / kThreadsPerGroup;
        _bucketInitShader.SetBuffer(0, _idBucketOffsetBuffer, _bucketOffsetBuffer);
        _bucketInitShader.Dispatch(0, groups, 1, 1);
    }

    // calculate the particle zindex
    // increment the corresponding bucket offset buffer element
    // store the zindex + count in the zindex buffer
    void CalculateParticleZIndices()
    {
        const int kThreadsPerGroup = 512;

        int groups = Mathf.CeilToInt((float)_numParticles / kThreadsPerGroup);
        _bucketCountShader.SetBuffer(0, _idPositionBuffer, inputPositionBuffer);
        _bucketCountShader.SetBuffer(0, _idZIndexBuffer, _zIndexBuffer);
        _bucketCountShader.SetBuffer(0, _idBucketOffsetBuffer, _bucketOffsetBuffer);
        _bucketCountShader.SetBuffer(0, _idMinBuffer, _minBuffer);
        _bucketCountShader.SetBuffer(0, _idBucketBuffer, _bucketBuffer);
        _bucketCountShader.SetInt(_idNumParticles, _numParticles);
        _bucketCountShader.SetFloat(_idGridSize, _gridSize);

        _bucketCountShader.Dispatch(0, groups, 1, 1);
    }

    // iterate through all buckets
    // calculate bucket group index (divide by BUCKET_GROUP_SIZE)
    // if any are non-empty, write 1, otherwise 0
    void InitBucketGroupOffsetBuffer()
    {
        const int kThreadsPerGroup = 512;

        int groups = MAX_BUCKET_GROUPS / kThreadsPerGroup;
        _bucketGroupInitShader.SetBuffer(0, _idBucketOffsetBuffer, _bucketOffsetBuffer);
        _bucketGroupInitShader.SetBuffer(0, _idBucketGroupOffsetBuffer, _bucketGroupOffsetBuffer);

        _bucketGroupInitShader.Dispatch(0, groups, 1, 1);
    }

    // prefix sum the bucket group offset buffer
    // to get the offset destinations
    void ScanBucketGroupOffsetBuffer()
    {
        _scanProfile.Execute(_bucketGroupOffsetBuffer, _bucketGroupOffsetBuffer, MAX_BUCKET_GROUPS);

        // copy the bucket group sum to a dispatch indirect buffer
    }

    // iterate through all particles, grab their zindex
    // atomic OR the element in the bucket offset buffer w/ 1
    void BucketParticles()
    {

    }

    // do an (exclusive) prefix sum on the bucket groups to determine the
    // index of the zindex bucket indices
    void ScanBucketGroups()
    {

    }

    // clear the zbucket index buffer with 0xffffffff so that we can tell
    // that it is empty
    void ClearZBucketIndexBuffer()
    {

    }

    // void InitializeParticlesIds()
    // {
    //     const int kThreadsPerGroup = 512;
    //     uint numThreads = SPHMath.NearestPowerOf2((uint)_numParticles);

    //     _initializeShader.SetFloat(_idGridSize, _gridSize);
    //     _initializeShader.SetInt(_idNumParticles, _numParticles);
    //     _initializeShader.SetBuffer(0, _idPositionBuffer, inputPositionBuffer);
    //     _initializeShader.SetBuffer(0, _idIdZIndexBuffer, _idZIndexBuffer);
    //     _initializeShader.SetBuffer(0, _idMinBuffer, _minBuffer);
    //     int groupsX = Mathf.CeilToInt((float)numThreads / kThreadsPerGroup);

    //     _initializeShader.Dispatch(0, groupsX, 1, 1);
    // }

    // void SortParticlesIds()
    // {
    //     _bitonicSort.Sort(_idZIndexBuffer, false, true, (uint)_numParticles);

    //     const int kThreadsPerGroup = 512;

    //     _sortShader.SetInt(_idNumParticles, _numParticles);
    //     _sortShader.SetBuffer(0, _idIdZIndexBuffer, _idZIndexBuffer);
    //     _sortShader.SetBuffer(0, _idPositionBuffer, inputPositionBuffer);
    //     _sortShader.SetBuffer(0, _idSortedPositionBuffer, sortedPositionBuffer);
    //     int groupsX = Mathf.CeilToInt((float)_numParticles / kThreadsPerGroup);
    //     _sortShader.Dispatch(0, groupsX, 1, 1);
    // }

    // void BucketCount()
    // {
    //     const int kThreadsPerGroup = 512;
    //     int groupsX = Mathf.CeilToInt((float)MAX_BUCKETS / kThreadsPerGroup);
    //     _bucketInitShader.SetBuffer(0, _idBucketCountBuffer, _bucketCountBuffer);
    //     _bucketInitShader.Dispatch(0, groupsX, 1, 1);

    //     _bucketCountShader.SetInt(_idNumParticles, _numParticles);
    //     _bucketCountShader.SetBuffer(0, _idIdZIndexBuffer, _idZIndexBuffer);
    //     _bucketCountShader.SetBuffer(0, _idBucketCountBuffer, _bucketCountBuffer);
    //     groupsX = Mathf.CeilToInt((float)_numParticles / kThreadsPerGroup);
    //     _bucketCountShader.Dispatch(0, groupsX, 1, 1);
    // }

    void DilateVoxels()
    {
        const int kThreadsPerGroup = 512;

        int groups = MAX_BUCKETS / kThreadsPerGroup;
        _voxelDilateShader.SetBuffer(0, _idBucketOffsetBuffer, _bucketOffsetBuffer);
        _voxelDilateShader.SetBuffer(0, _idVoxelDilateBuffer, _voxelDilateBuffer);
        _voxelDilateShader.Dispatch(0, groups, 1, 1);
    }

    #endregion

    #region Monobehaviour functions

    private void Awake()
    {
        InitializeResources();
        _spawners = FindObjectsOfType<SPHSpawner>();
    }

    uint[] data = new uint[8192];
    int offset = 0;

    void Update()
    {
        // _bufferIndex = (_bufferIndex + 1) % NUM_POSITION_BUFFERS;
        SpawnAllParticles();
        ComputeMinMax();

        ClearBucketOffsetBuffer();
        CalculateParticleZIndices();

        // _bucketOffsetBuffer.GetData(data, 0, offset, data.Length);
        // uint count = 0;
        // Debug.Log("offset: " + offset);
        // Debug.Log("length: " + data.Length);
        // offset = (offset + data.Length) % _bucketOffsetBuffer.count;
        // for (uint i = 0; i < data.Length; i++)
        // {
        //     if (data[i] != 0)
        //     {
        //         count++;
        //     }
        // }
        // Debug.Log(count);

        // InitBucketGroupOffsetBuffer();
        // ScanBucketGroupOffsetBuffer();

        // dilate particle buckets
        DilateVoxels();
        // count surface faces

        // prefix sum surface face counts



        // InitializeParticlesIds();
        // if (!_skipSort)
            // SortParticlesIds();
        // _bitonicSort.VerifySortedness(_idZIndexBuffer);
        // BucketCount();

        // clear bucket/zindex buffer (with -1)
        // clear bucket offset buffer

        // set zIndex
        

    }
    
    private void OnDestroy()
    {
        for (int i = 0; i < NUM_POSITION_BUFFERS; i++)
        {
            _positionBuffers[i].Release();
        }
        _zIndexBuffer.Release();
        _minBuffer.Release();
        _maxBuffer.Release();
        _bucketBuffer.Release();
        _bucketOffsetBuffer.Release();
        // _bucketGroupOffsetBuffer.Release();
        _voxelDilateBuffer.Release();
    }

    #endregion
}
