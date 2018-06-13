// ported from Microsoft MiniEngine
// https://github.com/Microsoft/DirectX-Graphics-Samples/blob/master/MiniEngine/Core/BitonicSort.h

// Bitonic Sort is a highly parallel sorting algorithm well-suited to the
// GPU.  It has a complexity of O( N*(log N)^2 ), which is inferior to most
// traditional sorting algorithms, but because GPUs have so many threads,
// and because each thread can be utilized, the algorithm can fully load
// the GPU, taking advantage of its high ALU and bandwidth capabilities.
// 
// Another reason why sorting on the GPU is useful is when the GPU is creating
// work on its own timeline and needs to sort the work without CPU intervention.
// One example in MiniEngine is with GPU simulated particle systems.  Before
// rendering the particles, it is beneficial to sort the particles and render
// them either front-to-back or back-to-front.
//
// The way a bitonic sort works is by iteratively sorting groups of increasing
// size and then blending the sorted groups together to form larger sorted
// groups.  The core of the algorithm can be expressed like so:
//
// for (k = 2; k < NumItems; k *= 2)      // k = group size
//     for (j = k / 2; j > 0; j /= 2)     // j = compare distance
//         for (i = 0; i < NumItems; ++i) // i = element index
//             if (ShouldSwap(i, i ^ j))  // Are the two in proper order?
//                 Swap(i, i ^ j)         // If not, swap them
//
// In this modified form of bitonic sort, all groups of size k are sorted in
// the same direction.  This facilitates sorting lists with non-power-of-two
// lengths.  So the ShouldSwap() test is informed only by the intent to have
// an ascending or descending list.  If sorting ascending, null items should
// have a sort key of 0xffffffff to guarantee sorting to the end.  Likewise,
// sorting descending has a null item of 0x00000000.
//
// The value of the null item is also useful in the key comparison.  Notice
// that with ascending lists, we want A < B, and with descending lists, we
// want A > B.  So if they are reversed, we must swap them.  By using the
// null item value, we can automatically reverse the test like so:
//
// Descending:  Swap if (A < B) == (A ^ 0x00000000 < B ^ 0x00000000)
// Ascending:   Swap if (A > B) == (~A < ~B) == (A ^ 0xffffffff < B ^ 0xffffffff)
// Generalized: Swap if (A ^ NullItem) < (B ^ NullItem)
//
// As an optimization, you can pre-sort the list for k values up to 2048 in
// LDS before writing to memory.  (You do not have to write null items at the
// end of your list to memory.)  It is always better for the caller of this
// system to pre-sort their list as they create it.
//
// The expected usage of this API is that you will have an array of data of
// unspecified stride.  You will generate a list of sort keys and array index
// pairs to pass to the system to be sorted.  The sorted key/index pairs can
// then be used to reorder your data array (with a double buffer) or you can
// read your data array in place using indirection.
//
// We also expect that consumers of this system have a GPU-visible count of
// items.  This probably came from an AppendBuffer (structured buffer with an
// atomic counter).  So you will need to provide the counter buffer as well as
// the offset to the counter.
//
// Also note that key/index pairs may be packed into 32-bit words with the key
// in the most significant bits.  Sorting 32-bit elements is faster than sorting
// 64-bit elements because it uses less bandwidth.

using System;
using UnityEngine;

[CreateAssetMenu]
public class BitonicSort : ScriptableObject
{
    [SerializeField]
    ComputeShader _bitonicIndirectArgsCS;
    [SerializeField]
    ComputeShader _bitonic32PreSortCS;
    [SerializeField]
    ComputeShader _bitonic32InnerSortCS;
    [SerializeField]
    ComputeShader _bitonic32OuterSortCS;
    [SerializeField]
    ComputeShader _bitonic64PreSortCS;
    [SerializeField]
    ComputeShader _bitonic64InnerSortCS;
    [SerializeField]
    ComputeShader _bitonic64OuterSortCS;

    ComputeBuffer _dispatchArgsBuffer;

    int _idListCount;
    int _idNullItem;
    int _idMaxIterations;
    int _idK;
    int _idJ;

    int _idIndirectArgsBuffer;
    int _idSortBuffer;

    private void OnEnable()
    {
        _idListCount = Shader.PropertyToID("ListCount");
        _idNullItem = Shader.PropertyToID("NullItem");
        _idMaxIterations = Shader.PropertyToID("MaxIterations");
        _idK = Shader.PropertyToID("k");
        _idJ = Shader.PropertyToID("j");

        _idIndirectArgsBuffer = Shader.PropertyToID("g_IndirectArgsBuffer");
        _idSortBuffer = Shader.PropertyToID("g_SortBuffer");

        _dispatchArgsBuffer = new ComputeBuffer(22*23/2, 12, ComputeBufferType.IndirectArguments);

    }

    private void OnDisable()
    {
        _dispatchArgsBuffer.Release();

    }

    public void Sort(
        // List to be sorted.  If element size is 4 bytes, it is assumed the key and index are packed
        // together with the key in the most significant bytes.  If element size is 8 bytes, the key
        // is assumed in the upper 4 bytes (i.e. uint2.y).
        ComputeBuffer keyIndexList,
        bool isPartiallyPresorted,
        bool sortAscending,
        uint numElements)
    {
        uint elementSizeBytes = (uint)keyIndexList.stride;
        uint alignedNumElements = SPHMath.NearestPowerOf2(numElements);
        uint maxIterations = SPHMath.CeilingLog2(Math.Max(2048u, alignedNumElements)) - 10;

        Debug.AssertFormat(elementSizeBytes == 4 || elementSizeBytes == 8, "Invalid key-index list for bitonic sort");

        // Generate execute indirect arguments
        _bitonicIndirectArgsCS.SetInt(_idListCount, (int)numElements);
        // This controls two things.  It is a key that will sort to the end, and it is a mask used to
        // determine whether the current group should sort ascending or descending.
        _bitonicIndirectArgsCS.SetInt(_idNullItem, (int)(sortAscending ? 0xffffffff : 0));
        _bitonicIndirectArgsCS.SetInt(_idMaxIterations, (int)maxIterations);
        _bitonicIndirectArgsCS.SetBuffer(0, _idIndirectArgsBuffer, _dispatchArgsBuffer);
        _bitonicIndirectArgsCS.Dispatch(0, 1, 1, 1);

        // Pre-Sort the buffer up to k = 2048.  This also pads the list with invalid indices
        // that will drift to the end of the sorted list.
        if (!isPartiallyPresorted)
        {
            Debug.Log("presort");
            Debug.Log(Mathf.CeilToInt((float)numElements / 2048) + " groups");
            ComputeShader preSortShader = elementSizeBytes == 4 ? _bitonic32PreSortCS : _bitonic64PreSortCS;
            preSortShader.SetInt(_idListCount, (int)numElements);
            preSortShader.SetInt(_idNullItem, (int)(sortAscending ? 0xffffffff : 0));
            preSortShader.SetBuffer(0, _idSortBuffer, keyIndexList);
            // preSortShader.DispatchIndirect(0, _dispatchArgsBuffer, 0);
            preSortShader.Dispatch(0, Mathf.CeilToInt((float)numElements / 2048), 1, 1);
        }

        // VerifySortedness(keyIndexList);

        return;

        uint indirectArgsOffset = 12;

        // We have already pre-sorted up through k = 2048 when first writing our list, so
        // we continue sorting with k = 4096.  For unnecessarily large values of k, these
        // indirect dispatches will be skipped over with thread counts of 0.
        ComputeShader outerSortShader = elementSizeBytes == 4 ? _bitonic32OuterSortCS : _bitonic64OuterSortCS;
        outerSortShader.SetInt(_idListCount, (int)numElements);
        outerSortShader.SetInt(_idNullItem, (int)(sortAscending ? 0xffffffff : 0));
        outerSortShader.SetBuffer(0, _idSortBuffer, keyIndexList);

        ComputeShader innerSortShader = elementSizeBytes == 4 ? _bitonic32InnerSortCS : _bitonic64InnerSortCS;
        innerSortShader.SetInt(_idListCount, (int)numElements);
        innerSortShader.SetInt(_idNullItem, (int)(sortAscending ? 0xffffffff : 0));
        innerSortShader.SetBuffer(0, _idSortBuffer, keyIndexList);

        for (uint k = 4096; k <= alignedNumElements; k *= 2)
        {
            for (uint j = k / 2; j >= 2048; j /= 2)
            {
                Debug.Log("Outer sort J " + j);
                outerSortShader.SetInt(_idK, (int)k);
                outerSortShader.SetInt(_idJ, (int)j);
                outerSortShader.DispatchIndirect(0, _dispatchArgsBuffer, indirectArgsOffset);

                indirectArgsOffset += 12;
            }

            Debug.Log("Inner sort K " + k);
            innerSortShader.SetInt(_idK, (int)k);
            innerSortShader.DispatchIndirect(0, _dispatchArgsBuffer, indirectArgsOffset);

            indirectArgsOffset += 12;
        }
    }

    uint[] _data;

    public void VerifySortedness(ComputeBuffer keyIndexList)
    {
        if (_data == null || _data.Length == 0)
        {
            _data = new uint[128 * 2];
        }
        keyIndexList.GetData(_data, 0, 0, 128 * 2);

        uint incorrect = 0;
        for (uint i = 0; i < 127; i++)
        {
            uint first = _data[i*2 + 1];
            uint second = _data[i*2 + 3];
            if (first > second)
            {
                Debug.Log("incorrect: " + (_data[i*2]) + " {" + first + " > " + second + "}");
                incorrect++;
            }
        }
        Debug.Log(incorrect + " number of elements");
    }
}
