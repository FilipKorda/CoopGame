// Stone surfaces in the style-1 look, drawn straight on any box (greybox cubes included), at any size and in any
// scene. Side faces: staggered courses of stone blocks with random lengths, three tones, wide dark joints and a fake
// unevenness (each block gets its own tilt, a lit top edge and a shadowed bottom edge); an optional taller plinth
// course at the bottom of the object. Top and bottom faces: square slabs in a checker with wide recessed joints.
// Everything is in world space (courses start at the object's bottom), so neighbouring objects line up.
Shader "CoopGame/Stylized Stone"
{
    Properties
    {
        [Header(Sides  stone blocks)]
        _Stone1 ("Stone Light", Color) = (0.92, 0.79, 0.63, 1)
        _Stone2 ("Stone Mid", Color) = (0.87, 0.67, 0.47, 1)
        _Stone3 ("Stone Dark", Color) = (0.79, 0.54, 0.37, 1)
        _MixLight ("Share of Light Stones", Range(0, 1)) = 0.45
        _MixMid ("Share of Light + Mid Stones", Range(0, 1)) = 0.85
        _JointColor ("Joint Color", Color) = (0.49, 0.29, 0.21, 1)
        _BrickLength ("Block Length (m)", Float) = 2
        _BrickJitter ("Block Length Variation", Range(0, 0.9)) = 0.7
        _CourseHeight ("Course Height (m)", Float) = 0.8
        _JointWidth ("Joint Width (m)", Range(0, 0.3)) = 0.07
        _Relief ("Unevenness", Range(0, 1)) = 0.7
        _PlinthHeight ("Plinth Height (m, 0 = none)", Float) = 1.2
        _PlinthColor ("Plinth Color", Color) = (0.72, 0.41, 0.3, 1)
        [Header(Tops and bottoms  slabs)]
        _TopTile ("Slab Size (m)", Float) = 2
        _TopJoint ("Slab Joint (m)", Range(0, 0.4)) = 0.1
        _TopA ("Slab Color A", Color) = (0.23, 0.54, 0.53, 1)
        _TopB ("Slab Color B (checker)", Color) = (0.27, 0.59, 0.56, 1)
        _TopJointColor ("Slab Joint Color", Color) = (0.12, 0.31, 0.32, 1)
        [Header(Lighting)]
        _ShadowColor ("Shadow Color", Color) = (0.42, 0.36, 0.62, 1)
        _RampSoftness ("Light/Shadow Edge Softness", Range(0.01, 1)) = 0.35
        _AmbientStrength ("Ambient Strength", Range(0, 3)) = 1.3
        _TopTint ("Gradient Top Tint", Color) = (1.06, 1.03, 0.98, 1)
        _BottomTint ("Gradient Bottom Tint", Color) = (0.86, 0.84, 0.92, 1)
        _GradBottom ("Gradient Bottom Y", Float) = -30
        _GradTop ("Gradient Top Y", Float) = 30
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
                half4 _Stone1, _Stone2, _Stone3, _JointColor, _PlinthColor, _TopA, _TopB, _TopJointColor, _ShadowColor, _TopTint, _BottomTint;
                float _MixLight, _MixMid, _BrickLength, _BrickJitter, _CourseHeight, _JointWidth, _Relief, _PlinthHeight, _TopTile, _TopJoint;
                half _RampSoftness, _AmbientStrength; float _GradBottom, _GradTop;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float fog : TEXCOORD2; float bottomY : TEXCOORD3; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.fog = ComputeFogFactor(o.positionCS.z);
                o.bottomY = TransformObjectToWorld(float3(0, -0.5, 0)).y;   // bottom of a unit cube (greybox boxes)
                return o;
            }

            float Hash(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            half Ramp(half ndl) { return smoothstep(0.5 - _RampSoftness * 0.5, 0.5 + _RampSoftness * 0.5, ndl * 0.5 + 0.5); }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 an = abs(n);
                half3 albedo; half shade = 1;

                if (an.y > 0.7)
                {
                    // Slabs.
                    float2 uv = i.positionWS.xz / _TopTile;
                    float2 cell = floor(uv), f = frac(uv);
                    float2 dd = min(f, 1 - f) * _TopTile;
                    float edge = min(dd.x, dd.y);
                    half h = Hash(cell), h2 = Hash(cell + 17.3);
                    albedo = fmod(abs(cell.x + cell.y), 2) > 0.5 ? _TopB.rgb : _TopA.rgb;
                    albedo *= 1 + (h - 0.5) * 0.14;
                    float j = _TopJoint * 0.5;
                    // Each slab sits at its own height: a lit bevel on one side, a shadowed one on the other.
                    float bev = 0.04 + h2 * 0.06;
                    if (edge < j) { albedo = _TopJointColor.rgb; shade = 0.8; }
                    else if (edge < j + bev) shade = (f.x > 0.5 || f.y > 0.5) ? 0.84 : 1.12;
                    n = normalize(n + float3(h - 0.5, 0, h2 - 0.5) * 0.18 * _Relief * sign(n.y));
                }
                else
                {
                    // Stone blocks.
                    bool alongZ = an.x > an.z;
                    float u = alongZ ? i.positionWS.z : i.positionWS.x;
                    float y = i.positionWS.y - i.bottomY;
                    bool plinth = _PlinthHeight > 0 && y < _PlinthHeight;
                    float rowH = plinth ? _PlinthHeight : _CourseHeight;
                    float yy = plinth ? y : y - max(0, _PlinthHeight);
                    float row = plinth ? -1 : floor(yy / rowH);
                    float yIn = plinth ? y : yy - row * rowH;
                    float L = _BrickLength * (plinth ? 1.1 : 1);
                    u += Hash(float2(row, 7.1)) * L;                         // stagger the joints row by row
                    float k0 = floor(u / L);
                    float b0 = k0 * L + (Hash(float2(k0, row)) - 0.5) * L * _BrickJitter;
                    float b1 = (k0 + 1) * L + (Hash(float2(k0 + 1, row)) - 0.5) * L * _BrickJitter;
                    float k = u < b0 ? k0 - 1 : (u >= b1 ? k0 + 1 : k0);
                    float a = k * L + (Hash(float2(k, row)) - 0.5) * L * _BrickJitter;
                    float e = (k + 1) * L + (Hash(float2(k + 1, row)) - 0.5) * L * _BrickJitter;
                    float du = min(u - a, e - u), dv = min(yIn, rowH - yIn);
                    float2 id = float2(k, row);
                    half h1 = Hash(id + 3.1), h2 = Hash(id + 9.7), h3 = Hash(id + 21.4), h4 = Hash(id + 33.3);
                    albedo = plinth ? _PlinthColor.rgb : (h1 < _MixLight ? _Stone1.rgb : h1 < _MixMid ? _Stone2.rgb : _Stone3.rgb);
                    albedo *= 1 + (h2 - 0.5) * 0.1;
                    float j = _JointWidth * 0.5;
                    float d = h3 * _Relief;                                    // how far this block "sticks out"
                    if (du < j || dv < j) { albedo = _JointColor.rgb; shade = 0.75; }
                    else
                    {
                        float fromTop = rowH - yIn, fromBottom = yIn;
                        if (fromTop < j + 0.03 + d * 0.06) shade = 1.12;           // lit top edge
                        else if (fromBottom < j + 0.03 + d * 0.1) shade = 0.72;    // shadow under the block
                        else if (u - a < j + 0.02 + d * 0.04) shade = 0.9;
                        shade *= lerp(0.9, 1, smoothstep(0, 0.12, min(du, dv) - j));
                        // Each block leans a little, so the light catches the blocks unevenly.
                        float3 t = alongZ ? float3(0, 0, 1) : float3(1, 0, 0);
                        n = normalize(n + (t * (h4 - 0.5) + float3(0, 1, 0) * (h2 - 0.5)) * 0.35 * _Relief);
                    }
                }

                half g = saturate((i.positionWS.y - _GradBottom) / max(0.01, _GradTop - _GradBottom));
                albedo *= lerp(_BottomTint.rgb, _TopTint.rgb, g) * shade;

                float3 viewDir = GetWorldSpaceNormalizeViewDir(i.positionWS);
                Light main = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half lit = Ramp(dot(n, main.direction)) * main.shadowAttenuation;
                half3 direct = lerp(_ShadowColor.rgb * 0.35, 1, lit) * main.color;
                #if defined(_ADDITIONAL_LIGHTS) || USE_CLUSTER_LIGHT_LOOP
                // Forward+ (cluster) and classic forward both go through the URP light loop macros.
                InputData inputData = (InputData)0; inputData.positionWS = i.positionWS; inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                uint count = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(count)
                    uint li = lightIndex;
                {
                    Light l = GetAdditionalLight(li, i.positionWS, half4(1, 1, 1, 1));
                    direct += l.color * l.distanceAttenuation * l.shadowAttenuation * Ramp(dot(n, l.direction));
                }
                LIGHT_LOOP_END
                #endif
                half3 ambient = SampleSH(n) * _AmbientStrength;
                #if defined(_SCREEN_SPACE_OCCLUSION)
                AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(i.positionCS));
                ambient *= ao.indirectAmbientOcclusion; direct *= ao.directAmbientOcclusion;
                #endif
                half3 color = albedo * (direct + ambient * lerp(_ShadowColor.rgb, 1, 0.5));
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
