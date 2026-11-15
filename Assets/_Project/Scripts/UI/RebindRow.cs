using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Frontline
{
    /// <summary>Строка переназначения: «Действие — [клавиша]». Клик → ожидание новой клавиши (Esc — отмена).</summary>
    public class RebindRow : MonoBehaviour
    {
        public TMP_Text actionLabel, bindingLabel;
        public Button button;

        static readonly System.Collections.Generic.Dictionary<string, string> Names = new()
        {
            { "Fire", "Огонь" }, { "Aim", "Прицеливание" }, { "Reload", "Перезарядка" }, { "Jump", "Прыжок" },
            { "Crouch", "Присесть" }, { "Sprint", "Бег" }, { "BuildMode", "Строительство" }, { "KnifeSpin", "Вращение ножа" },
            { "Melee", "Удар ножом" }, { "Interact", "Действие" }, { "Grenade", "Граната" }, { "Move", "Движение" }
        };

        string _action; int _index;

        public void Init(string action, int bindingIndex)
        {
            _action = action; _index = bindingIndex;
            var a = GameInput.FindAction(action);
            string part = a.bindings[bindingIndex].isPartOfComposite ? " (" + a.bindings[bindingIndex].name + ")" : "";
            actionLabel.text = (Names.TryGetValue(action, out var n) ? n : action) + part;
            Refresh();
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() =>
            {
                bindingLabel.text = "…нажмите клавишу";
                GameInput.Rebind(_action, _index, Refresh);
            });
        }

        void Refresh()
        {
            var a = GameInput.FindAction(_action);
            bindingLabel.text = a.GetBindingDisplayString(_index, InputBinding.DisplayStringOptions.DontIncludeInteractions);
        }
    }
}
