using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Frontline
{
    /// <summary>
    /// Главное меню: «Офлайн-игра», «Кастомизация», «Настройки», «Выход». Сетевых пунктов, модов и split-screen нет.
    /// Панель офлайн-игры: карта, режим, фракция, класс, число ботов (0–128), сложность, время суток, погода.
    /// </summary>
    public class MainMenu : MonoBehaviour
    {
        public GameObject rootPanel, offlinePanel, customizationPanel, settingsPanel;
        public Button offlineButton, customizationButton, settingsButton, exitButton, startButton, backButton;

        [Header("Офлайн-игра")]
        public TMP_Dropdown mapDropdown, modeDropdown, factionDropdown, classDropdown, difficultyDropdown, timeDropdown, weatherDropdown;
        public Toggle altPrimaryToggle;   // «FG-42 / АВС-36» для штурмовика
        public Slider botsSlider; public TMP_Text botsLabel;
        public Slider timeLimitSlider; public TMP_Text timeLimitLabel;
        public TMP_Text currencyText;

        void Awake()
        {
            SaveSystem.LoadAll();
            GameInput.Init();
            GameInput.SetEnabled(false);
        }

        void Start()
        {
            offlineButton.onClick.AddListener(() => Show(offlinePanel));
            customizationButton.onClick.AddListener(() => Show(customizationPanel));
            settingsButton.onClick.AddListener(() => Show(settingsPanel));
            exitButton.onClick.AddListener(Quit);
            startButton.onClick.AddListener(StartMatch);
            if (backButton) backButton.onClick.AddListener(() => Show(rootPanel));
            if (Application.platform == RuntimePlatform.Android) exitButton.gameObject.SetActive(true);

            var db = GameDatabase.Instance;
            mapDropdown.ClearOptions();
            mapDropdown.AddOptions(db.maps.ConvertAll(m => m.displayName));
            Fill(modeDropdown, "Territory Control", "Team Deathmatch");
            Fill(factionDropdown, "СССР", "Германия");
            Fill(classDropdown, "Штурмовик", "Инженер", "Поддержка", "Снайпер");
            Fill(difficultyDropdown, "Лёгкая", "Средняя", "Сложная", "Реалистичная");
            Fill(timeDropdown, "День", "Вечер");
            Fill(weatherDropdown, "Ясно", "Лёгкий дождь", "Снег");
            difficultyDropdown.value = 1;
            if (altPrimaryToggle)
            {
                classDropdown.onValueChanged.AddListener(v => altPrimaryToggle.gameObject.SetActive(v == (int)SoldierClass.Assault));
                altPrimaryToggle.gameObject.SetActive(classDropdown.value == (int)SoldierClass.Assault);
            }

            botsSlider.minValue = 0; botsSlider.maxValue = 512; botsSlider.wholeNumbers = true;
            botsSlider.value = Application.isMobilePlatform ? 48 : 128; // на слабых телефонах по умолчанию меньше
            botsSlider.onValueChanged.AddListener(v => botsLabel.SetText("Ботов: {0} ({1} на {2})", v, Mathf.Ceil(v / 2), Mathf.Floor(v / 2)));
            botsSlider.onValueChanged.Invoke(botsSlider.value);

            timeLimitSlider.minValue = 20; timeLimitSlider.maxValue = 30; timeLimitSlider.wholeNumbers = true; timeLimitSlider.value = 25;
            timeLimitSlider.onValueChanged.AddListener(v => timeLimitLabel.SetText("Время: {0} мин", v));
            timeLimitSlider.onValueChanged.Invoke(timeLimitSlider.value);

            Show(rootPanel);
        }

        static void Fill(TMP_Dropdown d, params string[] items) { d.ClearOptions(); d.AddOptions(new System.Collections.Generic.List<string>(items)); }

        void Show(GameObject panel)
        {
            foreach (var p in new[] { rootPanel, offlinePanel, customizationPanel, settingsPanel }) if (p) p.SetActive(p == panel);
            if (currencyText) currencyText.SetText("Деньги: {0}", (float)SaveSystem.Progress.currency);
        }

        void StartMatch()
        {
            var db = GameDatabase.Instance;
            var map = db.maps[mapDropdown.value];
            MatchSession.Current = new MatchConfig
            {
                map = map,
                mode = (GameModeType)modeDropdown.value,
                playerFaction = factionDropdown.value == 0 ? Faction.USSR : Faction.Germany,
                playerClass = (SoldierClass)classDropdown.value,
                useAltPrimary = altPrimaryToggle && altPrimaryToggle.isOn,
                botCount = (int)botsSlider.value,
                difficulty = (BotDifficulty)difficultyDropdown.value,
                timeOfDay = (TimeOfDay)timeDropdown.value,
                weather = (Weather)weatherDropdown.value,
                timeLimitMinutes = timeLimitSlider.value,
                scoreLimit = 1000,
                killLimit = 50
            };
            SceneManager.LoadScene(map.sceneName);
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
