using UnityEngine;

[CreateAssetMenu]
public class SPHScanProfile : ScriptableObject
{
    [SerializeField]
    ComputeShader _scanShader;

    const int BLOCK_SIZE = 128;

    int _localScanKernel;
    int _blockSumKernel;
    int _propagationKernel;

    int _idDst;
    int _idSrc;
    int _idSumBuffer;
    int _idBlockSum2;
    int _idNumElems;
    int _idNumBlocks;
    int _idNumScanBlocks;

    ComputeBuffer _workBuffer;
    int _maxSize;


    private void OnEnable()
    {
        _localScanKernel = _scanShader.FindKernel("LocalScanKernel");
        _blockSumKernel = _scanShader.FindKernel("TopLevelScanKernel");
        _propagationKernel = _scanShader.FindKernel("AddOffsetKernel");

        _idDst = Shader.PropertyToID("dst");
        _idSrc = Shader.PropertyToID("src");
        _idSumBuffer = Shader.PropertyToID("sumBuffer");
        _idBlockSum2 = Shader.PropertyToID("blockSum2");
        _idNumElems = Shader.PropertyToID("m_numElems");
        _idNumBlocks = Shader.PropertyToID("m_numBlocks");
        _idNumScanBlocks = Shader.PropertyToID("m_numScanBlocks");
    }

    public void Initialize(int maxSize)
    {
        Debug.Assert(maxSize <= BLOCK_SIZE * 2 * 2048);

        int bufSize = SPHMath.NextMultipleOf(
            Mathf.Max(maxSize/BLOCK_SIZE, BLOCK_SIZE),
            BLOCK_SIZE
        ) + 1;
        _workBuffer = new ComputeBuffer(bufSize, sizeof(uint));
        _maxSize = maxSize;
    }

    public void Execute(ComputeBuffer src, ComputeBuffer dst, int n)
    {
        Debug.Assert(n <= _maxSize);
        int numBlocks = (n+BLOCK_SIZE*2-1)/(BLOCK_SIZE*2);

        _scanShader.SetInt(_idNumElems, n);
        _scanShader.SetInt(_idNumBlocks, numBlocks);
        _scanShader.SetInt(_idNumScanBlocks, (int)SPHMath.NearestPowerOf2((uint)numBlocks));

        {
            _scanShader.SetBuffer(_localScanKernel, _idDst, dst);
            _scanShader.SetBuffer(_localScanKernel, _idSrc, src);
            _scanShader.SetBuffer(_localScanKernel, _idSumBuffer, _workBuffer);
            _scanShader.Dispatch(_localScanKernel, numBlocks, 1, 1);
        }

        {
            _scanShader.SetBuffer(_blockSumKernel, _idDst, _workBuffer);
            _scanShader.Dispatch(_blockSumKernel, 1, 1, 1);
        }

        if (numBlocks > 1)
        {
            _scanShader.SetBuffer(_propagationKernel, _idDst, dst);
            _scanShader.SetBuffer(_propagationKernel, _idBlockSum2, _workBuffer);
            _scanShader.Dispatch(_propagationKernel, numBlocks - 1, 1, 1);
        }
    }

    private void OnDisable()
    {
        if (_workBuffer != null)
        {
            _workBuffer.Release();
        }
    }
}
