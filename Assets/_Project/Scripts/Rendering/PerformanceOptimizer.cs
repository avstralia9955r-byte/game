using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Frontline
{
    /// <summary>
    /// Оптимизатор матча:
    /// • дальность теней, число каскадов, MSAA и дальность травы/деревьев — по пресету качества;
    /// • дальняя плоскость камеры под карту 1×1 км, сферическое отсечение, дальность отрисовки мелочи;
    /// • адаптивное разрешение (ПК и мобильные): render scale плавно снижается, если кадр не укладывается в целевой
    ///   FPS, и возвращается при запасе — без рывков и без лишних аллокаций;
    /// • физика 60 Гц, без автосинхронизации трансформов;
    /// • тени у мелких деталей и листвы на «Низком» выключены.
    /// </summary>
    public class PerformanceOptimizer : MonoBehaviour
    {
        public Camera worldCamera;
        public static float BotLodBase;     // базовая детализация ботов матча (выставляет GameFlow)
        float _scale = 1f, _accum; int _frames;
        float _checkTimer;
        UniversalRenderPipelineAsset _asset;

        void Start()
        {
            Time.fixedDeltaTime = 1f / 60f;
            Physics.autoSyncTransforms = false;
            Apply();
            SaveSystem.SettingsChanged += Apply;
        }

        void OnDestroy() => SaveSystem.SettingsChanged -= Apply;

        /// <summary>Глобальная часть (ассет URP, тени, сглаживание MSAA, SSAO, текстуры) — вызывается и из меню.</summary>
        public static void ApplyGlobal(GameSettingsData s)
        {
            if (s == null) return;
            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (asset)
            {
                asset.shadowDistance = s.shadowQuality switch { 0 => 0f, 1 => 50f, 2 => 90f, _ => 140f };
                asset.shadowCascadeCount = s.shadowQuality switch { 0 or 1 => 1, 2 => 2, _ => 4 };
                asset.msaaSampleCount = s.antiAliasing == 3 ? 4 : 1;
                asset.renderScale = Mathf.Clamp(s.renderScale, 0.5f, 2f);
                SetSsao(asset, s.ssao);
                if (!Application.isMobilePlatform) NiceShadows(asset, s.shadowQuality);
            }
            QualitySettings.globalTextureMipmapLimit = Mathf.Clamp(s.textureQuality, 0, 2);
            QualitySettings.lodBias = s.preset switch { GraphicsPreset.Low => 0.6f, GraphicsPreset.Medium => 0.9f, GraphicsPreset.High => 1.2f, _ => 1.6f };
        }

        public void Apply()
        {
            var s = SaveSystem.Settings;
            var p = s != null ? s.preset : GraphicsPreset.High;
            _asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            int sq = s != null ? s.shadowQuality : 3, aa = s != null ? s.antiAliasing : 2;
            if (_asset)
            {
                _asset.shadowDistance = sq switch { 0 => 0f, 1 => 50f, 2 => 90f, _ => 140f };
                _asset.shadowCascadeCount = sq switch { 0 or 1 => 1, 2 => 2, _ => 4 };
                _asset.msaaSampleCount = aa == 3 ? 4 : 1;
                _scale = s != null ? Mathf.Clamp(s.renderScale, 0.5f, 2f) : 1f;
                _asset.renderScale = _scale;
                SetSsao(_asset, s == null || s.ssao);
            }
            foreach (var l in FindObjectsByType<Light>())
                if (l.type == LightType.Directional) l.shadows = sq == 0 ? LightShadows.None : sq == 1 ? LightShadows.Hard : LightShadows.Soft;
            if (!Application.isMobilePlatform) NiceShadows(_asset, sq);
            GpuDriven(s);
            if (s != null) QualitySettings.globalTextureMipmapLimit = Mathf.Clamp(s.textureQuality, 0, 2);
            QualitySettings.anisotropicFiltering = p >= GraphicsPreset.High ? AnisotropicFiltering.ForceEnable : AnisotropicFiltering.Enable;
            if (worldCamera)
            {
                var acd = worldCamera.GetUniversalAdditionalCameraData();
                if (acd) acd.antialiasing = aa switch { 1 => AntialiasingMode.FastApproximateAntialiasing, 2 => AntialiasingMode.SubpixelMorphologicalAntiAliasing, _ => AntialiasingMode.None };
                if (acd && aa == 2) acd.antialiasingQuality = AntialiasingQuality.High;
            }
            QualitySettings.lodBias = p switch { GraphicsPreset.Low => 0.6f, GraphicsPreset.Medium => 0.9f, GraphicsPreset.High => 1.2f, _ => 1.6f };

            var t = Terrain.activeTerrain;
            if (t)
            {
                t.detailObjectDistance = p switch { GraphicsPreset.Low => 45f, GraphicsPreset.Medium => 75f, GraphicsPreset.High => 110f, _ => 150f };
                t.detailObjectDensity = s != null ? Mathf.Clamp01(s.grassDensity) : 1f;
                t.treeDistance = (s != null ? s.viewDistance : 2) switch { 0 => 300f, 1 => 500f, _ => 750f };
                t.treeBillboardDistance = p switch { GraphicsPreset.Low => 80f, GraphicsPreset.Medium => 120f, GraphicsPreset.High => 160f, _ => 220f };
                t.heightmapPixelError = p == GraphicsPreset.Low ? 8 : 4;
                t.basemapDistance = p == GraphicsPreset.Low ? 150 : 260;
            }

            if (worldCamera)
            {
                worldCamera.farClipPlane = (s != null ? s.viewDistance : 2) switch { 0 => 450f, 1 => 750f, _ => 1100f };
                worldCamera.layerCullSpherical = true;
                var d = new float[32];
                for (int i = 0; i < 32; i++) d[i] = 0;                     // 0 = дальняя плоскость
                d[2] = p == GraphicsPreset.Low ? 120f : 220f;             // обломки, доски, гильзы (Ignore Raycast)
                worldCamera.layerCullDistances = d;
            }

            // Телефон: короче тени и дальность, реже трава, грубее рельеф вдали — запас под 128 ботов
            if (Application.isMobilePlatform)
            {
                if (_asset) { _asset.shadowDistance = Mathf.Min(_asset.shadowDistance, 35f); _asset.shadowCascadeCount = 1; }
                if (t)
                {
                    t.detailObjectDistance = Mathf.Min(t.detailObjectDistance, 40f);
                    t.treeDistance = Mathf.Min(t.treeDistance, 260f); t.treeBillboardDistance = Mathf.Min(t.treeBillboardDistance, 60f);
                    t.heightmapPixelError = 12; t.basemapDistance = 100;
                    t.drawTreesAndFoliage = true;
                }
                if (worldCamera)
                {
                    worldCamera.farClipPlane = Mathf.Min(worldCamera.farClipPlane, 420f);
                    var d2 = worldCamera.layerCullDistances; d2[2] = 70f; worldCamera.layerCullDistances = d2;
                }
                QualitySettings.lodBias = Mathf.Min(QualitySettings.lodBias, 0.5f);
                Physics.defaultSolverIterations = 4;
                MobileProfile(s, t);
            }

            // На «Низком» листва и мелкие детали не отбрасывают тени
            if (p == GraphicsPreset.Low || Application.isMobilePlatform)
                foreach (var r in FindObjectsByType<MeshRenderer>())
                    if (r.name == "Foliage" || r.name == "Glass" || r.name == "Metal") r.shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>
        /// Телефон, цель — 60 к/с при 128 ботах (или 70 к/с при 64): без HDR и MSAA, жёсткие короткие тени,
        /// разрешение рендера не выше 80%, без постобработки на «Низком/Среднем», физика 50 Гц, меньше травы,
        /// одна попиксельная лампа, без отражений в реальном времени. Боты: дальние — проще и реже.
        /// </summary>
        void MobileProfile(GameSettingsData s, Terrain t)
        {
            var p = s != null ? s.preset : GraphicsPreset.Low;
            if (_asset)
            {
                _asset.supportsHDR = false;
                _asset.msaaSampleCount = 1;
                _asset.shadowDistance = Mathf.Min(_asset.shadowDistance, p <= GraphicsPreset.Medium ? 22f : 35f);
                _scale = Mathf.Min(_scale, p <= GraphicsPreset.Low ? 0.7f : 0.8f);
                _asset.renderScale = _scale;
                SetSsao(_asset, false);
            }
            foreach (var l in FindObjectsByType<Light>())
            {
                if (l.type == LightType.Directional) { if (l.shadows == LightShadows.Soft) l.shadows = LightShadows.Hard; }
                else l.renderMode = LightRenderMode.ForceVertex;
            }
            QualitySettings.pixelLightCount = 1;
            QualitySettings.realtimeReflectionProbes = false;
            QualitySettings.globalTextureMipmapLimit = Mathf.Max(QualitySettings.globalTextureMipmapLimit, 1);
            Time.fixedDeltaTime = 1f / 50f;
            if (t)
            {
                t.detailObjectDistance = Mathf.Min(t.detailObjectDistance, 28f);
                t.detailObjectDensity = Mathf.Min(t.detailObjectDensity, 0.35f);
                t.treeDistance = Mathf.Min(t.treeDistance, 220f);
                t.heightmapPixelError = 16;
            }
            if (worldCamera)
            {
                var acd = worldCamera.GetUniversalAdditionalCameraData();
                if (acd)
                {
                    acd.renderPostProcessing = p >= GraphicsPreset.High;      // полноэкранные проходы — самая дорогая часть на телефоне
                    acd.antialiasing = p >= GraphicsPreset.High ? AntialiasingMode.FastApproximateAntialiasing : AntialiasingMode.None;
                    acd.renderShadows = s == null || s.shadowQuality > 0;
                }
                worldCamera.farClipPlane = Mathf.Min(worldCamera.farClipPlane, 380f);
            }
            // Чем больше ботов, тем раньше дальние упрощаются (ИИ, анимация, движение через кадр)
            int bots = GameModeManager.I && GameModeManager.I.Config != null ? GameModeManager.I.Config.botCount : Frontline.Bots.BotRegistry.BotCount;
            BotLodBase = bots > 96 ? 0.38f : bots > 48 ? 0.45f : 0.55f;
            Frontline.Bots.BotLod.Scale = Mathf.Min(Frontline.Bots.BotLod.Scale, BotLodBase);
        }

        /// <summary>
        /// Способы ускорения без потери картинки (Unity 6 URP):
        /// • GPU Resident Drawer — одинаковые меши (дома, изгороди, ящики, детали техники) рисуются инстансингом через
        ///   BatchRendererGroup, CPU почти не тратит время на draw call'ы;
        /// • GPU occlusion culling — то, что закрыто домами и холмами, отсекает видеокарта, а не процессор;
        /// • STP-апскейлинг — при масштабе рендера &lt; 100% картинка восстанавливается временным апскейлером почти без
        ///   потери чёткости (рисуем меньше пикселей);
        /// • отсев совсем мелких на экране мешей (гильзы, болты) GPU-отрисовщиком.
        /// На телефоне (GLES/слабые GPU) — выключено: там уже работает свой профиль.
        /// </summary>
        void GpuDriven(GameSettingsData s)
        {
            if (!_asset) return;
            try
            {
                bool pc = !Application.isMobilePlatform && SystemInfo.supportsComputeShaders;
                SetGpuResidentDrawer(_asset, pc);
                _asset.gpuResidentDrawerEnableOcclusionCullingInCameras = pc;
                _asset.smallMeshScreenPercentage = pc ? 0.25f : 0f;
                _asset.upscalingFilter = _scale < 0.99f && pc ? UpscalingFilterSelection.STP : UpscalingFilterSelection.Auto;
            }
            catch (System.Exception e) { Debug.LogWarning("[Frontline] GPU-driven: " + e.Message); }
        }

        /// <summary>
        /// Режим GPU Resident Drawer задаём через отражение: тип перечисления лежит в сборке
        /// Unity.RenderPipelines.GPUDriven.Runtime, на которую сборка игры не ссылается (так не нужно править asmdef).
        /// </summary>
        public static void SetGpuResidentDrawer(object asset, bool on)
        {
            var p = asset?.GetType().GetProperty("gpuResidentDrawerMode");
            if (p == null || !p.CanWrite || !p.PropertyType.IsEnum) return;
            p.SetValue(asset, System.Enum.Parse(p.PropertyType, on ? "InstancedDrawing" : "Disabled"));
        }

        /// <summary>
        /// «Классные» тени на ПК: карта теней 4096 (на «Средних» 2048), 4 каскада с плотным ближним
        /// разбиением (чёткие тени от бойцов, техники и заборов вблизи), дальность 160 м, мягкие тени
        /// высокого качества (PCF 7×7), аккуратные смещения без «висящих» тeней. Поля ассета URP закрыты —
        /// пишем их через отражение, чтобы не зависеть от версии пакета.
        /// </summary>
        static void NiceShadows(UniversalRenderPipelineAsset asset, int sq)
        {
            if (!asset || sq < 2) return;
            try
            {
                const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                var t = typeof(UniversalRenderPipelineAsset);
                void Set(string name, object v)
                {
                    var f = t.GetField(name, F); if (f == null) return;
                    if (f.FieldType.IsEnum) v = System.Enum.ToObject(f.FieldType, v);
                    f.SetValue(asset, v);
                }
                Set("m_MainLightShadowmapResolution", sq >= 3 ? 4096 : 2048);
                Set("m_SoftShadowsSupported", true);
                Set("m_SoftShadowQuality", sq >= 3 ? 3 : 2);          // 3 = High, 2 = Medium
                Set("m_ShadowDepthBias", 0.6f);
                Set("m_ShadowNormalBias", 0.5f);
                asset.shadowDistance = sq >= 3 ? 160f : 100f;
                asset.shadowCascadeCount = sq >= 3 ? 4 : 2;
                if (sq >= 3) Set("m_Cascade4Split", new Vector3(0.05f, 0.15f, 0.42f));
                else Set("m_Cascade2Split", 0.22f);
            }
            catch (System.Exception e) { Debug.LogWarning("[Frontline] shadows: " + e.Message); }
            foreach (var l in FindObjectsByType<Light>())
            {
                if (l.type != LightType.Directional) continue;
                l.shadows = LightShadows.Soft;
                l.shadowStrength = 0.88f;
                var ad = l.GetComponent<UniversalAdditionalLightData>();
                var pq = ad ? ad.GetType().GetProperty("softShadowQuality") : null;
                if (pq != null && pq.CanWrite && pq.PropertyType.IsEnum) pq.SetValue(ad, System.Enum.ToObject(pq.PropertyType, sq >= 3 ? 3 : 2));
            }
        }

        /// <summary>SSAO — внутренняя фича рендерера URP: находим её через отражение и включаем/выключаем.</summary>
        static void SetSsao(UniversalRenderPipelineAsset asset, bool on)
        {
            try
            {
                var f = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (f?.GetValue(asset) is not ScriptableRendererData[] list) return;
                foreach (var d in list)
                    if (d) foreach (var feat in d.rendererFeatures)
                        if (feat && feat.GetType().Name == "ScreenSpaceAmbientOcclusion") feat.SetActive(on);
            }
            catch { }
        }

        // ---------------- Автоподбор графики ----------------
        // Уровни 0..4: при нехватке кадров 3 с подряд — ступень вниз, при большом запасе 10 с — ступень вверх.
        int _autoLevel = -1; float _slow, _fast;

        static readonly (int shadows, int aa, int view, float grass, bool ssao, float scale)[] Levels =
        {
            (0, 1, 0, 0.2f, false, 0.7f),
            (1, 1, 0, 0.4f, false, 0.8f),
            (2, 2, 1, 0.6f, false, 0.9f),
            (3, 2, 2, 0.85f, true, 1f),
            (3, 3, 2, 1f, true, 1f),
        };

        void AutoTune(float avg, float budget)
        {
            var s = SaveSystem.Settings;
            if (_autoLevel < 0) _autoLevel = Application.isMobilePlatform ? 1 : s.preset switch { GraphicsPreset.Low => 0, GraphicsPreset.Medium => 2, GraphicsPreset.High => 3, _ => 4 };
            if (avg > budget * 1.15f) { _slow += 0.75f; _fast = 0; } else if (avg < budget * 0.72f) { _fast += 0.75f; _slow = 0; } else { _slow = Mathf.Max(0, _slow - 0.75f); _fast = 0; }
            int lvl = _autoLevel;
            if (_slow >= 3f && lvl > 0) { lvl--; _slow = 0; }
            else if (_fast >= 10f && lvl < Levels.Length - 1) { lvl++; _fast = 0; }
            if (lvl == _autoLevel && _autoApplied) return;
            _autoLevel = lvl; _autoApplied = true;
            var L = Levels[lvl];
            // На ПК тени не опускаем ниже «Средних» — они почти не стоят CPU, а без них картинка плоская
            s.shadowQuality = Application.isMobilePlatform ? L.shadows : Mathf.Max(L.shadows, 2); s.antiAliasing = L.aa; s.viewDistance = L.view; s.grassDensity = L.grass; s.ssao = L.ssao;
            s.renderScale = L.scale; _scale = Mathf.Min(_scale, L.scale);
            s.preset = lvl <= 0 ? GraphicsPreset.Low : lvl <= 2 ? GraphicsPreset.Medium : lvl == 3 ? GraphicsPreset.High : GraphicsPreset.Ultra;
            Apply();
        }
        bool _autoApplied;

        void Update()
        {
            if (!worldCamera && GameModeManager.I && GameModeManager.I.Player) { worldCamera = GameModeManager.I.Player.worldCamera; Apply(); }
            var s = SaveSystem.Settings;
            if (s == null) return;
            _accum += Time.unscaledDeltaTime; _frames++;
            _checkTimer += Time.unscaledDeltaTime;
            if (_checkTimer < 0.75f) return;
            float avg = _accum / Mathf.Max(1, _frames);
            _accum = 0; _frames = 0; _checkTimer = 0;
            float target = s.targetFps > 0 ? s.targetFps : (Application.isMobilePlatform ? 60 : 120);
            float budget = 1f / target;
            // Адаптивная детализация ботов: не хватает кадров — дальние бойцы раньше упрощаются (анимация, ИИ),
            // запас появился — детализация возвращается к базовой
            if (BotLodBase <= 0) BotLodBase = Frontline.Bots.BotLod.Scale;
            float lod = Frontline.Bots.BotLod.Scale;
            if (avg > budget * 1.2f) lod = Mathf.Max(BotLodBase * 0.45f, lod - 0.06f);
            else if (avg < budget * 0.9f) lod = Mathf.Min(BotLodBase, lod + 0.03f);
            Frontline.Bots.BotLod.Scale = lod;
            if (s.autoGraphics) AutoTune(avg, budget);
            if (!s.dynamicResolution || !_asset) return;
            float max = Mathf.Clamp(s.renderScale, 0.5f, 2f), min = Mathf.Max(0.55f, max * 0.6f);
            if (avg > budget * 1.12f) _scale = Mathf.Max(min, _scale - 0.05f);
            else if (avg < budget * 0.85f) _scale = Mathf.Min(max, _scale + 0.025f);
            if (Mathf.Abs(_asset.renderScale - _scale) > 0.001f) _asset.renderScale = _scale;
        }
    }
}
