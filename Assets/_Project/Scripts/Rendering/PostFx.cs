using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Frontline
{
    /// <summary>
    /// Постобработка URP, собранная кодом: виньетка, ACES, контраст/насыщенность по карте, лёгкий bloom и зерно.
    /// Динамика: при уроне виньетка вспыхивает красным, при низком HP — пульсирует и мир обесцвечивается.
    /// </summary>
    public class PostFx : MonoBehaviour
    {
        public PlayerHealth health;
        Volume _vol; Vignette _vig; ColorAdjustments _ca; LensDistortion _lens; ChromaticAberration _chroma;
        DepthOfField _dof;
        Bloom _bloom; FilmGrain _grain; MotionBlur _blur; WhiteBalance _wb; SplitToning _split;
        float _baseContrast, _bc;
        float _baseVig = 0.45f, _flash, _baseSat;

        /// <summary>Работает ли постобработка URP (у рендерера есть PostProcessData). Если нет — виньетку рисует HUD.</summary>
        public static bool Available { get; private set; } = true;
        /// <summary>Текущая сила «красной» вспышки урона 0..1 (для HUD-виньетки).</summary>
        public static float DamageRed { get; private set; }

        static bool CheckAvailable()
        {
            try
            {
                var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                if (!asset) return false;
                var f = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var fi = typeof(UniversalRenderPipelineAsset).GetField("m_DefaultRendererIndex", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (f?.GetValue(asset) is ScriptableRendererData[] list && list.Length > 0)
                {
                    int idx = fi != null ? Mathf.Clamp((int)fi.GetValue(asset), 0, list.Length - 1) : 0;
                    if (list[idx] is UniversalRendererData urd) return urd.postProcessData != null;
                }
            }
            catch { }
            return true;
        }

        public static PostFx Create(Transform parent, MapTheme theme, TimeOfDay time)
        {
            Available = CheckAvailable();
            if (!Available) Debug.LogWarning("[Frontline] Постобработка URP выключена (нет PostProcessData у рендерера) — виньетка рисуется поверх HUD. " +
                                             "Исправление: меню Frontline → Graphics → Fix Post-Processing.");
            var go = new GameObject("PostFx");
            go.transform.SetParent(parent);
            var fx = go.AddComponent<PostFx>();
            fx._vol = go.AddComponent<Volume>();
            fx._vol.isGlobal = true; fx._vol.priority = 10;
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            fx._vol.sharedProfile = p;

            fx._vig = p.Add<Vignette>(true);
            fx._vig.intensity.value = fx._baseVig; fx._vig.smoothness.value = 0.5f; fx._vig.color.value = Color.black; fx._vig.rounded.value = false;

            var tm = p.Add<Tonemapping>(true); tm.mode.value = TonemappingMode.ACES;
            fx._ca = p.Add<ColorAdjustments>(true);
            // Приглушённая, немного мрачная картинка: цвета выцветшие, тени холодные с оливковым оттенком,
            // света тёплые, но тусклые, контраст выше, экспозиция чуть ниже
            fx._baseSat = theme == MapTheme.Winter ? -30f : -24f;
            fx._ca.contrast.value = 20f; fx._ca.saturation.value = fx._baseSat;
            fx._ca.postExposure.value = time == TimeOfDay.Evening ? -0.35f : -0.12f;
            fx._ca.colorFilter.value = time == TimeOfDay.Evening ? new Color(0.95f, 0.86f, 0.78f) : theme == MapTheme.Winter ? new Color(0.88f, 0.92f, 0.96f) : new Color(0.93f, 0.94f, 0.88f);
            var bloom = p.Add<Bloom>(true); bloom.intensity.value = 0.35f; bloom.threshold.value = 1.1f; bloom.scatter.value = 0.6f;
            var grain = p.Add<FilmGrain>(true); grain.intensity.value = 0.12f; grain.type.value = FilmGrainLookup.Thin1;
            fx._bloom = bloom; fx._grain = grain; fx._baseContrast = fx._ca.contrast.value;
            // Bodycam: смаз при резких поворотах, холодный баланс белого, тени в зелень, света в тёплое
            fx._blur = p.Add<MotionBlur>(true); fx._blur.mode.value = MotionBlurMode.CameraOnly; fx._blur.quality.value = MotionBlurQuality.Medium; fx._blur.intensity.value = 0f;
            fx._wb = p.Add<WhiteBalance>(true); fx._wb.temperature.value = 0f; fx._wb.tint.value = 0f;
            fx._split = p.Add<SplitToning>(true); fx._split.shadows.value = new Color(0.42f, 0.5f, 0.48f); fx._split.highlights.value = new Color(0.58f, 0.53f, 0.45f); fx._split.balance.value = -15f;
            // «Рыбий глаз» как в Polyfield (настройка), лёгкая хроматическая аберрация по краям при нём же
            fx._lens = p.Add<LensDistortion>(true); fx._lens.intensity.value = 0f; fx._lens.scale.value = 1f;
            fx._chroma = p.Add<ChromaticAberration>(true); fx._chroma.intensity.value = 0f;
            // Объём: тени глубже и холоднее, света чуть приглушены — картинка «плотнее», без пересвета
            var smh = p.Add<ShadowsMidtonesHighlights>(true);
            smh.shadows.value = new Vector4(0.94f, 0.97f, 1.0f, -0.04f);
            smh.midtones.value = new Vector4(1f, 1f, 0.97f, 0f);
            smh.highlights.value = new Vector4(1f, 0.98f, 0.94f, -0.03f);
            // В прицеле дальний фон слегка размывается (как глаз, сфокусированный на мушке/цели) — только ПК
            fx._dof = p.Add<DepthOfField>(true); fx._dof.mode.value = DepthOfFieldMode.Gaussian;
            fx._dof.gaussianStart.value = 90f; fx._dof.gaussianEnd.value = 420f; fx._dof.gaussianMaxRadius.value = 0.6f; fx._dof.highQualitySampling.value = false;
            fx._dof.active = false;
            return fx;
        }

        public void Bind(PlayerHealth h)
        {
            if (health) health.Damaged -= OnHit;
            health = h;
            if (health) health.Damaged += OnHit;
        }

        void OnHit(Vector3 dir, float dmg) => _flash = Mathf.Clamp01(_flash + dmg / 50f);

        void Update()
        {
            if (_vig == null) return;
            if (!health && GameModeManager.I && GameModeManager.I.Player) Bind(GameModeManager.I.Player.GetComponent<PlayerHealth>());
            _flash = Mathf.MoveTowards(_flash, 0, Time.deltaTime * 1.5f);
            float low = health ? Mathf.Clamp01(1f - health.Health / 22f) : 0;
            float pulse = low * (0.5f + 0.5f * Mathf.Sin(Time.time * 5f));
            float red = Mathf.Max(_flash, pulse);
            DamageRed = red;
            _vig.intensity.value = _baseVig + red * 0.25f;
            _vig.color.value = Color.Lerp(Color.black, new Color(0.55f, 0, 0), red);
            _ca.saturation.value = Mathf.Lerp(_baseSat, -60f, low);

            // Рыбий глаз: слабее в прицеле и в прицеле наводчика танка
            int level = SaveSystem.Settings != null ? SaveSystem.Settings.fisheye : 0;
            float want = level == 1 ? -0.24f : level == 2 ? -0.45f : 0f;
            var pc = Bots.PlayerBridge.Controller;
            if (pc)
            {
                if (pc.Vehicle && pc.Vehicle.Zoomed) want = 0f;
                else if (!pc.Vehicle) { var wc = pc.GetComponent<WeaponController>(); if (wc) want *= 1f - wc.Ads01 * 0.85f; }
            }
            // Bodycam: широкий объектив (бочка), хроматизм по краям, крупное зерно, пересвеченные блики, тяжёлая виньетка
            bool bodycam = SaveSystem.Settings != null && SaveSystem.Settings.bodycam;
            float adsK = 0f;
            if (pc && !pc.Vehicle) { var wc2 = pc.GetComponent<WeaponController>(); if (wc2) adsK = wc2.Ads01; }
            _bc = Mathf.MoveTowards(_bc, bodycam && !(pc && (pc.Vehicle || pc.Flying || pc.Gunning)) ? 1f : 0f, Time.deltaTime * 3f);
            if (_bc > 0) want = Mathf.Min(want, Mathf.Lerp(-0.34f, -0.12f, adsK));
            _lens.intensity.value = Mathf.Lerp(_lens.intensity.value, want, Time.deltaTime * 8f);
            _lens.scale.value = 1f + Mathf.Abs(_lens.intensity.value) * 0.3f;
            _chroma.intensity.value = Mathf.Abs(_lens.intensity.value) * 0.5f + red * 0.3f + _bc * 0.25f;
            var st = SaveSystem.Settings;
            float vigK = st != null ? st.vignette : 1f;
            _vig.intensity.value = (_baseVig + _bc * 0.08f + adsK * 0.08f) * vigK + red * 0.25f;
            _vig.smoothness.value = Mathf.Lerp(0.5f, 0.62f, _bc);
            _grain.type.value = _bc > 0.5f ? FilmGrainLookup.Medium3 : FilmGrainLookup.Thin1;
            _grain.intensity.value = Mathf.Lerp(0.12f, 0.42f, _bc);
            _grain.active = st == null || st.filmGrain;
            _bloom.active = st == null || st.bloom;
            _dof.active = !Application.isMobilePlatform && adsK > 0.6f && !(pc && pc.GetComponent<WeaponController>() && pc.GetComponent<WeaponController>().HasOptic);
            _chroma.active = st == null || st.chromatic;
            _bloom.intensity.value = Mathf.Lerp(0.35f, 0.75f, _bc); _bloom.threshold.value = Mathf.Lerp(1.1f, 0.92f, _bc);
            bool mb = SaveSystem.Settings == null || SaveSystem.Settings.motionBlur;
            _blur.intensity.value = mb ? Mathf.Lerp(0.14f, 0.2f, _bc) : 0f; _blur.clamp.value = 0.03f;   // ограничение — чтобы оружие у камеры не смазывалось
            _blur.active = mb;
            _wb.temperature.value = Mathf.Lerp(-4f, -7f, _bc); _wb.tint.value = Mathf.Lerp(3f, 5f, _bc);   // холодно-оливковый
            _split.balance.value = Mathf.Lerp(-15f, -25f, _bc);
            _split.active = true;
            _ca.contrast.value = _baseContrast + _bc * 10f;
            _ca.saturation.value = Mathf.Lerp(_baseSat, -60f, low) - _bc * 4f;   // бодикам больше не «сереет» картинку
        }

        void OnDestroy() { if (health) health.Damaged -= OnHit; }
    }
}
