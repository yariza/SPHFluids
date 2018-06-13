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
    ComputeShader _initializeShader;

    [SerializeField]
    ComputeShader _minMaxShader;

    [SerializeField]
    ComputeShader _minShader;

    [SerializeField]
    ComputeShader _maxShader;

    [SerializeField]
    ComputeShader _sortShader;

    [SerializeField]
    BitonicSort _bitonicSort;

    #endregion

    #region Private fields

    int _numParticles;
    public int numParticles
    {
        get { return _numParticles; }
    }
    float _gridSize;

    SPHSpawner[] _spawners;

    // position is triple-buffered
    int _bufferIndex;
    ComputeBuffer[] _positionBuffers = new ComputeBuffer[3];
    ComputeBuffer inputPositionBuffer
    {
        get { return _positionBuffers[_bufferIndex]; }
    }
    public ComputeBuffer sortedPositionBuffer
    {
        get { return _positionBuffers[(_bufferIndex + 1) % 3]; }
    }
    ComputeBuffer _minBuffer;
    ComputeBuffer _maxBuffer;
    // ComputeBuffer _idBuffer;
    // ComputeBuffer _zIndexBuffer;
    // public ComputeBuffer zIndexBuffer
    // {
        // get { return _zIndexBuffer; }
    // }
    ComputeBuffer _idZIndexBuffer;
    public ComputeBuffer idZIndexBuffer
    {
        get { return _idZIndexBuffer; }
    }
    ComputeBuffer _gridBuffer;

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

    int _idPositionBuffer;
    int _idGridBuffer;
    // int _idIdBuffer;
    // int _idZIndexBuffer;
    int _idIdZIndexBuffer;
    int _idInputBuffer;
    int _idOutputBuffer;
    int _idMinBuffer;
    int _idMaxBuffer;
    int _idSortedPositionBuffer;

    #endregion

    #region Constants

    const int MAX_PARTICLES = 1 << 20;
    const int BUCKET_SIZE = 8;

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

        _idPositionBuffer = Shader.PropertyToID("_positionBuffer");
        _idGridBuffer = Shader.PropertyToID("_gridBuffer");
        _idIdZIndexBuffer = Shader.PropertyToID("_idZIndexBuffer");
        // _idIdBuffer = Shader.PropertyToID("_idBuffer");
        // _idZIndexBuffer = Shader.PropertyToID("_zIndexBuffer");
        _idInputBuffer = Shader.PropertyToID("_inputBuffer");
        _idOutputBuffer = Shader.PropertyToID("_outputBuffer");
        _idMinBuffer = Shader.PropertyToID("_minBuffer");
        _idMaxBuffer = Shader.PropertyToID("_maxBuffer");
        _idSortedPositionBuffer = Shader.PropertyToID("_sortedPositionBuffer");

        // set grid size based on rest density and bucket size
        float idealVolume = BUCKET_SIZE / _profile.restDensity;
        // grid size should be cube root of volume
        _gridSize = Mathf.Pow(idealVolume, 1f/3f);

        _positionBuffers[0] = new ComputeBuffer(MAX_PARTICLES, sizeof(float) * 4);
        _positionBuffers[1] = new ComputeBuffer(MAX_PARTICLES, sizeof(float) * 4);
        _positionBuffers[2] = new ComputeBuffer(MAX_PARTICLES, sizeof(float) * 4);
        // _idBuffer = new ComputeBuffer(MAX_PARTICLES, sizeof(uint));
        // _zIndexBuffer = new ComputeBuffer(MAX_PARTICLES, sizeof(uint));
        _idZIndexBuffer = new ComputeBuffer(MAX_PARTICLES, sizeof(uint) * 2, ComputeBufferType.Raw);
        _minBuffer = new ComputeBuffer(512, sizeof(float) * 4);
        _maxBuffer = new ComputeBuffer(512, sizeof(float) * 4);
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
        int numGroups = Mathf.CeilToInt(numThreads / kThreadsPerGroup);
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

    void InitializeParticlesIds()
    {
        const int kThreadsPerGroup = 512;
        uint numThreads = SPHMath.NearestPowerOf2((uint)_numParticles);

        _initializeShader.SetFloat(_idGridSize, _gridSize);
        _initializeShader.SetInt(_idNumParticles, _numParticles);
        _initializeShader.SetBuffer(0, _idPositionBuffer, inputPositionBuffer);
        // _initializeShader.SetBuffer(0, _idIdBuffer, _idBuffer);
        // _initializeShader.SetBuffer(0, _idZIndexBuffer, _zIndexBuffer);
        _initializeShader.SetBuffer(0, _idIdZIndexBuffer, _idZIndexBuffer);
        _initializeShader.SetBuffer(0, _idMinBuffer, _minBuffer);
        int groupsX = Mathf.CeilToInt((float)numThreads / kThreadsPerGroup);

        _initializeShader.Dispatch(0, groupsX, 1, 1);
    }

    void SortParticlesIds()
    {
        _bitonicSort.Sort(_idZIndexBuffer, false, true, (uint)_numParticles);

        const int kThreadsPerGroup = 512;

        _sortShader.SetInt(_idNumParticles, _numParticles);
        _sortShader.SetBuffer(0, _idIdZIndexBuffer, _idZIndexBuffer);
        _sortShader.SetBuffer(0, _idPositionBuffer, inputPositionBuffer);
        _sortShader.SetBuffer(0, _idSortedPositionBuffer, sortedPositionBuffer);
        int groupsX = Mathf.CeilToInt((float)_numParticles / kThreadsPerGroup);
        _sortShader.Dispatch(0, groupsX, 1, 1);
    }

    #endregion

    #region Monobehaviour functions

    private void Awake()
    {
        InitializeResources();
        _spawners = FindObjectsOfType<SPHSpawner>();
    }

    [SerializeField]
    bool _skipSort;

    void Update()
    {
        _bitonicSort.VerifySortedness(_idZIndexBuffer);
        _bufferIndex = (_bufferIndex + 1) % 3;
        SpawnAllParticles();
        ComputeMinMax();
        InitializeParticlesIds();
        if (!_skipSort)
            SortParticlesIds();
    }
    
    private void OnDestroy()
    {
        _positionBuffers[0].Release();
        _positionBuffers[1].Release();
        _positionBuffers[2].Release();
        // _idBuffer.Release();
        // _zIndexBuffer.Release();
        _idZIndexBuffer.Release();
        _minBuffer.Release();
        _maxBuffer.Release();
    }

    #endregion
}
