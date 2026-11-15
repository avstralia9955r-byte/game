using UnityEngine;
using UnityEngine.Rendering;

namespace Frontline
{
    /// <summary>
    /// Время суток (день/вечер) и погода (лёгкий дождь, снег). Освещение: реалтайм-солнце с каскадными тенями +
    /// запечённый GI в Adaptive Probe Volumes с двумя сценариями освещения ("Day", "Evening") — это и даёт
    /// «читаемость уровня CS2» при низкой стоимости: мягкий отражённый свет без реалтайм-GI.
    /// </summary>
    public class EnvironmentController : MonoBehaviour
    {
        [System.Serializable]
        public class LightingPreset
        {
            public string apvScenario = "Day";
            public Vector3 sunEuler = new(50, 30, 0);
            public Color sunColor = new(1f, 0.96f, 0.88f);
            public float sunIntensity = 2.2f;
            public Color ambient = new(0.55f, 0.6f, 0.68f);
            public Color fogColor = new(0.72f, 0.78f, 0.84f);
            public float fogDensity = 0.0025f;
            public Material skybox;
            public VolumeProfile post;
        }

        public Light sun;
        public Volume globalVolume;
        public LightingPreset summerDay, summerEvening, winterDay, winterEvening;
        public ParticleSystem rain, snow;   // следуют за камерой, ~2–4k частиц, GPU-friendly
        public AudioSource rainLoop, windLoop;

        public void Apply(MapTheme theme, TimeOfDay time, Weather weather)
        {
            var p = theme == MapTheme.Winter
                ? (time == TimeOfDay.Day ? winterDay : winterEvening)
                : (time == TimeOfDay.Day ? summerDay : summerEvening);
            if (p == null) return;

            if (sun)
            {
                sun.transform.rotation = Quaternion.Euler(p.sunEuler);
                sun.color = p.sunColor; sun.intensity = p.sunIntensity;
                sun.shadows = LightShadows.Soft;
            }
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = p.ambient;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = p.fogColor;
            RenderSettings.fogDensity = p.fogDensity * (weather == Weather.Clear ? 1f : 1.8f);
            if (p.skybox) RenderSettings.skybox = p.skybox;
            if (globalVolume && p.post) globalVolume.sharedProfile = p.post;

#if UNITY_6000_0_OR_NEWER
            // Сценарии APV, запечённые в Lighting → Adaptive Probe Volumes.
            var apv = ProbeReferenceVolume.instance;
            if (apv != null && apv.isInitialized) apv.lightingScenario = p.apvScenario;
#endif
            if (weather == Weather.Clear && theme == MapTheme.Winter) weather = Weather.Snow; // зимой лёгкий снег по умолчанию
            if (rain) rain.gameObject.SetActive(weather == Weather.LightRain);
            if (snow) snow.gameObject.SetActive(weather == Weather.Snow);
            if (rainLoop) rainLoop.enabled = weather == Weather.LightRain;
            Shader.SetGlobalFloat("_Wetness", weather == Weather.LightRain ? 0.6f : 0f);
        }

        void LateUpdate()
        {
            // Осадки держатся вокруг камеры — не нужно покрывать всю карту.
            var cam = Camera.main;
            if (!cam) return;
            if (rain && rain.gameObject.activeSelf) rain.transform.position = cam.transform.position + Vector3.up * 8f;
            if (snow && snow.gameObject.activeSelf) snow.transform.position = cam.transform.position + Vector3.up * 8f;
        }
    }
}
