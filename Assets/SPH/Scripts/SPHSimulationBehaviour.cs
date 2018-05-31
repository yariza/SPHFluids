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

    [SerializeField, HideInInspector]
    ComputeShader _spawnShader;

    #endregion

    #region Private fields

    int _numParticles;
    ComputeBuffer _positionBuffer;

    #endregion

    #region Shader IDs

    int _idTransform;
    int _idBoundsMin;
    int _idBoundsSize;
    int _idDimensions;
    int _idOffset;
    int _idPositionBuffer;

    #endregion

    #region Constants

    const int THREADS_PER_GROUP = 8;

    #endregion

    #region Private functions

    void InitializeResources()
    {
        _idTransform = Shader.PropertyToID("_transform");
        _idBoundsMin = Shader.PropertyToID("_boundsMin");
        _idBoundsSize = Shader.PropertyToID("_boundsSize");
        _idDimensions = Shader.PropertyToID("_dimensions");
        _idOffset = Shader.PropertyToID("_offset");
        _idPositionBuffer = Shader.PropertyToID("_positionBuffer");
    }

    #endregion

    #region Monobehaviour functions

    // Use this for initialization
    void Start()
    {
        InitializeResources();

        // collect all spawners
        SPHSpawner[] spawners = FindObjectsOfType<SPHSpawner>();

        // count number of particles
        int numParticles = 0;
        for (int i = 0; i < spawners.Length; i++)
        {
            numParticles += spawners[i].particleCount;
        }
        _numParticles = numParticles;

        _positionBuffer = new ComputeBuffer(_numParticles, sizeof(float) * 4);

        // fill up the buffer
        numParticles = 0;
        for (int i = 0; i < spawners.Length; i++)
        {
            var spawner = spawners[i];
            var dimension = spawner.particleDimensions;

            _spawnShader.SetMatrix(_idTransform, spawner.transform.localToWorldMatrix);
            _spawnShader.SetVector(_idBoundsMin, spawner.bounds.min);
            _spawnShader.SetVector(_idBoundsSize, spawner.bounds.size);
            _spawnShader.SetInts(_idDimensions,
                     new int[] { dimension.x, dimension.y, dimension.z });
            _spawnShader.SetInt(_idOffset, numParticles);
            _spawnShader.SetBuffer(0, _idPositionBuffer, _positionBuffer);

            int groupsX = Mathf.CeilToInt((float)dimension.x / THREADS_PER_GROUP);
            int groupsY = Mathf.CeilToInt((float)dimension.y / THREADS_PER_GROUP);
            int groupsZ = Mathf.CeilToInt((float)dimension.z / THREADS_PER_GROUP);

            _spawnShader.Dispatch(0, groupsX, groupsY, groupsZ);

            numParticles += spawner.particleCount;
        }
        _material.SetBuffer(_idPositionBuffer, _positionBuffer);
    }

    private void OnDestroy()
    {
        if (_positionBuffer != null)
        {
            _positionBuffer.Release();
        }
    }

    private void OnRenderObject()
    {
        _material.SetPass(0);
        Graphics.DrawProcedural(MeshTopology.Points, 1, _numParticles);
    }

    #endregion
}
