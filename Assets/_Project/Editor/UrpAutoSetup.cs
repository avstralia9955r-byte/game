#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Frontline.EditorTools
{
    /// <summary>
    /// Розовые материалы = проект на Built-in, а не на URP. При загрузке редактора создаёт URP-ассет
    /// (Assets/_Project/Settings) и назначает его в Graphics и во все уровни Quality. Повторно — меню
    /// Frontline → Fix Pink Materials (URP).
    /// </summary>
    [InitializeOnLoad]
    public static class UrpAutoSetup
    {
        const string Dir = "Assets/_Project/Settings";

        static UrpAutoSetup()
        {
            EditorApplication.delayCall += () =>
            {
                if (GraphicsSettings.defaultRenderPipeline == null) Fix();
                EnsureAmbientOcclusion();
                EnsurePostProcessing();
                // Сетевая игра на Android: сокетам нужно разрешение INTERNET
                PlayerSettings.Android.forceInternetPermission = true;
                // Только новый Input System: режим «Both» Android не поддерживает
                Frontline.EditorTools.BuildTools.SetNewInputSystemOnly();
            };
        }

        [MenuItem("Frontline/Fix Pink Materials (URP)")]
        public static void Fix()
        {
            if (!Directory.Exists(Dir)) { Directory.CreateDirectory(Dir); AssetDatabase.Refresh(); }

            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(Dir + "/URP_PC.asset");
            if (!asset)
            {
                var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, Dir + "/URP_PC_Renderer.asset");
                asset = UniversalRenderPipelineAsset.Create(renderer);
                asset.shadowDistance = 150f;
                asset.shadowCascadeCount = 4;
                asset.supportsHDR = true;
                asset.msaaSampleCount = 4;
                asset.renderScale = 1f;
                asset.useSRPBatcher = true;
                AssetDatabase.CreateAsset(asset, Dir + "/URP_PC.asset");
            }

            GraphicsSettings.defaultRenderPipeline = asset;
            int cur = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = asset;
            }
            QualitySettings.SetQualityLevel(cur, false);
            PlayerSettings.colorSpace = ColorSpace.Linear;
            AssetDatabase.SaveAssets();
            Debug.Log("[Frontline] URP назначен (Assets/_Project/Settings/URP_PC.asset). Розовые материалы должны исчезнуть.");
            EnsureAmbientOcclusion();
        }

        /// <summary>
        /// Рендерер, созданный кодом (ScriptableObject.CreateInstance), остаётся без PostProcessData — тогда URP
        /// молча отключает ВСЮ постобработку: нет виньетки, свечения, зерна, размытия, цветокоррекции.
        /// Подключаем стандартный PostProcessData из пакета URP ко всем рендерерам всех URP-ассетов проекта.
        /// </summary>
        [MenuItem("Frontline/Graphics/Fix Post-Processing (vignette, bloom)")]
        public static void EnsurePostProcessing()
        {
            try
            {
                PostProcessData ppd = AssetDatabase.LoadAssetAtPath<PostProcessData>("Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
                if (!ppd)
                    foreach (var g in AssetDatabase.FindAssets("t:PostProcessData"))
                    { ppd = AssetDatabase.LoadAssetAtPath<PostProcessData>(AssetDatabase.GUIDToAssetPath(g)); if (ppd) break; }
                if (!ppd) { Debug.LogWarning("[Frontline] PostProcessData не найден — постобработка может не работать."); return; }
                int fixedCount = 0;
                foreach (var g in AssetDatabase.FindAssets("t:UniversalRendererData"))
                {
                    var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(AssetDatabase.GUIDToAssetPath(g));
                    if (!data || data.postProcessData) continue;
                    data.postProcessData = ppd;
                    EditorUtility.SetDirty(data);
                    fixedCount++;
                }
                if (fixedCount > 0) { AssetDatabase.SaveAssets(); Debug.Log($"[Frontline] Постобработка включена в {fixedCount} рендерер(ах) URP — появятся виньетка, свечение, зерно."); }
            }
            catch (System.Exception e) { Debug.LogWarning("[Frontline] Не удалось подключить постобработку: " + e.Message); }
        }

        /// <summary>
        /// Добавляет в рендерер SSAO (затенение в углах, под крышами, у основания стен и кустов — картинка сразу «плотнее»).
        /// Класс фичи в URP внутренний, поэтому создаём его через отражение; при неудаче просто пишем предупреждение.
        /// </summary>
        [MenuItem("Frontline/Graphics/Add SSAO")]
        public static void EnsureAmbientOcclusion()
        {
            try
            {
                var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(Dir + "/URP_PC_Renderer.asset");
                if (!data) return;
                foreach (var f in data.rendererFeatures) if (f && f.GetType().Name == "ScreenSpaceAmbientOcclusion") return;
                var type = typeof(UniversalRendererData).Assembly.GetType("UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion");
                if (type == null) return;
                var feature = (ScriptableRendererFeature)ScriptableObject.CreateInstance(type);
                feature.name = "SSAO";
                AssetDatabase.AddObjectToAsset(feature, data);
                data.rendererFeatures.Add(feature);
                // Карта локальных ID фич — иначе URP предупреждает о несовпадении
                var so = new SerializedObject(data);
                var map = so.FindProperty("m_RendererFeatureMap");
                if (map != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId))
                {
                    map.arraySize++;
                    map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                EditorUtility.SetDirty(data);
                AssetDatabase.SaveAssets();
                Debug.Log("[Frontline] SSAO добавлен в рендерер URP.");
            }
            catch (System.Exception e) { Debug.LogWarning("[Frontline] SSAO не удалось добавить автоматически: " + e.Message); }
        }
    }
}
#endif
