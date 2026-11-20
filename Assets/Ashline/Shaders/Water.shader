Shader "Ashline/Water"
{
    Properties
    {
        _ShallowColor ("Shallow", Color) = (0.16, 0.62, 0.62, 1)
        _DeepColor ("Deep", Color) = (0.02, 0.16, 0.24, 1)
        _FoamColor ("Foam", Color) = (0.92, 0.95, 0.95, 1)
        _DepthRange ("Depth Range (m)", Float) = 3.5
        _FoamRange ("Foam Range (m)", Float) = 0.7
        _WaveScale ("Wave Scale", Float) = 1
        _WaveSpeed ("Wave Speed", Float) = 1
        _NormalStrength ("Normal Strength", Range(0, 2)) = 0.8
        _Smoothness ("Smoothness", Range(0, 1)) = 0.93
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-100" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor, _DeepColor, _FoamColor;
                float _DepthRange, _FoamRange, _WaveScale, _WaveSpeed, _NormalStrength, _Smoothness;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
                float fog : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.screenPos = ComputeScreenPos(p.positionCS);
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            // Sum of directional sine waves; returns the height gradient (d/dx, d/dz).
            float2 WaveGradient(float2 xz, float t)
            {
                const float2 dirs[6] = {
                    float2(0.94, 0.34), float2(-0.42, 0.91), float2(0.71, -0.70),
                    float2(-0.88, -0.47), float2(0.18, 0.98), float2(0.99, -0.12) };
                const float lens[6] = { 23.0, 13.7, 7.9, 4.6, 2.3, 1.15 };
                const float amps[6] = { 0.16, 0.09, 0.05, 0.028, 0.012, 0.006 };
                float2 g = 0;
                [unroll]
                for (int i = 0; i < 6; i++)
                {
                    float k = 6.2831853 / (lens[i] * _WaveScale);
                    float w = sqrt(9.81 * k) * _WaveSpeed;
                    float ph = dot(dirs[i], xz) * k - w * t;
                    g += dirs[i] * (amps[i] * _WaveScale * k * cos(ph));
                }
                return g;
            }

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3 - 2 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x), lerp(Hash(i + float2(0, 1)), Hash(i + 1), u.x), u.y);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float t = _Time.y;
                float3 posWS = i.positionWS;
                float3 V = normalize(_WorldSpaceCameraPos - posWS);
                float dist = length(_WorldSpaceCameraPos - posWS);

                float2 g = WaveGradient(posWS.xz, t) * _NormalStrength;
                // Flatten the fine waves with distance so the horizon doesn't shimmer.
                g *= lerp(1.0, 0.35, saturate(dist / 600.0));
                float3 n = normalize(float3(-g.x, 1, -g.y));

                float2 uv = i.screenPos.xy / i.screenPos.w;
                float sceneEye = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                float surfEye = i.screenPos.w;
                // Convert view-ray thickness to vertical depth using the view angle.
                float thickness = max(0, sceneEye - surfEye) * saturate(abs(V.y) + 0.15);

                float deep = 1 - exp(-thickness / _DepthRange);
                half3 water = lerp(_ShallowColor.rgb, _DeepColor.rgb, deep);

                Light sun = GetMainLight(TransformWorldToShadowCoord(posWS));
                float shadow = sun.shadowAttenuation;
                half3 ambient = SampleSH(n);
                half3 body = water * (ambient * 0.9 + sun.color * saturate(dot(n, sun.direction)) * (0.25 + 0.35 * shadow));

                float NdotV = saturate(dot(n, V));
                float fresnel = 0.02 + 0.98 * pow(1 - NdotV, 5);
                float3 R = reflect(-V, n);
                R.y = abs(R.y);
                half3 env = GlossyEnvironmentReflection(R, 1 - _Smoothness, 1.0);

                float3 H = normalize(sun.direction + V);
                float spec = pow(saturate(dot(n, H)), 900) * 22 + pow(saturate(dot(n, H)), 90) * 0.35;
                half3 col = lerp(body, env, fresnel) + sun.color * spec * shadow;

                // Shore foam: broken bands that drift with the swell.
                float shore = saturate(1 - thickness / _FoamRange);
                float band = sin(thickness * 9 - t * 1.6 + Noise(posWS.xz * 0.35) * 4) * 0.5 + 0.5;
                float foamNoise = Noise(posWS.xz * 1.7 + t * 0.3) * Noise(posWS.xz * 0.6 - t * 0.1);
                float foam = saturate(shore * shore * 1.4 + shore * band * 0.6) * smoothstep(0.08, 0.5, foamNoise + shore * 0.5);
                col = lerp(col, _FoamColor.rgb * (ambient + sun.color * 0.6), foam);

                float alpha = saturate(0.45 + deep * 0.7 + fresnel * 0.6 + foam);
                col = MixFog(col, i.fog);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
}
