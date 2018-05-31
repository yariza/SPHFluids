using UnityEngine;
using System.Collections;

public class MinValueAttribute : PropertyAttribute
{
    public float min = 0;

    public MinValueAttribute(float min)
    {
        this.min = min;
    }
}
