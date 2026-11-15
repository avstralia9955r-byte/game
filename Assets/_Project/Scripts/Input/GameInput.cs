using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Frontline
{
    /// <summary>Снимок ввода за кадр — общий для клавиатуры/мыши и сенсора.</summary>
    public struct PlayerInputState
    {
        public Vector2 move;        // -1..1
        public Vector2 lookDelta;   // градусы за кадр (уже с чувствительностью)
        public bool fireHeld, firePressed, aimHeld;
        public bool reloadPressed, jumpPressed, crouchHeld, sprintHeld;
        public bool buildToggle, knifeSpinPressed, meleePressed, interactPressed, seatPressed, grenadePressed, inspectPressed, viewTogglePressed;
        public bool dropPressed, binoPressed, cameraViewPressed;
        public int weaponSlot;      // 0 = без смены, 1..4
        public float scroll;
    }

    /// <summary>
    /// Ввод только клавиатура+мышь (ПК) и сенсор (мобильные). Геймпады намеренно отключаются:
    /// карта действий не содержит gamepad-биндингов, а подключённые геймпады отключаются в InputSystem.
    /// </summary>
    public static class GameInput
    {
        public static InputActionAsset Asset { get; private set; }
        static InputActionMap _map;
        static InputAction _move, _look, _fire, _aim, _reload, _jump, _crouch, _sprint,
                           _build, _knifeSpin, _melee, _interact, _seat, _grenade, _drop, _bino, _camView, _inspect, _view, _slot5, _slot1, _slot2, _slot3, _slot4, _scroll;

        /// <summary>Сенсорный слой пишет сюда (см. TouchControls).</summary>
        public static PlayerInputState Touch;

        public static bool Initialized => Asset != null;

        public static void Init()
        {
            if (Initialized) return;

            Asset = ScriptableObject.CreateInstance<InputActionAsset>();
            _map = Asset.AddActionMap("Player");

            _move = _map.AddAction("Move", InputActionType.Value);
            _move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");

            _look = _map.AddAction("Look", InputActionType.Value, "<Mouse>/delta");
            _fire = _map.AddAction("Fire", InputActionType.Button, "<Mouse>/leftButton");
            _aim = _map.AddAction("Aim", InputActionType.Button, "<Mouse>/rightButton");
            _reload = _map.AddAction("Reload", InputActionType.Button, "<Keyboard>/r");
            _jump = _map.AddAction("Jump", InputActionType.Button, "<Keyboard>/space");
            _crouch = _map.AddAction("Crouch", InputActionType.Button, "<Keyboard>/leftCtrl");
            _crouch.AddBinding("<Keyboard>/c");
            _sprint = _map.AddAction("Sprint", InputActionType.Button, "<Keyboard>/leftShift");
            _build = _map.AddAction("BuildMode", InputActionType.Button, "<Keyboard>/b");
            _knifeSpin = _map.AddAction("KnifeSpin", InputActionType.Button, "<Keyboard>/f");
            _melee = _map.AddAction("Melee", InputActionType.Button, "<Keyboard>/v");
            _interact = _map.AddAction("Interact", InputActionType.Button, "<Keyboard>/e");
            _grenade = _map.AddAction("Grenade", InputActionType.Button, "<Keyboard>/q");
            _drop = _map.AddAction("DropWeapon", InputActionType.Button, "<Keyboard>/g");
            _bino = _map.AddAction("Binoculars", InputActionType.Button, "<Keyboard>/z");
            _camView = _map.AddAction("CameraView", InputActionType.Button, "<Keyboard>/f5");
            _inspect = _map.AddAction("Inspect", InputActionType.Button, "<Keyboard>/t");
            _view = _map.AddAction("ViewToggle", InputActionType.Button, "<Keyboard>/h");
            _seat = _map.AddAction("SwitchSeat", InputActionType.Button, "<Keyboard>/x");
            _slot5 = _map.AddAction("Slot5", InputActionType.Button, "<Keyboard>/5");
            _slot1 = _map.AddAction("Slot1", InputActionType.Button, "<Keyboard>/1");
            _slot2 = _map.AddAction("Slot2", InputActionType.Button, "<Keyboard>/2");
            _slot3 = _map.AddAction("Slot3", InputActionType.Button, "<Keyboard>/3");
            _slot4 = _map.AddAction("Slot4", InputActionType.Button, "<Keyboard>/4");
            _scroll = _map.AddAction("Scroll", InputActionType.Value, "<Mouse>/scroll/y");

            LoadBindingOverrides();
            DisableGamepads();
            InputSystem.onDeviceChange += OnDeviceChange;
            _map.Enable();
        }

        static void DisableGamepads()
        {
            foreach (var d in InputSystem.devices)
                if (d is Gamepad || d is Joystick) InputSystem.DisableDevice(d);
        }

        static void OnDeviceChange(InputDevice d, InputDeviceChange change)
        {
            if (change == InputDeviceChange.Added && (d is Gamepad || d is Joystick))
                InputSystem.DisableDevice(d);
        }

        public static void SetEnabled(bool enabled)
        {
            if (enabled) _map.Enable(); else _map.Disable();
            Cursor.lockState = enabled && !Application.isMobilePlatform ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !enabled || Application.isMobilePlatform;
        }

        public static PlayerInputState Read(bool aiming)
        {
            var s = SaveSystem.Settings;
            float sens = (aiming ? s.adsSensitivity : 1f) * s.mouseSensitivity * 0.06f;

            var st = new PlayerInputState
            {
                move = _move.ReadValue<Vector2>(),
                lookDelta = _look.ReadValue<Vector2>() * sens,
                fireHeld = _fire.IsPressed(),
                firePressed = _fire.WasPressedThisFrame(),
                aimHeld = _aim.IsPressed(),
                reloadPressed = _reload.WasPressedThisFrame(),
                jumpPressed = _jump.WasPressedThisFrame(),
                crouchHeld = _crouch.IsPressed(),
                sprintHeld = _sprint.IsPressed(),
                buildToggle = _build.WasPressedThisFrame(),
                knifeSpinPressed = _knifeSpin.WasPressedThisFrame(),
                meleePressed = _melee.WasPressedThisFrame(),
                interactPressed = _interact.WasPressedThisFrame(),
                seatPressed = _seat.WasPressedThisFrame(),
                dropPressed = _drop.WasPressedThisFrame(),
                binoPressed = _bino.WasPressedThisFrame(),
                cameraViewPressed = _camView.WasPressedThisFrame(),
                grenadePressed = _grenade.WasPressedThisFrame(),
                inspectPressed = _inspect.WasPressedThisFrame(),
                viewTogglePressed = _view.WasPressedThisFrame(),
                scroll = _scroll.ReadValue<float>(),
                weaponSlot = _slot1.WasPressedThisFrame() ? 1 : _slot2.WasPressedThisFrame() ? 2 :
                             _slot3.WasPressedThisFrame() ? 3 : _slot4.WasPressedThisFrame() ? 4 : _slot5.WasPressedThisFrame() ? 5 : 0,
            };
            if (s.invertY) st.lookDelta.y = -st.lookDelta.y;

            // Сенсорный слой объединяется с клавиатурой (на мобильных клавиатура обычно пустая).
            var t = Touch;
            st.move = Vector2.ClampMagnitude(st.move + t.move, 1f);
            st.lookDelta += t.lookDelta * s.touchLookSensitivity * (aiming ? s.adsSensitivity : 1f);
            st.fireHeld |= t.fireHeld; st.firePressed |= t.firePressed; st.aimHeld |= t.aimHeld;
            st.reloadPressed |= t.reloadPressed; st.jumpPressed |= t.jumpPressed;
            st.crouchHeld |= t.crouchHeld; st.sprintHeld |= t.sprintHeld;
            st.buildToggle |= t.buildToggle; st.knifeSpinPressed |= t.knifeSpinPressed;
            st.meleePressed |= t.meleePressed; st.interactPressed |= t.interactPressed; st.grenadePressed |= t.grenadePressed;
            st.dropPressed |= t.dropPressed; st.binoPressed |= t.binoPressed; st.cameraViewPressed |= t.cameraViewPressed; st.seatPressed |= t.seatPressed;
            if (t.weaponSlot != 0) st.weaponSlot = t.weaponSlot;

            // «Нажатия» сенсора одноразовые — сбрасываем после чтения.
            Touch.firePressed = Touch.reloadPressed = Touch.jumpPressed = Touch.buildToggle =
                Touch.knifeSpinPressed = Touch.meleePressed = Touch.interactPressed = Touch.grenadePressed =
                Touch.dropPressed = Touch.binoPressed = Touch.cameraViewPressed = Touch.seatPressed = false;
            Touch.weaponSlot = 0;
            Touch.lookDelta = Vector2.zero;
            return st;
        }

        // ---------- Переназначение клавиш ----------

        public static InputAction FindAction(string name) => _map.FindAction(name, true);

        /// <summary>Интерактивное переназначение: принимает только клавиатуру и мышь.</summary>
        public static void Rebind(string actionName, int bindingIndex, Action onDone)
        {
            var action = FindAction(actionName);
            action.Disable();
            action.PerformInteractiveRebinding(bindingIndex)
                .WithControlsHavingToMatchPath("<Keyboard>")
                .WithControlsHavingToMatchPath("<Mouse>")
                .WithControlsExcluding("<Mouse>/position")
                .WithControlsExcluding("<Mouse>/delta")
                .WithCancelingThrough("<Keyboard>/escape")
                .OnComplete(op => { op.Dispose(); action.Enable(); SaveBindingOverrides(); onDone?.Invoke(); })
                .OnCancel(op => { op.Dispose(); action.Enable(); onDone?.Invoke(); })
                .Start();
        }

        public static void ResetBindings()
        {
            Asset.RemoveAllBindingOverrides();
            SaveBindingOverrides();
        }

        static void SaveBindingOverrides()
        {
            SaveSystem.Settings.keyBindingOverridesJson = Asset.SaveBindingOverridesAsJson();
            SaveSystem.SaveSettings();
        }

        static void LoadBindingOverrides()
        {
            var json = SaveSystem.Settings?.keyBindingOverridesJson;
            if (!string.IsNullOrEmpty(json)) Asset.LoadBindingOverridesFromJson(json);
        }
    }
}
