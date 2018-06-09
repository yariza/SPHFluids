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

    [SerializeField]
    Material _material;

    [Header("Shaders")]
    [SerializeField]
    ComputeShader _spawnShader;

    [SerializeField]
    ComputeShader _initializeShader;

    [SerializeField]
    ComputeShader _minimumShader;

    #endregion

    #region Private fields

    int _numParticles;
    float _gridSize;

    SPHSpawner[] _spawners;

    ComputeBuffer _positionBuffer;
    ComputeBuffer _minimumPositionBuffer;
    ComputeBuffer _idBuffer;
    ComputeBuffer _zIndexBuffer;
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
    int _idIdBuffer;
    int _idZIndexBuffer;
    int _idInputBuffer;
    int _idOutputBuffer;
    int _idMinimumPositionBuffer;

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
        _idIdBuffer = Shader.PropertyToID("_idBuffer");
        _idZIndexBuffer = Shader.PropertyToID("_zIndexBuffer");
        _idInputBuffer = Shader.PropertyToID("_inputBuffer");
        _idOutputBuffer = Shader.PropertyToID("_outputBuffer");
        _idMinimumPositionBuffer = Shader.PropertyToID("_minimumPositionBuffer");

        // set grid size based on rest density and bucket size
        float idealVolume = BUCKET_SIZE / _profile.restDensity;
        // grid size should be cube root of volume
        _gridSize = Mathf.Pow(idealVolume, 1f/3f);

        _positionBuffer = new ComputeBuffer(MAX_PARTICLES, sizeof(float) * 4);
        _idBuffer = new ComputeBuffer(MAX_PARTICLES, sizeof(uint));
        _zIndexBuffer = new ComputeBuffer(MAX_PARTICLES, sizeof(uint));
        _minimumPositionBuffer = new ComputeBuffer(512, sizeof(float) * 4);
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
        _spawnShader.SetBuffer(0, _idPositionBuffer, _positionBuffer);

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

    void ComputeMinimumPosition()
    {
        const int kThreadsPerGroup = 512;
        const int kMaxGroups = 512;

        // reduce from _numParticles to 512
        int numThreads = _numParticles / 2;
        int numGroups = Mathf.CeilToInt(numThreads / kThreadsPerGroup);
        numGroups = (kMaxGroups < numGroups) ? kMaxGroups : numGroups;

        _minimumShader.SetInt(_idNumParticles, _numParticles);
        _minimumShader.SetInt(_idDispatchDim, numGroups);
        _minimumShader.SetBuffer(0, _idInputBuffer, _positionBuffer);
        _minimumShader.SetBuffer(0, _idOutputBuffer, _minimumPositionBuffer);
        _minimumShader.Dispatch(0, numGroups, 1, 1);

        // reduce from 512 to 1
        _minimumShader.SetInt(_idNumParticles, 512);
        _minimumShader.SetInt(_idDispatchDim, 1);
        _minimumShader.SetBuffer(0, _idInputBuffer, _minimumPositionBuffer);
        _minimumShader.SetBuffer(0, _idOutputBuffer, _minimumPositionBuffer);
        _minimumShader.Dispatch(0, 1, 1, 1);
    }

    void InitializeParticlesIds()
    {
        const int kThreadsPerGroup = 256;
        uint numThreads = SPHMath.NearestPowerOf2((uint)_numParticles);

        _initializeShader.SetFloat(_idGridSize, _gridSize);
        _initializeShader.SetInt(_idNumParticles, _numParticles);
        _initializeShader.SetBuffer(0, _idPositionBuffer, _positionBuffer);
        _initializeShader.SetBuffer(0, _idIdBuffer, _idBuffer);
        _initializeShader.SetBuffer(0, _idZIndexBuffer, _zIndexBuffer);
        _initializeShader.SetBuffer(0, _idMinimumPositionBuffer, _minimumPositionBuffer);
        int groupsX = Mathf.CeilToInt((float)numThreads / kThreadsPerGroup);

        _initializeShader.Dispatch(0, groupsX, 1, 1);
    }

    #endregion

    #region Monobehaviour functions

    // Use this for initialization
    void Start()
    {
        InitializeResources();

        // collect all spawners
        _spawners = FindObjectsOfType<SPHSpawner>();
    }

    void Update()
    {
        // initialize particle id and zindex buffer
        
        SpawnAllParticles();

        ComputeMinimumPosition();
        InitializeParticlesIds();
        
        _material.SetBuffer(_idPositionBuffer, _positionBuffer);
        _material.SetBuffer(_idZIndexBuffer, _zIndexBuffer);
    }
    
    private void OnDestroy()
    {
        _positionBuffer.Release();
        _idBuffer.Release();
        _zIndexBuffer.Release();
        _minimumPositionBuffer.Release();
    }

    private void OnRenderObject()
    {
        _material.SetPass(0);
        Graphics.DrawProcedural(MeshTopology.Points, 1, _numParticles);
    }

    #endregion
}
