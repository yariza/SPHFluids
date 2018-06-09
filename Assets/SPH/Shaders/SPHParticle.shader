// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

Shader "SPHParticle/Unlit"
{
    Properties
    {
        _color1("Color 1", Color) = (1, 0, 0, 1)
        _color2("Color 2", Color) = (1, 1, 0, 1)
        _scale("Scale", Float) = 1
        _threshold("Threshold", Int) = 1
    }

    SubShader 
    {
        Pass 
        {
            Blend SrcAlpha one

            CGPROGRAM
            #pragma target 5.0
            
            #pragma vertex vert
            #pragma fragment frag
            
            #include "UnityCG.cginc"
            
            // Pixel shader input
            struct PS_INPUT
            {
                float4 position : SV_POSITION;
                float4 color : COLOR;
            };
            
            StructuredBuffer<float4> _positionBuffer;
            StructuredBuffer<int> _zIndexBuffer;
            uniform float4 _color1;
            uniform float4 _color2;
            uniform float _scale;
            uniform int _threshold;
            
            // Vertex shader
            PS_INPUT vert(uint vertex_id : SV_VertexID, uint instance_id : SV_InstanceID)
            {
                PS_INPUT o = (PS_INPUT)0;

                // Position
                int zIndex = _zIndexBuffer[instance_id];
                o.color = lerp(
                    lerp(_color1, _color2, frac(((float)zIndex) / _scale)),
                    float4(0,0,0,0),
                    step(_threshold, (float)zIndex)
                );
                // o.color = lerp(_color1, float4(0,0,0,0), step(_threshold, zIndex));
                o.position = UnityObjectToClipPos(float4(_positionBuffer[instance_id].xyz, 1.0f));

                return o;
            }

            // Pixel shader
            float4 frag(PS_INPUT i) : COLOR
            {
                return i.color;
            }
            
            ENDCG
        }
    }

    Fallback Off
}