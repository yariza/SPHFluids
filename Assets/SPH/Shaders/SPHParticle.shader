// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

Shader "SPHParticle/Unlit"
{
    Properties
    {
        _Color("Color", Color) = (1, 0, 0, 0.3)
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
            uniform float4 _Color;
            
            // Vertex shader
            PS_INPUT vert(uint vertex_id : SV_VertexID, uint instance_id : SV_InstanceID)
            {
                PS_INPUT o = (PS_INPUT)0;

                o.color = _Color;

                // Position
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