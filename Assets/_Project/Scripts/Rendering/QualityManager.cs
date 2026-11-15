using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Frontline
{
    /// <summary>
    /// Применение графических настроек: URP-ассет по пресету (Low…Ultra, отдельные ассеты для ПК и Android),
    /// SSAO (переключаемая фича рендерера), цель FPS, VSync, render scale.
    /// На мобильных — адаптивное разрешение: render scale подстраивается под бюджет кадра (удержание 60/30 FPS).
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class QualityManager : MonoBehaviour
    {
        [Header("URP-ассеты: индекс = GraphicsPreset")]
        public UniversalRenderPipelineAsset[] pcAssets = new UniversalRenderPipelineAsset[4];
        public UniversalRenderPipelineAsset[] mobileAssets = new UniversalRenderPipelineAsset[4];
        [Tooltip("ScriptableRendererFeature SSAO в используемых рендерерах (переключается по настройке).")]
        public ScriptableRendererFeature[] ssaoFeatures;

        [Header("Динамическое разрешение (мобильные)")]
        public float minScale = 0.55f, maxScale = 1f, adjustStep = 0.05f, sampleWindow = 0.5f;

        static QualityManager I;
        float _accum; int _frames; float _scale = 1f;
        readonly FrameTiming[] _timings = new FrameTiming[1];

        void Awake()
        {
            if (I) { Destroy(gameObject); return; }
            I = this; DontDestroyOnLoad(gameObject);
            if (SaveSystem.Settings == null) SaveSystem.LoadAll();
            Apply();
            SaveSystem.SettingsChanged += Apply;
        }

        void OnDestroy() { if (I == this) SaveSystem.SettingsChanged -= Apply; }

        public void Apply()
        {
            var s = SaveSystem.Settings;
            var set = Application.isMobilePlatform ? mobileAssets : pcAssets;
            var asset = set != null && (int)s.preset < set.Length ? set[(int)s.preset] : null;
            if (asset)
            {
                GraphicsSettings.defaultRenderPipeline = asset;
                QualitySettings.renderPipeline = asset;
                asset.renderScale = Mathf.Clamp(s.renderScale, 0.5f, 2f);
            }
            _scale = asset ? asset.renderScale : 1f;

            QualitySettings.vSyncCount = s.vSync ? 1 : 0;
            // ПК: 200 FPS по умолчанию (0 = без ограничения). Мобильные: 30/60/90/120 по настройке.
            Application.targetFrameRate = s.vSync ? -1 : (s.targetFps <= 0 ? -1 : s.targetFps);
            if (ssaoFeatures != null) foreach (var f in ssaoFeatures) if (f) f.SetActive(s.ssao && s.preset >= GraphicsPreset.High);

            QualitySettings.lodBias = s.preset switch { GraphicsPreset.Low => 0.6f, GraphicsPreset.Medium => 0.9f, GraphicsPreset.High => 1.2f, _ => 1.6f };
            QualitySettings.globalTextureMipmapLimit = s.preset == GraphicsPreset.Low ? 1 : 0;
            QualitySettings.anisotropicFiltering = s.preset >= GraphicsPreset.High ? AnisotropicFiltering.ForceEnable : AnisotropicFiltering.Enable;
        }

        void Update()
        {
            var s = SaveSystem.Settings;
            if (!Application.isMobilePlatform || !s.dynamicResolution) return;
            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (!asset) return;

            // При ограничении FPS длительность кадра всегда ≈ бюджету, поэтому меряем реальную работу CPU/GPU
            // через FrameTimingManager (Player Settings → Frame Timing Stats = on). Fallback — deltaTime.
            FrameTimingManager.CaptureFrameTimings();
            float workMs = Time.unscaledDeltaTime * 1000f;
            if (FrameTimingManager.GetLatestTimings(1, _timings) > 0)
                workMs = (float)System.Math.Max(_timings[0].cpuFrameTime, _timings[0].gpuFrameTime);

            _accum += workMs; _frames++;
            if (_frames < 30) return;
            float avgMs = _accum / _frames;
            _accum = 0; _frames = 0;

            float budgetMs = 1000f / Mathf.Max(30, s.targetFps);
            if (avgMs > budgetMs * 1.08f) _scale = Mathf.Max(minScale, _scale - adjustStep);        // не успеваем — снижаем
            else if (avgMs < budgetMs * 0.8f) _scale = Mathf.Min(Mathf.Min(maxScale, s.renderScale), _scale + adjustStep * 0.5f); // запас — поднимаем медленно
            asset.renderScale = _scale;
        }
    }
}
