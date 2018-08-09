Shader "Custom/SPHRaymarch"
{
	Properties
	{
		_MainTex ("Texture", 2D) = "white" {}
	}
	SubShader
	{
		// No culling or depth
		Cull Off ZWrite Off ZTest Always

		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			
			#include "UnityCG.cginc"
			#include "Assets/SPH/ComputeShaders/SPHMortonCode.cginc"
			#define BUCKET_SIZE 8

			float4x4 _cameraInverseView;

			StructuredBuffer<float4> _positionBuffer;
			StructuredBuffer<float4> _minBuffer;
			StructuredBuffer<float4> _maxBuffer;
			StructuredBuffer<uint> _bucketOffsetBuffer;
			StructuredBuffer<uint> _voxelDilateBuffer;
			StructuredBuffer<uint> _bucketBuffer;
			float _gridSize;
			uint _maxVoxels;
			float _metaballThreshold;
			float _precision;
			
			sampler2D _MainTex;

			struct appdata
			{
				float4 vertex : POSITION;
				float2 uv : TEXCOORD0;
			};

			struct v2f
			{
				float4 vertex : SV_POSITION;
				float2 uv : TEXCOORD0;
				float3 rayDir : TEXCOORD1;
			};

			v2f vert (appdata v)
			{
				v2f o;

				// Setup rays
				// Image plane in NDC space - UNITY_NEAR_CLIP_VALUE will give you the near plane value based on the platform you are working on. ex, Direct3D-like platforms use 0.0 while OpenGL-like platforms use –1.0.
				// https://docs.unity3d.com/Manual/SL-BuiltinMacros.html
				float4 pixels = float4(v.uv * 2. - 1., UNITY_NEAR_CLIP_VALUE, 1);
				// to view space
				pixels = mul(unity_CameraInvProjection, pixels);
				// Perspective division
				pixels.xyz /= pixels.w;
				// to world space
				// you can get the inversed view matrix on .cs  
				// cam.cameraToWorldMatrix
				pixels = mul(_cameraInverseView, pixels);
				// camera position in world space
				float3 rayOrigin = _WorldSpaceCameraPos;
				// compute ray direction
				float3 rayDir = normalize(pixels.xyz - rayOrigin);

				o.vertex = UnityObjectToClipPos(v.vertex);
				o.uv = v.uv;
				o.rayDir = rayDir;
				return o;
			}

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

			struct Grid
			{
				float3 offset;
				float size;
			};

			struct Intersection
			{
				bool intersect;
				float t;
			};

			bool rayBoxIntersection(Ray r, Box b, out float tNear, out float tFar)
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

				// make sure that tFar sits slightly before the grid boundary
				tFar -= 0.00001 * _gridSize;
				tNear += 0.00001 * _gridSize;
				return tFar > tNear;
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
				return count;
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
				inout float m,
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
					m += 1.0;
					h = max( h, 0.5333*blob.w );
				}
				else // bouncing sphere distance
				{
					dmin = min( dmin, db - blob.w );
				}
			}

			void iterateMetaballs(
				inout float m,
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

			float sdMetaballs(float3 pos, int3 voxel)
			{
				float m = 0.0;
				float p = 0.0;
				float dmin = 1e20;

				float h = 1.0; // track Lipschitz constant

				iterateMetaballs(m, p, dmin, h, pos, voxel);

				float d = dmin + 0.1;

				if (m > 0.2)
				{
					float th = _metaballThreshold;
					d = h * (th - p);
				}

				return d;
			}

			float map(float3 pos, int3 voxel)
			{
				return sdMetaballs(pos, voxel);
			}

			Intersection intersect(float3 ro, float3 rd, float maxd, int3 voxel)
			{
				float precis = _precision;
				float h = precis*2.0;
				float t = 0.0;

				for( uint i=0; i<75; i++ )
				{
					if( h<precis||t>maxd ) break;
					t += h;
					h = map( ro+rd*t, voxel );
				}

				Intersection s;
				s.t = t;
				s.intersect = (t <= maxd);
				return s;
			}

			Intersection processVoxel(int3 voxel, float3 pos, float maxd, float3 dir, Grid grid, inout float value, inout float voxelCount)
			{
				uint m = mortonCode((uint3)voxel);
				uint count = _voxelDilateBuffer[m];
				if (count != 0)
				{
					// value += 1.0;
					// return float2(0,-1);
					// [unroll]
					// for (int dx = -1; dx <= 1; dx++)
					// {
					// 	[unroll]
					// 	for (int dy = -1; dy <= 1; dy++)
					// 	{
					// 		[unroll]
					// 		for (int dz = -1; dz <= 1; dz++)
					// 		{
					// 			int3 otherVoxel = voxel + int3(dx, dy, dz);
					// 			// int3 otherVoxel = voxel;
					// 			uint otherM;
					// 			value += (float)getBucketCount(otherVoxel, otherM);
					// 		}
					// 	}
					// }
					voxelCount++;
					// return float2(0, 0);

					return intersect(pos, dir, maxd, voxel);
				}
				else
				{
					Intersection i;
					i.intersect = false;
					i.t = 0.0;
					return i;
				}
			}

			void voxelTraversal(float3 rayStart, float3 rayEnd, float3 rayDir, Grid grid, uint maxCount, out float value)
			{
				value = 0.0;
				uint count = 0;

				int3 curVoxel = pos2Voxel(rayStart, grid);
				int3 lastVoxel = pos2Voxel(rayEnd, grid);

				float3 ray = (rayEnd - rayStart);

				float3 invRay = 1.0 / ray;
				float3 rayNormX = invRay.x * ray;
				float3 rayNormY = invRay.y * ray;
				float3 rayNormZ = invRay.z * ray;
				// float3 rayNormLen = float3(
				// 	length(rayNormX),
				// 	length(rayNormY),
				// 	length(rayNormZ)
				// );

				int3 step = sign(ray);
				float3 nextVoxelBoundary = voxel2Pos(curVoxel + step, grid);

				float3 tMax = (nextVoxelBoundary - rayStart) * invRay;
				float3 tDelta = float3(grid.size, grid.size, grid.size) * invRay * step;

				tMax -= (ray < 0) * tDelta;

				float3 pos = rayStart;

				Intersection i;
				uint voxelCount = 0;

				i = processVoxel(curVoxel, pos, _gridSize * 2, rayDir, grid, value, voxelCount);
				count++;

				if (i.intersect)
				{
					voxelCount++;
					// value = 20.0;
					pos += rayDir * i.t;
					value = length(pos - _WorldSpaceCameraPos);
					return;
				}

				// bool exit = false;
				while (any(lastVoxel != curVoxel) && count < maxCount && voxelCount < _maxVoxels)
				// while(true)
				{
					// tmat = processVoxel(curVoxel, pos, _gridSize * 2, ray, grid, value, voxelCount);
					// count++;

					// if (all(lastVoxel == curVoxel) || count >= 20) break;

					// memoize positions before updating them
					// in order to calculate the maxd to next voxel
					// int3 oldVoxel = curVoxel;
					// float3 oldPos = pos;
					float maxd;

					if (tMax.x < tMax.y)
					{
						if (tMax.x < tMax.z)
						{
							pos = tMax.x * ray.x * rayNormX + rayStart;
							curVoxel.x += step.x;
							tMax.x += tDelta.x;
							// maxd = tDelta.x * rayNormLen.x;
						}
						else
						{
							pos = tMax.z * ray.z * rayNormZ + rayStart;
							curVoxel.z += step.z;
							tMax.z += tDelta.z;
							// maxd = tDelta.z * rayNormLen.z;
						}
					}
					else
					{
						if (tMax.y < tMax.z)
						{
							pos = tMax.y * ray.y * rayNormY + rayStart;
							curVoxel.y += step.y;
							tMax.y += tDelta.y;
							// maxd = tDelta.y * rayNormLen.y;
						}
						else
						{
							pos = tMax.z * ray.z * rayNormZ + rayStart;
							curVoxel.z += step.z;
							tMax.z += tDelta.z;
							// maxd = tDelta.z * rayNormLen.z;
						}
					}

					maxd = _gridSize * 2;
					i = processVoxel(curVoxel, pos, maxd, rayDir, grid, value, voxelCount);
					count++;

					if (i.intersect)
					{
						// voxelCount++;
						// value = 20.0;
						pos += rayDir * i.t;
						value = length(pos - _WorldSpaceCameraPos);
						return;
					}
					// if (exit) break;

					// if (voxelCount >= _maxVoxels) break;

					// if (all(lastVoxel == curVoxel) || count >= 20) exit = true;
				}

				// if (tmat.y > -0.5) value = 20.0;
				// value = voxelCount;
			}

			fixed4 frag (v2f i) : SV_Target
			{
				Ray r;
				r.origin = _WorldSpaceCameraPos;
				r.dir = normalize(i.rayDir);

				Box b;
				b.boxMin = _minBuffer[0].xyz - _gridSize.xxx;
				b.boxMax = _maxBuffer[0].xyz + _gridSize.xxx;

				Grid grid;
				grid.offset = b.boxMin;
				grid.size = _gridSize;

				float tMin, tMax;
				bool intersect = rayBoxIntersection(r, b, tMin, tMax);

				// float3 col = lerp(float3(1,1,1), float3(1,0,0), width / 20.0f);
				if (intersect)
				{
					float3 rayStart = r.dir * tMin + r.origin;
					float3 rayEnd = r.dir * tMax + r.origin;
					float value;

					// value = 20.0;

					voxelTraversal(rayStart, rayEnd, r.dir, grid, 20, value);
					// value = length(rayStart - r.origin) + 1.0;

					float3 col = lerp(float3(0,0,0), float3(1,0,0), saturate(value / 10));
					return fixed4(col, 1.0);
				}
				else
				{
					fixed4 bg = tex2D(_MainTex, i.uv);
					return fixed4(bg.rgb, 1.0);
				}
			}
			ENDCG
		}
	}
}
