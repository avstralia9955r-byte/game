using System.Collections.Generic;
using UnityEngine;

namespace Frontline
{
    /// <summary>
    /// Контент по умолчанию в памяти: 2 арсенала × 8 образцов, 7 обвесов. Используется, если в Resources нет
    /// GameDatabase.asset (т.е. «Frontline → Generate Content» ещё не запускали) — игра всё равно стартует.
    /// </summary>
    public static class RuntimeContent
    {
        struct W { public string id, name; public WeaponCategory c; public FireMode m; public float rpm, dmg, mv, range; public int mag, res; public ReloadType rt; public float rel, rv, rh, hip, fov, move, loud; }

        static W Wp(string id, string n, WeaponCategory c, FireMode m, float rpm, float dmg, float mv, float range, int mag, int res, ReloadType rt, float rel, float rv, float rh, float hip, float fov, float move, float loud)
            => new() { id = id, name = n, c = c, m = m, rpm = rpm, dmg = dmg, mv = mv, range = range, mag = mag, res = res, rt = rt, rel = rel, rv = rv, rh = rh, hip = hip, fov = fov, move = move, loud = loud };

        static readonly W[] De =
        {
            Wp("kar98k","Kar98k",WeaponCategory.SniperRifle,FireMode.Bolt,50,98,760,500,5,40,ReloadType.StripperClip,2.0f,4.5f,0.6f,4f,55,0.95f,230),
            Wp("stg44","StG-44",WeaponCategory.AssaultRifle,FireMode.Auto,550,30,685,220,30,150,ReloadType.Magazine,2.6f,1.15f,0.45f,3.2f,58,0.95f,160),
            Wp("mp40","MP-40",WeaponCategory.SubmachineGun,FireMode.Auto,500,23,400,110,32,192,ReloadType.Magazine,2.4f,0.75f,0.35f,2.6f,62,1f,120),
            Wp("p38","Walther P38",WeaponCategory.Pistol,FireMode.Semi,400,28,365,60,8,48,ReloadType.Magazine,1.7f,1.6f,0.5f,2.2f,66,1.05f,100),
            Wp("p08","Luger P08",WeaponCategory.Pistol2,FireMode.Semi,450,27,350,60,8,48,ReloadType.Magazine,1.8f,1.5f,0.45f,2.2f,66,1.05f,100),
            Wp("mg42","MG-42",WeaponCategory.MachineGun,FireMode.Auto,1200,30,740,260,50,250,ReloadType.Magazine,5.5f,0.8f,0.6f,5f,60,0.8f,210),
            Wp("fg42","FG-42",WeaponCategory.BattleRifle,FireMode.Auto,750,38,760,300,20,120,ReloadType.Magazine,2.7f,2f,0.7f,3.6f,54,0.92f,200),
            Wp("bayonet_de","Штык-нож S84/98",WeaponCategory.Knife,FireMode.Semi,90,55,0,2,1,0,ReloadType.Magazine,0,0,0,0,80,1.1f,4),
            Wp("panzerfaust","Panzerfaust 60",WeaponCategory.Launcher,FireMode.Semi,30,160,60,150,1,0,ReloadType.Magazine,3.4f,6f,1.2f,2.4f,58,0.9f,210),
            // Дополнительные образцы — выбираются в кастомизации и на экране высадки
            Wp("g43","Gewehr 43",WeaponCategory.BattleRifle,FireMode.Semi,300,60,746,380,10,80,ReloadType.Magazine,2.9f,2.6f,0.6f,3.8f,52,0.93f,215),
            Wp("mg34","MG-34",WeaponCategory.MachineGun,FireMode.Auto,850,31,755,260,50,250,ReloadType.Magazine,5.2f,0.95f,0.55f,4.8f,60,0.82f,210),
            Wp("mp28","MP-28",WeaponCategory.SubmachineGun,FireMode.Auto,600,22,380,100,32,192,ReloadType.Magazine,2.6f,0.7f,0.35f,2.7f,62,0.98f,118),
            Wp("mp3008","MP 3008",WeaponCategory.SubmachineGun,FireMode.Auto,500,21,365,95,32,192,ReloadType.Magazine,2.3f,0.8f,0.42f,3.0f,62,1.02f,118),
            Wp("vg15","Volkssturmgewehr VG 1-5",WeaponCategory.AssaultRifle,FireMode.Semi,420,36,640,220,30,150,ReloadType.Magazine,2.7f,1.6f,0.55f,3.4f,58,0.95f,165),
            Wp("mp35","MP 35 (Бергманн)",WeaponCategory.SubmachineGun,FireMode.Auto,540,22,390,105,32,192,ReloadType.Magazine,2.8f,0.68f,0.32f,2.6f,62,0.97f,118),
            Wp("mp41","MP 41",WeaponCategory.SubmachineGun,FireMode.Auto,500,23,400,110,32,192,ReloadType.Magazine,2.5f,0.66f,0.33f,2.5f,62,0.97f,120),
            Wp("mg15","MG 15",WeaponCategory.MachineGun,FireMode.Auto,1000,29,750,250,75,225,ReloadType.Magazine,4.6f,0.9f,0.6f,5.2f,60,0.84f,205),
            Wp("mg30","MG 30 (Солотурн)",WeaponCategory.MachineGun,FireMode.Auto,800,31,760,260,25,200,ReloadType.Magazine,3.4f,0.95f,0.55f,4.8f,60,0.85f,205),
            Wp("gew98","Gewehr 98",WeaponCategory.SniperRifle,FireMode.Bolt,46,104,870,560,5,40,ReloadType.StripperClip,2.2f,5.0f,0.6f,4.2f,54,0.93f,240),
            Wp("g3340","Gewehr 33/40",WeaponCategory.SniperRifle,FireMode.Bolt,54,92,740,420,5,40,ReloadType.StripperClip,1.9f,5.4f,0.7f,3.6f,56,0.99f,235),
            Wp("wrench_de","Гаечный ключ",WeaponCategory.Knife,FireMode.Semi,90,40,0,2,1,0,ReloadType.Magazine,0,0,0,0,80,1.08f,4),
        };
        /// <summary>Цены в магазине: внутри класса — по возрастанию силы образца.</summary>
        static readonly Dictionary<string, int> Prices = new()
        {
            // Германия
            ["vg15"] = 1500, ["g43"] = 3000, ["stg44"] = 4000, ["fg42"] = 5000,
            ["mp3008"] = 1200, ["mp28"] = 1800, ["mp41"] = 2400, ["mp35"] = 3000, ["mp40"] = 3800,
            ["mg15"] = 3500, ["mg30"] = 4200, ["mg34"] = 5500, ["mg42"] = 7000,
            ["gew98"] = 1800, ["g3340"] = 2600, ["kar98k"] = 4000,
            ["p08"] = 900, ["panzerfaust"] = 4000,
            // СССР
            ["svt40"] = 1500, ["sks"] = 2200, ["avs36"] = 3000, ["avt40"] = 4000, ["fedorov"] = 5000,
            ["ppd34"] = 1200, ["ppd40"] = 2200, ["pps43"] = 3000, ["ppsh41"] = 3800,
            ["dp28"] = 3500, ["dt"] = 4200, ["dpm"] = 5500,
            ["mosin91"] = 1800, ["m38"] = 2600, ["m44"] = 3200, ["mosin"] = 4000, ["rp46"] = 7000,
            ["nagant"] = 900, ["ptrd"] = 4000,
        };

        static readonly W[] Su =
        {
            Wp("mosin","Мосин-Наган",WeaponCategory.SniperRifle,FireMode.Bolt,48,100,865,520,5,40,ReloadType.StripperClip,2.1f,4.8f,0.6f,4f,55,0.95f,235),
            Wp("avt40","АВТ-40",WeaponCategory.AssaultRifle,FireMode.Auto,700,42,840,260,10,90,ReloadType.Magazine,2.5f,2.2f,0.8f,3.4f,56,0.94f,185),
            Wp("ppsh41","ППШ-41",WeaponCategory.SubmachineGun,FireMode.Auto,1000,19,488,100,71,284,ReloadType.Magazine,3.6f,0.6f,0.4f,3f,62,0.97f,125),
            Wp("tt33","ТТ",WeaponCategory.Pistol,FireMode.Semi,400,30,420,60,8,48,ReloadType.Magazine,1.7f,1.7f,0.55f,2.3f,66,1.05f,105),
            Wp("nagant","Наган",WeaponCategory.Pistol2,FireMode.Semi,150,34,272,50,7,42,ReloadType.SingleRound,5f,2f,0.4f,2f,66,1.05f,95),
            Wp("dp28","ДП-28",WeaponCategory.MachineGun,FireMode.Auto,550,32,840,260,47,235,ReloadType.Magazine,4.8f,1f,0.5f,4.6f,60,0.82f,200),
            Wp("avs36","АВС-36",WeaponCategory.BattleRifle,FireMode.Auto,800,38,840,300,15,105,ReloadType.Magazine,2.8f,2.4f,0.8f,3.6f,54,0.92f,200),
            Wp("bayonet_su","Штык-нож СВТ",WeaponCategory.Knife,FireMode.Semi,90,55,0,2,1,0,ReloadType.Magazine,0,0,0,0,80,1.1f,4),
            Wp("ptrd","ПТРД-41",WeaponCategory.Launcher,FireMode.Bolt,25,150,1012,800,1,20,ReloadType.SingleRound,2.4f,7.5f,1.4f,5f,48,0.85f,280),
            // Дополнительные образцы
            Wp("pps43","ППС-43",WeaponCategory.SubmachineGun,FireMode.Auto,650,20,500,105,35,210,ReloadType.Magazine,2.3f,0.65f,0.35f,2.6f,62,1.02f,120),
            Wp("m44","Карабин обр. 1944",WeaponCategory.SniperRifle,FireMode.Bolt,52,92,820,420,5,40,ReloadType.StripperClip,2.0f,5.2f,0.7f,3.6f,56,0.98f,240),
            Wp("dt","ДТ",WeaponCategory.MachineGun,FireMode.Auto,600,32,840,260,63,252,ReloadType.Magazine,5f,0.95f,0.5f,4.6f,60,0.8f,205),
            Wp("fedorov","Автомат Фёдорова",WeaponCategory.AssaultRifle,FireMode.Auto,600,34,654,240,25,150,ReloadType.Magazine,2.7f,1.5f,0.55f,3.3f,58,0.95f,170),
            Wp("sks","СКС",WeaponCategory.BattleRifle,FireMode.Semi,350,44,735,320,10,90,ReloadType.StripperClip,2.6f,1.9f,0.55f,3.4f,56,0.96f,190),
            Wp("ppd34","ППД-34/38",WeaponCategory.SubmachineGun,FireMode.Auto,850,19,490,100,25,200,ReloadType.Magazine,2.5f,0.62f,0.38f,2.8f,62,0.97f,122),
            Wp("rp46","РП-46",WeaponCategory.MachineGun,FireMode.Auto,650,34,840,300,100,300,ReloadType.Magazine,6.2f,0.9f,0.45f,4.4f,60,0.78f,215),
            Wp("dpm","ДПМ",WeaponCategory.MachineGun,FireMode.Auto,600,32,840,260,47,235,ReloadType.Magazine,4.6f,0.95f,0.48f,4.5f,60,0.82f,200),
            Wp("m38","Карабин обр. 1938",WeaponCategory.SniperRifle,FireMode.Bolt,52,90,820,420,5,40,ReloadType.StripperClip,2.0f,5.2f,0.7f,3.6f,56,0.98f,240),
            Wp("wrench_su","Гаечный ключ",WeaponCategory.Knife,FireMode.Semi,90,40,0,2,1,0,ReloadType.Magazine,0,0,0,0,80,1.08f,4),
            Wp("svt40","СВТ-40",WeaponCategory.BattleRifle,FireMode.Semi,320,58,840,380,10,90,ReloadType.Magazine,2.6f,2.6f,0.65f,3.7f,52,0.93f,215),
            Wp("ppd40","ППД-40",WeaponCategory.SubmachineGun,FireMode.Auto,900,19,490,100,71,284,ReloadType.Magazine,3.8f,0.62f,0.4f,2.9f,62,0.95f,124),
            Wp("mosin91","Мосин обр. 1891/30",WeaponCategory.SniperRifle,FireMode.Bolt,46,104,880,560,5,40,ReloadType.StripperClip,2.2f,5.0f,0.6f,4.2f,54,0.93f,240),
        };

        public static GameDatabase Create()
        {
            var db = ScriptableObject.CreateInstance<GameDatabase>();
            db.name = "GameDatabase (runtime)";
            db.attachments = new List<AttachmentDefinition>
            {
                Att("suppressor","Глушитель",AttachmentType.Suppressor,AttachmentSlot.Muzzle,1200,0.05f,0.92f,1,1,1,1.05f,0,true,false),
                Att("muzzle_brake","Дульный тормоз",AttachmentType.MuzzleBrake,AttachmentSlot.Muzzle,600,1.4f,0.75f,0.85f,1,1,1,0,false,false),
                Att("ext_mag","Улучшенный магазин",AttachmentType.ExtendedMag,AttachmentSlot.Magazine,700,1,1,1,1,1.5f,1.1f,0,false,false),
                Att("scope_4x","Оптика 4×",AttachmentType.Scope,AttachmentSlot.Optic,900,1,1,1,1.1f,1,1.3f,18,false,true),
                Att("scope_2x","Оптика 2×",AttachmentType.Scope,AttachmentSlot.Optic,700,1,1,1,1.05f,1,1.15f,36,false,false),
                Att("grip","Тактическая рукоятка",AttachmentType.TacticalGrip,AttachmentSlot.Underbarrel,650,1,0.8f,0.95f,1,1,1.05f,0,false,false),
                Att("laser","Лазерный целеуказатель",AttachmentType.LaserSight,AttachmentSlot.Laser,750,1,1,1,0.65f,1,1,0,false,false),
                Att("flash_hider","Пламегаситель",AttachmentType.FlashHider,AttachmentSlot.Muzzle,500,0.9f,0.92f,0.95f,1,1,1,0,true,false),
                Att("bayonet","Примкнутый штык",AttachmentType.Bayonet,AttachmentSlot.Muzzle,350,1,0.92f,0.9f,0.9f,1,1.08f,0,false,false),
                Att("drum_mag","Барабанный магазин",AttachmentType.DrumMag,AttachmentSlot.Magazine,1100,1,1.05f,1,1.05f,2.2f,1.18f,0,false,false),
                Att("scope_15x","Оптика 1,5× (разведчика)",AttachmentType.ScopeShort,AttachmentSlot.Optic,600,1,1,1,1.02f,1,1.08f,46,false,false),
                Att("bipod","Сошки",AttachmentType.Bipod,AttachmentSlot.Underbarrel,550,1,0.65f,0.7f,1.15f,1,1.12f,0,false,false),
            };
            db.germany = Arsenal(Faction.Germany, De, db.attachments);
            db.ussr = Arsenal(Faction.USSR, Su, db.attachments);
            db.maps = new List<MapDefinition> { Map("normandy", "Нормандия", MapTheme.Summer, Weather.Clear), Map("ardennes", "Арденны", MapTheme.Winter, Weather.Snow),
                                               Map("rzhev", "Ржев: окопы", MapTheme.Summer, Weather.LightRain) };
            return db;
        }

        static MapDefinition Map(string id, string name, MapTheme theme, Weather w)
        {
            var m = ScriptableObject.CreateInstance<MapDefinition>();
            m.id = id; m.displayName = name; m.theme = theme; m.defaultWeather = w; m.sceneName = "";
            m.camo = CamoController.DefaultFor(theme);
            m.minimapWorldMin = new Vector2(-MapGenerator.Zone, -MapGenerator.Zone); m.minimapWorldMax = new Vector2(MapGenerator.Zone, MapGenerator.Zone);
            return m;
        }

        static AttachmentDefinition Att(string id, string n, AttachmentType t, AttachmentSlot s, int price, float loud, float rv, float rh, float hip, float mag, float ads, float fov, bool flash, bool overlay)
        {
            var a = ScriptableObject.CreateInstance<AttachmentDefinition>();
            a.id = id; a.displayName = n; a.type = t; a.slot = s; a.price = price;
            a.loudnessMultiplier = loud; a.verticalRecoilMultiplier = rv; a.horizontalRecoilMultiplier = rh; a.hipSpreadMultiplier = hip;
            a.magazineMultiplier = mag; a.adsTimeMultiplier = ads; a.adsFovOverride = fov; a.suppressesMuzzleFlash = flash; a.usesScopeOverlay = overlay;
            if (t == AttachmentType.Scope || t == AttachmentType.ScopeShort) a.hideBaseParts = new[] { "IronSights", "RearSight*", "RearNotch*", "Hood*", "FrontSightBase" };
            if (t == AttachmentType.Bipod) a.hideBaseParts = new[] { "Bipod*" };     // штатные сошки пулемёта заменяются
            if (t == AttachmentType.ExtendedMag || t == AttachmentType.DrumMag) a.hideBaseParts = new[] { "Mag_Standard" };
            a.description = id switch
            {
                "flash_hider" => "Гасит дульную вспышку — стрелка труднее заметить; чуть меньше отдача.",
                "bayonet" => "Штык на стволе: точнее от бедра, но медленнее вскидывание.",
                "drum_mag" => "Ёмкость ×2,2, но оружие тяжелее: медленнее прицеливание.",
                "scope_15x" => "Лёгкая оптика с большим выносом: прицеливание почти как с открытым прицелом.",
                "bipod" => "Отдача заметно меньше, но разброс от бедра и вскидывание хуже.",
                _ => a.description
            };
            a.visualPrefab = ProceduralModels.Attachment(t);
            return a;
        }

        // Магазины снизу (вертикальные коробчатые/изогнутые) — только к ним подходят увеличенные и барабанные магазины;
        // у боковых, дисковых, ленточных и магазинов-обойм (винтовки с продольным затвором) их нет.
        static readonly string[] BottomBoxMag = { "stg44", "mp40", "avt40", "avs36", "g43", "pps43", "p38", "p08", "tt33", "ppsh41", "svt40", "vg15", "mp3008",
                                                   "mp41", "ppd34", "fedorov" };
        static readonly string[] DrumOk = { "stg44", "mp40", "pps43", "mp3008" };

        /// <summary>«Сила» образца: урон × темп (с убывающей отдачей) × управляемость.</summary>
        static float Power(WeaponDefinition w) =>
            w.damage * Mathf.Sqrt(Mathf.Min(w.roundsPerMinute, 1000f) / 60f) / (1f + 0.22f * w.recoilVertical + 0.07f * w.hipSpread) * (0.85f + 0.15f * Mathf.Min(w.magazineSize, 60) / 30f);

        /// <summary>Подтягивает характеристики так, чтобы каждый следующий по цене образец был сильнее предыдущего хотя бы на 6%.</summary>
        static void PriceQuality(List<WeaponDefinition> list)
        {
            if (list == null || list.Count < 2) return;
            var sorted = new List<WeaponDefinition>(list);
            sorted.Sort((a, b) => a.price.CompareTo(b.price));
            for (int i = 1; i < sorted.Count; i++)
            {
                var w = sorted[i];
                if (sorted[i - 1].price == w.price) continue;
                float need = Power(sorted[i - 1]) * 1.06f, p = Power(w);
                if (p >= need) continue;
                float k = need / p;
                // половину разницы — уроном, половину — управляемостью (меньше отдача и разброс)
                float dk = Mathf.Sqrt(k);
                w.damage *= dk;
                w.recoilVertical /= dk; w.recoilHorizontal /= dk; w.hipSpread /= Mathf.Lerp(1f, dk, 0.6f);
                for (int g = 0; g < 4 && Power(w) < need; g++) w.damage *= 1.04f;
                w.reloadTime = Mathf.Max(1.2f, w.reloadTime / Mathf.Lerp(1f, dk, 0.3f)); w.reloadTimeEmpty = w.reloadTime * 1.2f;
            }
        }

        static bool Fits(AttachmentDefinition a, WeaponCategory c, string id) => a.id switch
        {
            "suppressor" => c != WeaponCategory.MachineGun,
            "muzzle_brake" => c is WeaponCategory.SniperRifle or WeaponCategory.AssaultRifle or WeaponCategory.MachineGun or WeaponCategory.BattleRifle,
            "ext_mag" => System.Array.IndexOf(BottomBoxMag, id) >= 0,
            "scope_4x" => c == WeaponCategory.SniperRifle,
            "scope_2x" => c == WeaponCategory.BattleRifle,
            "grip" => c is WeaponCategory.AssaultRifle or WeaponCategory.SubmachineGun or WeaponCategory.MachineGun or WeaponCategory.BattleRifle,
            "laser" => c is WeaponCategory.AssaultRifle or WeaponCategory.SubmachineGun or WeaponCategory.Pistol or WeaponCategory.Pistol2,
            "flash_hider" => c is WeaponCategory.AssaultRifle or WeaponCategory.SubmachineGun or WeaponCategory.MachineGun or WeaponCategory.BattleRifle,
            "bayonet" => c is WeaponCategory.SniperRifle or WeaponCategory.BattleRifle or WeaponCategory.AssaultRifle,
            "drum_mag" => System.Array.IndexOf(DrumOk, id) >= 0,
            "scope_15x" => c is WeaponCategory.AssaultRifle or WeaponCategory.BattleRifle or WeaponCategory.SniperRifle,
            "bipod" => c is WeaponCategory.MachineGun or WeaponCategory.SniperRifle or WeaponCategory.BattleRifle,
            _ => false
        };

        static FactionArsenal Arsenal(Faction f, W[] list, List<AttachmentDefinition> atts)
        {
            var ars = ScriptableObject.CreateInstance<FactionArsenal>();
            ars.faction = f; ars.displayName = f == Faction.Germany ? "Германия" : "СССР";
            foreach (var w in list)
            {
                var d = ScriptableObject.CreateInstance<WeaponDefinition>();
                d.id = w.id; d.displayName = w.name; d.faction = f; d.category = w.c; d.fireMode = w.m;
                d.price = Prices.TryGetValue(w.id, out var pr) ? pr : Shop.DefaultPrice(w.c, !ars.weapons.Exists(x => x.category == w.c));
                d.roundsPerMinute = w.rpm; d.damage = w.dmg; d.muzzleVelocity = w.mv; d.maxRange = w.range;
                d.magazineSize = w.mag; d.reserveAmmo = w.res; d.reloadType = w.rt; d.reloadTime = w.rel; d.reloadTimeEmpty = w.rel * 1.2f;
                d.recoilVertical = w.rv; d.recoilHorizontal = w.rh; d.hipSpread = w.hip; d.adsFov = w.fov; d.moveSpeedMultiplier = w.move;
                d.loudnessRadius = w.loud; d.clipSize = 5;
                d.adsSpread = w.c == WeaponCategory.SniperRifle ? 0.02f : w.c == WeaponCategory.MachineGun ? 0.9f : 0.2f;
                d.headshotMultiplier = w.c == WeaponCategory.SniperRifle ? 2.5f : 1.8f;
                d.damageFalloff = AnimationCurve.Linear(0, 1, Mathf.Max(1, w.range), w.c == WeaponCategory.SniperRifle ? 0.85f : 0.55f);
                d.viewModelPrefab = ProceduralModels.Weapon(w.id, w.c, f);
                if (w.c != WeaponCategory.Knife)
                {
                    d.fireSound = SoundSynth.Gunshot(w.id, w.c);
                    d.fireSoundSuppressed = SoundSynth.Suppressed(w.id);
                    d.reloadSound = null;   // фазы перезарядки озвучивает ViewModelAnimator
                    d.dryFireSound = SoundSynth.DryFire();
                }
                if (w.id == "panzerfaust")
                {
                    d.projectileSpeed = 62f; d.projectileGravity = 0.45f; d.explosionRadius = 4.5f; d.explosionDamage = 160f; d.armorDamage = 1600f;   // фаустпатрон один, но танк — с одного выстрела
                    d.fireSound = SoundSynth.RocketLaunch(); d.adsSpread = 0.4f; d.reloadTimeEmpty = 3.4f;
                }
                else if (w.id == "ptrd")
                {
                    d.armorDamage = 140f; d.fireSound = SoundSynth.AntiTankRifle(); d.adsSpread = 0.05f; d.headshotMultiplier = 2f;
                    d.reloadTimeEmpty = 2.4f;
                }
                d.compatibleAttachments = w.c is WeaponCategory.Knife or WeaponCategory.Launcher ? new List<AttachmentDefinition>() : atts.FindAll(a => Fits(a, w.c, w.id));
                ars.weapons.Add(d);
            }
            // Шпатель как выбираемый предмет 3-го слота инженера (в бою — инструмент строительства)
            var trowel = ScriptableObject.CreateInstance<WeaponDefinition>();
            trowel.id = f == Faction.Germany ? "trowel_de" : "trowel_su"; trowel.displayName = "Шпатель (стройка)";
            trowel.faction = f; trowel.category = WeaponCategory.Knife; trowel.price = 0; trowel.damage = 30;
            trowel.viewModelPrefab = ProceduralModels.Trowel(); trowel.compatibleAttachments = new List<AttachmentDefinition>();
            ars.weapons.Add(trowel);
            WeaponDefinition G(WeaponCategory c) => ars.weapons.Find(x => x.category == c);
            List<WeaponDefinition> All(params WeaponCategory[] cs) => ars.weapons.FindAll(x => System.Array.IndexOf(cs, x.category) >= 0);
            ars.classes = new List<ClassLoadout>
            {
                // 3 слота у каждого: 1 основное, 2 пистолет, 3 второстепенное (нож / противотанковое / гаечный ключ);
                // шпатель — только у инженера, всегда в 4-м слоте. Противотанковое оружие — только у поддержки.
                new() { soldierClass = SoldierClass.Assault, primary = G(WeaponCategory.AssaultRifle), primaryAlt = G(WeaponCategory.BattleRifle), secondary = G(WeaponCategory.Pistol), melee = G(WeaponCategory.Knife) },
                new() { soldierClass = SoldierClass.Engineer, primary = G(WeaponCategory.SubmachineGun), secondary = G(WeaponCategory.Pistol2), melee = ars.weapons.Find(x => x.id.StartsWith("wrench")), hasTrowel = true },
                new() { soldierClass = SoldierClass.Support, primary = G(WeaponCategory.MachineGun), secondary = G(WeaponCategory.Pistol), melee = G(WeaponCategory.Knife), gadget = G(WeaponCategory.Launcher) },
                new() { soldierClass = SoldierClass.Recon, primary = G(WeaponCategory.SniperRifle), secondary = G(WeaponCategory.Pistol2), melee = G(WeaponCategory.Knife) },
            };
            // Варианты основного оружия класса (по 5) — от дешёвого к дорогому; часть образцов общая для двух классов
            // (самозарядные винтовки — у штурмовика и снайпера, автоматические винтовки — у штурмовика и поддержки)
            List<WeaponDefinition> Ids(params string[] ids)
            {
                var l = new List<WeaponDefinition>();
                foreach (var id in ids) { var w = ars.weapons.Find(x => x.id == id); if (w) l.Add(w); }
                l.Sort((a, b) => a.price.CompareTo(b.price));
                return l;
            }
            bool de = f == Faction.Germany;
            foreach (var c in ars.classes)
            {
                c.primaryOptions = c.soldierClass switch
                {
                    SoldierClass.Assault => de ? Ids("vg15", "g43", "stg44", "fg42") : Ids("svt40", "sks", "avs36", "avt40", "fedorov"),
                    // ППС-42 и ППС-43 почти одинаковы — оставлен один ППС-43; у поддержки — только пулемёты
                    SoldierClass.Engineer => de ? Ids("mp3008", "mp28", "mp41", "mp35", "mp40") : Ids("ppd34", "ppd40", "pps43", "ppsh41"),
                    SoldierClass.Support => de ? Ids("mg15", "mg30", "mg34", "mg42") : Ids("dp28", "dt", "dpm", "rp46"),
                    _ => de ? Ids("gew98", "g3340", "g43", "kar98k") : Ids("svt40", "mosin91", "m38", "m44", "mosin"),
                };
                if (c.primaryOptions.Count > 0) c.primary = c.primaryOptions[0];
            }
            // Чем дороже — тем лучше: внутри каждого списка мощь растёт с ценой (несколько проходов — часть образцов общая)
            for (int pass = 0; pass < 3; pass++)
            {
                foreach (var c in ars.classes) PriceQuality(c.primaryOptions);
                PriceQuality(ars.weapons.FindAll(x => x.category is WeaponCategory.Pistol or WeaponCategory.Pistol2));
            }
            return ars;
        }
    }
}
