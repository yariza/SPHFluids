Shader "Unlit/SPHVoxelRaymarchInstanced"
{
    Properties
    {
        _tint ("Tint", Color) = (1,1,1,1)
        _cubeMap ("Cubemap", CUBE) = "" {}
        _refraction ("Refration Index", float) = 0.9
        _fresnel ("Fresnel Coefficient", float) = 5.0
        _reflectance ("Reflectance", float) = 1.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100
        Cull Front
        ZWrite On
        ZTest LEqual

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            #include "UnityCG.cginc"
            #include "Assets/SPH/ComputeShaders/SPHMortonCode.cginc"
            #define BUCKET_SIZE 8

            samplerCUBE _cubeMap;
            float4 _tint;

            StructuredBuffer<uint> _voxelDilateCounterBuffer;
            StructuredBuffer<float4> _minBuffer;
            StructuredBuffer<float4> _positionBuffer;
            StructuredBuffer<uint> _bucketBuffer;
            StructuredBuffer<uint> _bucketOffsetBuffer;

            float _gridSize;
            float _metaballThreshold;
            float _precision;
            half _refraction;
            half _fresnel;
            half _reflectance;

            struct Grid
            {
                float3 offset;
                float size;
            };

            struct Ray
            {
                float3 origin;
                float3 dir;
            };

            struct Box
            {
                float3 boxMin;
                float3 boxMax;
            };

            struct Intersection
            {
                bool intersect;
                float t;
            };

            void rayBoxIntersection(Ray r, Box b, out float tNear, out float tFar)
            {
                float3 invDir = 1.0 / r.dir;
                float3 tbot = invDir * (b.boxMin - r.origin);
                float3 ttop = invDir * (b.boxMax - r.origin);

                float3 tmin = min(ttop, tbot);
                float3 tmax = max(ttop, tbot);

                float2 t0 = max(tmin.xx, tmin.yz);
                tNear = max(t0.x, t0.y);
                t0 = min(tmax.xx, tmax.yz);
                tFar = min(t0.x, t0.y);

                tNear = max(tNear, 0.0);
            }

            int3 pos2Voxel(float3 pos, Grid grid)
            {
                return (int3)floor((pos - grid.offset) / grid.size);
            }

            float3 voxel2Pos(int3 voxel, Grid grid)
            {
                return voxel * grid.size + grid.offset;
            }

            uint getBucketCount(int3 voxel, out uint m)
            {
                m = mortonCode((uint3)voxel);
                uint count = _bucketOffsetBuffer[m];
                return count <= BUCKET_SIZE ? count : BUCKET_SIZE;
            }

            float4 getBlob(uint bucket, uint index)
            {
                uint id = _bucketBuffer[bucket * BUCKET_SIZE + index];
                float3 pos = _positionBuffer[id].xyz;
                return float4(pos, _gridSize);
            }

            void calculateBlob(
                float3 pos,
                float4 blob,
                inout bool m,
                inout float p,
                inout float dmin,
                inout float h
            )
            {
                // bounding sphere for ball
                float db = length( blob.xyz - pos );
                if( db < blob.w )
                {
                    float x = db/blob.w;
                    p += 1.0 - x*x*x*(x*(x*6.0-15.0)+10.0);
                    m = true;
                    h = max( h, 0.5333*blob.w );
                }
                else // bouncing sphere distance
                {
                    dmin = min( dmin, db - blob.w );
                }
            }

            void calculateBlobNormal(
                float3 pos,
                float4 blob,
                inout float3 nor
            )
            {
                float db = length( blob.xyz - pos );
                float x = clamp( db/blob.w, 0.0, 1.0 );
                float p = x*x*(30.0*x*x - 60.0*x + 30.0);
                nor += normalize( pos - blob.xyz ) * p / blob.w;
            }

            void iterateMetaballs(
                inout bool m,
                inout float p,
                inout float dmin,
                inout float h,
                float3 pos, int3 voxel
            )
            {
                [unroll]
                for (int dx = -1; dx <= 1; dx++)
                {
                    [unroll]
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        [unroll]
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            int3 otherVoxel = voxel + int3(dx, dy, dz);
                            uint bucket;
                            uint count = getBucketCount(otherVoxel, bucket);

                            for (uint i = 0; i < count; i++)
                            {
                                float4 blob = getBlob(bucket, i);
                                calculateBlob(
                                    pos, blob, m, p, dmin, h
                                );
                            }
                        }
                    }
                }
            }

            void iterateMetaballsNormal(
                inout float3 nor,
                float3 pos, int3 voxel
            )
            {
                [unroll]
                for (int dx = -1; dx <= 1; dx++)
                {
                    [unroll]
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        [unroll]
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            int3 otherVoxel = voxel + int3(dx, dy, dz);
                            uint bucket;
                            uint count = getBucketCount(otherVoxel, bucket);

                            for (uint i = 0; i < count; i++)
                            {
                                float4 blob = getBlob(bucket, i);
                                calculateBlobNormal(
                                    pos, blob, nor
                                );
                            }
                        }
                    }
                }
            }

            float sdMetaballs(float3 pos, int3 voxel)
            {
                bool m = false;
                float p = 0.0;
                float dmin = 1e20;

                float h = 1.0; // track Lipschitz constant

                iterateMetaballs(m, p, dmin, h, pos, voxel);

                float d = dmin + 0.1;

                if (m)
                {
                    float th = _metaballThreshold;
                    d = h * (th - p);
                }

                return d;
            }

            float3 norMetaballs(float3 pos, int3 voxel)
            {
                float3 nor = float3(0.0, 0.0, 0.0);

                iterateMetaballsNormal(nor, pos, voxel);

                return normalize(nor);
            }

            float map(float3 pos, int3 voxel)
            {
                return sdMetaballs(pos, voxel);
            }

            Intersection intersect(float3 ro, float3 rd, float t, float maxd, int3 voxel)
            {
                float precis = _precision;
                float h = precis*2.0;

                Intersection s;

                for( uint i=0; i<75; i++ )
                {
                    if( (h)<precis||t>maxd )
                    {
                        break;
                    }
                    t += h;
                    h = map( ro+rd*t, voxel );
                }

                s.t = t;
                s.intersect = (t <= maxd);
                return s;
            }



            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPosition : TEXCOORD0;
                float3 boxMin : TEXCOORD1;
                int3 voxel : TEXCOORD2;
                // uint instanceID : TEXCOORD1;
            };

            v2f vert (appdata v, uint instanceID : SV_InstanceID)
            {
                Grid grid;
                grid.offset = _minBuffer[0].xyz - _gridSize.xxx;
                grid.size = _gridSize;

                uint voxelId = _voxelDilateCounterBuffer[instanceID];
                int3 voxel = mortonCodeDecode(voxelId);
                float3 position = v.vertex.xyz * 1.0001;
                position += 0.5.xxx;
                position = (voxel + position) * grid.size + grid.offset;

                v2f o;
                o.pos = mul(UNITY_MATRIX_VP, float4(position, 1.0f));
                o.worldPosition = position;
                o.boxMin = voxel2Pos(voxel, grid);
                o.voxel = voxel;
                // o.instanceID = instanceID;
                return o;
            }
            
            fixed4 frag (v2f i) : SV_Target
            {
                Box box;
                box.boxMin = i.boxMin;
                box.boxMax = i.boxMin + _gridSize.xxx;

                Ray ray;
                ray.origin = _WorldSpaceCameraPos;
                ray.dir = normalize(i.worldPosition - _WorldSpaceCameraPos);

                float tNear;
                float tFar;
                rayBoxIntersection(ray, box, tNear, tFar);

                tNear -= _gridSize * 0.2;

                Intersection s;

                fixed4 col = fixed4(0,0,0,1);
                if (map(ray.dir*tNear + ray.origin, i.voxel) > 0)
                {
                    s = intersect(ray.origin, ray.dir, tNear, tFar, i.voxel);

                    if (s.intersect)
                    {
                        float3 pos = ray.dir * s.t + ray.origin;
                        float3 nor = norMetaballs(pos, i.voxel);
                        half fr = pow(1.0 - dot(-ray.dir, nor), _fresnel) * _reflectance;

                        float3 refr = refract(ray.dir, nor, _refraction);
                        float3 refl = reflect(ray.dir, nor);
                        // col.xyz = refl + 0.5;

                        float3 reflColor = texCUBE(_cubeMap, refl).rgb;
                        float3 refrColor = texCUBE(_cubeMap, refr).rgb;

                        col.rgb = reflColor * fr + refrColor;
                        col.rgb *= _tint.rgb;
                        
                        // col.rgb = texCUBE(_cubeMap, refl).rgb * _tint.rgb;
                        // float t = s.t % 1.0;
                        // col.xyz = saturate(t.xxx);
                        // col.xyz = nor + 0.5;
                        return col;
                    }
                }

                discard;
                return col;
            }
            ENDCG
        }
    }
}
