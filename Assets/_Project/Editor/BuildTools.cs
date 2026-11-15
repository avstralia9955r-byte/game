#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace Frontline.EditorTools
{
    /// <summary>
    /// Frontline → Setup Project / Build. Настройка слоёв, Player Settings и сборки:
    /// • Windows x64: Mono (собирается без Visual Studio), DX12 → DX11, BC7. IL2CPP — отдельным пунктом меню,
    ///   ему нужен Visual Studio с компонентом «Разработка классических приложений на C++» и Windows SDK.
    /// • Android: IL2CPP, ARMv7 + ARM64, ASTC, Vulkan → GLES3, minSdk 26.
    /// Сетевых пакетов в проекте нет; разрешение INTERNET на Android включено — для кооператива по Wi-Fi (UDP).
    /// </summary>
    public static class BuildTools
    {
        static readonly string[] Scenes = { "Assets/_Project/Scenes/MainMenu.unity", "Assets/_Project/Scenes/Map_Normandy.unity", "Assets/_Project/Scenes/Map_Ardennes.unity" };
        const string BootScene = "Assets/_Project/Scenes/Boot.unity";

        /// <summary>
        /// Пустая стартовая сцена. Меню, карта и бои создаются кодом (GameFlow запускается после загрузки сцены),
        /// но в сборке должна быть хотя бы одна сцена — иначе игра стартует в пустоту и меню не появляется.
        /// </summary>
        static string EnsureBootScene()
        {
            if (File.Exists(BootScene)) return BootScene;
            Directory.CreateDirectory(Path.GetDirectoryName(BootScene));
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Additive);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, BootScene);
            UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
            AssetDatabase.Refresh();
            return BootScene;
        }

        [MenuItem("Frontline/Setup Project")]
        public static void Setup()
        {
            EnsureLayer(8, "World"); EnsureLayer(9, "ViewModel"); EnsureLayer(10, "Preview"); EnsureLayer(11, "Bricks");
            EnsureTag("CoverPoint");

            PlayerSettings.companyName = "Frontline";
            PlayerSettings.productName = "Polyfield Remaster";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.enableFrameTimingStats = true;   // для адаптивного разрешения (FrameTimingManager)
            PlayerSettings.gpuSkinning = true;
            // Нагрузка на все ядра: рендер-команды готовятся в рабочих потоках (graphics jobs), а не в главном
            PlayerSettings.graphicsJobs = true;
            PlayerSettings.MTRendering = true;
            SetupGpuDrivenRendering();
            SetNewInputSystemOnly();
            EnsureRuntimeShaders();

            // --- Windows ---
            var win = NamedBuildTarget.Standalone;
            // Mono: не требует компилятора C++ (ошибка «Could not set up a toolchain for Architecture x64» — это IL2CPP без VS)
            PlayerSettings.SetScriptingBackend(win, _winIl2Cpp ? ScriptingImplementation.IL2CPP : ScriptingImplementation.Mono2x);
            if (_winIl2Cpp) PlayerSettings.SetIl2CppCompilerConfiguration(win, Il2CppCompilerConfiguration.Master);
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D12, GraphicsDeviceType.Direct3D11 });
            PlayerSettings.SetManagedStrippingLevel(win, ManagedStrippingLevel.Low);

            // --- Android: ARM32 (ARMv7) + ARM64 ---
            var and = NamedBuildTarget.Android;
            PlayerSettings.SetScriptingBackend(and, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetIl2CppCompilerConfiguration(and, Il2CppCompilerConfiguration.Master);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARMv7 | AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.forceInternetPermission = true;    // нужно для кооператива по локальной сети (UDP)
            // Ландшафт и его коллайдер создаются из кода — вырезание движка (Strip Engine Code) у IL2CPP может выкинуть
            // модуль физики ландшафта, и игрок проваливается сквозь землю. Не вырезаем; плюс link.xml с нужными модулями.
            PlayerSettings.stripEngineCode = false;
            PlayerSettings.SetManagedStrippingLevel(and, ManagedStrippingLevel.Minimal);
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            // Entities Graphics (BatchRendererGroup) на Android требует Vulkan или GLES 3.1+.
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.SetMobileMTRendering(and, true);
            PlayerSettings.SetManagedStrippingLevel(and, ManagedStrippingLevel.Medium);
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;

            // Ориентация — только альбомная.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;

            AssetDatabase.SaveAssets();
            Debug.Log("[Frontline] Проект настроен. Проверьте вручную: Player → Active Input Handling = Input System Package (New).");
        }

        static bool _winIl2Cpp;

        [MenuItem("Frontline/Build/Windows x64")]
        public static void BuildWindows()
        {
            _winIl2Cpp = false;
            Build(BuildTarget.StandaloneWindows64, "Builds/Windows/PolyfieldRemaster.exe");
        }

        /// <summary>Чуть быстрее в игре, но нужен Visual Studio 2022 с C++ и Windows SDK.</summary>
        [MenuItem("Frontline/Build/Windows x64 (IL2CPP, нужен Visual Studio C++)")]
        public static void BuildWindowsIl2Cpp()
        {
            _winIl2Cpp = true;
            try { Build(BuildTarget.StandaloneWindows64, "Builds/Windows_IL2CPP/PolyfieldRemaster.exe"); }
            finally { _winIl2Cpp = false; }
        }

        [MenuItem("Frontline/Build/Android (ARMv7 + ARM64)")]
        public static void BuildAndroid()
        {
            EditorUserBuildSettings.buildAppBundle = false;
            Build(BuildTarget.Android, "Builds/Android/PolyfieldRemaster.apk");
        }

        [MenuItem("Frontline/Build/Android AAB (Google Play)")]
        public static void BuildAndroidAab()
        {
            EditorUserBuildSettings.buildAppBundle = true;
            Build(BuildTarget.Android, "Builds/Android/PolyfieldRemaster.aab");
        }

        static void Build(BuildTarget target, string path)
        {
            Setup();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var scenes = Scenes.Where(File.Exists).ToArray();
            if (scenes.Length == 0) scenes = new[] { EnsureBootScene() };
            EditorBuildSettings.scenes = scenes.Select(p => new EditorBuildSettingsScene(p, true)).ToArray();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes, locationPathName = path, target = target, options = BuildOptions.None
            });
            var s = report.summary;
            Debug.Log($"[Build] {target}: {s.result}, {s.totalSize / (1024 * 1024)} МБ, {s.totalTime}");
            if (s.result != BuildResult.Succeeded && Application.isBatchMode) EditorApplication.Exit(1);
        }

        // CLI: Unity -batchmode -quit -projectPath . -executeMethod Frontline.EditorTools.BuildTools.BuildAndroid

        /// <summary>
        /// Все материалы игры создаются кодом через Shader.Find. В сборку попадают только шейдеры, на которые ссылаются
        /// ассеты, — остальные вырезаются, и в собранной игре мир становится розовым или невидимым (в редакторе всё есть).
        /// Добавляем используемые шейдеры URP в «Always Included Shaders».
        /// </summary>
        [MenuItem("Frontline/Setup/Include Runtime Shaders")]
        /// <summary>
        /// GPU-driven отрисовка Unity 6: GPU Resident Drawer (автоматический инстансинг через BatchRendererGroup —
        /// резко меньше draw call'ов на CPU) и GPU occlusion culling. Требует Forward+ и сохранения всех вариантов
        /// шейдеров BatchRendererGroup. Картинка не меняется — меняется только способ, которым она отправляется на GPU.
        /// </summary>
        [MenuItem("Frontline/Graphics/Enable GPU-Driven Rendering")]
        public static void SetupGpuDrivenRendering()
        {
            // «BatchRendererGroup Variants = Keep All» (Project Settings → Graphics). Свойство в API только для чтения —
            // пишем напрямую в ProjectSettings/GraphicsSettings.asset (поле m_BrgStripping: 2 = Keep All).
            try
            {
                var gs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
                if (gs != null && gs.Length > 0)
                {
                    var so = new SerializedObject(gs[0]);
                    var prop = so.FindProperty("m_BrgStripping");
                    if (prop != null) { prop.intValue = (int)UnityEditor.Rendering.BatchRendererGroupStrippingMode.KeepAll; so.ApplyModifiedPropertiesWithoutUndo(); }
                    else Debug.LogWarning("[Frontline] Поле m_BrgStripping не найдено — выставьте вручную: Project Settings → Graphics → BatchRendererGroup Variants = Keep All.");
                }
            }
            catch (System.Exception e) { Debug.LogWarning("[Frontline] BRG stripping: " + e.Message); }
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset"))
            {
                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (!asset) continue;
                Frontline.PerformanceOptimizer.SetGpuResidentDrawer(asset, true);
                asset.gpuResidentDrawerEnableOcclusionCullingInCameras = true;
                EditorUtility.SetDirty(asset);
            }
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRendererData"))
            {
                var rd = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRendererData>(AssetDatabase.GUIDToAssetPath(guid));
                if (!rd) continue;
                rd.renderingMode = UnityEngine.Rendering.Universal.RenderingMode.ForwardPlus;
                EditorUtility.SetDirty(rd);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Frontline] GPU Resident Drawer + GPU occlusion culling + Forward+ включены.");
        }

        public static void EnsureRuntimeShaders()
        {
            string[] names =
            {
                "Universal Render Pipeline/Lit", "Universal Render Pipeline/Simple Lit", "Universal Render Pipeline/Unlit",
                "Universal Render Pipeline/Particles/Unlit", "Universal Render Pipeline/Terrain/Lit",
                "Hidden/TerrainEngine/Details/UniversalPipeline/WavingDoublePass", "Hidden/TerrainEngine/Details/UniversalPipeline/Vertexlit",
                "Hidden/TerrainEngine/Details/UniversalPipeline/BillboardWavingDoublePass",
            };
            var gs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (gs == null || gs.Length == 0) return;
            var so = new SerializedObject(gs[0]);
            var arr = so.FindProperty("m_AlwaysIncludedShaders");
            if (arr == null) return;
            bool changed = false;
            foreach (var n in names)
            {
                var sh = Shader.Find(n);
                if (!sh) continue;
                bool has = false;
                for (int i = 0; i < arr.arraySize; i++) if (arr.GetArrayElementAtIndex(i).objectReferenceValue == sh) { has = true; break; }
                if (has) continue;
                arr.InsertArrayElementAtIndex(arr.arraySize);
                arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = sh;
                changed = true;
            }
            if (changed) { so.ApplyModifiedProperties(); AssetDatabase.SaveAssets(); Debug.Log("[Setup] Шейдеры URP добавлены в Always Included Shaders."); }
        }

        /// <summary>
        /// Active Input Handling = «Input System Package (New)». Режим «Both» Android не поддерживает
        /// (окно «Unsupported Input Handling on Android»), а старый Input Manager в проекте не используется:
        /// весь ввод — Input System 1.20.1, интерфейс — InputSystemUIInputModule. Действует после перезапуска редактора.
        /// </summary>
        [MenuItem("Frontline/Setup/Use New Input System Only")]
        public static void SetNewInputSystemOnly()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (assets == null || assets.Length == 0) return;
            var so = new SerializedObject(assets[0]);
            var prop = so.FindProperty("activeInputHandler");
            if (prop == null || prop.intValue == 1) return;   // 0 — старый, 1 — новый, 2 — оба
            prop.intValue = 1;
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
            Debug.Log("[Setup] Active Input Handling → Input System Package (New). Перезапустите редактор, чтобы изменение вступило в силу.");
        }

        static void EnsureLayer(int index, string name)
        {
            var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tm.FindProperty("layers");
            var el = layers.GetArrayElementAtIndex(index);
            if (string.IsNullOrEmpty(el.stringValue)) { el.stringValue = name; tm.ApplyModifiedProperties(); }
            else if (el.stringValue != name) Debug.LogWarning($"[Setup] Слой {index} занят '{el.stringValue}', ожидался '{name}'");
        }

        static void EnsureTag(string tag)
        {
            var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var tags = tm.FindProperty("tags");
            for (int i = 0; i < tags.arraySize; i++) if (tags.GetArrayElementAtIndex(i).stringValue == tag) return;
            tags.InsertArrayElementAtIndex(tags.arraySize);
            tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = tag;
            tm.ApplyModifiedProperties();
        }
    }

    /// <summary>
    /// Импорт текстур: мипмапы всегда (кроме UI), ПК — BC7 (нормали BC5), Android — ASTC 6×6 (нормали/UI 4×4).
    /// </summary>
    public class TextureImportEnforcer : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/_Project")) return;
            var ti = (TextureImporter)assetImporter;
            bool ui = ti.textureType == TextureImporterType.Sprite || assetPath.Contains("/UI/");
            bool normal = ti.textureType == TextureImporterType.NormalMap;
            bool vat = assetPath.Contains("/VAT/");

            ti.mipmapEnabled = !ui && !vat;
            ti.streamingMipmaps = !ui && !vat;
            if (vat)
            {
                // VAT — точные данные: без сжатия, без фильтрации между вершинами.
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.filterMode = FilterMode.Point; ti.sRGBTexture = false; ti.npotScale = TextureImporterNPOTScale.None;
                return;
            }

            var pc = ti.GetPlatformTextureSettings("Standalone");
            pc.overridden = true;
            pc.format = normal ? TextureImporterFormat.BC5 : TextureImporterFormat.BC7;
            pc.maxTextureSize = ui ? 2048 : 2048;
            ti.SetPlatformTextureSettings(pc);

            var an = ti.GetPlatformTextureSettings("Android");
            an.overridden = true;
            an.format = normal || ui ? TextureImporterFormat.ASTC_4x4 : TextureImporterFormat.ASTC_6x6;
            an.maxTextureSize = ui ? 2048 : 1024;
            ti.SetPlatformTextureSettings(an);
        }
    }
}
#endif
