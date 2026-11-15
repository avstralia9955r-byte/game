using UnityEngine;

namespace Frontline
{
    /// <summary>
    /// Выбранное игроком основное оружие для каждого класса и стороны (кастомизация и экран высадки).
    /// Хранится в прогрессе строками «сторона:класс=id».
    /// </summary>
    public static class LoadoutChoice
    {
        static string Key(Faction f, SoldierClass c) => (int)f + ":" + (int)c + "=";

        public static WeaponDefinition Get(Faction f, SoldierClass c)
        {
            var lo = GameDatabase.Instance ? GameDatabase.Instance.Arsenal(f)?.GetClass(c) : null;
            if (lo == null) return null;
            var p = SaveSystem.Progress;
            if (p != null && lo.primaryOptions != null)
            {
                string k = Key(f, c);
                var entry = p.primaryChoices.Find(s => s.StartsWith(k));
                if (entry != null)
                {
                    string id = entry.Substring(k.Length);
                    var w = lo.primaryOptions.Find(x => x && x.id == id);
                    if (w) return w;
                }
            }
            return lo.primary;
        }

        public static void Set(Faction f, SoldierClass c, WeaponDefinition w)
        {
            var p = SaveSystem.Progress;
            if (p == null || !w) return;
            string k = Key(f, c);
            p.primaryChoices.RemoveAll(s => s.StartsWith(k));
            p.primaryChoices.Add(k + w.id);
            SaveSystem.SaveProgress();
        }

        // ---- Слоты 2 (пистолет) и 3 (второстепенное): «сторона:класс:слот=id» ----
        static string SlotKey(Faction f, SoldierClass c, int slot) => (int)f + ":" + (int)c + ":" + slot + "=";

        public static string GetSlotId(Faction f, SoldierClass c, int slot)
        {
            var p = SaveSystem.Progress; if (p == null) return null;
            string k = SlotKey(f, c, slot);
            var e = p.primaryChoices.Find(s => s.StartsWith(k));
            return e?.Substring(k.Length);
        }

        public static void SetSlot(Faction f, SoldierClass c, int slot, WeaponDefinition w)
        {
            if (slot == 1) { Set(f, c, w); return; }
            var p = SaveSystem.Progress; if (p == null || !w) return;
            string k = SlotKey(f, c, slot);
            p.primaryChoices.RemoveAll(s => s.StartsWith(k));
            p.primaryChoices.Add(k + w.id);
            SaveSystem.SaveProgress();
        }

        /// <summary>Следующий вариант по кругу (кнопка «сменить» на экране высадки).</summary>
        public static WeaponDefinition Next(Faction f, SoldierClass c)
        {
            var lo = GameDatabase.Instance ? GameDatabase.Instance.Arsenal(f)?.GetClass(c) : null;
            if (lo == null || lo.primaryOptions == null || lo.primaryOptions.Count == 0) return null;
            var cur = Get(f, c);
            int i = lo.primaryOptions.IndexOf(cur);
            var next = lo.primaryOptions[(i + 1) % lo.primaryOptions.Count];
            Set(f, c, next);
            return next;
        }

        /// <summary>Классы, у которых это оружие — один из вариантов основного.</summary>
        public static SoldierClass? ClassOf(WeaponDefinition w)
        {
            if (!w) return null;
            var ars = GameDatabase.Instance ? GameDatabase.Instance.Arsenal(w.faction) : null;
            if (!ars) return null;
            foreach (var c in ars.classes) if (c.primaryOptions != null && c.primaryOptions.Contains(w)) return c.soldierClass;
            return null;
        }
    }
}
