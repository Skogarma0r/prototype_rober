// Interior-mapped window glass: a fake room box behind every pane, ray-traced in tangent space.
// UV0: integer part = room id, fractional part = position across the pane. UV1: pane size in meters.
Shader "Ashline/WindowInterior"
{
    Properties
    {
        _GlassTint ("Glass Tint", Color) = (0.55, 0.6, 0.62, 1)
        _RoomDepth ("Room Depth (m)", Float) = 5
        _RoomWidth ("Room Width (m)", Float) = 4.2
        _RoomHeight ("Room Height (m)", Float) = 3.0
        _SillHeight ("Pane Bottom Above Floor (m)", Float) = 0.9
        _LightChance ("Lit Room Chance", Range(0, 1)) = 0.25
        _LightColor ("Room Light", Color) = (1, 0.82, 0.6, 1)
        _LightIntensity ("Room Light Intensity", Float) = 1.2
        _DayLight ("Daylight In Rooms", Float) = 0.35
        _BlindChance ("Blind Chance", Range(0, 1)) = 0.45
        _Reflectivity ("Extra Reflection", Range(0, 1)) = 0.12
        _Shop ("Shop Palette", Range(0, 1)) = 0
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    CBUFFER_START(UnityPerMaterial)
        half4 _GlassTint;
        float _RoomDepth, _RoomWidth, _RoomHeight, _SillHeight;
        half _LightChance;
        half4 _LightColor;
        half _LightIntensity, _DayLight, _BlindChance, _Reflectivity, _Shop;
    CBUFFER_END
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                float2 size : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 tangentWS : TEXCOORD2;
                float2 uv : TEXCOORD3;
                float2 size : TEXCOORD4;
                half fog : TEXCOORD5;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                VertexNormalInputs n = GetVertexNormalInputs(v.normalOS, v.tangentOS);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = n.normalWS;
                o.tangentWS = float4(n.tangentWS, v.tangentOS.w * GetOddNegativeScale());
                o.uv = v.uv;
                o.size = max(v.size, float2(0.3, 0.3));
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            float4 Hash4(float2 p)
            {
                float4 q = float4(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)),
                                  dot(p, float2(419.2, 371.9)), dot(p, float2(91.7, 53.1)));
                return frac(sin(q) * 43758.5453);
            }

            half3 Palette(float k, half shop)
            {
                half3 a = k < 0.25 ? half3(0.74, 0.71, 0.65) : k < 0.5 ? half3(0.62, 0.65, 0.66) : k < 0.75 ? half3(0.80, 0.78, 0.73) : half3(0.55, 0.49, 0.43);
                half3 s = k < 0.33 ? half3(0.85, 0.84, 0.82) : k < 0.66 ? half3(0.30, 0.31, 0.33) : half3(0.72, 0.62, 0.50);
                return lerp(a, s, shop);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 N = normalize(i.normalWS);
                float3 T = normalize(i.tangentWS.xyz);
                float3 B = cross(N, T) * i.tangentWS.w;
                float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);

                float2 id = floor(i.uv);
                float2 f = frac(i.uv);
                float4 h = Hash4(id);
                float4 h2 = Hash4(id + 17.31);

                float W = max(_RoomWidth * lerp(0.8, 1.3, h.x), i.size.x + 1.2);
                float H = max(_RoomHeight, i.size.y + 0.2);
                float D = _RoomDepth * lerp(0.7, 1.35, h.y);
                float sill = clamp(_SillHeight, 0.0, H - i.size.y);
                float offX = (h2.x - 0.5) * max(0.0, W - i.size.x) * 0.8;

                // Room space in meters: x across, y up from room center, z depth into the room.
                float3 ro = float3((f.x - 0.5) * i.size.x + offX, sill + f.y * i.size.y - H * 0.5, 0.0);
                float3 r = -V;
                float3 rd = float3(dot(r, T), dot(r, B), max(1e-3, -dot(r, N)));

                float3 inv = 1.0 / rd;
                float3 bounds = float3(W * 0.5, H * 0.5, D);
                float tx = ((rd.x > 0 ? bounds.x : -bounds.x) - ro.x) * inv.x;
                float ty = ((rd.y > 0 ? bounds.y : -bounds.y) - ro.y) * inv.y;
                float tz = (D - ro.z) * inv.z;
                float t = min(tx, min(ty, tz));
                float3 p = ro + rd * t;

                bool lit = h.z < lerp(_LightChance, 0.85, _Shop);
                half3 wallCol = Palette(h.w, _Shop);
                half3 col;
                float ao;
                if (t == tz)
                {
                    col = wallCol * 0.9;
                    // A door, shelf or artwork on the back wall.
                    float cx = (h2.y - 0.5) * W * 0.6;
                    float2 fr = abs(float2(p.x - cx, p.y + H * 0.5 - (h2.z < 0.5 ? 1.05 : 1.5)));
                    if (fr.x < (h2.z < 0.5 ? 0.45 : 0.6) && fr.y < (h2.z < 0.5 ? 1.05 : 0.4))
                        col = h2.z < 0.5 ? half3(0.36, 0.27, 0.19) : half3(0.25, 0.3, 0.36);
                    ao = saturate(min(bounds.x - abs(p.x), bounds.y - abs(p.y)) / 0.7);
                }
                else if (t == tx)
                {
                    col = wallCol;
                    ao = saturate(min(bounds.y - abs(p.y), min(p.z, D - p.z)) / 0.7);
                }
                else if (rd.y < 0)
                {
                    bool wood = h2.w < 0.55;
                    col = wood ? half3(0.36, 0.25, 0.16) : half3(0.28, 0.28, 0.3);
                    if (wood) col *= 0.85 + 0.15 * step(0.06, frac(p.x * 5.0));
                    col = lerp(col, half3(0.6, 0.6, 0.6), _Shop * 0.5);
                    ao = saturate(min(bounds.x - abs(p.x), D - p.z) / 0.9);
                }
                else
                {
                    col = half3(0.7, 0.7, 0.68);
                    float2 lp = abs(float2(frac(p.x / 1.8 + 0.5) - 0.5, frac(p.z / 2.4) - 0.5));
                    if (lit && lp.x < 0.2 && lp.y < 0.12) col = _LightColor.rgb * 2.5;
                    ao = saturate(min(bounds.x - abs(p.x), D - p.z) / 0.7);
                }

                // Furniture silhouettes on two depth layers give parallax; shops get shelving and a counter.
                [unroll] for (int k = 0; k < 2; k++)
                {
                    float4 hk = Hash4(id + 3.7 * (k + 1));
                    float zm = D * lerp(k == 0 ? 0.45 : 0.1, k == 0 ? 0.75 : 0.3, hk.x);
                    float tm = (zm - ro.z) * inv.z;
                    if (tm >= t) continue;
                    float3 q = ro + rd * tm;
                    float fh = _Shop > 0.5 ? (k == 0 ? 1.9 : 1.0) : lerp(0.7, 1.7, hk.y);
                    float x0 = (hk.z - 0.5) * W, x1 = x0 + lerp(0.8, 2.4, hk.w) * (_Shop > 0.5 && k == 0 ? 2.0 : 1.0);
                    if (q.y < -H * 0.5 + fh && q.x > x0 && q.x < x1)
                    {
                        half3 fc = Palette(hk.w, 0) * lerp(0.25, 0.6, hk.y);
                        // Shelf rows read as horizontal bands of goods.
                        if (_Shop > 0.5 && k == 0)
                            fc = lerp(half3(0.2, 0.2, 0.22), Palette(frac(q.x * 1.7 + hk.z), 1) * half3(1.0, 0.85, 0.7), step(0.25, frac((q.y + H * 0.5) * 2.4)));
                        col = fc;
                        p = q;
                        ao = 0.85;
                        t = tm;
                    }
                }

                float depthFade = lerp(1.0, 0.35, saturate(p.z / D));
                half3 light = _DayLight * depthFade * half3(0.95, 0.98, 1.05);
                if (lit) light += _LightColor.rgb * _LightIntensity * saturate(1.15 - (p.y + H * 0.5) / H * 0.4);
                half3 interior = col * light * lerp(0.45, 1.0, ao);

                // Blinds drawn on the pane itself.
                float blind = h2.w < _BlindChance * (1 - _Shop) ? lerp(0.15, 0.85, frac(h.x * 7.13)) : 0.0;
                if (f.y > 1.0 - blind)
                {
                    half slat = 0.85 + 0.15 * step(0.5, frac(f.y * i.size.y * 22.0));
                    interior = half3(0.8, 0.77, 0.7) * slat * (_DayLight * 1.4 + (lit ? _LightColor.rgb * 0.6 : 0));
                }

                interior *= _GlassTint.rgb;

                float NdotV = saturate(dot(N, V));
                half fres = 0.04 + 0.96 * pow(1.0 - NdotV, 5.0);
                fres = saturate(fres + _Reflectivity);
                float3 R = reflect(-V, N);
                half3 refl = GlossyEnvironmentReflection(R, 0.04, 1.0);

                Light sun = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                float3 Hv = normalize(sun.direction + V);
                half3 spec = sun.color * pow(saturate(dot(N, Hv)), 900.0) * 40.0 * sun.shadowAttenuation;

                half3 c = lerp(interior, refl, fres) + spec * fres;
                c = MixFog(c, i.fog);
                return half4(c, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; };

            float4 vert(A v) : SV_POSITION
            {
                float3 pws = TransformObjectToWorld(v.positionOS.xyz);
                float3 nws = TransformObjectToWorldNormal(v.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 ld = normalize(_LightPosition - pws);
            #else
                float3 ld = _LightDirection;
            #endif
                float4 cs = TransformWorldToHClip(ApplyShadowBias(pws, nws, ld));
            #if UNITY_REVERSED_Z
                cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
            #else
                cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return cs;
            }

            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            float4 vert(float4 positionOS : POSITION) : SV_POSITION { return TransformObjectToHClip(positionOS.xyz); }
            half frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct VOut { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };

            VOut vert(A v)
            {
                VOut o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 frag(VOut i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
            #if defined(_GBUFFER_NORMALS_OCT)
                float2 oct = PackNormalOctQuadEncode(n);
                float2 rem = saturate(oct * 0.5 + 0.5);
                return half4(PackFloat2To888(rem), 0.0);
            #else
                return half4(n, 0.0);
            #endif
            }
            ENDHLSL
        }
    }
}
