// Stylized low-poly look for plain greybox cubes of the protagonist warship (same lighting as "CoopGame/Stylized Low Poly").
// The pattern is picked per face in the frame of the object itself (its yaw, so planks follow the ship even when it is rotated):
// floors = wooden deck planks (or steel tread plate), walls = riveted steel plates (or wooden boards), ceilings = big dark plates.
// Every face also gets a steel trim strip along its edges (object space, for unit cubes), so plain boxes read as built parts.
Shader "CoopGame/Stylized Ship Surface"
{
    Properties
    {
        _ShadowColor ("Shadow Color", Color) = (0.42, 0.40, 0.58, 1)
        _RampSoftness ("Light/Shadow Edge Softness", Range(0.01, 1)) = 0.35
        _AmbientStrength ("Ambient Strength", Range(0, 2)) = 1
        _TopTint ("Gradient Top Tint", Color) = (1.06, 1.03, 1, 1)
        _BottomTint ("Gradient Bottom Tint", Color) = (0.80, 0.80, 0.88, 1)
        _GradBottom ("Gradient Bottom Y", Float) = -40
        _GradTop ("Gradient Top Y", Float) = 10
        _Gloss ("Gloss Power", Range(4, 128)) = 40
        _SpecStrength ("Gloss Strength", Range(0, 3)) = 1.0
        _RimColor ("Rim Color", Color) = (1, 0.85, 0.6, 1)
        _RimStrength ("Rim Strength", Range(0, 1)) = 0.12
        _MetalGlow ("Shiny Parts Self Light", Range(0, 1)) = 0.25

        [Header(Floors)]
        [Enum(Planks, 0, Tread Plate, 1)] _FloorMode ("Floor Pattern", Float) = 0
        _FloorA ("Floor Colour A", Color) = (0.64, 0.47, 0.30, 1)
        _FloorB ("Floor Colour B", Color) = (0.53, 0.37, 0.22, 1)
        _FloorGap ("Floor Gap / Nail Colour", Color) = (0.20, 0.13, 0.08, 1)
        _PlankW ("Plank Width (m)", Float) = 0.32
        _PlankL ("Plank Length (m)", Float) = 3.4
        _GapW ("Gap Width (m)", Float) = 0.018
        _FloorShine ("Floor Shine", Range(0, 1)) = 0.05

        [Header(Walls)]
        [Enum(Riveted Plates, 0, Wood Boards, 1)] _WallMode ("Wall Pattern", Float) = 0
        _WallA ("Wall Colour A", Color) = (0.36, 0.41, 0.44, 1)
        _WallB ("Wall Colour B", Color) = (0.31, 0.36, 0.40, 1)
        _SeamColor ("Seam Colour", Color) = (0.15, 0.17, 0.19, 1)
        _RivetColor ("Rivet Colour", Color) = (0.55, 0.58, 0.60, 1)
        _PlateW ("Plate Width (m)", Float) = 1.9
        _PlateH ("Plate Height (m)", Float) = 1.25
        _SeamW ("Seam Width (m)", Float) = 0.022
        _RivetR ("Rivet Radius (m)", Float) = 0.032
        _RivetStep ("Rivet Spacing (m)", Float) = 0.3
        _WallShine ("Wall Shine", Range(0, 1)) = 0.22

        [Header(Ceilings)]
        _CeilA ("Ceiling Colour A", Color) = (0.27, 0.30, 0.33, 1)
        _CeilB ("Ceiling Colour B", Color) = (0.23, 0.26, 0.29, 1)
        _CeilPlate ("Ceiling Plate Size (m)", Float) = 2.4

        [Header(Edge Trim)]
        _TrimColor ("Trim Colour", Color) = (0.22, 0.24, 0.26, 1)
        _TrimW ("Trim Width (m, 0 = off)", Float) = 0.11
        _TrimShine ("Trim Shine", Range(0, 1)) = 0.35
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
                half4 _ShadowColor, _TopTint, _BottomTint, _RimColor;
                half4 _FloorA, _FloorB, _FloorGap, _WallA, _WallB, _SeamColor, _RivetColor, _CeilA, _CeilB, _TrimColor;
                half _RampSoftness, _AmbientStrength, _Gloss, _SpecStrength, _RimStrength, _MetalGlow;
                float _GradBottom, _GradTop;
                float _FloorMode, _PlankW, _PlankL, _GapW, _FloorShine;
                float _WallMode, _PlateW, _PlateH, _SeamW, _RivetR, _RivetStep, _WallShine;
                float _CeilPlate, _TrimW, _TrimShine;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1;
                float3 frameP : TEXCOORD2; float3 frameN : TEXCOORD3; float3 posOS : TEXCOORD4; float3 nOS : TEXCOORD5; float3 scl : TEXCOORD6; float fog : TEXCOORD7;
            };

            float H21(float2 p) { return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453); }
            float H11(float p) { return frac(sin(p * 91.3458) * 47453.5453); }

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                // Frame yaw from the object's most horizontal axis (X or Z), folded into [-45, 45) deg.
                float3 ax = float3(UNITY_MATRIX_M._m00, UNITY_MATRIX_M._m10, UNITY_MATRIX_M._m20);
                float3 ay = float3(UNITY_MATRIX_M._m01, UNITY_MATRIX_M._m11, UNITY_MATRIX_M._m21);
                float3 az = float3(UNITY_MATRIX_M._m02, UNITY_MATRIX_M._m12, UNITY_MATRIX_M._m22);
                float3 h = abs(normalize(ax).y) < abs(normalize(az).y) ? ax : az;
                float yaw = atan2(h.z, h.x);
                yaw -= HALF_PI * floor((yaw + HALF_PI * 0.5) / HALF_PI);
                float c = cos(yaw), s = sin(yaw);
                float3 P = o.positionWS, N = o.normalWS;
                o.frameP = float3(P.x * c + P.z * s, P.y, -P.x * s + P.z * c);
                o.frameN = float3(N.x * c + N.z * s, N.y, -N.x * s + N.z * c);
                o.posOS = v.positionOS.xyz; o.nOS = v.normalOS;
                o.scl = float3(length(ax), length(ay), length(az));
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half Ramp(half ndl) { return smoothstep(0.5 - _RampSoftness * 0.5, 0.5 + _RampSoftness * 0.5, ndl * 0.5 + 0.5); }

            // Riveted plates on a (u, v) plane: returns colour, writes shine.
            half3 Plates(float u, float v, float pw, float ph, half3 ca, half3 cb, out half shine)
            {
                float row = floor(v / ph);
                float uu = u + fmod(abs(row), 2.0) * pw * 0.5;
                float col = floor(uu / pw);
                float fu = frac(uu / pw) * pw, fv = frac(v / ph) * ph;
                float du = min(fu, pw - fu), dv = min(fv, ph - fv);
                half3 c = lerp(ca, cb, H21(float2(col, row)));
                c *= 1 + (H21(float2(row, col) * 1.37) - 0.5) * 0.08;
                shine = _WallShine;
                float ru = frac(uu / _RivetStep) * _RivetStep - _RivetStep * 0.5;
                float rv = frac(v / _RivetStep) * _RivetStep - _RivetStep * 0.5;
                float rivet = min(length(float2(ru, dv - 0.08)), length(float2(du - 0.08, rv)));
                if (rivet < _RivetR) { c = _RivetColor.rgb * (rivet < _RivetR * 0.45 ? 1.15 : 0.9); shine = 0.55; }
                if (min(du, dv) < _SeamW) { c = _SeamColor.rgb; shine = 0.1; }
                return c;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 N = normalize(i.frameN);
                float3 P = i.frameP;
                float3 viewDir = GetWorldSpaceNormalizeViewDir(i.positionWS);
                half g = saturate((i.positionWS.y - _GradBottom) / max(0.01, _GradTop - _GradBottom));
                half3 albedo; half shine;

                if (N.y > 0.6)
                {
                    if (_FloorMode < 0.5)
                    {
                        // Wooden deck planks running along the frame x axis, staggered per row, with nail heads at the ends.
                        float row = floor(P.z / _PlankW);
                        float px = P.x + H11(row) * _PlankL;
                        float seg = floor(px / _PlankL);
                        float fy = frac(P.z / _PlankW) * _PlankW, fx = frac(px / _PlankL) * _PlankL;
                        float ex = min(fx, _PlankL - fx), ey = min(fy, _PlankW - fy);
                        albedo = lerp(_FloorA.rgb, _FloorB.rgb, H21(float2(row, seg)));
                        albedo *= 1 + (H21(float2(seg, row) * 1.7) - 0.5) * 0.14;
                        albedo *= 1 - 0.05 * step(0.6, frac(P.z * 23.0 + H11(seg) * 3.0));   // a little grain
                        shine = _FloorShine;
                        float nail = length(float2(ex - 0.07, abs(fy - _PlankW * 0.5) - _PlankW * 0.24));
                        if (nail < 0.017) { albedo = _FloorGap.rgb * 1.8; shine = 0.4; }
                        if (min(ex, ey) < _GapW) { albedo = _FloorGap.rgb; shine = 0.02; }
                    }
                    else
                    {
                        // Steel tread plate: riveted plates with a diamond pattern of raised bars.
                        albedo = Plates(P.x, P.z, 1.5, 1.5, _FloorA.rgb, _FloorB.rgb, shine);
                        float2 q = P.xz / 0.12; float2 ci = floor(q); float2 f = frac(q) - 0.5;
                        float sg = fmod(abs(ci.x + ci.y), 2.0) < 1.0 ? 1.0 : -1.0;
                        float2 d = normalize(float2(1.0, sg));
                        float along = dot(f, d), across = dot(f, float2(-d.y, d.x));
                        if (abs(along) < 0.3 && abs(across) < 0.07 && length(albedo - _SeamColor.rgb) > 0.01) { albedo *= 1.3; shine = max(shine, 0.45); }
                    }
                }
                else if (N.y < -0.6)
                {
                    albedo = Plates(P.x, P.z, _CeilPlate, _CeilPlate, _CeilA.rgb, _CeilB.rgb, shine);
                }
                else
                {
                    float u = abs(N.x) > abs(N.z) ? P.z : P.x;
                    if (_WallMode < 0.5) albedo = Plates(u, P.y, _PlateW, _PlateH, _WallA.rgb, _WallB.rgb, shine);
                    else
                    {
                        // Vertical wooden boards, broken every few metres.
                        float bw = 0.24, bl = 2.7;
                        float col = floor(u / bw);
                        float pv = P.y + H11(col) * bl;
                        float seg = floor(pv / bl);
                        float fu = frac(u / bw) * bw, fv = frac(pv / bl) * bl;
                        albedo = lerp(_WallA.rgb, _WallB.rgb, H21(float2(col, seg)));
                        albedo *= 1 + (H21(float2(seg, col) * 2.3) - 0.5) * 0.12;
                        shine = 0.05;
                        if (min(min(fu, bw - fu), min(fv, bl - fv)) < 0.014) { albedo = _SeamColor.rgb; shine = 0.02; }
                    }
                }

                // Steel trim along the face edges (unit cube in object space).
                if (_TrimW > 0)
                {
                    float3 d = (0.5 - abs(i.posOS)) * i.scl;
                    float3 an = abs(i.nOS);
                    float e = an.x > an.y && an.x > an.z ? min(d.y, d.z) : (an.y > an.z ? min(d.x, d.z) : min(d.x, d.y));
                    if (e < _TrimW)
                    {
                        albedo = _TrimColor.rgb * (e < 0.016 ? 0.55 : 1.0);
                        shine = _TrimShine;
                        float t = an.x > an.y && an.x > an.z ? (d.y < d.z ? i.posOS.z * i.scl.z : i.posOS.y * i.scl.y)
                                : (an.y > an.z ? (d.x < d.z ? i.posOS.z * i.scl.z : i.posOS.x * i.scl.x) : (d.x < d.y ? i.posOS.y * i.scl.y : i.posOS.x * i.scl.x));
                        float rr = frac(t / 0.35) * 0.35 - 0.175;
                        if (length(float2(rr, e - _TrimW * 0.5)) < 0.026) { albedo = _RivetColor.rgb; shine = 0.6; }
                    }
                }

                albedo *= lerp(_BottomTint.rgb, _TopTint.rgb, g);

                Light main = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half lit = Ramp(dot(n, main.direction)) * main.shadowAttenuation;
                half3 direct = lerp(_ShadowColor.rgb * 0.35, 1, lit) * main.color;
                half3 spec = pow(saturate(dot(n, normalize(main.direction + viewDir))), _Gloss) * shine * lit * _SpecStrength * main.color;

                #if defined(_ADDITIONAL_LIGHTS) || USE_CLUSTER_LIGHT_LOOP
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
                color += albedo * shine * _MetalGlow;
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
