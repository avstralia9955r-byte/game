using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.EnhancedTouch;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace Frontline
{
    /// <summary>
    /// Сенсорное управление: плавающий джойстик слева, область обзора справа.
    /// Пальцы, начавшиеся на UI-кнопке, игнорируются здесь (их обрабатывает TouchButton).
    /// </summary>
    public class TouchControls : MonoBehaviour
    {
        [SerializeField] RectTransform joystickBase, joystickKnob;
        [SerializeField] float joystickRadiusPx = 120f;
        [SerializeField] float sprintThreshold = 0.95f;   // джойстик до упора = бег
        [SerializeField] float lookDegreesPerPixel = 0.12f;

        int _moveFinger = -1, _lookFinger = -1;
        Vector2 _moveOrigin;

        void OnEnable()
        {
            if (!Application.isMobilePlatform && !Application.isEditor) { gameObject.SetActive(false); return; }
            EnhancedTouchSupport.Enable();
        }

        void OnDisable() { if (EnhancedTouchSupport.enabled) EnhancedTouchSupport.Disable(); }

        void Update()
        {
            foreach (var t in ETouch.activeTouches)
            {
                int id = t.touchId;
                switch (t.phase)
                {
                    case UnityEngine.InputSystem.TouchPhase.Began:
                        if (IsOverUI(t.screenPosition)) break;
                        if (t.screenPosition.x < Screen.width * 0.45f && _moveFinger < 0)
                        {
                            _moveFinger = id; _moveOrigin = t.screenPosition;
                            if (joystickBase) { joystickBase.position = _moveOrigin; joystickBase.gameObject.SetActive(true); }
                        }
                        else if (_lookFinger < 0) _lookFinger = id;
                        break;

                    case UnityEngine.InputSystem.TouchPhase.Moved:
                    case UnityEngine.InputSystem.TouchPhase.Stationary:
                        if (id == _moveFinger)
                        {
                            Vector2 d = Vector2.ClampMagnitude(t.screenPosition - _moveOrigin, joystickRadiusPx);
                            GameInput.Touch.move = d / joystickRadiusPx;
                            GameInput.Touch.sprintHeld = GameInput.Touch.move.y > sprintThreshold;
                            if (joystickKnob) joystickKnob.position = _moveOrigin + d;
                        }
                        else if (id == _lookFinger)
                        {
                            // Нормализация к DPI, чтобы чувствительность совпадала на разных экранах.
                            float dpiScale = Screen.dpi > 0 ? 160f / Screen.dpi : 1f;
                            GameInput.Touch.lookDelta += t.delta * lookDegreesPerPixel * dpiScale;
                        }
                        break;

                    case UnityEngine.InputSystem.TouchPhase.Ended:
                    case UnityEngine.InputSystem.TouchPhase.Canceled:
                        if (id == _moveFinger)
                        {
                            _moveFinger = -1; GameInput.Touch.move = Vector2.zero; GameInput.Touch.sprintHeld = false;
                            if (joystickBase) joystickBase.gameObject.SetActive(false);
                        }
                        if (id == _lookFinger) _lookFinger = -1;
                        break;
                }
            }
        }

        static readonly System.Collections.Generic.List<RaycastResult> _hits = new();
        static bool IsOverUI(Vector2 pos)
        {
            if (!EventSystem.current) return false;
            var ped = new PointerEventData(EventSystem.current) { position = pos };
            _hits.Clear();
            EventSystem.current.RaycastAll(ped, _hits);
            foreach (var h in _hits) if (h.gameObject.GetComponentInParent<TouchButton>()) return true;
            return false;
        }
    }

}
