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
            
            #define MAX_BUCKETS 1 << 21
            #define BUCKET_MASK ((MAX_BUCKETS) - 1)

            // Pixel shader input
            struct PS_INPUT
            {
                float4 position : SV_POSITION;
                float4 color : COLOR;
            };
            
            StructuredBuffer<float4> _positionBuffer;
            // StructuredBuffer<int> _zIndexBuffer;
            // ByteAddressBuffer _idZIndexBuffer;
            StructuredBuffer<uint> _idZIndexBuffer;
            StructuredBuffer<uint> _bucketCountBuffer;
            uniform float4 _color1;
            uniform float4 _color2;
            uniform float _scale;
            uniform int _threshold;
            
            // Vertex shader
            PS_INPUT vert(uint vertex_id : SV_VertexID, uint instance_id : SV_InstanceID)
            {
                PS_INPUT o = (PS_INPUT)0;

                // Position
                // int zIndex = _zIndexBuffer[instance_id];
                // int zIndex = _idZIndexBuffer.Load(instance_id*8+4);
                // int index = instance_id;
                // o.color = lerp(
                //     lerp(_color1, _color2, frac(((float)zIndex) / _scale)),
                //     float4(0,0,0,0),
                //     step(_threshold, (float)index)
                // );
                float4 color = float4(0,0,0,0);
                uint index = instance_id;
                // if (index - _threshold >= 0)
                // {
                //     uint curZ = _idZIndexBuffer.Load(index * 8 + 4);
                //     uint prevZ = _idZIndexBuffer.Load((index - _threshold) * 8 + 4);
                //     if (curZ < prevZ)
                //     {
                //         // this is an incorrect color
                //         color = float4(1,0,0,1);
                //     }
                //     else
                //     {
                //         color = float4(0,0,1,1);
                //     }
                // }
                uint zIndex = _idZIndexBuffer[index] & BUCKET_MASK;
                uint bucketCount = _bucketCountBuffer[zIndex];
                if (bucketCount > _threshold)
                {
                    color = lerp(float4(1,0,0,1), float4(0,0,1,1), ((float)bucketCount) / 256.0);
                }
                // color = lerp(_color1, _color2, (float)zIndex / (MAX_BUCKETS));
                // o.color = color;
                // o.position = UnityObjectToClipPos(float4(
                //     frac((float)index / 2048) * 4,
                //     floor((float)index / 2048) / 2048 * 4,
                //     0,
                //     1));

                // o.color = lerp(_color1, float4(0,0,0,0), step(_threshold, zIndex));
                o.position = UnityObjectToClipPos(float4(_positionBuffer[instance_id].xyz, 1.0));

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