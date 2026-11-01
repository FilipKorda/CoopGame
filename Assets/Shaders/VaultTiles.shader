// White cube tiles for the vault: the tile texture is projected in world space from three sides (triplanar),
// so every wall, floor and step shows 1 m tiles whatever its size. Main light with shadows + ambient.
Shader "CoopGame/Vault Tiles"
{
    Properties
    {
        _BaseMap ("Tile Texture", 2D) = "white" {}
        _BaseColor ("Color", Color) = (1, 1, 1, 1)
        _TileSize ("Tile Size (m per texture)", Float) = 2
        _Ambient ("Ambient Boost", Range(0, 1)) = 0.35
        _AmbientColor ("Ambient Fill Color", Color) = (1, 0.94, 0.86, 1)
        _SkyAmbient ("Sky Ambient Amount", Range(0, 1)) = 0.45
        _FaceTop ("Top Face Brightness", Range(0, 1.5)) = 1.05
        _FaceX ("X Face Brightness", Range(0, 1.5)) = 0.84
        _FaceZ ("Z Face Brightness", Range(0, 1.5)) = 0.93
        _FaceBottom ("Bottom Face Brightness", Range(0, 1.5)) = 0.7
    }
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
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                float _TileSize;
                half _Ambient;
                half _FaceTop, _FaceX, _FaceZ, _FaceBottom;
                half4 _AmbientColor; half _SkyAmbient;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float fog : TEXCOORD2; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 w = pow(abs(n), 6); w /= (w.x + w.y + w.z);
                float3 p = i.positionWS / max(0.01, _TileSize);
                half3 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, p.zy).rgb * w.x
                          + SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, p.xz).rgb * w.y
                          + SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, p.xy).rgb * w.z;
                half3 albedo = tex * _BaseColor.rgb;

                Light main = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half3 light = main.color * saturate(dot(n, main.direction)) * main.shadowAttenuation * main.distanceAttenuation;
                #if defined(_ADDITIONAL_LIGHTS) || USE_CLUSTER_LIGHT_LOOP
                // Forward+ (cluster) and classic forward both go through the URP light loop macros.
                InputData inputData = (InputData)0; inputData.positionWS = i.positionWS; inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                uint count = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(count)
                    uint li = lightIndex;
                {
                    Light l = GetAdditionalLight(li, i.positionWS);
                    light += l.color * saturate(dot(n, l.direction)) * l.distanceAttenuation * l.shadowAttenuation;
                }
                LIGHT_LOOP_END
                #endif
                // Less sky ambient (it tints the white blue) and a warm fill instead, for the pastel look.
                half3 ambient = SampleSH(n) * _SkyAmbient + _Ambient * _AmbientColor.rgb;
                // Voxel-style face shading: tops brightest, sides a step darker, undersides darkest, so cubes that
                // stick out of a wall read clearly against it.
                half face = n.y > 0 ? lerp(1, _FaceTop, n.y) : lerp(1, _FaceBottom, -n.y);
                face *= lerp(1, _FaceX, abs(n.x)) * lerp(1, _FaceZ, abs(n.z));
                // Screen-space AO from the renderer's SSAO feature: soft shadows in the corners between cubes.
                #if defined(_SCREEN_SPACE_OCCLUSION)
                AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(i.positionCS));
                ambient *= ao.indirectAmbientOcclusion; light *= ao.directAmbientOcclusion;
                #endif
                half3 color = albedo * (light + ambient) * face;
                color = MixFog(color, i.fog);
                return half4(color, 1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
    FallBack "Universal Render Pipeline/Lit"
}
