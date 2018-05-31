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
            int x = (int)(bounds.size.x * _density);
            int y = (int)(bounds.size.y * _density);
            int z = (int)(bounds.size.z * _density);
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
