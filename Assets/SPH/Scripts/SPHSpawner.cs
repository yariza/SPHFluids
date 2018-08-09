using UnityEngine;
using System.Collections;

public class SPHSpawner : MonoBehaviour
{
    [SerializeField]
    Bounds _bounds;
    public Bounds bounds
    {
        get { return _bounds; }
        set { _bounds = value; }
    }

    [SerializeField, MinValue(0)]
    float _density;
    public float density
    {
        get { return _density; }
        set { _density = value; }
    }

    public Vector3Int particleDimensions
    {
        get
        {
            float cbrtDensity = Mathf.Pow(_density, 1f/3f);
            int x = (int)(bounds.size.x * cbrtDensity);
            int y = (int)(bounds.size.y * cbrtDensity);
            int z = (int)(bounds.size.z * cbrtDensity);
            return new Vector3Int(x, y, z);
        }
    }

    public int particleCount
    {
        get
        {
            var dimension = particleDimensions;
            return dimension.x * dimension.y * dimension.z;
        }
    }

    [SerializeField]
    bool _debug;

    #region Gizmos

    private void DoDrawGizmos()
    {
        var old = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(_bounds.center, _bounds.size);
        Gizmos.matrix = old;
    }

    private void OnDrawGizmosSelected()
    {
        DoDrawGizmos();
    }

    private void OnDrawGizmos()
    {
        if (_debug) DoDrawGizmos();
    }

    #endregion
}
