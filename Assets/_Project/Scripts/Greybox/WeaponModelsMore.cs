using UnityEngine;

namespace Frontline
{
    /// <summary>
    /// Ещё шесть образцов: MP 3008, Volkssturmgewehr VG 1-5, Gewehr 41 — Германия; СВТ-40, ППД-40, винтовка
    /// Мосина обр. 1891 (длинная, пехотная) — СССР. Подвижные части (затвор, магазин), открытые прицелы, сокеты обвесов.
    /// </summary>
    public static partial class ProceduralModels
    {
        public static bool BuildMore(string id, GameObject root, Faction f, WeaponAnimProfile prof)
        {
            switch (id)
            {
                case "mp3008": Mp3008(root, f, prof); return true;
                case "vg15": Vg15(root, f, prof); return true;
                case "gew41": Gew41(root, f, prof); return true;
                case "svt40": Svt40(root, f, prof); return true;
                case "ppd40": Ppd40(root, f, prof); return true;
                case "mosin91": Mosin91(root, f, prof); return true;
                case "wrench_de": case "wrench_su": Wrench(root, f, prof); return true;
            }
            return false;
        }

        // ---------- MP 3008: упрощённый «Стэн» — трубчатая коробка, проволочный приклад, магазин снизу ----------
        static void Mp3008(GameObject root, Faction f, WeaponAnimProfile prof)
        {
            const float bore = 0.02f;
            cz("Receiver", 0, bore, 0.02f, 0.021f, 0.28f, Parker, 12);
            cz("EndCap", 0, bore, -0.125f, 0.022f, 0.02f, Blued, 12);
            b("CockingSlot", 0.02f, bore + 0.004f, 0.02f, 0.003f, 0.006f, 0.1f, new Color(0.03f, 0.03f, 0.03f));
            b("ChargingHandle", 0.026f, bore + 0.004f, 0.05f, 0.016f, 0.01f, 0.012f, Bright);
            // Проволочный приклад-рамка
            cz("StockTube", 0, bore - 0.004f, -0.24f, 0.007f, 0.22f, Steel, 6);
            b("StockStrut", 0, -0.03f, -0.22f, 0.008f, 0.008f, 0.22f, Steel, -14);
            b("Buttplate", 0, -0.014f, -0.355f, 0.032f, 0.08f, 0.01f, Steel);
            b("TriggerHousing", 0, -0.012f, -0.04f, 0.026f, 0.03f, 0.08f, Parker);
            TriggerGroup(-0.03f, -0.03f);
            b("Grip", 0, -0.055f, -0.07f, 0.03f, 0.08f, 0.04f, Parker, 10);
            // Ствол в перфорированном кожухе
            cz("Jacket", 0, bore, 0.21f, 0.016f, 0.12f, Parker, 10);
            for (int i = 0; i < 4; i++) cy("JHole" + i, 0.0158f, bore, 0.17f + i * 0.028f, 0.005f, 0.003f, new Color(0.03f, 0.03f, 0.03f), 0, 0, 90, 8);
            cz("Barrel", 0, bore, 0.3f, 0.008f, 0.08f, Blued, 8);
            // Магазин снизу (как у MP-40), горловина
            b("MagWell", 0, -0.008f, 0.1f, 0.03f, 0.03f, 0.04f, Parker);
            grpBox("Mag_Standard", 0, -0.1f, 0.1f, 0.024f, 0.17f, 0.04f, Blued);
            b("RearSight", 0, bore + 0.024f, -0.08f, 0.016f, 0.016f, 0.006f, Blued);
            FrontSight(0.31f, bore + 0.014f, 0.02f, false);
            HandRight(f, 0, -0.06f, -0.08f, 10);
            HandLeft(f, 0, -0.07f, 0.1f);
            var s = Sockets(root, 0.34f, bore, bore + 0.036f, new Vector3(0, -0.1f, 0.1f), 0.22f, 0.0f, bore + 0.03f);
            s.shellEjectPoint.localPosition = new Vector3(0.022f, bore + 0.01f, 0.04f);
            prof.style = WeaponAnimProfile.Style.Magazine; prof.casingLength = 0.019f; prof.casingRadius = 0.005f;
            prof.magOut = new Vector3(0, -0.28f, 0.03f);
        }

        // ---------- Volkssturmgewehr VG 1-5: грубая штамповка, деревянный приклад, кожух-поршень вокруг ствола ----------
        static void Vg15(GameObject root, Faction f, WeaponAnimProfile prof)
        {
            const float bore = 0.016f;
            b("Butt", 0, -0.035f, -0.3f, 0.04f, 0.1f, 0.26f, WoodLight, -4);
            b("Wrist", 0, -0.02f, -0.13f, 0.036f, 0.05f, 0.12f, WoodLight, -10);
            b("Buttplate", 0, -0.045f, -0.435f, 0.042f, 0.11f, 0.012f, Steel);
            b("Receiver", 0, bore, 0.02f, 0.034f, 0.04f, 0.22f, Parker);
            b("WeldSeam", 0, bore + 0.021f, 0.02f, 0.002f, 0.002f, 0.2f, Steel);
            // Затворный кожух скользит вместе с затвором (газовая задержка)
            cz("SlideSleeve", 0, bore, 0.24f, 0.019f, 0.2f, Parker, 10);
            for (int i = 0; i < 3; i++) cz("Ring" + i, 0, bore, 0.17f + i * 0.06f, 0.02f, 0.006f, Blued, 10);
            b("Foregrip", 0, -0.012f, 0.25f, 0.034f, 0.03f, 0.16f, WoodLight);
            cz("Barrel", 0, bore, 0.44f, 0.009f, 0.2f, Blued, 8);
            b("CockingKnob", 0.024f, bore + 0.006f, 0.15f, 0.014f, 0.012f, 0.014f, Bright);
            TriggerGroup(-0.02f, -0.04f);
            // Магазин StG-44 снизу (изогнутый)
            CurvedMag(0, -0.01f, 0.06f, 0.028f, 0.06f, 0.2f, 14, Blued);
            b("RearSight", 0, bore + 0.026f, -0.02f, 0.02f, 0.016f, 0.008f, Blued);
            FrontSight(0.52f, bore + 0.01f, 0.026f, true);
            HandRight(f, 0, -0.065f, -0.14f, -16);
            HandLeft(f, 0, -0.05f, 0.25f);
            var s = Sockets(root, 0.54f, bore, bore + 0.04f, new Vector3(0, -0.01f, 0.06f), 0.3f, 0.0f, bore + 0.034f);
            s.shellEjectPoint.localPosition = new Vector3(0.022f, bore + 0.012f, 0.02f);
            prof.style = WeaponAnimProfile.Style.Magazine; prof.casingLength = 0.033f; prof.magOut = new Vector3(0, -0.3f, 0.04f);
        }

        // ---------- Gewehr 41: самозарядная, дульный «колокол» Бангмана, неотъёмный магазин на 10 (обоймы) ----------
        static void Gew41(GameObject root, Faction f, WeaponAnimProfile prof)
        {
            const float bore = 0.015f;
            WoodStock(WoodDark, 0.4f, -0.46f);
            b("Receiver", 0, bore, 0.0f, 0.034f, 0.042f, 0.22f, Blued);
            b("BoltHousing", 0, bore + 0.024f, -0.02f, 0.026f, 0.014f, 0.15f, Blued);
            b("HandguardTop", 0, bore + 0.024f, 0.3f, 0.03f, 0.014f, 0.3f, WoodDark);
            cz("Barrel", 0, bore, 0.5f, 0.0095f, 0.5f, Blued, 8);
            // Газовый «колокол» на дуле и тяга поршня над стволом
            cz("MuzzleCone", 0, bore, 0.76f, 0.019f, 0.06f, Blued, 12);
            fz("MuzzleBell", 0, bore, 0.8f, 0.019f, 0.03f, 0.7f, Blued, 12);
            cz("OpRod", 0, bore + 0.02f, 0.5f, 0.0045f, 0.46f, Bright, 6);
            b("BarrelBand", 0, bore - 0.004f, 0.62f, 0.042f, 0.05f, 0.016f, Steel);
            b("Mag_Standard", 0, -0.045f, 0.05f, 0.03f, 0.04f, 0.08f, Blued);     // неотъёмный магазин (обойма сверху)
            b("Sling", -0.03f, -0.045f, 0.0f, 0.004f, 0.026f, 0.6f, Leather, -2.5f);
            TriggerGroup(-0.02f, -0.05f);
            var prev = _t; _t = grp("BoltHandle", 0, bore + 0.026f, -0.06f);
            b("BoltKnob", 0.02f, 0, 0, 0.014f, 0.012f, 0.02f, Bright);
            _t = prev;
            RearSight(0.12f, bore + 0.03f, 0.06f);
            FrontSight(0.72f, bore + 0.012f, 0.03f, true);
            HandRight(f, 0, -0.07f, -0.13f, -16);
            HandLeft(f, 0, -0.07f, 0.28f);
            var s = Sockets(root, 0.82f, bore, bore + 0.045f, new Vector3(0, -0.045f, 0.05f), 0.32f, 0.02f, bore + 0.04f);
            s.shellEjectPoint.localPosition = new Vector3(0.024f, bore + 0.03f, 0.0f);
            prof.style = WeaponAnimProfile.Style.Clip; prof.showClip = true; prof.boltTravel = 0.06f; prof.boltLift = 0f;
            prof.casingLength = 0.057f; prof.casingRadius = 0.006f; prof.clipHandPos = new Vector3(0.01f, 0.1f, -0.03f);
        }

        // ---------- СВТ-40: самозарядная Токарева — короткое цевьё, металлический кожух, дульный тормоз ----------
        static void Svt40(GameObject root, Faction f, WeaponAnimProfile prof)
        {
            const float bore = 0.014f;
            WoodStock(Wood, 0.34f, -0.44f);
            b("Receiver", 0, 0.014f, 0.01f, 0.036f, 0.04f, 0.22f, Blued);
            b("ReceiverCover", 0, 0.036f, -0.02f, 0.03f, 0.012f, 0.16f, Blued);
            b("ChargingHandle", 0.024f, 0.022f, 0.03f, 0.016f, 0.008f, 0.012f, Bright);
            // Перфорированный металлический кожух над газовой трубкой
            b("HandguardMetal", 0, 0.018f, 0.33f, 0.036f, 0.026f, 0.2f, Parker);
            for (int i = 0; i < 5; i++) { b("VentL" + i, -0.0185f, 0.018f, 0.25f + i * 0.036f, 0.002f, 0.012f, 0.018f, Blued); b("VentR" + i, 0.0185f, 0.018f, 0.25f + i * 0.036f, 0.002f, 0.012f, 0.018f, Blued); }
            cz("Barrel", 0, bore, 0.42f, 0.0095f, 0.62f, Blued, 8);   // от коробки — без разрыва
            cz("GasRegulator", 0, bore + 0.012f, 0.6f, 0.011f, 0.03f, Steel, 8);
            // Шестикамерный дульный тормоз
            b("Brake", 0, bore, 0.76f, 0.024f, 0.026f, 0.06f, Blued);
            for (int i = 0; i < 6; i++) b("BrakeSlot" + i, 0, bore, 0.737f + i * 0.009f, 0.028f, 0.008f, 0.004f, Parker);
            b("BayonetLug", 0.014f, 0.0f, 0.72f, 0.008f, 0.014f, 0.03f, Blued);
            RearSight(0.12f, bore + 0.024f, 0.06f);
            FrontSight(0.7f, bore + 0.008f, 0.03f, true);
            grpBox("Mag_Standard", 0, -0.062f - 0.045f, 0.06f, 0.03f, 0.09f, 0.068f, Blued);
            cz("CleaningRod", 0.024f, -0.02f, 0.4f, 0.003f, 0.5f, Bright, 5);
            b("Sling", -0.03f, -0.045f, 0.0f, 0.004f, 0.026f, 0.6f, Leather, -2.5f);
            HandRight(f, 0, -0.07f, -0.13f, -16);
            HandLeft(f, 0, -0.07f, 0.26f);
            var s = Sockets(root, 0.8f, bore, bore + 0.038f, new Vector3(0, -0.107f, 0.06f), 0.3f, 0.0f, 0.045f);
            s.shellEjectPoint.localPosition = new Vector3(0.025f, 0.035f, 0.0f);
            prof.style = WeaponAnimProfile.Style.Magazine; prof.casingLength = 0.054f; prof.magOut = new Vector3(0, -0.3f, 0.05f);
        }

        // ---------- ППД-40: деревянная ложа, кожух с овальными окнами, барабан на 71 ----------
        static void Ppd40(GameObject root, Faction f, WeaponAnimProfile prof)
        {
            const float bore = 0.018f;
            b("StockFore", 0, -0.03f, 0.06f, 0.044f, 0.05f, 0.2f, Wood);
            b("StockWrist", 0, -0.05f, -0.11f, 0.038f, 0.05f, 0.16f, Wood, -14);
            b("StockButt", 0, -0.075f, -0.29f, 0.042f, 0.11f, 0.24f, Wood, -5);
            b("Buttplate", 0, -0.086f, -0.415f, 0.044f, 0.116f, 0.012f, Steel);
            cz("Receiver", 0, bore, 0.0f, 0.02f, 0.22f, Blued, 10);
            b("SafetyCatch", 0.022f, bore + 0.006f, 0.04f, 0.014f, 0.008f, 0.02f, Bright);
            cz("Jacket", 0, bore, 0.24f, 0.018f, 0.24f, Blued, 12);
            for (int i = 0; i < 5; i++)
            {
                b("SlotL" + i, -0.0178f, bore, 0.15f + i * 0.042f, 0.002f, 0.01f, 0.026f, new Color(0.03f, 0.03f, 0.03f));
                b("SlotR" + i, 0.0178f, bore, 0.15f + i * 0.042f, 0.002f, 0.01f, 0.026f, new Color(0.03f, 0.03f, 0.03f));
            }
            cz("MuzzleRing", 0, bore, 0.365f, 0.019f, 0.012f, Blued, 12);
            cz("Barrel", 0, bore, 0.37f, 0.008f, 0.04f, Blued, 8);
            TriggerGroup(-0.04f, -0.06f);
            // Барабанный магазин с горловиной
            var prev = _t; _t = grp("Mag_Standard", 0, -0.08f, 0.08f);
            cy("Drum", 0, -0.05f, 0, 0.085f, 0.06f, Blued, 0, 0, 90, 18);
            cy("DrumCap", 0.032f, -0.05f, 0, 0.072f, 0.005f, Parker, 0, 0, 90, 18);
            b("DrumNeck", 0, 0.04f, 0, 0.026f, 0.06f, 0.036f, Blued);
            cy("Key", 0.04f, -0.05f, 0, 0.012f, 0.014f, Steel, 0, 0, 90, 8);
            _t = prev;
            b("RearSight", 0, bore + 0.026f, -0.04f, 0.018f, 0.018f, 0.04f, Blued);
            FrontSight(0.35f, bore + 0.014f, 0.024f, true);
            HandRight(f, 0, -0.065f, -0.11f, -15);
            HandLeft(f, 0, -0.06f, 0.2f);
            var s = Sockets(root, 0.39f, bore, bore + 0.04f, new Vector3(0, -0.08f, 0.08f), 0.24f, 0.0f, bore + 0.032f);
            s.shellEjectPoint.localPosition = new Vector3(0.024f, bore + 0.01f, 0.05f);
            prof.style = WeaponAnimProfile.Style.Magazine; prof.casingLength = 0.025f; prof.casingRadius = 0.005f;
            prof.magOut = new Vector3(0, -0.3f, 0.06f);
        }

        // ---------- Гаечный ключ инженера: разводной, с червячной гайкой, ручка в кулаке ----------
        static void Wrench(GameObject root, Faction f, WeaponAnimProfile prof)
        {
            var prev = _t; _t = grp("WrenchBody", 0.004f, 0.0f, 0.02f, 75, 0, 0);
            b("Handle", 0, 0, 0, 0.022f, 0.012f, 0.2f, Steel);
            b("HandleGrip", 0, 0, -0.04f, 0.026f, 0.016f, 0.1f, new Color(0.25f, 0.12f, 0.08f));
            b("HandleHole", 0, 0, -0.088f, 0.01f, 0.014f, 0.01f, new Color(0.03f, 0.03f, 0.03f));
            b("HeadBody", 0, 0, 0.12f, 0.05f, 0.016f, 0.05f, Steel);
            b("JawFixed", 0.02f, 0, 0.16f, 0.014f, 0.016f, 0.05f, Steel);
            b("JawMoving", -0.016f, 0, 0.155f, 0.012f, 0.014f, 0.04f, Bright);
            cy("WormScrew", 0, 0, 0.125f, 0.008f, 0.03f, Brass, 0, 0, 90, 8);
            _t = prev;
            HandRight(f, 0.0f, -0.01f, -0.02f, 0, 0, 0);
            var s = root.AddComponent<WeaponSockets>();
            s.muzzleFlashPoint = Empty(root.transform, "TipPoint", new Vector3(0.004f, 0.18f, 0.06f));
            prof.ejectsCasings = false;
        }

        // ---------- Винтовка Мосина обр. 1891/30 (пехотная, длинная) с примкнутым штыком ----------
        static void Mosin91(GameObject r, Faction f, WeaponAnimProfile p)
        {
            BoltAction(r, f, p, WoodLight, true, 0.94f, 0.03f);
            b("MagBox", 0, -0.085f, 0.055f, 0.026f, 0.03f, 0.07f, Blued, 8);
            b("BayonetSocket", 0.008f, 0.014f, 0.93f, 0.022f, 0.024f, 0.05f, Blued);
            b("Bayonet", 0.014f, 0.014f, 1.16f, 0.006f, 0.006f, 0.44f, Bright);
            b("BandMid", 0, -0.006f, 0.52f, 0.052f, 0.054f, 0.014f, Blued);
        }
    }
}
