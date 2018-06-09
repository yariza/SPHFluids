using UnityEngine;

public class MaterialPropertyAnimator : MonoBehaviour
{
    [System.Serializable]
    public enum AnimationType
    {
        Sine,
        Saw,
        Triangle
    }

    [SerializeField]
    Material _material;
    [SerializeField]
    string _propertyName;
    
    [Header("Animation")]
    [SerializeField]
    AnimationType _type;
    [SerializeField]
    float _period;
    [SerializeField]
    Vector2 _outputRange;

    int _propertyId;
    float _startTime;

    private void Start()
    {
        _startTime = Time.time;
        _propertyId = Shader.PropertyToID(_propertyName);
    }

    private void Update()
    {
        float time = Time.time - _startTime;
        float norm = 0f;
        if (_type == AnimationType.Sine)
        {
            norm = -Mathf.Cos(time * 2 * Mathf.PI / _period) * 0.5f + 0.5f;
        }
        else if (_type == AnimationType.Saw)
        {
            norm = (time / _period) % 1f;
        }
        else if (_type == AnimationType.Triangle)
        {
            norm = Mathf.PingPong(time * 2 / _period, 1f);
        }

        float value = Mathf.Lerp(_outputRange.x, _outputRange.y, norm);
        _material.SetFloat(_propertyId, value);
    }
}

