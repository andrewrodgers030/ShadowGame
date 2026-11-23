// Shadow soldier body: animated noise dissolve (alpha clip, no real transparency), edges that eat away
// at the silhouette and feet, a thin glowing edge band and glowing cracks taken from the bright parts
// of the albedo. Port of the Blender look-dev shader (shadow_sentinel.blend). URP, main light + ambient.
Shader "ShadowFrit/ShadowDissolve"
{
    Properties
    {
        [MainTexture] _BaseMap ("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor ("Tint (darkens the armour)", Color) = (0.45, 0.45, 0.45, 1)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.45

        [Header(Dissolve)]
        _NoiseScale ("Noise Scale", Float) = 4
        _NoiseSpeed ("Noise Speed", Float) = 0.6
        _Dissolve ("Dissolve Amount", Range(0, 1)) = 0.42
        _SilhouetteBias ("Silhouette Erosion", Range(0, 1)) = 0.45
        _FeetFade ("Feet Fade", Range(0, 0.6)) = 0.2
        _FeetHeight ("Feet Fade Height (m)", Float) = 0.7
        _PivotHeight ("Mesh Pivot Height Above Feet (m)", Float) = 0

        [Header(Glow)]
        [HDR] _EdgeColor ("Edge Colour", Color) = (0.45, 0.57, 0.9, 1)
        _EdgeWidth ("Edge Width", Range(0.001, 0.2)) = 0.03
        _CrackGlow ("Crack Glow", Float) = 2
        _CrackLo ("Crack Threshold Low", Range(0, 1)) = 0.35
        _CrackHi ("Crack Threshold High", Range(0, 1)) = 0.6
        [HDR] _RimColor ("Rim Colour", Color) = (0.08, 0.1, 0.16, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float4 _BaseColor;
            float _Smoothness;
            float _NoiseScale, _NoiseSpeed, _Dissolve, _SilhouetteBias, _FeetFade, _FeetHeight, _PivotHeight;
            float4 _EdgeColor;
            float _EdgeWidth;
            float _CrackGlow, _CrackLo, _CrackHi;
            float4 _RimColor;
        CBUFFER_END
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

        float Hash3(float3 p)
        {
            p = frac(p * 0.3183099 + 0.1);
            p *= 17.0;
            return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
        }

        float ValueNoise(float3 x)
        {
            float3 i = floor(x);
            float3 f = frac(x);
            f = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(lerp(Hash3(i + float3(0, 0, 0)), Hash3(i + float3(1, 0, 0)), f.x),
                             lerp(Hash3(i + float3(0, 1, 0)), Hash3(i + float3(1, 1, 0)), f.x), f.y),
                        lerp(lerp(Hash3(i + float3(0, 0, 1)), Hash3(i + float3(1, 0, 1)), f.x),
                             lerp(Hash3(i + float3(0, 1, 1)), Hash3(i + float3(1, 1, 1)), f.x), f.y), f.z);
        }

        float Fbm(float3 p)
        {
            float sum = 0.0, amp = 0.5;
            [unroll] for (int o = 0; o < 4; o++)
            {
                sum += ValueNoise(p) * amp;
                p = p * 2.03 + 17.1;
                amp *= 0.5;
            }
            return sum / 0.9375;   // back to roughly 0..1
        }

        // Signed distance past the dissolve threshold: < 0 means the pixel is eaten away.
        float DissolveValue(float3 positionWS, float3 normalWS)
        {
            float3 originWS = TransformObjectToWorld(float3(0, 0, 0));
            float3 local = positionWS - originWS;                 // world-scaled, follows the object
            local.y += _PivotHeight;                              // y = height above the feet
            float t = _Time.y * _NoiseSpeed;
            float n = Fbm(local * _NoiseScale + float3(0.0, -t, t * 0.35));
            float3 viewDir = SafeNormalize(GetWorldSpaceViewDir(positionWS));
            float facing = 1.0 - saturate(abs(dot(normalize(normalWS), viewDir)));
            float feet = lerp(_FeetFade, 0.0, saturate(local.y / max(_FeetHeight, 1e-3)));
            return n - feet - facing * _SilhouetteBias - _Dissolve;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                half fogFactor : TEXCOORD3;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i, bool frontFace : SV_IsFrontFace) : SV_Target
            {
                float3 n = normalize(i.normalWS) * (frontFace ? 1.0 : -1.0);
                float d = DissolveValue(i.positionWS, n);
                clip(d);

                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                half3 albedo = tex.rgb * _BaseColor.rgb;
                float3 v = SafeNormalize(GetWorldSpaceViewDir(i.positionWS));

                Light light = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half atten = light.shadowAttenuation * light.distanceAttenuation;
                half ndl = saturate(dot(n, light.direction));
                float3 h = SafeNormalize(light.direction + v);
                half spec = pow(saturate(dot(n, h)), exp2(10.0 * _Smoothness + 1.0)) * _Smoothness;
                half3 color = albedo * (SampleSH(n) + light.color * ndl * atten) + light.color * spec * atten * 0.5;

                half rim = pow(1.0 - saturate(dot(n, v)), 3.0);
                half lum = dot(tex.rgb, half3(0.299, 0.587, 0.114));
                half3 cracks = tex.rgb * smoothstep(_CrackLo, _CrackHi, lum) * _CrackGlow;
                half edge = 1.0 - saturate(d / _EdgeWidth);
                color += cracks + _EdgeColor.rgb * edge + _RimColor.rgb * rim;

                color = MixFog(color, i.fogFactor);
                return half4(color, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDir = normalize(_LightPosition - o.positionWS);
            #else
                float3 lightDir = _LightDirection;
            #endif
                float4 cs = TransformWorldToHClip(ApplyShadowBias(o.positionWS, o.normalWS, lightDir));
            #if UNITY_REVERSED_Z
                cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
            #else
                cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                o.positionCS = cs;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                clip(DissolveValue(i.positionWS, i.normalWS));
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }
            half Frag(Varyings i) : SV_Target
            {
                clip(DissolveValue(i.positionWS, i.normalWS));
                return i.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                clip(DissolveValue(i.positionWS, i.normalWS));
                return half4(NormalizeNormalPerPixel(i.normalWS), 0.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
