// Stylized low-poly look: flat colours come from the mesh vertex colours (alpha = shine, 0 stone .. 1 gold),
// soft two-tone shading with a tinted shadow colour, a vertical gradient (lighter up high, deeper near the floor),
// a little gloss on shiny parts and a rim light. Supports main-light shadows, extra lights, SSAO and fog.
Shader "CoopGame/Stylized Low Poly"
{
    Properties
    {
        _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _ShadowColor ("Shadow Color", Color) = (0.45, 0.4, 0.6, 1)
        _RampSoftness ("Light/Shadow Edge Softness", Range(0.01, 1)) = 0.35
        _AmbientStrength ("Ambient Strength", Range(0, 2)) = 1
        _TopTint ("Gradient Top Tint", Color) = (1.06, 1.03, 1, 1)
        _BottomTint ("Gradient Bottom Tint", Color) = (0.82, 0.8, 0.9, 1)
        _GradBottom ("Gradient Bottom Y", Float) = 0
        _GradTop ("Gradient Top Y", Float) = 10
        _Gloss ("Gloss Power", Range(4, 128)) = 40
        _SpecStrength ("Gloss Strength", Range(0, 3)) = 1.2
        _RimColor ("Rim Color", Color) = (1, 0.85, 0.6, 1)
        _RimStrength ("Rim Strength", Range(0, 1)) = 0.18
        _MetalGlow ("Shiny Parts Self Light", Range(0, 1)) = 0.35
        _Shine ("Shine Multiplier (meshes without vertex colours: keep low)", Range(0, 1)) = 1
        [Header(World Space Tiles)]
        _TileSize ("Tile Width in m (0 = off)", Float) = 0
        _TileAspect ("Tile Height / Width (on walls)", Float) = 1
        _BrickOffset ("Row Offset (bricks)", Range(0, 1)) = 0
        _GroutWidth ("Grout Width in m", Range(0, 0.3)) = 0.05
        _GroutColor ("Grout Color", Color) = (0.4, 0.3, 0.3, 1)
        _TileVariation ("Per-Tile Brightness Variation", Range(0, 0.5)) = 0.08
        _CheckerColor ("Checker Tint (every other tile)", Color) = (1, 1, 1, 1)
        [Toggle] _Unlit ("Glow (unlit, for flames and gems)", Float) = 0
        _EmissionBoost ("Glow Brightness", Range(0, 8)) = 2.5
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
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor, _ShadowColor, _TopTint, _BottomTint, _RimColor, _GroutColor, _CheckerColor;
                float _TileSize, _TileAspect, _BrickOffset, _GroutWidth; half _TileVariation, _Shine;
                half _RampSoftness, _AmbientStrength, _Gloss, _SpecStrength, _RimStrength, _Unlit, _EmissionBoost, _MetalGlow;
                float _GradBottom, _GradTop;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; half4 color : TEXCOORD2; float fog : TEXCOORD3; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.color = v.color;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half Ramp(half ndl) { return smoothstep(0.5 - _RampSoftness * 0.5, 0.5 + _RampSoftness * 0.5, ndl * 0.5 + 0.5); }

            half4 frag(Varyings i) : SV_Target
            {
                if (_Unlit > 0.5) return half4(MixFog(i.color.rgb * _EmissionBoost, i.fog), 1);
                float3 n = normalize(i.normalWS);
                float3 viewDir = GetWorldSpaceNormalizeViewDir(i.positionWS);
                half g = saturate((i.positionWS.y - _GradBottom) / max(0.01, _GradTop - _GradBottom));
                half3 albedo = i.color.rgb * _BaseColor.rgb * lerp(_BottomTint.rgb, _TopTint.rgb, g);
                half shine = i.color.a * _Shine;
                // World-space tiles / bricks on the face's dominant plane (for plain greybox cubes).
                if (_TileSize > 0)
                {
                    float3 an = abs(n);
                    float2 p = an.y > an.x && an.y > an.z ? i.positionWS.xz : an.x > an.z ? i.positionWS.zy : i.positionWS.xy;
                    float th = an.y > an.x && an.y > an.z ? _TileSize : _TileSize * _TileAspect;
                    float2 uv = p / float2(_TileSize, th);
                    uv.x += floor(uv.y) * _BrickOffset;
                    float2 cell = floor(uv), f = frac(uv);
                    float edge = min(min(f.x, 1 - f.x) * _TileSize, min(f.y, 1 - f.y) * th);
                    half h = frac(sin(dot(cell, float2(12.9898, 78.233))) * 43758.5453);
                    albedo *= 1 + (h - 0.5) * 2 * _TileVariation;
                    if (fmod(abs(cell.x + cell.y), 2) > 0.5) albedo *= _CheckerColor.rgb;
                    if (edge < _GroutWidth) albedo = _GroutColor.rgb * lerp(_BottomTint.rgb, _TopTint.rgb, g);
                }

                Light main = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half lit = Ramp(dot(n, main.direction)) * main.shadowAttenuation;
                half3 direct = lerp(_ShadowColor.rgb * 0.35, 1, lit) * main.color;
                half3 spec = pow(saturate(dot(n, normalize(main.direction + viewDir))), _Gloss) * shine * lit * _SpecStrength * main.color;

                #if defined(_ADDITIONAL_LIGHTS) || USE_CLUSTER_LIGHT_LOOP
                // Forward+ (cluster) and classic forward both go through the URP light loop macros.
                InputData inputData = (InputData)0; inputData.positionWS = i.positionWS; inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                uint count = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(count)
                    uint li = lightIndex;
                {
                    Light l = GetAdditionalLight(li, i.positionWS, half4(1, 1, 1, 1));
                    half a = l.distanceAttenuation * l.shadowAttenuation;
                    half r = Ramp(dot(n, l.direction));
                    direct += l.color * a * r;
                    spec += l.color * a * r * pow(saturate(dot(n, normalize(l.direction + viewDir))), _Gloss) * shine * _SpecStrength;
                }
                LIGHT_LOOP_END
                #endif

                half3 ambient = SampleSH(n) * _AmbientStrength;
                #if defined(_SCREEN_SPACE_OCCLUSION)
                AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(i.positionCS));
                ambient *= ao.indirectAmbientOcclusion; direct *= ao.directAmbientOcclusion;
                #endif
                half rim = pow(1 - saturate(dot(n, viewDir)), 3) * _RimStrength;
                half3 color = albedo * (direct + ambient * lerp(_ShadowColor.rgb, 1, 0.5)) + spec + rim * _RimColor.rgb * albedo;
                color += albedo * shine * _MetalGlow;   // shiny parts (gold) keep their colour in shadow instead of going brown
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
