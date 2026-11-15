using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Frontline
{
    [Serializable]
    public class GameSettingsData
    {
        public int version = 1;

        // Графика
        public GraphicsPreset preset = GraphicsPreset.High;
        public int targetFps = 200;         // ПК: 0 = без ограничения
        public bool vSync = false;
        public int language = 0;
        public float shake = 1f;            // сила тряски экрана (0 — выкл, 4 — вчетверо сильнее)
        public int maxWrecks = 10;          // сколько уничтоженной техники может лежать одновременно            // 0 — русский, 1 — English
        public bool ssao = true;
        public bool autoGraphics = true;     // автоподбор качества под желаемый FPS (меню «Настройки → Графика»)
        // Подробные настройки графики (меню «ГРАФИКА»)
        public int shadowQuality = 3;        // 0 выкл, 1 низкие (жёсткие, 1 каскад), 2 средние, 3 высокие (мягкие, 4 каскада)
        public int antiAliasing = 2;         // 0 выкл, 1 FXAA, 2 SMAA, 3 MSAA 4x
        public bool bloom = true, filmGrain = true, chromatic = true;
        public float vignette = 1f;          // сила виньетки 0..1.5
        public int viewDistance = 2;         // 0 близко, 1 средне, 2 далеко
        public float grassDensity = 1f;      // 0..1
        public int textureQuality = 0;       // 0 полные, 1 половина
        public float renderScale = 1f;
        public bool dynamicResolution = true; // включается только на мобильных
        public float fieldOfView = 90f;     // по горизонтали
        public int fisheye = 0;              // 0 выкл, 1 слабый, 2 сильный (как в Polyfield)
        public bool thirdPerson = false;     // вид от третьего лица (H)
        public bool bodycam = true;
        public bool motionBlur = true;       // размытие в движении
        public bool heavyLook = true;        // инерция обзора: камера разгоняется и тормозит, тяжёлое оружие поворачивается медленнее          // камера «нагрудный регистратор»: широкий объектив, инерция, тряска шага, плёнка

        // Звук
        public float masterVolume = 1f, sfxVolume = 1f, musicVolume = 0.6f;

        // Управление
        public float mouseSensitivity = 1f, adsSensitivity = 0.8f;
        public bool invertY = false;
        public string keyBindingOverridesJson = ""; // InputActionAsset.SaveBindingOverridesAsJson()
        public float touchLookSensitivity = 1f;
        public List<TouchButtonLayout> touchLayout = new();
    }

    [Serializable]
    public class TouchButtonLayout
    {
        public string id;          // "fire", "aim", "reload", "build", "jump", "crouch", "knife"
        public Vector2 anchoredPos; // в долях экрана 0..1
        public float scale = 1f;
        public float opacity = 0.7f;
    }

    [Serializable]
    public class WeaponLoadoutData
    {
        public string weaponId;
        public List<string> installedAttachmentIds = new();
    }

    [Serializable]
    public class ProgressData
    {
        public int version = 2;
        public int currency;              // локальная валюта, без микротранзакций
        public int totalKills, matchesPlayed, matchesWon, bricksPlaced;
        public List<string> unlockedAttachmentIds = new();
        public List<string> achievementIds = new();
        public List<WeaponLoadoutData> loadouts = new();
        public List<string> primaryChoices = new();      // «сторона:класс=id» — выбранное основное оружие класса
        public List<string> ownedClasses = new();        // купленные классы (штурмовик — всегда)
        public List<string> ownedWeapons = new();        // купленное оружие (id)
        public List<string> ownedItems = new();          // прочее: "binoculars"

        public bool IsUnlocked(string attachmentId) => unlockedAttachmentIds.Contains(attachmentId);

        public WeaponLoadoutData GetLoadout(string weaponId)
        {
            var l = loadouts.Find(x => x.weaponId == weaponId);
            if (l == null) { l = new WeaponLoadoutData { weaponId = weaponId }; loadouts.Add(l); }
            return l;
        }
    }

    /// <summary>
    /// Локальные сохранения в Application.persistentDataPath.
    /// Запись атомарная: сначала во временный файл, затем замена — сбой посреди записи не портит сейв.
    /// </summary>
    public static class SaveSystem
    {
        const string SettingsFile = "settings.json";
        const string ProgressFile = "progress.json";

        public static GameSettingsData Settings { get; private set; }
        public static ProgressData Progress { get; private set; }

        public static event Action SettingsChanged;

        static string PathOf(string file) => Path.Combine(Application.persistentDataPath, file);

        public static void LoadAll()
        {
            Settings = Load<GameSettingsData>(SettingsFile) ?? CreateDefaultSettings();
            Progress = Load<ProgressData>(ProgressFile) ?? new ProgressData();
            Progress.ownedClasses ??= new(); Progress.ownedWeapons ??= new(); Progress.ownedItems ??= new(); Progress.primaryChoices ??= new();
            if (Progress.version < 2)
            {
                // Раньше новичку выдавали 3000 на старте — теперь стартового капитала нет, оружие и классы покупаются
                Progress.currency = Mathf.Max(0, Progress.currency - 3000);
                Progress.version = 2;
                SaveProgress();
            }
        }

        public static void SaveSettings() { Write(SettingsFile, Settings); SettingsChanged?.Invoke(); }
        public static void SaveProgress() => Write(ProgressFile, Progress);

        static GameSettingsData CreateDefaultSettings()
        {
            var s = new GameSettingsData();
            if (Application.isMobilePlatform)
            {
                // Телефон: лёгкий профиль, рассчитанный на 128 ботов при 60 к/с
                s.preset = GraphicsPreset.Low;
                s.targetFps = 60;
                s.ssao = false;
                s.renderScale = 0.75f;
                s.dynamicResolution = true;
                s.shadowQuality = 1; s.antiAliasing = 1; s.viewDistance = 0; s.grassDensity = 0.35f; s.textureQuality = 1;
                s.motionBlur = false; s.bloom = false; s.filmGrain = false; s.chromatic = false; s.vignette = 0.7f;
                s.heavyLook = false; s.fieldOfView = 85f;
            }
            return s;
        }

        static T Load<T>(string file) where T : class
        {
            string path = PathOf(file);
            try
            {
                if (!File.Exists(path)) return null;
                return JsonUtility.FromJson<T>(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Save] Повреждён {file}, используются значения по умолчанию: {e.Message}");
                try { File.Copy(path, path + ".corrupt", true); } catch { /* ignore */ }
                return null;
            }
        }

        static void Write<T>(string file, T data)
        {
            string path = PathOf(file);
            string tmp = path + ".tmp";
            try
            {
                File.WriteAllText(tmp, JsonUtility.ToJson(data, true));
                // File.Replace ненадёжен на Android/IL2CPP — копируем поверх и удаляем tmp.
                File.Copy(tmp, path, true);
                File.Delete(tmp);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Save] Не удалось записать {file}: {e.Message}");
            }
        }
    }
}
