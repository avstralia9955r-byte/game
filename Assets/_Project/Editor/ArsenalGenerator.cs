#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Frontline.EditorTools
{
    /// <summary>
    /// Frontline → Generate Content: создаёт ScriptableObject-данные обеих фракций (по одному образцу в каждой
    /// категории), обвесы, карты и GameDatabase. Модели/звуки назначаются потом в инспекторе.
    /// Характеристики — игровые (баланс), ориентированы на реальные ТТХ: магазин, темп, начальная скорость.
    /// </summary>
    public static class ArsenalGenerator
    {
        const string Root = "Assets/_Project/Data";

        struct W
        {
            public string id, name; public WeaponCategory cat; public FireMode mode; public float rpm, dmg, mv, range;
            public int mag, reserve; public ReloadType rt; public float reload, reloadEmpty, recV, recH, hip, ads, adsFov, move, loud;
            public SoldierClass[] classes;
        }

        static W Wp(string id, string name, WeaponCategory c, FireMode m, float rpm, float dmg, float mv, float range, int mag, int reserve,
                    ReloadType rt, float reload, float recV, float recH, float hip, float adsFov, float move, float loud) =>
            new() { id = id, name = name, cat = c, mode = m, rpm = rpm, dmg = dmg, mv = mv, range = range, mag = mag, reserve = reserve,
                    rt = rt, reload = reload, reloadEmpty = reload * 1.2f, recV = recV, recH = recH, hip = hip, ads = 0.25f,
                    adsFov = adsFov, move = move, loud = loud };

        static readonly W[] Germany =
        {
            Wp("kar98k",  "Kar98k",       WeaponCategory.SniperRifle,   FireMode.Bolt, 50,  98, 760, 500,  5, 40, ReloadType.StripperClip, 2.0f, 4.5f, 0.6f, 4.0f, 55, 0.95f, 230),
            Wp("stg44",   "StG-44",       WeaponCategory.AssaultRifle,  FireMode.Auto, 550, 30, 685, 220, 30,150, ReloadType.Magazine,     2.6f, 1.15f,0.45f,3.2f, 58, 0.95f, 160),
            Wp("mp40",    "MP-40",        WeaponCategory.SubmachineGun, FireMode.Auto, 500, 23, 400, 110, 32,192, ReloadType.Magazine,     2.4f, 0.75f,0.35f,2.6f, 62, 1.00f, 120),
            Wp("p38",     "Walther P38",  WeaponCategory.Pistol,        FireMode.Semi, 400, 28, 365,  60,  8, 48, ReloadType.Magazine,     1.7f, 1.6f, 0.5f, 2.2f, 66, 1.05f, 100),
            Wp("p08",     "Luger P08",    WeaponCategory.Pistol2,       FireMode.Semi, 450, 27, 350,  60,  8, 48, ReloadType.Magazine,     1.8f, 1.5f, 0.45f,2.2f, 66, 1.05f, 100),
            Wp("mg42",    "MG-42",        WeaponCategory.MachineGun,    FireMode.Auto,1200, 30, 740, 260, 50,250, ReloadType.Magazine,     5.5f, 0.8f, 0.6f, 5.0f, 60, 0.80f, 210),
            Wp("fg42",    "FG-42",        WeaponCategory.BattleRifle,   FireMode.Auto, 750, 38, 760, 300, 20,120, ReloadType.Magazine,     2.7f, 2.0f, 0.7f, 3.6f, 54, 0.92f, 200),
            Wp("bayonet_de","Штык-нож S84/98", WeaponCategory.Knife,    FireMode.Semi, 90,  55,   0,   2,  1,  0, ReloadType.Magazine,     0f,   0f,   0f,   0f,   80, 1.10f,   4),
        };

        static readonly W[] USSR =
        {
            Wp("mosin",   "Мосин-Наган",  WeaponCategory.SniperRifle,   FireMode.Bolt, 48, 100, 865, 520,  5, 40, ReloadType.StripperClip, 2.1f, 4.8f, 0.6f, 4.0f, 55, 0.95f, 235),
            Wp("avt40",   "АВТ-40",       WeaponCategory.AssaultRifle,  FireMode.Auto, 700, 42, 840, 260, 10, 90, ReloadType.Magazine,     2.5f, 2.2f, 0.8f, 3.4f, 56, 0.94f, 185),
            Wp("ppsh41",  "ППШ-41",       WeaponCategory.SubmachineGun, FireMode.Auto,1000, 19, 488, 100, 71,284, ReloadType.Magazine,     3.6f, 0.6f, 0.4f, 3.0f, 62, 0.97f, 125),
            Wp("tt33",    "ТТ",           WeaponCategory.Pistol,        FireMode.Semi, 400, 30, 420,  60,  8, 48, ReloadType.Magazine,     1.7f, 1.7f, 0.55f,2.3f, 66, 1.05f, 105),
            Wp("nagant",  "Наган",        WeaponCategory.Pistol2,       FireMode.Semi, 150, 34, 272,  50,  7, 42, ReloadType.SingleRound,  5.0f, 2.0f, 0.4f, 2.0f, 66, 1.05f,  95),
            Wp("dp28",    "ДП-28",        WeaponCategory.MachineGun,    FireMode.Auto, 550, 32, 840, 260, 47,235, ReloadType.Magazine,     4.8f, 1.0f, 0.5f, 4.6f, 60, 0.82f, 200),
            Wp("avs36",   "АВС-36",       WeaponCategory.BattleRifle,   FireMode.Auto, 800, 38, 840, 300, 15,105, ReloadType.Magazine,     2.8f, 2.4f, 0.8f, 3.6f, 54, 0.92f, 200),
            Wp("bayonet_su","Штык-нож СВТ", WeaponCategory.Knife,       FireMode.Semi, 90,  55,   0,   2,  1,  0, ReloadType.Magazine,     0f,   0f,   0f,   0f,   80, 1.10f,   4),
        };

        struct A { public string id, name, desc, ach; public AttachmentType t; public AttachmentSlot s; public int price;
                   public float loud, rv, rh, hip, mag, adsT, fov; public bool flash, overlay; public WeaponCategory[] cats; }

        static readonly WeaponCategory[] Guns = { WeaponCategory.SniperRifle, WeaponCategory.AssaultRifle, WeaponCategory.SubmachineGun,
            WeaponCategory.Pistol, WeaponCategory.Pistol2, WeaponCategory.MachineGun, WeaponCategory.BattleRifle };

        static readonly A[] Attachments =
        {
            new() { id = "suppressor", name = "Глушитель", desc = "Выстрел почти не слышен ботам; тише звук, нет вспышки. Небольшое снижение отдачи.",
                    t = AttachmentType.Suppressor, s = AttachmentSlot.Muzzle, price = 1200, ach = "kills_25_match",
                    loud = 0.05f, rv = 0.92f, rh = 1f, hip = 1f, mag = 1f, adsT = 1.05f, flash = true,
                    cats = new[] { WeaponCategory.SniperRifle, WeaponCategory.AssaultRifle, WeaponCategory.SubmachineGun, WeaponCategory.Pistol, WeaponCategory.Pistol2, WeaponCategory.BattleRifle } },
            new() { id = "muzzle_brake", name = "Дульный тормоз", desc = "−25% вертикальной отдачи, но выстрел громче (+40% радиус слышимости).",
                    t = AttachmentType.MuzzleBrake, s = AttachmentSlot.Muzzle, price = 600,
                    loud = 1.4f, rv = 0.75f, rh = 0.85f, hip = 1f, mag = 1f, adsT = 1f,
                    cats = new[] { WeaponCategory.SniperRifle, WeaponCategory.AssaultRifle, WeaponCategory.MachineGun, WeaponCategory.BattleRifle } },
            new() { id = "ext_mag", name = "Улучшенный магазин", desc = "+50% патронов в магазине, чуть медленнее прицеливание.",
                    t = AttachmentType.ExtendedMag, s = AttachmentSlot.Magazine, price = 700,
                    loud = 1f, rv = 1f, rh = 1f, hip = 1f, mag = 1.5f, adsT = 1.1f, cats = Guns },
            new() { id = "scope_4x", name = "Оптика 4×", desc = "Снайперский прицел (ПУ / ZF39): сильное увеличение.",
                    t = AttachmentType.Scope, s = AttachmentSlot.Optic, price = 900, ach = "first_blood",
                    loud = 1f, rv = 1f, rh = 1f, hip = 1.1f, mag = 1f, adsT = 1.3f, fov = 18f, overlay = true,
                    cats = new[] { WeaponCategory.SniperRifle } },
            new() { id = "scope_2x", name = "Оптика 2×", desc = "Короткий прицел для самозарядных винтовок.",
                    t = AttachmentType.Scope, s = AttachmentSlot.Optic, price = 700,
                    loud = 1f, rv = 1f, rh = 1f, hip = 1.05f, mag = 1f, adsT = 1.15f, fov = 36f,
                    cats = new[] { WeaponCategory.BattleRifle } },
            new() { id = "grip", name = "Тактическая рукоятка", desc = "−20% вертикальной отдачи.",
                    t = AttachmentType.TacticalGrip, s = AttachmentSlot.Underbarrel, price = 650,
                    loud = 1f, rv = 0.8f, rh = 0.95f, hip = 1f, mag = 1f, adsT = 1.05f,
                    cats = new[] { WeaponCategory.AssaultRifle, WeaponCategory.SubmachineGun, WeaponCategory.MachineGun, WeaponCategory.BattleRifle } },
            new() { id = "laser", name = "Лазерный целеуказатель", desc = "−35% разброса от бедра.",
                    t = AttachmentType.LaserSight, s = AttachmentSlot.Laser, price = 750, ach = "builder_200",
                    loud = 1f, rv = 1f, rh = 1f, hip = 0.65f, mag = 1f, adsT = 1f,
                    cats = new[] { WeaponCategory.AssaultRifle, WeaponCategory.SubmachineGun, WeaponCategory.Pistol, WeaponCategory.Pistol2 } },
        };

        [MenuItem("Frontline/Generate Content")]
        public static void Generate()
        {
            Dir(Root); Dir(Root + "/Weapons"); Dir(Root + "/Attachments"); Dir(Root + "/Maps"); Dir("Assets/_Project/Resources");
            AssetDatabase.Refresh();

            var atts = new List<AttachmentDefinition>();
            foreach (var a in Attachments)
            {
                var so = Load<AttachmentDefinition>($"{Root}/Attachments/ATT_{a.id}.asset");
                so.id = a.id; so.displayName = a.name; so.description = a.desc; so.type = a.t; so.slot = a.s; so.price = a.price;
                so.requiredAchievementId = a.ach; so.loudnessMultiplier = a.loud; so.verticalRecoilMultiplier = a.rv;
                so.horizontalRecoilMultiplier = a.rh; so.hipSpreadMultiplier = a.hip; so.magazineMultiplier = a.mag;
                so.adsTimeMultiplier = a.adsT; so.adsFovOverride = a.fov; so.suppressesMuzzleFlash = a.flash; so.usesScopeOverlay = a.overlay;
                if (a.t == AttachmentType.Scope) so.hideBaseParts = new[] { "IronSights" };
                if (a.t == AttachmentType.ExtendedMag) so.hideBaseParts = new[] { "Mag_Standard" };
                EditorUtility.SetDirty(so);
                atts.Add(so);
            }

            var de = BuildArsenal(Faction.Germany, "Германия", Germany, atts);
            var su = BuildArsenal(Faction.USSR, "СССР", USSR, atts);

            var summer = Load<MapDefinition>($"{Root}/Maps/MAP_Normandy.asset");
            summer.id = "normandy"; summer.displayName = "Нормандия (лето)"; summer.sceneName = "Map_Normandy";
            summer.theme = MapTheme.Summer; summer.camo = CamoController.DefaultFor(MapTheme.Summer); summer.defaultWeather = Weather.Clear;
            summer.minimapWorldMin = new Vector2(-200, -200); summer.minimapWorldMax = new Vector2(200, 200);
            EditorUtility.SetDirty(summer);

            var winter = Load<MapDefinition>($"{Root}/Maps/MAP_Ardennes.asset");
            winter.id = "ardennes"; winter.displayName = "Арденны (зима)"; winter.sceneName = "Map_Ardennes";
            winter.theme = MapTheme.Winter; winter.camo = CamoController.DefaultFor(MapTheme.Winter); winter.defaultWeather = Weather.Snow;
            winter.minimapWorldMin = new Vector2(-200, -200); winter.minimapWorldMax = new Vector2(200, 200);
            EditorUtility.SetDirty(winter);

            var db = Load<GameDatabase>("Assets/_Project/Resources/GameDatabase.asset");
            db.germany = de; db.ussr = su; db.attachments = atts; db.maps = new List<MapDefinition> { summer, winter };
            EditorUtility.SetDirty(db);
            AssetDatabase.SaveAssets();
            Debug.Log("[Frontline] Контент сгенерирован: 16 образцов оружия, 7 обвесов, 2 карты.");
        }

        static FactionArsenal BuildArsenal(Faction f, string name, W[] list, List<AttachmentDefinition> atts)
        {
            var ars = Load<FactionArsenal>($"{Root}/ARS_{f}.asset");
            ars.faction = f; ars.displayName = name; ars.weapons = new List<WeaponDefinition>();
            foreach (var w in list)
            {
                var so = Load<WeaponDefinition>($"{Root}/Weapons/WPN_{w.id}.asset");
                so.id = w.id; so.displayName = w.name; so.faction = f; so.category = w.cat; so.fireMode = w.mode;
                so.roundsPerMinute = w.rpm; so.damage = w.dmg; so.muzzleVelocity = w.mv; so.maxRange = w.range;
                so.magazineSize = w.mag; so.reserveAmmo = w.reserve; so.reloadType = w.rt; so.reloadTime = w.reload; so.reloadTimeEmpty = w.reloadEmpty;
                so.recoilVertical = w.recV; so.recoilHorizontal = w.recH; so.hipSpread = w.hip; so.adsFov = w.adsFov;
                so.moveSpeedMultiplier = w.move; so.loudnessRadius = w.loud; so.clipSize = 5;
                so.adsSpread = w.cat == WeaponCategory.SniperRifle ? 0.02f : w.cat == WeaponCategory.MachineGun ? 0.9f : 0.2f;
                so.headshotMultiplier = w.cat == WeaponCategory.SniperRifle ? 2.5f : 1.8f;
                so.damageFalloff = AnimationCurve.Linear(0, 1, Mathf.Max(1, w.range), w.cat == WeaponCategory.SniperRifle ? 0.85f : 0.55f);
                so.compatibleAttachments = atts.FindAll(a => System.Array.IndexOf(Find(a.id).cats, w.cat) >= 0);
                EditorUtility.SetDirty(so);
                ars.weapons.Add(so);
            }

            WeaponDefinition G(WeaponCategory c) => ars.weapons.Find(x => x.category == c);
            ars.classes = new List<ClassLoadout>
            {
                new() { soldierClass = SoldierClass.Assault,  primary = G(WeaponCategory.AssaultRifle),  primaryAlt = G(WeaponCategory.BattleRifle),
                        secondary = G(WeaponCategory.Pistol),  melee = G(WeaponCategory.Knife), hasTrowel = true },
                new() { soldierClass = SoldierClass.Engineer, primary = G(WeaponCategory.SubmachineGun), secondary = G(WeaponCategory.Pistol2), melee = G(WeaponCategory.Knife), hasTrowel = true },
                new() { soldierClass = SoldierClass.Support,  primary = G(WeaponCategory.MachineGun),    secondary = G(WeaponCategory.Pistol),  melee = G(WeaponCategory.Knife), hasTrowel = false },
                new() { soldierClass = SoldierClass.Recon,    primary = G(WeaponCategory.SniperRifle),   secondary = G(WeaponCategory.Pistol2), melee = G(WeaponCategory.Knife), hasTrowel = false },
            };
            // FG-42 / АВС-36 — альтернативное основное оружие штурмовика (переключатель в меню офлайн-игры).
            EditorUtility.SetDirty(ars);
            return ars;
        }

        static A Find(string id) => System.Array.Find(Attachments, a => a.id == id);

        static T Load<T>(string path) where T : ScriptableObject
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a) return a;
            a = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(a, path);
            return a;
        }

        static void Dir(string p) { if (!Directory.Exists(p)) Directory.CreateDirectory(p); }
    }
}
#endif
