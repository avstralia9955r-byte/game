// Frontline/StylizedLit — единый шейдер персонажей, оружия, кирпичей и окружения.
// PBR (metallic/roughness) + глобальный камуфляж по карте + процедурная грязь/сколы + снег на верхних гранях
// + VAT-анимация ECS-ботов. Совместим с SRP Batcher и Entities Graphics (DOTS instancing).
// Примечание: GI здесь — SH-пробы. Для Adaptive Probe Volumes используйте Shader Graph-вариант
// (те же свойства, см. DESIGN.md §5) — Lit Shader Graph подхватывает APV автоматически.
Shader "Frontline/StylizedLit"
{
    Properties
    {
        [MainTexture] _BaseMap ("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor ("Color", Color) = (1,1,1,1)
        _MaskMap ("Mask (R Metal, G AO, B Camo, A Smooth)", 2D) = "white" {}
        _Metallic ("Metallic", Range(0,1)) = 0
        _Smoothness ("Smoothness", Range(0,1)) = 0.35
        _CamoStrength ("Camo Strength", Range(0,1)) = 0
        _GrimeStrength ("Grime", Range(0,1)) = 0.35
        _ScratchStrength ("Edge Scratches", Range(0,1)) = 0.25
        _SnowStrength ("Snow", Range(0,1)) = 1
        [Toggle] _UseBrickAlbedo ("Use Map Brick Albedo", Float) = 0

        [Header(VAT)]
        [Toggle(_VAT)] _VatEnabled ("Vertex Animation", Float) = 0
        _VatPositionTex ("VAT Positions", 2D) = "black" {}
        _VatNormalTex ("VAT Normals", 2D) = "bump" {}
        _VatBoundsMin ("Bounds Min", Vector) = (-1,0,-1,0)
        _VatBoundsMax ("Bounds Max", Vector) = (1,2,1,0)
        _VatClipStartA ("Clip Start 0-3", Vector) = (0,0,0,0)
        _VatClipStartB ("Clip Start 4-7", Vector) = (0,0,0,0)
        _VatClipLenA ("Clip Len 0-3", Vector) = (1,1,1,1)
        _VatClipLenB ("Clip Len 4-7", Vector) = (1,1,1,1)
        _VatFps ("FPS", Float) = 30
        _VatFrameCount ("Total Frames", Float) = 1
        _VatVertexCount ("Vertex Count", Float) = 1
        _VatClip ("Clip (per instance)", Float) = 0
        _VatTime ("Time (per instance)", Float) = 0
        _TeamTint ("Team Tint (per instance)", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 300

        HLSLINCLUDE
        #pragma target 3.5
        #pragma shader_feature_local _VAT
        #include "FrontlineInput.hlsl"
        ENDHLSL

        // ================= Forward =================
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;        // A — «кривизна» рёбер для сколов, запекается в DCC
                uint   vertexID   : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3  normalWS   : TEXCOORD2;
                half4  color      : TEXCOORD3;
                half   fogFactor  : TEXCOORD4;
                half3  vertexSH   : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                float3 pos = v.positionOS.xyz, nrm = v.normalOS;
                ApplyVat(v.vertexID, pos, nrm);
                VertexPositionInputs p = GetVertexPositionInputs(pos);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(nrm);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.color = v.color;
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                o.vertexSH = SampleSH(o.normalWS);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half3 n = normalize(i.normalWS);
                half4 baseTex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                half4 mask = SAMPLE_TEXTURE2D(_MaskMap, sampler_MaskMap, i.uv);

                half3 albedo = baseTex.rgb * _BaseColor.rgb;
                if (_UseBrickAlbedo > 0.5) albedo = SAMPLE_TEXTURE2D(_BrickAlbedo, sampler_BrickAlbedo, i.uv).rgb * _BaseColor.rgb;
                albedo = ApplyCamo(albedo, mask.b, i.uv);
                albedo *= _TeamTint.rgb;

                // Грязь снизу и в низкочастотных пятнах; сколы — светлее по рёбрам.
                float grimeN = ValueNoise(i.positionWS * 1.7) * 0.6 + ValueNoise(i.positionWS * 6.3) * 0.4;
                half bottom = saturate(1.0 - (i.positionWS.y - floor(i.positionWS.y * 0.5) * 2.0)) * 0.3;
                half grime = saturate((grimeN - 0.45) * 2.5 + bottom) * _GrimeStrength;
                albedo = lerp(albedo, albedo * half3(0.55, 0.5, 0.42), grime);
                half scratch = saturate((i.color.a - 0.6) * 4.0) * step(0.55, ValueNoise(i.positionWS * 22.0)) * _ScratchStrength;
                albedo = lerp(albedo, albedo * 1.6 + 0.08, scratch);

                half metallic = _Metallic * mask.r;
                half smooth = _Smoothness * mask.a * (1.0 - grime * 0.6);
                smooth = lerp(smooth, 0.85, _Wetness * saturate(n.y));          // мокрые поверхности под дождём

                // Снег на верхних гранях (зимняя карта).
                half snow = saturate((n.y - 0.55) * 4.0) * _SnowCover * _SnowStrength;
                snow *= saturate(ValueNoise(i.positionWS * 3.0) * 1.5 + 0.2);
                albedo = lerp(albedo, half3(0.92, 0.94, 0.97), snow);
                smooth = lerp(smooth, 0.2, snow); metallic *= 1.0 - snow;

                InputData d = (InputData)0;
                d.positionWS = i.positionWS;
                d.positionCS = i.positionCS;
                d.normalWS = n;
                d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                d.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                d.fogCoord = i.fogFactor;
                d.bakedGI = i.vertexSH;
                d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                d.shadowMask = half4(1, 1, 1, 1);

                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo;
                s.metallic = metallic;
                s.smoothness = smooth;
                s.occlusion = mask.g;
                s.alpha = 1;
                s.specular = 0;

                half4 c = UniversalFragmentPBR(d, s);
                c.rgb = MixFog(c.rgb, i.fogFactor);
                return c;
            }
            ENDHLSL
        }

        // ================= Shadows =================
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back

            HLSLPROGRAM
            #pragma vertex VertShadow
            #pragma fragment FragEmpty
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; uint vertexID : SV_VertexID; UNITY_VERTEX_INPUT_INSTANCE_ID };

            float4 VertShadow(A v) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(v);
                float3 pos = v.positionOS.xyz, nrm = v.normalOS;
                ApplyVat(v.vertexID, pos, nrm);
                float3 posWS = TransformObjectToWorld(pos);
                float3 nWS = TransformObjectToWorldNormal(nrm);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDir = normalize(_LightPosition - posWS);
            #else
                float3 lightDir = _LightDirection;
            #endif
                float4 cs = TransformWorldToHClip(ApplyShadowBias(posWS, nWS, lightDir));
            #if UNITY_REVERSED_Z
                cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
            #else
                cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return cs;
            }
            half4 FragEmpty() : SV_Target { return 0; }
            ENDHLSL
        }

        // ================= Depth (для SSAO / Depth Prepass) =================
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R

            HLSLPROGRAM
            #pragma vertex VertDepth
            #pragma fragment FragDepth
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; uint vertexID : SV_VertexID; UNITY_VERTEX_INPUT_INSTANCE_ID };

            float4 VertDepth(A v) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(v);
                float3 pos = v.positionOS.xyz, nrm = v.normalOS;
                ApplyVat(v.vertexID, pos, nrm);
                return TransformObjectToHClip(pos);
            }
            half FragDepth(float4 p : SV_POSITION) : SV_Target { return p.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma vertex VertDN
            #pragma fragment FragDN
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; uint vertexID : SV_VertexID; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; half3 normalWS : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };

            V VertDN(A v)
            {
                V o = (V)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                float3 pos = v.positionOS.xyz, nrm = v.normalOS;
                ApplyVat(v.vertexID, pos, nrm);
                o.positionCS = TransformObjectToHClip(pos);
                o.normalWS = TransformObjectToWorldNormal(nrm);
                return o;
            }
            half4 FragDN(V i) : SV_Target { return half4(NormalizeNormalPerPixel(i.normalWS), 0); }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
