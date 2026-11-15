using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Frontline
{

    /// <summary>
    /// Экран кастомизации: фракция → оружие → слоты обвесов. Кнопка «До/После» показывает оружие без/с выбранным
    /// обвесом до покупки. Покупка — только за локальную валюту или по достижению.
    /// </summary>
    public class CustomizationScreen : MonoBehaviour
    {
        public WeaponPreviewStage stage;
        public TMP_Dropdown factionDropdown;
        public Transform weaponListRoot, attachmentListRoot;
        public Button listButtonPrefab;
        public TMP_Text statsText, currencyText, selectedAttachmentText;
        public Button buyButton, installButton, uninstallButton, beforeAfterButton;
        public TMP_Text buyLabel, beforeAfterLabel;

        WeaponDefinition _weapon;
        AttachmentDefinition _selected;
        bool _showingBefore;

        void OnEnable()
        {
            factionDropdown.ClearOptions();
            factionDropdown.AddOptions(new List<string> { "СССР", "Германия" });
            factionDropdown.onValueChanged.RemoveAllListeners();
            factionDropdown.onValueChanged.AddListener(_ => RebuildWeapons());
            buyButton.onClick.RemoveAllListeners(); buyButton.onClick.AddListener(Buy);
            installButton.onClick.RemoveAllListeners(); installButton.onClick.AddListener(Install);
            uninstallButton.onClick.RemoveAllListeners(); uninstallButton.onClick.AddListener(Uninstall);
            beforeAfterButton.onClick.RemoveAllListeners(); beforeAfterButton.onClick.AddListener(ToggleBeforeAfter);
            RebuildWeapons();
        }

        Faction CurrentFaction => factionDropdown.value == 0 ? Faction.USSR : Faction.Germany;

        void RebuildWeapons()
        {
            Clear(weaponListRoot);
            var arsenal = GameDatabase.Instance.Arsenal(CurrentFaction);
            foreach (var w in arsenal.weapons)
            {
                if (!w || w.category == WeaponCategory.Knife || w.category == WeaponCategory.Trowel) continue;
                var b = Instantiate(listButtonPrefab, weaponListRoot);
                b.GetComponentInChildren<TMP_Text>().text = w.displayName;
                var wl = w; b.onClick.AddListener(() => SelectWeapon(wl));
            }
            SelectWeapon(arsenal.weapons.Find(x => x && x.category != WeaponCategory.Knife));
        }

        void SelectWeapon(WeaponDefinition w)
        {
            if (!w) return;
            _weapon = w; _selected = null; _showingBefore = false;
            stage.Show(w, Installed());
            Clear(attachmentListRoot);
            foreach (var a in w.compatibleAttachments)
            {
                if (!a) continue;
                var b = Instantiate(listButtonPrefab, attachmentListRoot);
                bool owned = SaveSystem.Progress.IsUnlocked(a.id);
                b.GetComponentInChildren<TMP_Text>().text = owned ? a.displayName : $"{a.displayName}  ({a.price})";
                var al = a; b.onClick.AddListener(() => SelectAttachment(al));
            }
            Refresh();
        }

        List<AttachmentDefinition> Installed() => AttachmentRules.Resolve(SaveSystem.Progress.GetLoadout(_weapon.id));

        /// <summary>Выбор обвеса = предпросмотр: модель сразу показывается с ним (даже если ещё не куплен).</summary>
        void SelectAttachment(AttachmentDefinition a)
        {
            _selected = a; _showingBefore = false;
            stage.ApplyAttachments(WithSelected());
            Refresh();
        }

        List<AttachmentDefinition> WithSelected()
        {
            var list = Installed();
            if (_selected) { list.RemoveAll(x => x.slot == _selected.slot); list.Add(_selected); }
            return list;
        }

        void ToggleBeforeAfter()
        {
            _showingBefore = !_showingBefore;
            stage.ApplyAttachments(_showingBefore ? Installed() : WithSelected());
            beforeAfterLabel.text = _showingBefore ? "Показать: ПОСЛЕ" : "Показать: ДО";
        }

        void Buy() { if (_selected && AttachmentRules.TryUnlock(_selected)) SelectWeapon(_weapon); }
        void Install() { if (_selected) AttachmentRules.Install(_weapon, _selected); stage.ApplyAttachments(Installed()); Refresh(); }
        void Uninstall() { if (_selected) AttachmentRules.Uninstall(_weapon, _selected.slot); stage.ApplyAttachments(Installed()); Refresh(); }

        void Refresh()
        {
            var p = SaveSystem.Progress;
            currencyText.SetText("Деньги: {0}", (float)p.currency);
            bool owned = _selected && p.IsUnlocked(_selected.id);
            bool installed = _selected && Installed().Contains(_selected);
            buyButton.gameObject.SetActive(_selected && !owned);
            installButton.gameObject.SetActive(owned && !installed);
            uninstallButton.gameObject.SetActive(installed);
            beforeAfterButton.gameObject.SetActive(_selected);
            if (_selected)
            {
                bool byAch = !string.IsNullOrEmpty(_selected.requiredAchievementId);
                buyLabel.text = byAch && !p.achievementIds.Contains(_selected.requiredAchievementId)
                    ? $"Купить {_selected.price} (или достижение)" : $"Купить {_selected.price}";
                buyButton.interactable = p.currency >= _selected.price ||
                                         (byAch && p.achievementIds.Contains(_selected.requiredAchievementId));
                selectedAttachmentText.text = $"<b>{_selected.displayName}</b>\n{_selected.description}";
            }
            else selectedAttachmentText.text = "";

            // Сравнение характеристик: текущая сборка → с выбранным обвесом.
            var before = WeaponStats.Compute(_weapon, Installed());
            var after = WeaponStats.Compute(_weapon, WithSelected());
            statsText.text =
                Line("Магазин", before.magazineSize, after.magazineSize, true) +
                Line("Отдача верт.", before.recoilV, after.recoilV, false) +
                Line("Разброс от бедра", before.hipSpread, after.hipSpread, false) +
                Line("Громкость (м)", before.loudnessRadius, after.loudnessRadius, false) +
                Line("Время прицел. (с)", before.adsTime, after.adsTime, false) +
                $"Урон: {_weapon.damage}   Темп: {_weapon.roundsPerMinute} в/мин";
        }

        static string Line(string name, float a, float b, bool higherBetter)
        {
            string val = Mathf.Approximately(a, b) ? $"{a:0.##}" :
                $"{a:0.##} → <color={((b > a) == higherBetter ? "#7CFC7C" : "#FF7070")}>{b:0.##}</color>";
            return $"{name}: {val}\n";
        }

        static void Clear(Transform t) { for (int i = t.childCount - 1; i >= 0; i--) Destroy(t.GetChild(i).gameObject); }
    }
}
