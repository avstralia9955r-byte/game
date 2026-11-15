using System.Collections.Generic;
using UnityEngine;

namespace Frontline
{
    /// <summary>
    /// Магазин главного меню: классы, оружие, бинокль. Бесплатно — только класс «Штурмовик» и его пистолет
    /// (нож, гранаты и шпатель у всех). Всё остальное покупается за валюту, которую дают за матчи.
    /// В бою можно подобрать любое оружие с земли — это не покупка, оно только до смерти.
    /// </summary>
    public static class Shop
    {
        public const int BinocularsPrice = 300;

        /// <summary>Сколько слотов у класса в меню: у поддержки 4 (ещё противотанковое), у остальных 3.</summary>
        public static int SlotCount(SoldierClass c) => c == SoldierClass.Support ? 4 : 3;

        /// <summary>Классы слева направо по цене.</summary>
        public static readonly SoldierClass[] ClassOrder = { SoldierClass.Assault, SoldierClass.Recon, SoldierClass.Engineer, SoldierClass.Support };
        static ProgressData P => SaveSystem.Progress;

        public static int ClassPrice(SoldierClass c) => c switch
        {
            // По ценности: штурмовик (бесплатно) < снайпер < инженер < поддержка
            SoldierClass.Assault => 0, SoldierClass.Recon => 2000, SoldierClass.Engineer => 3000, _ => 5000
        };

        public static string ClassName(SoldierClass c) => c switch
        {
            SoldierClass.Assault => "Штурмовик", SoldierClass.Engineer => "Инженер", SoldierClass.Support => "Поддержка", _ => "Снайпер"
        };

        public static bool ClassOwned(SoldierClass c) => c == SoldierClass.Assault || (P != null && P.ownedClasses.Contains(c.ToString()));

        public static bool WeaponOwned(WeaponDefinition w)
        {
            if (!w) return false;
            if (w.category == WeaponCategory.Knife || w.price <= 0) return true;
            return P != null && P.ownedWeapons.Contains(w.id);
        }

        public static bool HasBinoculars => P != null && P.ownedItems.Contains("binoculars");

        static bool Pay(int price)
        {
            if (P == null || P.currency < price) return false;
            P.currency -= price;
            return true;
        }

        public static int Coins => P != null ? P.currency : 0;
        /// <summary>Разовая трата прямо в бою (например, нанять заряжающего к орудию).</summary>
        public static bool Spend(int price) { if (!Pay(price)) return false; SaveSystem.SaveProgress(); return true; }
        public const int LoaderPrice = 200;

        public static bool BuyClass(SoldierClass c)
        {
            if (ClassOwned(c)) return true;
            if (!Pay(ClassPrice(c))) return false;
            P.ownedClasses.Add(c.ToString()); SaveSystem.SaveProgress(); return true;
        }

        public static bool BuyWeapon(WeaponDefinition w)
        {
            if (!w || WeaponOwned(w)) return true;
            if (!Pay(w.price)) return false;
            P.ownedWeapons.Add(w.id); SaveSystem.SaveProgress(); return true;
        }

        // ---- Специализации (покупаются один раз, действуют для любого класса) ----
        public const string PerkArtillerist = "perk_artillerist", PerkTanker = "perk_tanker";
        public static readonly string[] Perks = { PerkArtillerist, PerkTanker };
        public static int PerkPrice(string id) => id == PerkArtillerist ? 8000 : 6000;
        public static string PerkName(string id) => id == PerkArtillerist ? "Артиллерист" : "Танкист";
        public static string PerkInfo(string id) => id == PerkArtillerist ? "промах у орудия 5% (без — 60%)" : "по вашему танку попадают в 70% (без — 95%)";
        public static bool HasPerk(string id) => P != null && P.ownedItems.Contains(id);
        public static bool BuyPerk(string id)
        {
            if (HasPerk(id)) return true;
            if (!Pay(PerkPrice(id))) return false;
            P.ownedItems.Add(id); SaveSystem.SaveProgress(); return true;
        }

        public static bool BuyBinoculars()
        {
            if (HasBinoculars) return true;
            if (!Pay(BinocularsPrice)) return false;
            P.ownedItems.Add("binoculars"); SaveSystem.SaveProgress(); return true;
        }

        /// <summary>Варианты для слота класса (1 основное, 2 пистолет, 3 второстепенное) — от дешёвого к дорогому.</summary>
        public static List<WeaponDefinition> SlotOptions(Faction f, SoldierClass c, int slot)
        {
            var res = new List<WeaponDefinition>();
            var ars = GameDatabase.Instance ? GameDatabase.Instance.Arsenal(f) : null;
            var lo = ars ? ars.GetClass(c) : null;
            if (lo == null) return res;
            if (slot == 1) { if (lo.primaryOptions != null) res.AddRange(lo.primaryOptions); }
            else if (slot == 2)
            {
                // Второстепенное: пистолеты или слабенькие пистолеты-пулемёты (два самых дешёвых ПП стороны)
                res.AddRange(ars.weapons.FindAll(w => w && w.category is WeaponCategory.Pistol or WeaponCategory.Pistol2));
                var smg = ars.weapons.FindAll(w => w && w.category == WeaponCategory.SubmachineGun);
                smg.Sort((a, b) => a.price.CompareTo(b.price));
                for (int i = 0; i < smg.Count && i < 2; i++) res.Add(smg[i]);
            }
            else if (slot == 3)
            {
                // Нож; у инженера — нож, гаечный ключ (ремонт техники) или шпатель (стройка)
                var knife = ars.weapons.Find(w => w && w.category == WeaponCategory.Knife && w.id.StartsWith("bayonet"));
                if (knife) res.Add(knife);
                if (c == SoldierClass.Engineer)
                {
                    var wr = ars.weapons.Find(w => w && w.id.StartsWith("wrench")); if (wr) res.Add(wr);
                }
            }
            else if (slot == 4) { if (lo.gadget) res.Add(lo.gadget); }       // противотанковое — только поддержка
            res.Sort((a, b) => a.price.CompareTo(b.price));
            return res;
        }

        /// <summary>Что стоит в слоте класса: выбранное игроком, если куплено; иначе первое купленное; иначе null.</summary>
        public static WeaponDefinition Chosen(Faction f, SoldierClass c, int slot)
        {
            var opts = SlotOptions(f, c, slot);
            WeaponDefinition pick = null;
            if (slot == 1) pick = LoadoutChoice.Get(f, c);
            else
            {
                string id = LoadoutChoice.GetSlotId(f, c, slot);
                if (id != null) pick = opts.Find(w => w.id == id);
                if (!pick && slot == 3 && c == SoldierClass.Engineer) pick = opts.Find(w => w.id.StartsWith("wrench"));   // по умолчанию — гаечный ключ
                if (!pick && slot == 4) pick = GameDatabase.Instance.Arsenal(f).GetClass(c).gadget;
                if (!pick && slot == 2) pick = GameDatabase.Instance.Arsenal(f).GetClass(c).secondary;
            }
            if (pick && opts.Contains(pick) && WeaponOwned(pick)) return pick;
            foreach (var w in opts) if (WeaponOwned(w)) return w;
            return null;
        }

        /// <summary>Всё оружие класса (для меню): основные варианты, пистолет, гаджет.</summary>
        public static List<WeaponDefinition> ClassWeapons(Faction f, SoldierClass c)
        {
            var res = new List<WeaponDefinition>();
            var lo = GameDatabase.Instance ? GameDatabase.Instance.Arsenal(f)?.GetClass(c) : null;
            if (lo == null) return res;
            if (lo.primaryOptions != null) foreach (var w in lo.primaryOptions) if (w && !res.Contains(w)) res.Add(w);
            if (lo.secondary && !res.Contains(lo.secondary)) res.Add(lo.secondary);
            if (lo.gadget && !res.Contains(lo.gadget)) res.Add(lo.gadget);
            if (lo.melee && lo.melee.id.StartsWith("wrench") && !res.Contains(lo.melee)) res.Add(lo.melee);   // ключ инженера — бесплатно
            return res;
        }

        /// <summary>Снаряжение класса с учётом покупок: некупленное оружие не выдаётся.</summary>
        public static ClassLoadout OwnedLoadout(Faction f, SoldierClass c)
        {
            var lo = GameDatabase.Instance.Arsenal(f).GetClass(c);
            var third = Chosen(f, c, 3);
            bool trowel = third && third.id.StartsWith("trowel");
            return new ClassLoadout
            {
                soldierClass = lo.soldierClass, primary = Chosen(f, c, 1), primaryAlt = null, primaryOptions = lo.primaryOptions,
                secondary = Chosen(f, c, 2), melee = trowel ? null : third, hasTrowel = false,
                tool = c == SoldierClass.Assault ? ToolKind.Medkit : c == SoldierClass.Engineer ? ToolKind.Sandbags : ToolKind.None,
                gadget = Chosen(f, c, 4), grenades = lo.grenades
            };
        }

        public static int DefaultPrice(WeaponCategory c, bool basic) => c switch
        {
            WeaponCategory.Knife => 0,
            WeaponCategory.Pistol => 0,                                   // пистолет штурмовика — бесплатно
            WeaponCategory.Pistol2 => 400,
            WeaponCategory.SubmachineGun => basic ? 800 : 1200,
            WeaponCategory.AssaultRifle => basic ? 1500 : 1900,
            WeaponCategory.BattleRifle => basic ? 1700 : 2000,
            WeaponCategory.MachineGun => basic ? 2000 : 2400,
            WeaponCategory.SniperRifle => basic ? 1600 : 2000,
            WeaponCategory.Launcher => 1500,
            _ => 1000
        };
    }
}
