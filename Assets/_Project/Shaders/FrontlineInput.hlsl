#ifndef FRONTLINE_INPUT_INCLUDED
#define FRONTLINE_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

// ---------- Материал (SRP Batcher: всё в UnityPerMaterial) ----------
CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    half4  _BaseColor;
    half   _Metallic;
    half   _Smoothness;
    half   _CamoStrength;     // 0 — без камуфляжа (дерево, металл), 1 — ткань/окрашенный металл
    half   _GrimeStrength;    // процедурная грязь/потёртости
    half   _ScratchStrength;  // светлые сколы по рёбрам (через кривизну из vertex color A)
    half   _SnowStrength;     // множитель глобального снега
    half   _UseBrickAlbedo;
    float4 _VatBoundsMin, _VatBoundsMax;
    float4 _VatClipStartA, _VatClipStartB;   // кадр начала клипов 0–3 / 4–7
    float4 _VatClipLenA,   _VatClipLenB;     // длина клипов в кадрах
    float  _VatFps, _VatFrameCount, _VatVertexCount;
    float  _VatClip, _VatTime;
    half4  _TeamTint;
CBUFFER_END

#ifdef UNITY_DOTS_INSTANCING_ENABLED
UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
    UNITY_DOTS_INSTANCED_PROP(float, _VatClip)
    UNITY_DOTS_INSTANCED_PROP(float, _VatTime)
    UNITY_DOTS_INSTANCED_PROP(float4, _TeamTint)
UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)
#define _VatClip  UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _VatClip)
#define _VatTime  UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _VatTime)
#define _TeamTint UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _TeamTint)
#endif

TEXTURE2D(_BaseMap);        SAMPLER(sampler_BaseMap);
TEXTURE2D(_MaskMap);        SAMPLER(sampler_MaskMap);     // R metallic, G AO, B маска камуфляжа, A smoothness
TEXTURE2D(_VatPositionTex); SAMPLER(sampler_VatPositionTex);
TEXTURE2D(_VatNormalTex);

// ---------- Глобальные (CamoController / EnvironmentController) ----------
half4 _CamoPrimary, _CamoSecondary, _CamoTertiary;
half  _SnowCover, _Wetness;
TEXTURE2D(_CamoPattern);    SAMPLER(sampler_CamoPattern);
TEXTURE2D(_BrickAlbedo);    SAMPLER(sampler_BrickAlbedo);

// ---------- VAT (Vertex Animation Texture) для ECS-ботов ----------
// Текстура: X = индекс вершины, Y = кадр. Позиции нормализованы в [BoundsMin, BoundsMax].
float VatClipValue(float4 a, float4 b, int i) { return i < 4 ? a[i] : b[i - 4]; }

void ApplyVat(uint vertexID, inout float3 positionOS, inout float3 normalOS)
{
#if defined(_VAT)
    int   clip  = (int)round(_VatClip);
    float start = VatClipValue(_VatClipStartA, _VatClipStartB, clip);
    float len   = max(1.0, VatClipValue(_VatClipLenA, _VatClipLenB, clip));
    float f     = _VatTime * _VatFps;
    // Клип смерти (5) не зацикливается.
    f = clip == 5 ? min(f, len - 1.0) : fmod(f, len);
    float frame = start + f;
    float u = (vertexID + 0.5) / _VatVertexCount;
    float v0 = (floor(frame) + 0.5) / _VatFrameCount;
    float v1 = (min(floor(frame) + 1.0, start + len - 1.0) + 0.5) / _VatFrameCount;
    float t = frac(frame);
    float3 p0 = SAMPLE_TEXTURE2D_LOD(_VatPositionTex, sampler_VatPositionTex, float2(u, v0), 0).xyz;
    float3 p1 = SAMPLE_TEXTURE2D_LOD(_VatPositionTex, sampler_VatPositionTex, float2(u, v1), 0).xyz;
    positionOS = lerp(_VatBoundsMin.xyz, _VatBoundsMax.xyz, lerp(p0, p1, t));
    float3 n = SAMPLE_TEXTURE2D_LOD(_VatNormalTex, sampler_VatPositionTex, float2(u, v0), 0).xyz * 2.0 - 1.0;
    normalOS = normalize(n);
#endif
}

// ---------- Процедурные детали (без фотореализма) ----------
float Hash31(float3 p) { p = frac(p * 0.1031); p += dot(p, p.yzx + 33.33); return frac((p.x + p.y) * p.z); }

float ValueNoise(float3 p)
{
    float3 i = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(lerp(Hash31(i), Hash31(i + float3(1,0,0)), f.x), lerp(Hash31(i + float3(0,1,0)), Hash31(i + float3(1,1,0)), f.x), f.y),
                lerp(lerp(Hash31(i + float3(0,0,1)), Hash31(i + float3(1,0,1)), f.x), lerp(Hash31(i + float3(0,1,1)), Hash31(i + float3(1,1,1)), f.x), f.y), f.z);
}

half3 ApplyCamo(half3 albedo, half mask, float2 uv)
{
    half3 pat = SAMPLE_TEXTURE2D(_CamoPattern, sampler_CamoPattern, uv * 2.0).rgb;
    half3 camo = _CamoPrimary.rgb;
    camo = lerp(camo, _CamoSecondary.rgb, pat.r);
    camo = lerp(camo, _CamoTertiary.rgb, pat.g);
    // Альбедо модели служит «затенением» (швы, складки): умножаем на его яркость.
    half lum = dot(albedo, half3(0.3, 0.59, 0.11));
    return lerp(albedo, camo * saturate(lum * 2.0), saturate(mask * _CamoStrength));
}

#endif
