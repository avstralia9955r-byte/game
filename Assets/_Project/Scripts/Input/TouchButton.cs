using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.EnhancedTouch;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace Frontline
{
    /// <summary>Сенсорная кнопка. id совпадает с TouchButtonLayout.id для сохранения раскладки.</summary>
    public class TouchButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
    {
        public enum Kind { Fire, Aim, Reload, Jump, Crouch, Build, KnifeSpin, Melee, Interact, Grenade, Slot1, Slot2, Slot3, Slot4, Drop, Binoculars, CameraView, Seat }
        public Kind kind;
        public string layoutId;
        [Tooltip("Кнопка огня также поворачивает камеру при перетаскивании — как в большинстве мобильных FPS.")]
        public bool dragAlsoLooks = true;
        [Tooltip("Удержание (прицел/присед) или переключение по нажатию.")]
        public bool toggle;

        bool _toggled;

        public void OnPointerDown(PointerEventData e)
        {
            ref var t = ref GameInput.Touch;
            switch (kind)
            {
                case Kind.Fire: t.fireHeld = true; t.firePressed = true; break;
                case Kind.Aim: if (toggle) { _toggled = !_toggled; t.aimHeld = _toggled; } else t.aimHeld = true; break;
                case Kind.Crouch: if (toggle) { _toggled = !_toggled; t.crouchHeld = _toggled; } else t.crouchHeld = true; break;
                case Kind.Reload: t.reloadPressed = true; break;
                case Kind.Jump: t.jumpPressed = true; break;
                case Kind.Build: t.buildToggle = true; break;
                case Kind.KnifeSpin: t.knifeSpinPressed = true; break;
                case Kind.Melee: t.meleePressed = true; break;
                case Kind.Interact: t.interactPressed = true; break;
                case Kind.Grenade: t.grenadePressed = true; break;
                case Kind.Slot1: t.weaponSlot = 1; break;
                case Kind.Slot2: t.weaponSlot = 2; break;
                case Kind.Slot3: t.weaponSlot = 3; break;
                case Kind.Slot4: t.weaponSlot = 4; break;
                case Kind.Drop: t.dropPressed = true; break;
                case Kind.Binoculars: t.binoPressed = true; break;
                case Kind.CameraView: t.cameraViewPressed = true; break;
                case Kind.Seat: t.seatPressed = true; break;
            }
        }

        public void OnPointerUp(PointerEventData e)
        {
            ref var t = ref GameInput.Touch;
            if (kind == Kind.Fire) t.fireHeld = false;
            if (kind == Kind.Aim && !toggle) t.aimHeld = false;
            if (kind == Kind.Crouch && !toggle) t.crouchHeld = false;
        }

        public void OnDrag(PointerEventData e)
        {
            if (kind == Kind.Fire && dragAlsoLooks)
            {
                float dpiScale = Screen.dpi > 0 ? 160f / Screen.dpi : 1f;
                GameInput.Touch.lookDelta += e.delta * 0.12f * dpiScale;
            }
        }
    }
}
