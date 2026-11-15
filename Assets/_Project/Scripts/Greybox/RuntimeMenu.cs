using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace Frontline
{
    /// <summary>
    /// Главное меню, собранное кодом (uGUI, без префабов и TMP): заставка с 3D-оружием на поворотном столе,
    /// «Офлайн-игра» (карты-карточки, режим, фракция, класс, боты, сложность, время, погода), «Кастомизация»
    /// (3D-превью с вращением мышью/пальцем, покупка и установка обвесов, ДО/ПОСЛЕ, сравнение характеристик),
    /// «Настройки» (графика, звук, управление, переназначение клавиш), «Выход».
    /// </summary>
    public class RuntimeMenu : MonoBehaviour
    {
        // Палитра
        static readonly Color Bg = new(0.06f, 0.07f, 0.055f), Panel = new(0.1f, 0.11f, 0.09f, 0.94f), PanelLight = new(0.16f, 0.17f, 0.14f, 0.95f),
            Accent = new(0.83f, 0.69f, 0.38f), TextCol = new(0.92f, 0.9f, 0.84f), Muted = new(0.6f, 0.59f, 0.53f),
            Good = new(0.5f, 0.85f, 0.45f), Bad = new(0.9f, 0.4f, 0.35f);

        static Font _font;
        static Font F => _font ? _font : (_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

        GameFlow _flow;
        RectTransform _content, _navRoot;
        readonly List<GameObject> _chrome = new();   // заголовок, навигация, профиль — прячутся в полноэкранной кастомизации

        /// <summary>Кастомизация во весь экран: заголовок и навигация скрываются, оружие — по центру и мельче.</summary>
        void SetFullscreen(bool on)
        {
            foreach (var g in _chrome) if (g) g.SetActive(!on);
            if (_content)
            {
                _content.offsetMin = on ? new Vector2(16, 16) : new Vector2(600, 90);
                _content.offsetMax = on ? new Vector2(-16, -16) : new Vector2(-70, -90);
            }
            if (_stage) _stage.Centered = on;
        }
        readonly List<(Button b, Image bar)> _nav = new();
        MenuStage _stage;
        Text _profile;
        static AudioSource _ui;

        public static GameObject Create(GameFlow flow)
        {
            var go = new GameObject("[MainMenu]");
            var m = go.AddComponent<RuntimeMenu>();
            m._flow = flow;
            m.Build();
            return go;
        }

        public static void ShowLoading(GameObject menuRoot, string text)
        {
            if (!menuRoot) return;
            var canvas = menuRoot.GetComponentInChildren<Canvas>();
            var o = Img(canvas.transform, "Loading", new Color(0, 0, 0, 0.92f)); Stretch(o.rectTransform);
            var t = Label(o.transform, text, 44, Accent, TextAnchor.MiddleCenter, FontStyle.Bold); Stretch(t.rectTransform);
            var h = Label(o.transform, "Рельеф, леса, деревни, бокаж, навигация ботов…", 22, Muted, TextAnchor.MiddleCenter);
            Place(h.rectTransform, 0.5f, 0.42f, 900, 40);
        }

        // ============================ Каркас ============================

        void Build()
        {
            EnsureEventSystem();
            // Музыка меню и звуки интерфейса
            var music = gameObject.AddComponent<AudioSource>();
            _music = music;
            music.clip = SoundSynth.MenuMusic(); music.loop = true; music.volume = 0.45f * SaveSystem.Settings.musicVolume; music.spatialBlend = 0; music.Play();
            _ui = gameObject.AddComponent<AudioSource>(); _ui.spatialBlend = 0; _ui.volume = 0.6f;
            _stage = MenuStage.Create(transform);

            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform);
            var canvas = canvasGo.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 10;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 0.5f;

            // Фон: 3D-сцена (рендер-текстура) + затемнение слева для читаемости
            var bg = Img(canvasGo.transform, "Bg", Bg); Stretch(bg.rectTransform);
            var view = new GameObject("StageView", typeof(RawImage)).GetComponent<RawImage>();
            view.transform.SetParent(canvasGo.transform, false); view.texture = _stage.Texture; Stretch(view.rectTransform);
            view.gameObject.AddComponent<DragRotate>().stage = _stage;
            var grad = new GameObject("Gradient", typeof(RawImage)).GetComponent<RawImage>();
            grad.transform.SetParent(canvasGo.transform, false); grad.texture = Gradient(); grad.raycastTarget = false; Stretch(grad.rectTransform);

            // Заголовок
            var title = Label(canvasGo.transform, "POLYFIELD", 92, TextCol, TextAnchor.UpperLeft, FontStyle.Bold);
            Place(title.rectTransform, 0, 1, 700, 110, new Vector2(80, -60), new Vector2(0, 1));
            var sub = Label(canvasGo.transform, "R E M A S T E R", 34, Accent, TextAnchor.UpperLeft, FontStyle.Bold);
            Place(sub.rectTransform, 0, 1, 700, 50, new Vector2(86, -165), new Vector2(0, 1));
            var tag = Label(canvasGo.transform, "Офлайн  ·  до 512 ботов  ·  Нормандия, Арденны и Ржев 1×1 км", 20, Muted, TextAnchor.UpperLeft);
            Place(tag.rectTransform, 0, 1, 800, 30, new Vector2(88, -215), new Vector2(0, 1));
            _chrome.Add(title.gameObject); _chrome.Add(sub.gameObject); _chrome.Add(tag.gameObject); _chrome.Add(grad.gameObject);

            // Навигация
            _navRoot = NewRect(canvasGo.transform, "Nav");
            Place(_navRoot, 0, 0.5f, 460, 560, new Vector2(70, -40), new Vector2(0, 0.5f));
            NavButton("НОВАЯ ИГРА", 0, ShowOffline);
            NavButton("ПОДКЛЮЧИТЬСЯ К ИГРЕ", 1, ShowLan, 28);
            NavButton("КАСТОМИЗАЦИЯ", 2, ShowCustomization);
            NavButton("НАСТРОЙКИ", 3, ShowSettings);      // графика — вкладка внутри настроек
            NavButton("ВЫХОД", 4, Quit);

            _profile = Label(canvasGo.transform, "", 20, Muted, TextAnchor.LowerLeft);
            Place(_profile.rectTransform, 0, 0, 700, 90, new Vector2(88, 50), new Vector2(0, 0));
            UpdateProfile();
            _chrome.Add(_navRoot.gameObject); _chrome.Add(_profile.gameObject);

            _content = NewRect(canvasGo.transform, "Content");
            _content.anchorMin = new Vector2(0, 0); _content.anchorMax = new Vector2(1, 1);
            _content.offsetMin = new Vector2(600, 90); _content.offsetMax = new Vector2(-70, -90);

            var ver = Label(canvasGo.transform, "v0.3 · офлайн-сборка", 16, new Color(1, 1, 1, 0.3f), TextAnchor.LowerRight);
            Place(ver.rectTransform, 1, 0, 400, 30, new Vector2(-30, 20), new Vector2(1, 0));

            _stage.ShowWeapon(GameDatabase.Instance.Arsenal(Faction.USSR).Get(WeaponCategory.SubmachineGun), null);
        }

        AudioSource _music;
        public void SetMusicVolume(float v) { if (_music) _music.volume = 0.45f * v; }

        // ============================ Графика ============================

        public static void ApplyPreset(GameSettingsData s, GraphicsPreset p)
        {
            s.preset = p;
            switch (p)
            {
                case GraphicsPreset.Low: s.shadowQuality = 1; s.antiAliasing = 1; s.ssao = false; s.viewDistance = 0; s.grassDensity = 0.35f; s.textureQuality = 1; s.renderScale = 0.85f; s.motionBlur = false; s.filmGrain = false; s.bloom = false; break;
                case GraphicsPreset.Medium: s.shadowQuality = 2; s.antiAliasing = 1; s.ssao = false; s.viewDistance = 1; s.grassDensity = 0.65f; s.textureQuality = 0; s.renderScale = 1f; s.motionBlur = true; s.filmGrain = true; s.bloom = true; break;
                case GraphicsPreset.High: s.shadowQuality = 3; s.antiAliasing = 2; s.ssao = true; s.viewDistance = 2; s.grassDensity = 1f; s.textureQuality = 0; s.renderScale = 1f; s.motionBlur = true; s.filmGrain = true; s.bloom = true; break;
                default: s.shadowQuality = 3; s.antiAliasing = 3; s.ssao = true; s.viewDistance = 2; s.grassDensity = 1f; s.textureQuality = 0; s.renderScale = 1.25f; s.motionBlur = true; s.filmGrain = true; s.bloom = true; break;
            }
        }

        void ShowGraphics()
        {
            ClearContent();
            SetFullscreen(true);
            _stage.Visible = false;
            var s = SaveSystem.Settings;
            var panel = Img(_content, "Panel", Panel); Stretch(panel.rectTransform);
            Header(panel.transform, "НАСТРОЙКИ · ГРАФИКА", "Пресет задаёт все параметры сразу; любой можно поменять отдельно. Сохраняется автоматически.");
            _setTab = 1;
            SettingsTabs(panel.transform);

            float y = -120, y2 = -120;
            const float c2 = 920;
            SubHeader(panel.transform, "Качество и производительность", ref y);
            // Автоподбор: в бою качество само подстраивается под желаемый FPS (ручные параметры ниже он меняет сам)
            Cycler2(panel.transform, "Автоподбор графики", new[] { "Выкл", "Вкл" }, s.autoGraphics ? 1 : 0, v => { s.autoGraphics = v == 1; ApplySettings(); }, ref y);
            int[] fpsOpt = { 40, 60, 80, 100, 120, 144, 165, 240, 0 };   // 0 — без ограничения
            int fi = 0; for (int i = 1; i < fpsOpt.Length; i++) if (Mathf.Abs(fpsOpt[i] - s.targetFps) < Mathf.Abs(fpsOpt[fi] - s.targetFps)) fi = i;
            Cycler2(panel.transform, "Желаемый FPS", Array.ConvertAll(fpsOpt, f => f > 0 ? f.ToString() : "без ограничения"), fi, v => { s.targetFps = fpsOpt[v]; ApplySettings(); }, ref y);
            Cycler2(panel.transform, "Пресет", new[] { "Низкий", "Средний", "Высокий", "Ультра" }, (int)s.preset, v => { s.autoGraphics = false; ApplyPreset(s, (GraphicsPreset)v); ApplySettings(); ShowGraphics(); }, ref y);
            Slider2(panel.transform, "Масштаб рендера", 0.5f, 1.5f, s.renderScale, false, v => { s.renderScale = v; ApplySettings(); }, v => $"{v * 100:0}%", ref y);
            Cycler2(panel.transform, "Динам. разрешение", new[] { "Выкл", "Вкл" }, s.dynamicResolution ? 1 : 0, v => { s.dynamicResolution = v == 1; ApplySettings(); }, ref y);
            Cycler2(panel.transform, "Верт. синхронизация", new[] { "Выкл", "Вкл" }, s.vSync ? 1 : 0, v => { s.vSync = v == 1; ApplySettings(); }, ref y);
            Cycler2(panel.transform, "Сглаживание", new[] { "Выкл", "FXAA", "SMAA", "MSAA 4x" }, Mathf.Clamp(s.antiAliasing, 0, 3), v => { s.antiAliasing = v; ApplySettings(); }, ref y);
            Cycler2(panel.transform, "Тени", new[] { "Выкл", "Низкие", "Средние", "Высокие" }, Mathf.Clamp(s.shadowQuality, 0, 3), v => { s.shadowQuality = v; ApplySettings(); }, ref y);
            Cycler2(panel.transform, "Затенение (SSAO)", new[] { "Выкл", "Вкл" }, s.ssao ? 1 : 0, v => { s.ssao = v == 1; ApplySettings(); }, ref y);
            Cycler2(panel.transform, "Текстуры", new[] { "Полные", "Половина" }, Mathf.Clamp(s.textureQuality, 0, 1), v => { s.textureQuality = v; ApplySettings(); }, ref y);
            Cycler2(panel.transform, "Дальность прорисовки", new[] { "Близко", "Средне", "Далеко" }, Mathf.Clamp(s.viewDistance, 0, 2), v => { s.viewDistance = v; ApplySettings(); }, ref y);
            Slider2(panel.transform, "Плотность травы", 0f, 1f, s.grassDensity, false, v => { s.grassDensity = v; ApplySettings(); }, v => $"{v * 100:0}%", ref y);

            SubHeader(panel.transform, "Камера и эффекты", ref y2, c2);
            Slider2(panel.transform, "Поле зрения (гориз.)", 70, 110, s.fieldOfView, true, v => { s.fieldOfView = v; ApplySettings(); }, v => $"{v:0}°", ref y2, c2);
            Cycler2(panel.transform, "Камера", new[] { "Нагрудная", "Обычная" }, s.bodycam ? 0 : 1, v => { s.bodycam = v == 0; ApplySettings(); }, ref y2, c2);
            Cycler2(panel.transform, "Рыбий глаз", new[] { "Выкл", "Слабый", "Сильный" }, Mathf.Clamp(s.fisheye, 0, 2), v => { s.fisheye = v; ApplySettings(); }, ref y2, c2);
            Cycler2(panel.transform, "Размытие движения", new[] { "Выкл", "Вкл" }, s.motionBlur ? 1 : 0, v => { s.motionBlur = v == 1; ApplySettings(); }, ref y2, c2);
            Cycler2(panel.transform, "Свечение (bloom)", new[] { "Выкл", "Вкл" }, s.bloom ? 1 : 0, v => { s.bloom = v == 1; ApplySettings(); }, ref y2, c2);
            Cycler2(panel.transform, "Зерно плёнки", new[] { "Выкл", "Вкл" }, s.filmGrain ? 1 : 0, v => { s.filmGrain = v == 1; ApplySettings(); }, ref y2, c2);
            Cycler2(panel.transform, "Хроматизм", new[] { "Выкл", "Вкл" }, s.chromatic ? 1 : 0, v => { s.chromatic = v == 1; ApplySettings(); }, ref y2, c2);
            Slider2(panel.transform, "Виньетка", 0f, 1.5f, s.vignette, false, v => { s.vignette = v; ApplySettings(); }, v => $"{v * 100:0}%", ref y2, c2);
            Slider2(panel.transform, "Тряска экрана", 0f, 4f, s.shake, false, v => { s.shake = v; ApplySettings(); }, v => v < 0.01f ? "выкл" : $"{v * 100:0}%", ref y2, c2);
            Slider2(panel.transform, "Обломки техники (макс.)", 0, 40, s.maxWrecks, true, v => { s.maxWrecks = Mathf.RoundToInt(v); ApplySettings(); }, v => $"{v:0}", ref y2, c2);
        }

        static void Cycler2(Transform parent, string label, string[] options, int value, Action<int> set, ref float y, float x = 40)
        {
            var l = Label(parent, label, 20, Muted, TextAnchor.MiddleLeft);
            Place(l.rectTransform, 0, 1, 230, 44, new Vector2(x, y), new Vector2(0, 1));
            int idx = Mathf.Clamp(value, 0, options.Length - 1);
            var val = Label(parent, options[idx], 20, TextCol, TextAnchor.MiddleCenter, FontStyle.Bold);
            Place(val.rectTransform, 0, 1, 220, 44, new Vector2(x + 284, y), new Vector2(0, 1));
            void Step(int d) { idx = (idx + d + options.Length) % options.Length; val.text = options[idx]; set(idx); }
            var prev = Btn(parent, "<", 20, () => Step(-1), true); Place((RectTransform)prev.transform, 0, 1, 44, 44, new Vector2(x + 236, y), new Vector2(0, 1));
            var next = Btn(parent, ">", 20, () => Step(1), true); Place((RectTransform)next.transform, 0, 1, 44, 44, new Vector2(x + 508, y), new Vector2(0, 1));
            y -= 52;
        }

        static void Slider2(Transform parent, string label, float min, float max, float value, bool whole, Action<float> set, Func<float, string> fmt, ref float y, float x = 40)
        {
            var l = Label(parent, label, 20, Muted, TextAnchor.MiddleLeft);
            Place(l.rectTransform, 0, 1, 230, 44, new Vector2(x, y), new Vector2(0, 1));
            var go = DefaultControls.CreateSlider(new DefaultControls.Resources());
            go.transform.SetParent(parent, false);
            var sl = go.GetComponent<Slider>();
            sl.minValue = min; sl.maxValue = max; sl.wholeNumbers = whole; sl.value = value;
            Place((RectTransform)go.transform, 0, 1, 340, 22, new Vector2(x + 240, y - 11), new Vector2(0, 1));
            foreach (var img in go.GetComponentsInChildren<Image>())
                img.color = img.name == "Background" ? new Color(1, 1, 1, 0.12f) : img.name == "Fill" ? Accent : TextCol;
            var val = Label(parent, fmt(value), 20, TextCol, TextAnchor.MiddleLeft);
            Place(val.rectTransform, 0, 1, 110, 44, new Vector2(x + 592, y), new Vector2(0, 1));
            sl.onValueChanged.AddListener(v => { val.text = fmt(v); set(v); });
            y -= 52;
        }

        void NavButton(string text, int index, Action onClick, int size = 34)
        {
            var b = Btn(_navRoot, text, size, () => { Select(index); onClick(); }, false);
            var rt = (RectTransform)b.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
            rt.sizeDelta = new Vector2(440, 76); rt.anchoredPosition = new Vector2(0, -index * 90);
            b.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;
            var trt = b.GetComponentInChildren<Text>().rectTransform; trt.offsetMin = new Vector2(28, 0);
            var bar = Img(b.transform, "Bar", Accent); bar.rectTransform.anchorMin = new Vector2(0, 0.15f); bar.rectTransform.anchorMax = new Vector2(0, 0.85f);
            bar.rectTransform.sizeDelta = new Vector2(6, 0); bar.rectTransform.anchoredPosition = new Vector2(3, 0); bar.enabled = false;
            _nav.Add((b, bar));
        }

        void Select(int index)
        {
            for (int i = 0; i < _nav.Count; i++)
            {
                _nav[i].bar.enabled = i == index;
                _nav[i].b.GetComponentInChildren<Text>().color = i == index ? Accent : TextCol;
            }
        }

        void ClearContent()
        {
            for (int i = _content.childCount - 1; i >= 0; i--) Destroy(_content.GetChild(i).gameObject);
            SetFullscreen(false);
        }

        void UpdateProfile()
        {
            var p = SaveSystem.Progress;
            _profile.text = $"Деньги: <color=#D4B060>{p.currency}</color>    Матчей: {p.matchesPlayed}    Побед: {p.matchesWon}    Убийств: {p.totalKills}";
            _profile.supportRichText = true;
        }

        // ============================ Офлайн-игра ============================

        int _map, _mode, _diff = 1, _time, _weather; float _bots = 64, _minutes = 20, _tanks = 2, _planes = 2, _guns = 2;
        bool _lan;

        // ============================ По сети (LAN) ============================

        string _ip = "", _lanStatus = "";
        RectTransform _lanList;
        Text _lanStatusText;
        float _lanRefresh;

        void ShowLan()
        {
            ClearContent();
            _stage.Visible = false;
            Select(1);
            Net.NetDriver.StartDiscovery();
            var panel = Img(_content, "Panel", Panel); Stretch(panel.rectTransform);
            Header(panel.transform, "ИГРА ПО СЕТИ", "Кооператив по кабелю или в одной сети Wi-Fi. Хост: «Новая игра» → «Сетевая игра: открыть». " +
                                                   "Остальные подключаются здесь. Порты UDP 27015–27016 (разрешите игру в брандмауэре).");
            var cap = Label(panel.transform, "Найденные игры:", 24, Accent, TextAnchor.UpperLeft, FontStyle.Bold);
            Place(cap.rectTransform, 0, 1, 800, 34, new Vector2(40, -130), new Vector2(0, 1));
            _lanList = NewRect(panel.transform, "List");
            Place(_lanList, 0, 1, 900, 330, new Vector2(40, -170), new Vector2(0, 1));

            var ipCap = Label(panel.transform, "Или адрес хоста (IP):", 22, TextCol, TextAnchor.MiddleLeft);
            Place(ipCap.rectTransform, 0, 0, 300, 50, new Vector2(40, 170), new Vector2(0, 0));
            var field = InputBox(panel.transform, _ip, v => _ip = v);
            Place((RectTransform)field.transform, 0, 0, 360, 56, new Vector2(330, 167), new Vector2(0, 0));
            var join = Btn(panel.transform, "ПОДКЛЮЧИТЬСЯ", 26, () => JoinLan(_ip), true);
            Tint(join, Accent); join.GetComponentInChildren<Text>().color = new Color(0.1f, 0.08f, 0.04f);
            Place((RectTransform)join.transform, 0, 0, 300, 56, new Vector2(710, 167), new Vector2(0, 0));

            _lanStatusText = Label(panel.transform, _lanStatus, 20, Muted, TextAnchor.UpperLeft);
            Place(_lanStatusText.rectTransform, 0, 0, 1000, 90, new Vector2(40, 60), new Vector2(0, 0));
            _lanRefresh = 0f;
            RefreshLanList();
        }

        void JoinLan(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) { _lanStatus = "Введите адрес хоста или выберите игру из списка."; if (_lanStatusText) _lanStatusText.text = _lanStatus; return; }
            _lanStatus = "Подключение к " + address + "…";
            if (_lanStatusText) _lanStatusText.text = _lanStatus;
            Net.NetDriver.Join(address, err => { _lanStatus = err; if (_lanStatusText) _lanStatusText.text = err; });
        }

        void RefreshLanList()
        {
            if (!_lanList) return;
            for (int i = _lanList.childCount - 1; i >= 0; i--) Destroy(_lanList.GetChild(i).gameObject);
            float y = 0;
            var found = Net.NetSession.Found;
            if (found.Count == 0)
            {
                var none = Label(_lanList, "Поиск игр в локальной сети…", 20, Muted, TextAnchor.UpperLeft);
                Place(none.rectTransform, 0, 1, 800, 30, new Vector2(0, 0), new Vector2(0, 1));
                return;
            }
            foreach (var g in found)
            {
                var gl = g;
                var b = Btn(_lanList, "", 1, () => { _ip = gl.address; JoinLan(gl.address); }, true);
                Place((RectTransform)b.transform, 0, 1, 900, 60, new Vector2(0, y), new Vector2(0, 1));
                var t = Label(b.transform, $"{g.hostName}  ·  {(g.winter ? "Арденны" : "Нормандия")}  ·  {g.mode}  ·  {g.minutes} мин  ·  ботов {g.bots}  ·  игроков {g.players}/4",
                              20, TextCol, TextAnchor.MiddleLeft);
                Stretch(t.rectTransform); t.rectTransform.offsetMin = new Vector2(16, 0);
                var a = Label(b.transform, g.address + "   ПОДКЛЮЧИТЬСЯ ›", 18, Accent, TextAnchor.MiddleRight);
                Stretch(a.rectTransform); a.rectTransform.offsetMax = new Vector2(-14, 0);
                y -= 68;
                if (y < -300) break;
            }
        }

        void Update()
        {
            if (!_lanList) return;
            _lanRefresh -= Time.unscaledDeltaTime;
            if (_lanRefresh <= 0f) { _lanRefresh = 1f; RefreshLanList(); }
            if (_lanStatusText && Net.NetSession.IsClient && Net.NetSession.Status != _lanStatusText.text) _lanStatusText.text = Net.NetSession.Status;
        }

        static InputField InputBox(Transform parent, string value, Action<string> onChange)
        {
            var bg = Img(parent, "Input", new Color(0.05f, 0.05f, 0.04f, 0.95f));
            var txt = Label(bg.transform, "", 24, TextCol, TextAnchor.MiddleLeft);
            Stretch(txt.rectTransform); txt.rectTransform.offsetMin = new Vector2(12, 0); txt.rectTransform.offsetMax = new Vector2(-12, 0);
            txt.supportRichText = false;
            var ph = Label(bg.transform, "192.168.1.10", 24, new Color(1, 1, 1, 0.25f), TextAnchor.MiddleLeft);
            Stretch(ph.rectTransform); ph.rectTransform.offsetMin = new Vector2(12, 0);
            var f = bg.gameObject.AddComponent<InputField>();
            f.textComponent = txt; f.placeholder = ph; f.text = value; f.contentType = InputField.ContentType.Standard;
            f.onValueChanged.AddListener(v => onChange(v));
            return f;
        }

        void ShowOffline()
        {
            ClearContent();
            _stage.Visible = false;
            var panel = Img(_content, "Panel", Panel); Stretch(panel.rectTransform);
            Header(panel.transform, "НОВАЯ ИГРА", "Выберите карту, длительность и число ботов. Сторону и класс — уже в матче, на экране высадки.");

            var maps = GameDatabase.Instance.maps;
            // Карточки карт делят ширину панели поровну — все помещаются на экран
            float cw = Mathf.Min(470f, (1250f - 80f - (maps.Count - 1) * 14f) / Mathf.Max(1, maps.Count));
            for (int i = 0; i < maps.Count; i++)
            {
                int idx = i;
                bool winter = maps[i].theme == MapTheme.Winter, trench = maps[i].id == "rzhev";
                var card = Btn(panel.transform, "", 1, () => { _map = idx; _weather = winter ? 2 : trench ? 1 : 0; ShowOffline(); }, true);
                Place((RectTransform)card.transform, 0, 1, cw, 170, new Vector2(40 + i * (cw + 14f), -130), new Vector2(0, 1));
                var sw = Img(card.transform, "Swatch", winter ? new Color(0.82f, 0.86f, 0.9f) : trench ? new Color(0.36f, 0.3f, 0.22f) : new Color(0.32f, 0.45f, 0.24f));
                Place(sw.rectTransform, 0, 0.5f, 18, 140, new Vector2(12, 0), new Vector2(0, 0.5f));
                var tl = Label(card.transform, maps[i].displayName.ToUpper(), 26, idx == _map ? Accent : TextCol, TextAnchor.UpperLeft, FontStyle.Bold);
                tl.resizeTextForBestFit = true; tl.resizeTextMinSize = 16; tl.resizeTextMaxSize = 26;
                Place(tl.rectTransform, 0, 1, cw - 50, 44, new Vector2(40, -16), new Vector2(0, 1));
                var dl = Label(card.transform, winter ? "Зима · ели, бункеры, замёрзшая\nрека, снег. 1×1 км."
                                             : trench ? "Осень 1942 · три линии окопов\nу каждой стороны, воронки,\nблиндажи, колючка, дождь."
                                                      : "Лето · изгороди, фермы, церковь,\nокопы, река с мостами. 1×1 км.", 17, Muted, TextAnchor.UpperLeft);
                Place(dl.rectTransform, 0, 1, cw - 50, 100, new Vector2(40, -62), new Vector2(0, 1));
                if (idx == _map) Tint(card, new Color(0.24f, 0.23f, 0.17f, 0.98f));
            }

            float y = -330;
            SliderRow(panel.transform, "Длительность матча", 5, 60, _minutes, true, v => _minutes = v, v => $"{v:0} мин", ref y);
            SliderRow(panel.transform, "Количество ботов", 0, 512, _bots, true, v => _bots = v, v => $"{v:0}  ({Mathf.CeilToInt(v / 2)} на {Mathf.FloorToInt(v / 2)})", ref y);
            SliderRow(panel.transform, "Количество танков", 0, 128, _tanks, true, v => _tanks = v, v => $"{v:0}  ({Mathf.CeilToInt(v / 2)} на {Mathf.FloorToInt(v / 2)}) · экипаж — боты", ref y);
            SliderRow(panel.transform, "Количество самолётов", 0, 32, _planes, true, v => _planes = v, v => $"{v:0}  ({Mathf.CeilToInt(v / 2)} на {Mathf.FloorToInt(v / 2)}) · пилоты — боты или вы", ref y);
            SliderRow(panel.transform, "Количество орудий", 0, 24, _guns, true, v => _guns = v, v => $"{v:0}  ({Mathf.CeilToInt(v / 2)} на {Mathf.FloorToInt(v / 2)}) · гаубицы за базой", ref y);
            Cycler(panel.transform, "Режим", new[] { "Захват точек", "Командный бой" }, _mode, v => _mode = v, ref y);
            Cycler(panel.transform, "Сложность ботов", new[] { "Лёгкая", "Средняя", "Сложная", "Реалистичная" }, _diff, v => _diff = v, ref y);
            Cycler(panel.transform, "Время суток", new[] { "День", "Вечер" }, _time, v => _time = v, ref y);
            Cycler(panel.transform, "Сетевая игра", new[] { "Нет (один)", "Открыть в локальной сети" }, _lan ? 1 : 0, v => _lan = v == 1, ref y);

            var go = Btn(panel.transform, "СТАРТ", 40, StartMatch, true);
            Tint(go, Accent);
            go.GetComponentInChildren<Text>().color = new Color(0.1f, 0.08f, 0.04f);
            Place((RectTransform)go.transform, 1, 0, 360, 90, new Vector2(-40, 40), new Vector2(1, 0));
        }

        void StartMatch()
        {
            var db = GameDatabase.Instance;
            var map = db.maps[Mathf.Clamp(_map, 0, db.maps.Count - 1)];
            var cfg = new MatchConfig
            {
                map = map, mode = (GameModeType)_mode, playerFaction = Faction.USSR,      // сторону выбирают на экране высадки
                playerClass = SoldierClass.Assault, useAltPrimary = false, botCount = Mathf.RoundToInt(_bots),
                difficulty = (BotDifficulty)_diff, timeOfDay = (TimeOfDay)_time, weather = (Weather)_weather,
                timeLimitMinutes = _minutes, scoreLimit = 1000, killLimit = 50,
                tanksTotal = Mathf.RoundToInt(_tanks), planesTotal = Mathf.RoundToInt(_planes), artilleryTotal = Mathf.RoundToInt(_guns)
            };
            if (_lan && !Net.NetDriver.StartHost(cfg)) { _lanStatus = Net.NetSession.Status; ShowLan(); return; }
            _flow.StartMatch(cfg);
        }

        // ============================ Кастомизация ============================

        Faction _cFaction = Faction.USSR;
        SoldierClass _cClass = SoldierClass.Assault;
        WeaponDefinition _cWeapon;
        AttachmentDefinition _cAtt;
        bool _before, _cBino;
        int _cSlot = 1;
        static readonly string[] SlotTitles = { "", "1 · ОСНОВНОЕ", "2 · ВТОРОСТЕПЕННОЕ", "3 · НОЖ", "4 · ПРОТИВОТАНК." };

        /// <summary>Картинка оружия (3D-рендер сбоку) в прямоугольнике.</summary>
        static void WeaponPic(Transform parent, WeaponDefinition w, float x, float y, float wdt, float hgt, Vector2 anchor, bool dim)
        {
            var ic = new GameObject("Pic", typeof(RawImage)).GetComponent<RawImage>();
            ic.transform.SetParent(parent, false); ic.raycastTarget = false;
            ic.texture = AttachmentIcons.Get(w);
            ic.color = ic.texture ? (dim ? new Color(0.55f, 0.55f, 0.55f, 1f) : Color.white) : new Color(0, 0, 0, 0);
            Place(ic.rectTransform, anchor.x, anchor.y, wdt, hgt, new Vector2(x, y), anchor);
        }

        /// <summary>
        /// Кастомизация = магазин. Снизу — классы слева направо по цене (купить / выбрать). Слева сверху — 3 слота
        /// класса (основное, пистолет, второстепенное) с картинками; под ними — варианты для выбранного слота сверху вниз
        /// по цене. Справа — покупка / «взять в слот» и обвесы. Шпатель инженера — всегда в 4-м слоте.
        /// </summary>
        void ShowCustomization()
        {
            ClearContent();
            SetFullscreen(true);
            _stage.Visible = true;
            var p = SaveSystem.Progress;
            var left = Img(_content, "Weapons", Panel);
            left.rectTransform.anchorMin = new Vector2(0, 0); left.rectTransform.anchorMax = new Vector2(0, 1); left.rectTransform.pivot = new Vector2(0, 0.5f);
            left.rectTransform.sizeDelta = new Vector2(470, 0); left.rectTransform.anchoredPosition = Vector2.zero;
            left.rectTransform.offsetMin = new Vector2(0, 140);     // под ним — полоса классов
            var right = Img(_content, "Attachments", Panel);
            right.rectTransform.anchorMin = new Vector2(1, 0); right.rectTransform.anchorMax = new Vector2(1, 1); right.rectTransform.pivot = new Vector2(1, 0.5f);
            right.rectTransform.sizeDelta = new Vector2(430, 0); right.rectTransform.anchoredPosition = Vector2.zero;
            right.rectTransform.offsetMax = new Vector2(0, -60);      // сверху — кнопка «Закрыть»
            var close = Btn(_content, "ЗАКРЫТЬ", 20, () => { Select(0); ShowOffline(); }, true);
            Place((RectTransform)close.transform, 1, 1, 200, 50, new Vector2(0, 0), new Vector2(1, 1));

            // ---------- Нижняя полоса: классы слева направо по цене ----------
            var bar = Img(_content, "Classes", Panel);
            bar.rectTransform.anchorMin = new Vector2(0, 0); bar.rectTransform.anchorMax = new Vector2(1, 0); bar.rectTransform.pivot = new Vector2(0.5f, 0);
            bar.rectTransform.offsetMin = new Vector2(0, 0); bar.rectTransform.offsetMax = new Vector2(-440, 132);
            var barTitle = Label(bar.transform, $"КЛАССЫ  ·  деньги <color=#D4B060>{p.currency}</color>", 16, Muted, TextAnchor.UpperLeft);
            barTitle.supportRichText = true;
            Place(barTitle.rectTransform, 0, 1, 500, 24, new Vector2(12, -6), new Vector2(0, 1));
            for (int i = 0; i < Shop.ClassOrder.Length; i++)
            {
                var c = Shop.ClassOrder[i];
                bool owned = Shop.ClassOwned(c);
                var tile = Btn(bar.transform, "", 1, () => { _cClass = c; _cWeapon = null; _cAtt = null; _cBino = false; _cSlot = 1; ShowCustomization(); }, c == _cClass);
                var rt = (RectTransform)tile.transform;
                rt.anchorMin = new Vector2(i / 6f, 0); rt.anchorMax = new Vector2((i + 1) / 6f, 1);
                rt.offsetMin = new Vector2(8, 10); rt.offsetMax = new Vector2(-8, -34);
                var tn = Label(tile.transform, Shop.ClassName(c).ToUpper(), 22, c == _cClass ? Accent : owned ? TextCol : Muted, TextAnchor.UpperCenter, FontStyle.Bold);
                Stretch(tn.rectTransform); tn.rectTransform.offsetMax = new Vector2(0, -8);
                var ts = Label(tile.transform, owned ? "<color=#7CD66A>открыт</color>" : $"закрыт · <color=#D4B060>{Shop.ClassPrice(c)}</color>", 16, Muted, TextAnchor.LowerCenter);
                ts.supportRichText = true; Stretch(ts.rectTransform); ts.rectTransform.offsetMin = new Vector2(0, 8);
                if (c == _cClass) Tint(tile, new Color(0.24f, 0.23f, 0.17f, 0.98f));
            }
            // Специализации: «Артиллерист» и «Танкист» — покупаются один раз, действуют для любого класса
            for (int k = 0; k < Shop.Perks.Length; k++)
            {
                string id = Shop.Perks[k];
                bool has = Shop.HasPerk(id);
                var tile = Btn(bar.transform, "", 1, () => { if (Shop.BuyPerk(id)) UpdateProfile(); ShowCustomization(); }, false);
                var rt = (RectTransform)tile.transform;
                rt.anchorMin = new Vector2((4 + k) / 6f, 0); rt.anchorMax = new Vector2((5 + k) / 6f, 1);
                rt.offsetMin = new Vector2(8, 10); rt.offsetMax = new Vector2(-8, -34);
                var tn = Label(tile.transform, Shop.PerkName(id).ToUpper(), 20, has ? Accent : TextCol, TextAnchor.UpperCenter, FontStyle.Bold);
                Stretch(tn.rectTransform); tn.rectTransform.offsetMax = new Vector2(0, -6);
                var ti = Label(tile.transform, Shop.PerkInfo(id), 12, Muted, TextAnchor.MiddleCenter);
                Stretch(ti.rectTransform); ti.rectTransform.offsetMin = new Vector2(4, 22); ti.rectTransform.offsetMax = new Vector2(-4, -30);
                var ts = Label(tile.transform, has ? "<color=#7CD66A>куплено</color>" : $"купить · <color=#D4B060>{Shop.PerkPrice(id)}</color>", 16, Muted, TextAnchor.LowerCenter);
                ts.supportRichText = true; Stretch(ts.rectTransform); ts.rectTransform.offsetMin = new Vector2(0, 6);
                tile.interactable = has || p.currency >= Shop.PerkPrice(id);
                if (has) Tint(tile, new Color(0.18f, 0.24f, 0.16f, 0.98f));
            }
            bool classOwned = Shop.ClassOwned(_cClass);

            // ---------- Левая колонка: сторона, 3 слота класса ----------
            var fde = Btn(left.transform, "ГЕРМАНИЯ", 20, () => { _cFaction = Faction.Germany; _cWeapon = null; _cAtt = null; ShowCustomization(); }, true);
            var fsu = Btn(left.transform, "СССР", 20, () => { _cFaction = Faction.USSR; _cWeapon = null; _cAtt = null; ShowCustomization(); }, true);
            Place((RectTransform)fsu.transform, 0, 1, 140, 40, new Vector2(15, -12), new Vector2(0, 1));
            Place((RectTransform)fde.transform, 0, 1, 140, 40, new Vector2(160, -12), new Vector2(0, 1));
            (_cFaction == Faction.USSR ? fsu : fde).GetComponentInChildren<Text>().color = Accent;
            var clsLabel = Label(left.transform, Shop.ClassName(_cClass).ToUpper() + (classOwned ? "" : " · не куплен"), 16, classOwned ? Accent : Muted, TextAnchor.MiddleLeft, FontStyle.Bold);
            Place(clsLabel.rectTransform, 0, 1, 440, 26, new Vector2(15, -56), new Vector2(0, 1));

            int nSlots = Shop.SlotCount(_cClass);
            if (_cSlot > nSlots) _cSlot = 1;
            float sw = (440f - nSlots * 6f) / (nSlots + 1);       // + отдельный слот бинокля
            for (int sl = 1; sl <= nSlots; sl++)
            {
                int slot = sl;
                var cur = Shop.Chosen(_cFaction, _cClass, slot);
                var box = Btn(left.transform, "", 1, () => { _cSlot = slot; _cWeapon = null; _cAtt = null; _cBino = false; ShowCustomization(); }, slot == _cSlot);
                Place((RectTransform)box.transform, 0, 1, sw, 116, new Vector2(15 + (slot - 1) * (sw + 6f), -86), new Vector2(0, 1));
                if (slot == _cSlot) Tint(box, new Color(0.24f, 0.23f, 0.17f, 0.98f));
                string slotTitle = slot == 3 && _cClass == SoldierClass.Engineer ? "3 · ИНСТРУМЕНТ" : SlotTitles[slot];
                var sh = Label(box.transform, slotTitle, 12, slot == _cSlot ? Accent : Muted, TextAnchor.UpperCenter, FontStyle.Bold);
                Stretch(sh.rectTransform); sh.rectTransform.offsetMax = new Vector2(0, -4);
                if (cur) WeaponPic(box.transform, cur, 0, -6, sw - 6, 54, new Vector2(0.5f, 0.5f), false);
                var sn = Label(box.transform, cur ? cur.displayName : "пусто", 13, cur ? TextCol : Muted, TextAnchor.LowerCenter);
                Stretch(sn.rectTransform); sn.rectTransform.offsetMin = new Vector2(2, 4); sn.rectTransform.offsetMax = new Vector2(-2, 0);
            }
            {
                // Отдельный слот: бинокль 6× (клавиша Z) — для любого класса, покупается один раз
                var bbox = Btn(left.transform, "", 1, () => { _cBino = true; _cWeapon = null; _cAtt = null; ShowCustomization(); }, _cBino);
                Place((RectTransform)bbox.transform, 0, 1, sw, 116, new Vector2(15 + nSlots * (sw + 6f), -86), new Vector2(0, 1));
                if (_cBino) Tint(bbox, new Color(0.24f, 0.23f, 0.17f, 0.98f));
                var bh = Label(bbox.transform, "Z · БИНОКЛЬ", 12, _cBino ? Accent : Muted, TextAnchor.UpperCenter, FontStyle.Bold);
                Stretch(bh.rectTransform); bh.rectTransform.offsetMax = new Vector2(0, -4);
                var bn = Label(bbox.transform, Shop.HasBinoculars ? "<color=#7CD66A>куплен</color>" : $"<color=#D4B060>{Shop.BinocularsPrice}</color>", 14, TextCol, TextAnchor.MiddleCenter);
                bn.supportRichText = true; Stretch(bn.rectTransform);
                var bt = Label(bbox.transform, "6×", 13, Muted, TextAnchor.LowerCenter);
                Stretch(bt.rectTransform); bt.rectTransform.offsetMin = new Vector2(2, 4);
            }

            // ---------- Варианты выбранного слота: сверху вниз по цене, с картинками ----------
            var list = Shop.SlotOptions(_cFaction, _cClass, _cSlot);
            var inSlot = Shop.Chosen(_cFaction, _cClass, _cSlot);
            if (!_cBino && (!_cWeapon || !list.Contains(_cWeapon))) _cWeapon = inSlot ? inSlot : list.Count > 0 ? list[0] : null;
            float y = -214;
            foreach (var w in list)
            {
                var wl = w;
                bool wOwned = Shop.WeaponOwned(w);
                bool sel = w == _cWeapon && !_cBino;
                var b = Btn(left.transform, "", 1, () => { _cWeapon = wl; _cAtt = null; _cBino = false; ShowCustomization(); }, sel);
                Place((RectTransform)b.transform, 0, 1, 440, 60, new Vector2(15, y), new Vector2(0, 1));
                if (sel) Tint(b, new Color(0.24f, 0.23f, 0.17f, 0.98f));
                WeaponPic(b.transform, w, 4, 0, 150, 56, new Vector2(0, 0.5f), !wOwned);
                var n = Label(b.transform, (w == inSlot ? "★ " : "") + w.displayName, 17, sel ? Accent : wOwned ? TextCol : Muted, TextAnchor.MiddleLeft);
                Stretch(n.rectTransform); n.rectTransform.offsetMin = new Vector2(160, 0); n.rectTransform.offsetMax = new Vector2(-80, 0);
                var stl = Label(b.transform, wOwned ? (w == inSlot ? "<color=#7CD66A>в слоте</color>" : "куплено") : $"<color=#D4B060>{w.price}</color>", 15, Muted, TextAnchor.MiddleRight);
                stl.supportRichText = true; Stretch(stl.rectTransform); stl.rectTransform.offsetMax = new Vector2(-10, 0);
                y -= 64;
            }
            if (false)
            {
                // Бинокль — классу с противотанковым оружием (отдельная клавиша Z, не занимает слот)
                var bb = Btn(left.transform, "", 1, () => { _cBino = true; _cAtt = null; ShowCustomization(); }, _cBino);
                Place((RectTransform)bb.transform, 0, 1, 440, 50, new Vector2(15, y), new Vector2(0, 1));
                var n = Label(bb.transform, "Бинокль 6× (клавиша Z)", 17, _cBino ? Accent : Shop.HasBinoculars ? TextCol : Muted, TextAnchor.MiddleLeft);
                Stretch(n.rectTransform); n.rectTransform.offsetMin = new Vector2(16, 0);
                var stb = Label(bb.transform, Shop.HasBinoculars ? "куплен" : $"<color=#D4B060>{Shop.BinocularsPrice}</color>", 15, Muted, TextAnchor.MiddleRight);
                stb.supportRichText = true; Stretch(stb.rectTransform); stb.rectTransform.offsetMax = new Vector2(-10, 0);
            }

            // ---------- Класс не куплен: кнопка покупки ----------
            if (!classOwned)
            {
                var cc = _cClass;
                Header(right.transform, Shop.ClassName(cc).ToUpper(), "Класс закрыт. После покупки можно покупать его оружие.");
                var info = Label(right.transform, ClassInfo(cc), 18, TextCol, TextAnchor.UpperLeft);
                Place(info.rectTransform, 0, 1, 400, 300, new Vector2(15, -120), new Vector2(0, 1));
                var buy = Btn(right.transform, $"КУПИТЬ КЛАСС · {Shop.ClassPrice(cc)}", 22, () => { if (Shop.BuyClass(cc)) UpdateProfile(); ShowCustomization(); }, true);
                buy.interactable = p.currency >= Shop.ClassPrice(cc);
                if (buy.interactable) { Tint(buy, Accent); buy.GetComponentInChildren<Text>().color = new Color(0.1f, 0.08f, 0.04f); }
                Place((RectTransform)buy.transform, 0, 0, 400, 60, new Vector2(15, 40), new Vector2(0, 0));
                var need = Label(right.transform, buy.interactable ? "" : $"Не хватает {Shop.ClassPrice(cc) - p.currency}. Деньги — за матчи: убийства, победы, танки.", 16, Muted, TextAnchor.UpperLeft);
                Place(need.rectTransform, 0, 0, 400, 50, new Vector2(15, 110), new Vector2(0, 0));
                if (_cWeapon) _stage.ShowWeapon(_cWeapon, new List<AttachmentDefinition>());
                return;
            }

            // ---------- Бинокль ----------
            if (_cBino)
            {
                _stage.Visible = false;
                Header(right.transform, "БИНОКЛЬ 6×", "Для поддержки (противотанковое оружие)");
                var info = Label(right.transform, "В бою — клавиша Z (или кнопка «БИНОКЛЬ» на телефоне).\nУвеличение 6×. Враг в центре бинокля\nотмечается для вас на 8 секунд — удобно\nискать танки и пулемётные гнёзда.", 18, TextCol, TextAnchor.UpperLeft);
                Place(info.rectTransform, 0, 1, 400, 200, new Vector2(15, -120), new Vector2(0, 1));
                if (!Shop.HasBinoculars)
                {
                    var buy = Btn(right.transform, $"КУПИТЬ · {Shop.BinocularsPrice}", 22, () => { if (Shop.BuyBinoculars()) UpdateProfile(); ShowCustomization(); }, true);
                    buy.interactable = p.currency >= Shop.BinocularsPrice;
                    Place((RectTransform)buy.transform, 0, 0, 400, 60, new Vector2(15, 40), new Vector2(0, 0));
                }
                return;
            }
            if (!_cWeapon) return;

            // ---------- Правая колонка: оружие не куплено ----------
            bool weaponOwned = Shop.WeaponOwned(_cWeapon);
            var p0 = new List<AttachmentDefinition>();
            if (!weaponOwned)
            {
                Header(right.transform, _cWeapon.displayName.ToUpper(), "Оружие не куплено");
                _stage.ShowWeapon(_cWeapon, p0);
                var bs = WeaponStats.Compute(_cWeapon, p0);
                var st0 = Label(right.transform, $"{CatName(_cWeapon.category)}\nУрон {_cWeapon.damage:0} · Темп {_cWeapon.roundsPerMinute:0} в/мин\nМагазин {bs.magazineSize} · Дальность {_cWeapon.maxRange:0} м\nОтдача {bs.recoilV:0.##} · Разброс {bs.hipSpread:0.##}", 18, TextCol, TextAnchor.UpperLeft);
                Place(st0.rectTransform, 0, 1, 400, 200, new Vector2(15, -120), new Vector2(0, 1));
                var cw = _cWeapon;
                var buy = Btn(right.transform, $"КУПИТЬ · {cw.price}", 22, () =>
                {
                    if (Shop.BuyWeapon(cw)) { LoadoutChoice.SetSlot(cw.faction, _cClass, _cSlot, cw); UpdateProfile(); }   // купленное сразу встаёт в слот
                    ShowCustomization();
                }, true);
                buy.interactable = p.currency >= cw.price;
                if (buy.interactable) { Tint(buy, Accent); buy.GetComponentInChildren<Text>().color = new Color(0.1f, 0.08f, 0.04f); }
                Place((RectTransform)buy.transform, 0, 0, 400, 60, new Vector2(15, 40), new Vector2(0, 0));
                return;
            }

            // ---------- Купленное оружие: обвесы ----------
            Header(right.transform, _cWeapon.displayName.ToUpper(), "Обвесы · перетаскивайте модель для осмотра · ★ — оружие класса в бою");
            {
                bool chosenNow = Shop.Chosen(_cFaction, _cClass, _cSlot) == _cWeapon;
                var cw = _cWeapon;
                var pick = Btn(right.transform, chosenNow ? "★ В СЛОТЕ" : "ВЗЯТЬ В СЛОТ " + _cSlot, 18,
                               () => { LoadoutChoice.SetSlot(cw.faction, _cClass, _cSlot, cw); ShowCustomization(); }, !chosenNow);
                if (!chosenNow) { Tint(pick, Accent); pick.GetComponentInChildren<Text>().color = new Color(0.1f, 0.08f, 0.04f); }
                Place((RectTransform)pick.transform, 1, 1, 200, 40, new Vector2(-15, -20), new Vector2(1, 1));
            }
            var installed = AttachmentRules.Resolve(p.GetLoadout(_cWeapon.id));
            y = -120;
            foreach (var a in _cWeapon.compatibleAttachments)
            {
                var al = a;
                bool owned = p.IsUnlocked(a.id), on = installed.Contains(a);
                string state = on ? "<color=#7CD66A>установлен</color>" : owned ? "куплен" : $"<color=#D4B060>{a.price}</color>";
                var b = Btn(right.transform, "", 1, () => { _cAtt = al; _before = false; ShowCustomization(); }, a == _cAtt);
                Place((RectTransform)b.transform, 0, 1, 400, 50, new Vector2(15, y), new Vector2(0, 1));
                var ic = new GameObject("Icon", typeof(RawImage)).GetComponent<RawImage>();
                ic.transform.SetParent(b.transform, false); ic.raycastTarget = false;
                ic.texture = AttachmentIcons.Get(a); ic.color = ic.texture ? Color.white : new Color(0, 0, 0, 0);
                Place(ic.rectTransform, 0, 0.5f, 60, 44, new Vector2(4, 0), new Vector2(0, 0.5f));
                var n = Label(b.transform, a.displayName, 19, a == _cAtt ? Accent : TextCol, TextAnchor.MiddleLeft); Stretch(n.rectTransform); n.rectTransform.offsetMin = new Vector2(72, 0);
                var s2 = Label(b.transform, state, 16, Muted, TextAnchor.MiddleRight); s2.supportRichText = true; Stretch(s2.rectTransform); s2.rectTransform.offsetMax = new Vector2(-12, 0);
                y -= 54;
            }

            var preview = WithSelected(installed);
            _stage.ShowWeapon(_cWeapon, _before ? installed : preview);

            var before = WeaponStats.Compute(_cWeapon, installed);
            var after = WeaponStats.Compute(_cWeapon, preview);
            string Cmp(string name, float a, float b, bool higherBetter, string fmt = "0.##")
            {
                if (Mathf.Approximately(a, b)) return $"{name}: {a.ToString(fmt)}\n";
                bool better = (b > a) == higherBetter;
                return $"{name}: {a.ToString(fmt)} → <color={(better ? "#7CD66A" : "#E06A5A")}>{b.ToString(fmt)}</color>\n";
            }
            string stats = $"Урон {_cWeapon.damage:0} · Темп {_cWeapon.roundsPerMinute:0} в/мин\n"
                + Cmp("Магазин", before.magazineSize, after.magazineSize, true, "0")
                + Cmp("Отдача (верт.)", before.recoilV, after.recoilV, false)
                + Cmp("Разброс от бедра", before.hipSpread, after.hipSpread, false)
                + Cmp("Слышимость, м", before.suppressed ? 0 : before.loudnessRadius, after.suppressed ? 0 : after.loudnessRadius, false, "0")
                + Cmp("Прицеливание, с", before.adsTime, after.adsTime, false);
            var st = Label(right.transform, (_cAtt ? $"<b>{_cAtt.displayName}</b>\n" : "") + stats, 17, TextCol, TextAnchor.UpperLeft);
            st.supportRichText = true;
            Place(st.rectTransform, 0, 0, 400, 210, new Vector2(15, 110), new Vector2(0, 0));

            if (_cAtt)
            {
                bool owned = p.IsUnlocked(_cAtt.id), on = installed.Contains(_cAtt);
                Button act;
                if (!owned)
                {
                    bool canAch = !string.IsNullOrEmpty(_cAtt.requiredAchievementId) && p.achievementIds.Contains(_cAtt.requiredAchievementId);
                    act = Btn(right.transform, canAch ? "ПОЛУЧИТЬ (достижение)" : $"КУПИТЬ · {_cAtt.price}", 22, () =>
                    {
                        if (AttachmentRules.TryUnlock(_cAtt)) { AttachmentRules.Install(_cWeapon, _cAtt); UpdateProfile(); }
                        ShowCustomization();
                    }, true);
                    act.interactable = canAch || p.currency >= _cAtt.price;
                }
                else if (!on) act = Btn(right.transform, "УСТАНОВИТЬ", 22, () => { AttachmentRules.Install(_cWeapon, _cAtt); ShowCustomization(); }, true);
                else act = Btn(right.transform, "СНЯТЬ", 22, () => { AttachmentRules.Uninstall(_cWeapon, _cAtt.slot); ShowCustomization(); }, true);
                Place((RectTransform)act.transform, 0, 0, 250, 56, new Vector2(15, 40), new Vector2(0, 0));
                if (!on)
                {
                    var ba = Btn(right.transform, _before ? "ПОСЛЕ" : "ДО", 22, () => { _before = !_before; ShowCustomization(); }, true);
                    Place((RectTransform)ba.transform, 0, 0, 135, 56, new Vector2(280, 40), new Vector2(0, 0));
                }
            }
        }

        static string ClassInfo(SoldierClass c) => c switch
        {
            SoldierClass.Engineer => "1 пистолет-пулемёт · 2 пистолет или лёгкий ПП ·\n3 гаечный ключ (ремонт техники) или нож ·\n4 два мешка с песком (ставятся за 3 с, урон сквозь них ×¼).",
            SoldierClass.Support => "1 пулемёт · 2 пистолет или лёгкий ПП · 3 нож ·\n4 противотанковое (фаустпатрон / ПТРД).\nМожет купить бинокль.",
            SoldierClass.Recon => "1 снайперская винтовка · 2 пистолет или лёгкий ПП ·\n3 нож. Работает с дальней дистанции.",
            _ => "1 автомат или самозарядная винтовка ·\n2 пистолет или лёгкий ПП · 3 нож ·\n4 аптечка: поднимает тяжелораненых (+50 HP)."
        };

        List<AttachmentDefinition> WithSelected(List<AttachmentDefinition> installed)
        {
            var list = new List<AttachmentDefinition>(installed);
            if (_cAtt) { list.RemoveAll(x => x.slot == _cAtt.slot); list.Add(_cAtt); }
            return list;
        }

        static string CatName(WeaponCategory c) => c switch
        {
            WeaponCategory.SniperRifle => "снайп.", WeaponCategory.AssaultRifle => "автомат", WeaponCategory.SubmachineGun => "ПП",
            WeaponCategory.Pistol => "пистолет", WeaponCategory.Pistol2 => "пистолет", WeaponCategory.MachineGun => "пулемёт",
            WeaponCategory.BattleRifle => "штурм.", WeaponCategory.Launcher => "противотанк.", _ => ""
        };

        // ============================ Настройки ============================

        /// <summary>Вкладки категорий настроек: «Игра и звук» и «Графика».</summary>
        void SettingsTabs(Transform panel)
        {
            string[] tabs = { "ИГРА И ЗВУК", "ГРАФИКА" };
            for (int i = 0; i < tabs.Length; i++)
            {
                int ti = i;
                var tb = Btn(panel, tabs[i], 20, () => { _setTab = ti; if (ti == 1) ShowGraphics(); else ShowSettings(); }, _setTab == i);
                Place((RectTransform)tb.transform, 1, 1, 220, 44, new Vector2(-40 - (tabs.Length - 1 - i) * 230, -36), new Vector2(1, 1));
                if (_setTab == i) { Tint(tb, Accent); tb.GetComponentInChildren<Text>().color = new Color(0.1f, 0.08f, 0.04f); }
            }
            // Настройки открыты во весь экран — выход в главное меню
            var close = Btn(panel, "ЗАКРЫТЬ", 20, () => { Select(0); ShowOffline(); }, true);
            Place((RectTransform)close.transform, 1, 1, 200, 44, new Vector2(-40 - tabs.Length * 230, -36), new Vector2(1, 1));
        }

        void ShowSettings()
        {
            if (_setTab == 1) { ShowGraphics(); return; }
            ClearContent();
            SetFullscreen(true);
            _stage.Visible = false;
            var s = SaveSystem.Settings;
            var panel = Img(_content, "Panel", Panel); Stretch(panel.rectTransform);
            Header(panel.transform, "НАСТРОЙКИ", "Сохраняются автоматически");
            SettingsTabs(panel.transform);

            float y = -120;
            SubHeader(panel.transform, "Игра", ref y);
            Cycler(panel.transform, "Язык", new[] { "Русский", "English" }, s.language, v => { s.language = v; ApplySettings(); ShowSettings(); }, ref y);
            Cycler(panel.transform, "Инерция обзора", new[] { "Вкл (тяжёлая)", "Выкл (резкая)" }, s.heavyLook ? 0 : 1, v => { s.heavyLook = v == 0; ApplySettings(); }, ref y);
            SubHeader(panel.transform, "Звук", ref y);
            SliderRow(panel.transform, "Общая громкость", 0, 1, s.masterVolume, false, v => { s.masterVolume = v; ApplySettings(); }, v => $"{v * 100:0}%", ref y);
            SliderRow(panel.transform, "Музыка", 0, 1, s.musicVolume, false, v => { s.musicVolume = v; ApplySettings(); var m = FindFirstObjectByType<RuntimeMenu>(); if (m) m.SetMusicVolume(v); }, v => $"{v * 100:0}%", ref y);

            // Правая колонка — управление
            float y2 = -120;
            SubHeader(panel.transform, "Управление", ref y2, 760);
            SliderRow(panel.transform, "Чувствительность мыши", 0.1f, 5, s.mouseSensitivity, false, v => { s.mouseSensitivity = v; ApplySettings(); }, v => $"{v:0.00}", ref y2, 760);
            SliderRow(panel.transform, "Чувствит. в прицеле", 0.2f, 1.5f, s.adsSensitivity, false, v => { s.adsSensitivity = v; ApplySettings(); }, v => $"{v:0.00}", ref y2, 760);
            Cycler(panel.transform, "Инверсия Y", new[] { "Выкл", "Вкл" }, s.invertY ? 1 : 0, v => { s.invertY = v == 1; ApplySettings(); }, ref y2, 760);
            y2 -= 10;
            string[] actions = { "Fire", "Aim", "Reload", "Jump", "Crouch", "Sprint", "BuildMode", "KnifeSpin", "Melee", "Grenade", "Inspect", "Interact", "DropWeapon", "Binoculars", "CameraView", "SwitchSeat" };
            string[] names = { "Огонь", "Прицел", "Перезарядка", "Прыжок", "Присесть", "Бег", "Строительство", "Вращение ножа", "Удар ножом", "Граната", "Осмотр оружия", "Действие / машина", "Бросить оружие", "Бинокль", "Вид 1-е / 3-е лицо", "Пересесть в машине" };
            for (int i = 0; i < actions.Length; i++)
            {
                string act = actions[i];
                var l = Label(panel.transform, names[i], 19, Muted, TextAnchor.MiddleLeft);
                Place(l.rectTransform, 0, 1, 260, 36, new Vector2(760, y2), new Vector2(0, 1));
                var key = GameInput.FindAction(act).GetBindingDisplayString(0);
                Button b = null;
                b = Btn(panel.transform, key, 19, () =>
                {
                    b.GetComponentInChildren<Text>().text = "нажмите клавишу…";
                    GameInput.Rebind(act, 0, () => { if (b) b.GetComponentInChildren<Text>().text = GameInput.FindAction(act).GetBindingDisplayString(0); });
                }, true);
                Place((RectTransform)b.transform, 0, 1, 230, 34, new Vector2(1030, y2), new Vector2(0, 1));
                y2 -= 34;
            }
            var reset = Btn(panel.transform, "СБРОСИТЬ КЛАВИШИ", 18, () => { GameInput.ResetBindings(); ShowSettings(); }, true);
            Place((RectTransform)reset.transform, 0, 1, 260, 40, new Vector2(1000, y2 - 8), new Vector2(0, 1));
        }

        int _setTab;

        public static void ApplySettings()
        {
            var s = SaveSystem.Settings;
            QualitySettings.vSyncCount = s.vSync ? 1 : 0;
            Application.targetFrameRate = s.vSync ? -1 : (s.targetFps <= 0 ? -1 : s.targetFps);
            AudioListener.volume = s.masterVolume;
            PerformanceOptimizer.ApplyGlobal(s);
            SaveSystem.SaveSettings();
        }

        void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ============================ Виджеты ============================

        static void Header(Transform parent, string title, string sub)
        {
            var t = Label(parent, title, 40, TextCol, TextAnchor.UpperLeft, FontStyle.Bold);
            Place(t.rectTransform, 0, 1, 1000, 56, new Vector2(40, -28), new Vector2(0, 1));
            var s = Label(parent, sub, 19, Muted, TextAnchor.UpperLeft);
            Place(s.rectTransform, 0, 1, 1000, 30, new Vector2(42, -82), new Vector2(0, 1));
        }

        static void SubHeader(Transform parent, string text, ref float y, float x = 40)
        {
            var t = Label(parent, text.ToUpper(), 20, Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            Place(t.rectTransform, 0, 1, 400, 34, new Vector2(x, y), new Vector2(0, 1));
            y -= 44;
        }

        static void Cycler(Transform parent, string label, string[] options, int value, Action<int> set, ref float y, float x = 40)
        {
            var l = Label(parent, label, 22, Muted, TextAnchor.MiddleLeft);
            Place(l.rectTransform, 0, 1, 300, 46, new Vector2(x, y), new Vector2(0, 1));
            int idx = Mathf.Clamp(value, 0, options.Length - 1);
            var val = Label(parent, options[idx], 23, TextCol, TextAnchor.MiddleCenter, FontStyle.Bold);
            Place(val.rectTransform, 0, 1, 330, 46, new Vector2(x + 350, y), new Vector2(0, 1));
            void Step(int d) { idx = (idx + d + options.Length) % options.Length; val.text = options[idx]; set(idx); }
            var prev = Btn(parent, "<", 22, () => Step(-1), true); Place((RectTransform)prev.transform, 0, 1, 46, 46, new Vector2(x + 300, y), new Vector2(0, 1));
            var next = Btn(parent, ">", 22, () => Step(1), true); Place((RectTransform)next.transform, 0, 1, 46, 46, new Vector2(x + 684, y), new Vector2(0, 1));
            y -= 54;
        }

        static void SliderRow(Transform parent, string label, float min, float max, float value, bool whole, Action<float> set, Func<float, string> fmt, ref float y, float x = 40)
        {
            var l = Label(parent, label, 22, Muted, TextAnchor.MiddleLeft);
            Place(l.rectTransform, 0, 1, 300, 46, new Vector2(x, y), new Vector2(0, 1));
            var go = DefaultControls.CreateSlider(new DefaultControls.Resources());
            go.transform.SetParent(parent, false);
            var sl = go.GetComponent<Slider>();
            sl.minValue = min; sl.maxValue = max; sl.wholeNumbers = whole; sl.value = value;
            Place((RectTransform)go.transform, 0, 1, 476, 22, new Vector2(x + 300, y - 12), new Vector2(0, 1));   // в 1,7 раза длиннее — точнее
            foreach (var img in go.GetComponentsInChildren<Image>())
                img.color = img.name == "Background" ? new Color(1, 1, 1, 0.12f) : img.name == "Fill" ? Accent : TextCol;
            var val = Label(parent, fmt(value), 21, TextCol, TextAnchor.MiddleLeft);
            Place(val.rectTransform, 0, 1, 330, 46, new Vector2(x + 790, y), new Vector2(0, 1));
            sl.onValueChanged.AddListener(v => { val.text = fmt(v); set(v); });
            y -= 54;
        }

        static Button Btn(Transform parent, string text, int size, Action onClick, bool filled)
        {
            var img = Img(parent, "Btn", Color.white);
            var b = img.gameObject.AddComponent<Button>();
            Tint(b, filled ? PanelLight : new Color(1, 1, 1, 0f));
            b.onClick.AddListener(() => { if (_ui) _ui.PlayOneShot(SoundSynth.UiClick()); onClick(); });
            if (!string.IsNullOrEmpty(text))
            {
                var t = Label(img.transform, text, size, TextCol, TextAnchor.MiddleCenter, FontStyle.Bold);
                Stretch(t.rectTransform);
            }
            return b;
        }

        static void Tint(Button b, Color c)
        {
            var cb = b.colors;
            cb.colorMultiplier = 1f; cb.fadeDuration = 0.08f;
            cb.normalColor = c; cb.selectedColor = c;
            cb.highlightedColor = c.a < 0.01f ? new Color(1, 1, 1, 0.07f) : Color.Lerp(c, Color.white, 0.18f);
            cb.pressedColor = c.a < 0.01f ? new Color(1, 1, 1, 0.12f) : c * 0.8f;
            cb.disabledColor = new Color(c.r, c.g, c.b, c.a * 0.4f);
            b.colors = cb;
        }

        static Text Label(Transform parent, string text, int size, Color color, TextAnchor anchor, FontStyle style = FontStyle.Normal)
        {
            var go = new GameObject("Text", typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = F; t.text = text; t.fontSize = size; t.color = color; t.alignment = anchor; t.fontStyle = style;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow; t.raycastTarget = false;
            t.supportRichText = true;
            return t;
        }

        static Image Img(Transform parent, string name, Color c)
        {
            var go = new GameObject(name, typeof(Image));
            go.transform.SetParent(parent, false);
            var i = go.GetComponent<Image>(); i.color = c;
            return i;
        }

        static RectTransform NewRect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static void Stretch(RectTransform rt) { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero; }

        static void Place(RectTransform rt, float ax, float ay, float w, float h, Vector2 pos = default, Vector2? pivot = null)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(ax, ay);
            rt.pivot = pivot ?? new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = pos;
        }

        static Texture2D Gradient()
        {
            var t = new Texture2D(256, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int x = 0; x < 256; x++)
            {
                float k = x / 255f;
                t.SetPixel(x, 0, new Color(Bg.r, Bg.g, Bg.b, Mathf.Lerp(0.97f, 0.15f, Mathf.SmoothStep(0.2f, 0.75f, k))));
            }
            t.Apply();
            return t;
        }

        static void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>()) return;
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            es.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            DontDestroyOnLoad(es);
        }

        /// <summary>Перетаскивание по фону вращает модель на столе.</summary>
        class DragRotate : MonoBehaviour, IDragHandler
        {
            public MenuStage stage;
            public void OnDrag(PointerEventData e) => stage.Drag(e.delta);
        }
    }
}
