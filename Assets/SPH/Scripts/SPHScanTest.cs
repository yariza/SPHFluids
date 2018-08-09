using UnityEngine;

public class SPHScanTest : MonoBehaviour
{
    [SerializeField]
    SPHScanProfile _scan;

    private void Start()
    {
        const int DATA_SIZE = 5000;
        uint[] data = new uint[DATA_SIZE];
        for (uint i = 0; i < data.Length; i++)
        {
            data[i] = i;
        }
        ComputeBuffer src = new ComputeBuffer(DATA_SIZE, sizeof(uint));
        src.SetData(data, 0, 0, DATA_SIZE);
        ComputeBuffer dst = src; // works in place too! :D

        _scan.Initialize(DATA_SIZE);
        _scan.Execute(src, dst, DATA_SIZE);

        uint[] result = new uint[DATA_SIZE];
        dst.GetData(result);
        uint sum = 0;
        bool success = true;
        uint numFail = 0;
        for (uint i = 0; i < data.Length - 1; i++)
        {
            if (sum != result[i])
            {
                Debug.Log("failed at index " + i + ": " + sum + " != " + result[i]);
                success = false;
                numFail++;
                if (numFail > 10)
                {
                    break;
                }
            }
            sum += data[i];
        }
        Debug.Log("total sum: " + result[DATA_SIZE - 1]);
        Debug.Log("expected sum: " + sum);
        if (success)
        {
            Debug.Log("successful");
        }
    }

    private void Update()
    {
        
    }
}
