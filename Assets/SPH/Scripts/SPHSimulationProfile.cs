using UnityEngine;
using System.Collections;

[CreateAssetMenu]
public class SPHSimulationProfile : ScriptableObject
{
    [SerializeField]
    float _restDensity = 1f;
    public float restDensity
    {
        get { return _restDensity; }
        set { _restDensity = value; }
    }

    #region Public functions

    public void Initialize()
    {

    }

    #endregion
}
