// Low-poly ocean: a triangle grid lifted by a few travelling sine waves in the vertex shader, flat shaded per triangle
// (normals from screen-space derivatives), deep/crest colours by wave height, foam on the crests, sun glint, a fresnel
// blend towards the horizon colour and fog. Waves fade out with distance from the camera so the far sea stays calm.
Shader "CoopGame/Stylized Ocean"
{
    Properties
    {
        _DeepColor ("Deep Colour", Color) = (0.05, 0.21, 0.31, 1)
        _CrestColor ("Crest Colour", Color) = (0.13, 0.43, 0.53, 1)
        _FoamColor ("Foam Colour", Color) = (0.86, 0.94, 0.97, 1)
        _HorizonColor ("Horizon (Fresnel) Colour", Color) = (0.60, 0.73, 0.82, 1)
        _ShadowColor ("Shadow Colour", Color) = (0.35, 0.40, 0.58, 1)
        _WaveAmp ("Wave Height (m)", Float) = 0.6
        _WaveLen ("Wave Length (m)", Float) = 24
        _WaveSpeed ("Wave Speed", Float) = 0.8
        _FoamLevel ("Foam Level (0..1 of the wave height)", Range(0, 1.2)) = 0.82
        _FadeStart ("Waves Fade Start (m)", Float) = 260
        _FadeEnd ("Waves Fade End (m)", Float) = 900
        _EdgeStart ("Calm Edge Start (m from object origin)", Float) = 190
        _EdgeEnd ("Calm Edge End (m from object origin)", Float) = 292
        _Gloss ("Glint Power", Range(4, 256)) = 90
        _SpecStrength ("Glint Strength", Range(0, 4)) = 1.6
        _FresnelPower ("Fresnel Power", Range(0.5, 8)) = 4
        _FresnelStrength ("Fresnel Strength", Range(0, 1)) = 0.55
        _AmbientStrength ("Ambient Strength", Range(0, 2)) = 0.9
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+10" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _DeepColor, _CrestColor, _FoamColor, _HorizonColor, _ShadowColor;
            float _WaveAmp, _WaveLen, _WaveSpeed, _FoamLevel, _FadeStart, _FadeEnd, _EdgeStart, _EdgeEnd;
            half _Gloss, _SpecStrength, _FresnelPower, _FresnelStrength, _AmbientStrength;
        CBUFFER_END

        // Sum of four travelling waves, -1..1.
        float Wave(float2 p, float t)
        {
            float L = max(1.0, _WaveLen), k = 6.2831853 / L;
            float w = 0;
            w += 0.40 * sin(dot(p, float2(0.80, 0.60)) * k + t * _WaveSpeed * 1.00);
            w += 0.28 * sin(dot(p, float2(-0.45, 0.89)) * k * 1.6 + t * _WaveSpeed * 1.31 + 1.7);
            w += 0.20 * sin(dot(p, float2(0.96, -0.28)) * k * 2.4 + t * _WaveSpeed * 1.63 + 4.1);
            w += 0.12 * sin(dot(p, float2(-0.70, -0.71)) * k * 0.7 + t * _WaveSpeed * 0.83 + 2.3);
            return w;
        }

        float3 Displace(float3 positionOS, out float h, out float fadeOut)
        {
            float3 wp = TransformObjectToWorld(positionOS);
            float dist = distance(wp.xz, _WorldSpaceCameraPos.xz);
            float fade = 1 - saturate((dist - _FadeStart) / max(1, _FadeEnd - _FadeStart));
            // also calm towards the edge of the fine grid, so it meets the flat far ring without cracks
            float3 origin = TransformObjectToWorld(float3(0, 0, 0));
            fade *= 1 - saturate((distance(wp.xz, origin.xz) - _EdgeStart) / max(1, _EdgeEnd - _EdgeStart));
            fadeOut = fade;
            h = Wave(wp.xz, _Time.y) * fade;
            wp.y += h * _WaveAmp;
            return wp;
        }

        float3 FlatNormal(float3 positionWS)
        {
            float3 n = normalize(cross(ddy(positionWS), ddx(positionWS)));
            return n.y < 0 ? -n : n;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float h : TEXCOORD1; float fog : TEXCOORD2; float calm : TEXCOORD3; };

            Varyings vert(Attributes v)
            {
                Varyings o; float h;
                o.positionWS = Displace(v.positionOS.xyz, h, o.calm);
                o.calm = 1 - o.calm;
                o.h = h;
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = FlatNormal(i.positionWS);
                float3 v = GetWorldSpaceNormalizeViewDir(i.positionWS);
                half k = saturate(i.h * 0.5 + 0.5);
                half3 albedo = lerp(_DeepColor.rgb, _CrestColor.rgb, k);
                if (i.h > _FoamLevel) albedo = lerp(albedo, _FoamColor.rgb, 0.85);
                Light main = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half ndl = saturate(dot(n, main.direction) * 0.5 + 0.5);
                half lit = ndl * lerp(0.35, 1, main.shadowAttenuation);
                half3 col = albedo * (lerp(_ShadowColor.rgb * 0.45, 1, lit) * main.color * 0.75 + SampleSH(n) * _AmbientStrength);
                half fres = pow(1 - saturate(dot(n, v)), _FresnelPower) * _FresnelStrength * lerp(1, 0.55, i.calm);
                col = lerp(col, _HorizonColor.rgb, fres);
                col += pow(saturate(dot(n, normalize(main.direction + v))), _Gloss) * _SpecStrength * main.color * main.shadowAttenuation;
                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
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
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert(Attributes v) { Varyings o; float h, k; o.positionCS = TransformWorldToHClip(Displace(v.positionOS.xyz, h, k)); return o; }
            half frag(Varyings i) : SV_Target { return i.positionCS.z; }
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
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };
            Varyings vert(Attributes v) { Varyings o; float h, k; o.positionWS = Displace(v.positionOS.xyz, h, k); o.positionCS = TransformWorldToHClip(o.positionWS); return o; }
            half4 frag(Varyings i) : SV_Target { return half4(NormalizeNormalPerPixel(FlatNormal(i.positionWS)), 0); }
            ENDHLSL
        }
    }
}
