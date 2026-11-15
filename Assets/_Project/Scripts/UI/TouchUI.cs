using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Frontline
{
    /// <summary>
    /// Экранное управление для телефонов (собирается кодом, без префабов):
    /// • слева — плавающий джойстик (до упора вверх — бег), справа — область обзора пальцем;
    /// • большая кнопка ОГОНЬ (её можно вести пальцем — камера поворачивается), ПРИЦЕЛ (переключатель),
    ///   ПЕРЕЗАРЯДКА, ПРЫЖОК, ПРИСЕСТЬ (переключатель), ГРАНАТА, НОЖ, ДЕЙСТВИЕ (E), СТРОЙКА;
    /// • сверху — слоты оружия 1–4 и пауза.
    /// Кнопки полупрозрачные, размер — от высоты экрана; скрываются на паузе, экране высадки и после матча.
    /// </summary>
    public class TouchUI : MonoBehaviour
    {
        Canvas _canvas;
        static Sprite _circle;

        public static TouchUI Create(Transform parent)
        {
            var go = new GameObject("TouchUI");
            go.transform.SetParent(parent, false);
            var ui = go.AddComponent<TouchUI>();
            ui.Build();
            return ui;
        }

        static Sprite Circle()
        {
            if (_circle) return _circle;
            const int N = 128;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "TouchCircle" };
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(N / 2f, N / 2f)) / (N / 2f);
                    float fill = Mathf.Clamp01((1f - d) * 40f);                 // сглаженный край
                    float rim = Mathf.Clamp01(1f - Mathf.Abs(d - 0.93f) * 22f);  // светлый ободок
                    px[y * N + x] = new Color(1, 1, 1, Mathf.Max(fill * 0.35f, rim * 0.9f));
                }
            t.SetPixels(px); t.Apply();
            return _circle = Sprite.Create(t, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
        }

        void Build()
        {
            if (!FindAnyObjectByType<EventSystem>())
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                es.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay; _canvas.sortingOrder = 50;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 1f;
            gameObject.AddComponent<GraphicRaycaster>();

            // Джойстик (TouchControls сам двигает его к месту касания)
            var jb = Img("JoystickBase", 260, new Color(1, 1, 1, 0.25f));
            var jk = Img("JoystickKnob", 120, new Color(1, 1, 1, 0.55f));
            jk.transform.SetParent(jb.transform, false);
            jb.gameObject.SetActive(false);
            var tc = gameObject.AddComponent<TouchControls>();
            SetField(tc, "joystickBase", jb.rectTransform); SetField(tc, "joystickKnob", jk.rectTransform);

            // Правая группа
            Btn("ОГОНЬ", TouchButton.Kind.Fire, new Vector2(-190, 230), 210, new Color(1f, 0.35f, 0.25f, 0.55f));
            Btn("ПРИЦЕЛ", TouchButton.Kind.Aim, new Vector2(-390, 300), 130, new Color(1, 1, 1, 0.45f), toggle: true);
            Btn("ПЕРЕЗ.", TouchButton.Kind.Reload, new Vector2(-400, 140), 110, new Color(1, 1, 1, 0.45f));
            Btn("ПРЫЖОК", TouchButton.Kind.Jump, new Vector2(-120, 450), 120, new Color(1, 1, 1, 0.45f));
            Btn("ПРИСЕСТЬ", TouchButton.Kind.Crouch, new Vector2(-280, 470), 110, new Color(1, 1, 1, 0.45f), toggle: true);
            Btn("ГРАНАТА", TouchButton.Kind.Grenade, new Vector2(-540, 110), 100, new Color(1, 1, 1, 0.4f));
            Btn("НОЖ", TouchButton.Kind.Melee, new Vector2(-560, 240), 100, new Color(1, 1, 1, 0.4f));
            Btn("E", TouchButton.Kind.Interact, new Vector2(-560, 380), 100, new Color(1f, 0.85f, 0.4f, 0.5f));
            Btn("СТРОЙ.", TouchButton.Kind.Build, new Vector2(-120, 610), 100, new Color(1, 1, 1, 0.4f));
            Btn("БРОСИТЬ", TouchButton.Kind.Drop, new Vector2(-280, 620), 90, new Color(1, 1, 1, 0.35f));
            Btn("БИНОКЛЬ", TouchButton.Kind.Binoculars, new Vector2(-560, 520), 90, new Color(1, 1, 1, 0.35f));
            Btn("ВИД", TouchButton.Kind.CameraView, new Vector2(-420, 620), 80, new Color(1, 1, 1, 0.35f));
            Btn("МЕСТО", TouchButton.Kind.Seat, new Vector2(-700, 380), 80, new Color(1f, 0.85f, 0.4f, 0.4f));
            // Слоты оружия и пауза — сверху
            for (int i = 0; i < 4; i++)
                Btn((i + 1).ToString(), TouchButton.Kind.Slot1 + i, new Vector2(-470 + i * 105, -70), 90, new Color(1, 1, 1, 0.35f), top: true);
            var pause = Img("Pause", 90, new Color(1, 1, 1, 0.35f));
            Anchor(pause.rectTransform, new Vector2(0, 1), new Vector2(80, -170));
            Label(pause.transform, "II");
            var pb = pause.gameObject.AddComponent<Button>(); pb.onClick.AddListener(GameHUD.TogglePause);
        }

        Image Img(string name, float size, Color c)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            var im = go.GetComponent<Image>(); im.sprite = Circle(); im.color = c;
            im.rectTransform.sizeDelta = new Vector2(size, size);
            return im;
        }

        static void Anchor(RectTransform rt, Vector2 anchor, Vector2 pos)
        {
            rt.anchorMin = rt.anchorMax = anchor; rt.pivot = new Vector2(0.5f, 0.5f); rt.anchoredPosition = pos;
        }

        static void Label(Transform parent, string text)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.text = text; t.alignment = TextAnchor.MiddleCenter; t.color = new Color(1, 1, 1, 0.95f);
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = 26; t.fontStyle = FontStyle.Bold; t.raycastTarget = false;
            t.resizeTextForBestFit = true; t.resizeTextMinSize = 12; t.resizeTextMaxSize = 30;
            var rt = t.rectTransform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = new Vector2(8, 8); rt.offsetMax = new Vector2(-8, -8);
        }

        void Btn(string text, TouchButton.Kind kind, Vector2 pos, float size, Color c, bool toggle = false, bool top = false)
        {
            var im = Img("Btn_" + kind, size, c);
            Anchor(im.rectTransform, top ? new Vector2(1, 1) : new Vector2(1, 0), pos);
            Label(im.transform, text);
            var b = im.gameObject.AddComponent<TouchButton>();
            b.kind = kind; b.toggle = toggle; b.layoutId = kind.ToString().ToLowerInvariant();
            b.dragAlsoLooks = kind == TouchButton.Kind.Fire;
        }

        static void SetField(object target, string field, object value)
        {
            var f = target.GetType().GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            f?.SetValue(target, value);
        }

        void Update()
        {
            bool show = !GameHUD.BlocksTouch;
            if (_canvas.enabled != show)
            {
                _canvas.enabled = show;
                if (!show) GameInput.Touch = default;   // отпустить всё, что было нажато
            }
        }
    }
}
