using UnityEngine;
using UnityEngine.EventSystems;

namespace Frontline
{
    /// <summary>
    /// Режим настройки сенсорных кнопок (Настройки → Управление → «Расположение кнопок»).
    /// В режиме редактирования кнопки перетаскиваются, масштаб/прозрачность — слайдерами в меню.
    /// </summary>
    public class TouchLayoutEditor : MonoBehaviour
    {
        [SerializeField] RectTransform canvasRoot;
        [SerializeField] TouchButton[] buttons;
        public bool Editing { get; private set; }

        void Start() => ApplySaved();

        public void BeginEdit()
        {
            Editing = true;
            foreach (var b in buttons)
            {
                b.enabled = false;
                var h = b.GetComponent<DragHandle>();
                if (!h) h = b.gameObject.AddComponent<DragHandle>();
                h.canvasRoot = canvasRoot;
                h.enabled = true;
            }
        }

        public void EndEdit(bool save)
        {
            Editing = false;
            foreach (var b in buttons)
            {
                b.enabled = true;
                var h = b.GetComponent<DragHandle>(); if (h) h.enabled = false;
            }
            if (save) Save(); else ApplySaved();
        }

        public void SetScale(TouchButton b, float scale) => b.transform.localScale = Vector3.one * Mathf.Clamp(scale, 0.5f, 2f);

        public void SetOpacity(TouchButton b, float a)
        {
            var g = b.GetComponent<CanvasGroup>();
            if (!g) g = b.gameObject.AddComponent<CanvasGroup>();
            g.alpha = Mathf.Clamp(a, 0.15f, 1f);
        }

        void Save()
        {
            var list = SaveSystem.Settings.touchLayout;
            list.Clear();
            Vector2 size = canvasRoot.rect.size;
            foreach (var b in buttons)
            {
                var rt = (RectTransform)b.transform;
                var g = b.GetComponent<CanvasGroup>();
                list.Add(new TouchButtonLayout
                {
                    id = b.layoutId,
                    anchoredPos = new Vector2(rt.anchoredPosition.x / size.x, rt.anchoredPosition.y / size.y),
                    scale = rt.localScale.x,
                    opacity = g ? g.alpha : 0.7f
                });
            }
            SaveSystem.SaveSettings();
        }

        void ApplySaved()
        {
            Vector2 size = canvasRoot.rect.size;
            foreach (var l in SaveSystem.Settings.touchLayout)
            {
                var b = System.Array.Find(buttons, x => x.layoutId == l.id);
                if (!b) continue;
                var rt = (RectTransform)b.transform;
                rt.anchoredPosition = new Vector2(l.anchoredPos.x * size.x, l.anchoredPos.y * size.y);
                SetScale(b, l.scale);
                SetOpacity(b, l.opacity);
            }
        }

        class DragHandle : MonoBehaviour, IDragHandler
        {
            public RectTransform canvasRoot;
            public void OnDrag(PointerEventData e)
            {
                if (!enabled) return;
                var rt = (RectTransform)transform;
                rt.anchoredPosition += e.delta / canvasRoot.lossyScale.x;
            }
        }
    }
}
