using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;

namespace Frontline
{
    /// <summary>
    /// Настройки: графика, звук, управление. ПК — переназначение клавиш (клавиатура/мышь),
    /// мобильные — чувствительность и редактор расположения сенсорных кнопок.
    /// </summary>
    public class SettingsMenu : MonoBehaviour
    {
        [Header("Графика")]
        public TMP_Dropdown presetDropdown, fpsDropdown;
        public Toggle vsyncToggle, ssaoToggle, dynResToggle;
        public Slider renderScaleSlider, fovSlider;
        [Header("Звук")]
        public AudioMixer mixer;
        public Slider masterSlider, sfxSlider, musicSlider;
        [Header("Управление")]
        public Slider mouseSensSlider, adsSensSlider, touchSensSlider;
        public Toggle invertYToggle;
        public Transform rebindListRoot; public RebindRow rebindRowPrefab;
        public Button resetBindingsButton, editTouchLayoutButton;
        public TouchLayoutEditor touchLayoutEditor;
        public GameObject pcControlsRoot, mobileControlsRoot;

        static readonly int[] PcFps = { 60, 144, 200, 240, 0 };
        static readonly int[] MobileFps = { 30, 60, 90, 120 };
        static readonly string[] Rebindable = { "Move:1", "Move:2", "Move:3", "Move:4", "Fire", "Aim", "Reload", "Jump", "Crouch", "Sprint", "BuildMode", "KnifeSpin", "Melee", "Interact", "Grenade" };

        void OnEnable()
        {
            var s = SaveSystem.Settings;
            bool mobile = Application.isMobilePlatform;
            pcControlsRoot.SetActive(!mobile);
            mobileControlsRoot.SetActive(mobile);

            Bind(presetDropdown, new List<string> { "Низкие", "Средние", "Высокие", "Максимальные" }, (int)s.preset, v => s.preset = (GraphicsPreset)v);
            var fps = mobile ? MobileFps : PcFps;
            var fpsNames = new List<string>(); foreach (var f in fps) fpsNames.Add(f == 0 ? "Без ограничения" : f + " FPS");
            Bind(fpsDropdown, fpsNames, Mathf.Max(0, System.Array.IndexOf(fps, s.targetFps)), v => s.targetFps = fps[v]);
            Bind(vsyncToggle, s.vSync, v => s.vSync = v);
            Bind(ssaoToggle, s.ssao, v => s.ssao = v);
            Bind(dynResToggle, s.dynamicResolution, v => s.dynamicResolution = v);
            dynResToggle.gameObject.SetActive(mobile);
            Bind(renderScaleSlider, 0.5f, 1.5f, s.renderScale, v => s.renderScale = v);
            Bind(fovSlider, 65f, 100f, s.fieldOfView, v => s.fieldOfView = v);

            Bind(masterSlider, 0, 1, s.masterVolume, v => { s.masterVolume = v; ApplyAudio(); });
            Bind(sfxSlider, 0, 1, s.sfxVolume, v => { s.sfxVolume = v; ApplyAudio(); });
            Bind(musicSlider, 0, 1, s.musicVolume, v => { s.musicVolume = v; ApplyAudio(); });

            Bind(mouseSensSlider, 0.1f, 5f, s.mouseSensitivity, v => s.mouseSensitivity = v);
            Bind(adsSensSlider, 0.2f, 1.5f, s.adsSensitivity, v => s.adsSensitivity = v);
            Bind(touchSensSlider, 0.2f, 3f, s.touchLookSensitivity, v => s.touchLookSensitivity = v);
            Bind(invertYToggle, s.invertY, v => s.invertY = v);

            if (!mobile) BuildRebindList();
            resetBindingsButton.onClick.RemoveAllListeners();
            resetBindingsButton.onClick.AddListener(() => { GameInput.ResetBindings(); BuildRebindList(); });
            editTouchLayoutButton.onClick.RemoveAllListeners();
            editTouchLayoutButton.onClick.AddListener(() => { if (touchLayoutEditor) touchLayoutEditor.BeginEdit(); });
        }

        void OnDisable() => SaveSystem.SaveSettings();

        void ApplyAudio()
        {
            if (!mixer) return;
            var s = SaveSystem.Settings;
            mixer.SetFloat("MasterVol", ToDb(s.masterVolume));
            mixer.SetFloat("SfxVol", ToDb(s.sfxVolume));
            mixer.SetFloat("MusicVol", ToDb(s.musicVolume));
        }

        static float ToDb(float v) => v <= 0.0001f ? -80f : Mathf.Log10(v) * 20f;

        void BuildRebindList()
        {
            for (int i = rebindListRoot.childCount - 1; i >= 0; i--) Destroy(rebindListRoot.GetChild(i).gameObject);
            foreach (var entry in Rebindable)
            {
                var parts = entry.Split(':');
                int idx = parts.Length > 1 ? int.Parse(parts[1]) : 0;
                var row = Instantiate(rebindRowPrefab, rebindListRoot);
                row.Init(parts[0], idx);
            }
        }

        static void Bind(TMP_Dropdown d, List<string> opts, int value, System.Action<int> set)
        {
            d.onValueChanged.RemoveAllListeners(); d.ClearOptions(); d.AddOptions(opts);
            d.SetValueWithoutNotify(value);
            d.onValueChanged.AddListener(v => { set(v); SaveSystem.SaveSettings(); });
        }

        static void Bind(Toggle t, bool value, System.Action<bool> set)
        {
            t.onValueChanged.RemoveAllListeners(); t.SetIsOnWithoutNotify(value);
            t.onValueChanged.AddListener(v => { set(v); SaveSystem.SaveSettings(); });
        }

        static void Bind(Slider sl, float min, float max, float value, System.Action<float> set)
        {
            sl.onValueChanged.RemoveAllListeners(); sl.minValue = min; sl.maxValue = max; sl.SetValueWithoutNotify(value);
            sl.onValueChanged.AddListener(v => set(v));   // сохранение — при закрытии меню, чтобы не писать файл на каждый пиксель
        }
    }
}
