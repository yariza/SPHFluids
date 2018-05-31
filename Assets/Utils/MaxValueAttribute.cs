using UnityEngine;
using System.Collections;

public class MaxValueAttribute : PropertyAttribute
{
    public float max = 0;

    public MaxValueAttribute(float max)
    {
        this.max = max;
    }
}
