using UnityEngine;

namespace Frontline
{
    /// <summary>
    /// Третья волна образцов, чтобы у каждого класса было не меньше пяти вариантов основного оружия.
    /// Германия: MP 35 (Бергманн), MP 41, MG 15, MG 30 (Солотурн), Gewehr 98, Gewehr 33/40.
    /// СССР: автомат Фёдорова, СКС, ППД-34/38, ППС-42, ДПМ, карабин обр. 1938.
    /// </summary>
    public static partial class ProceduralModels
    {
        public static bool BuildWW2(string id, GameObject root, Faction f, WeaponAnimProfile prof)
        {
            switch (id)
            {
                case "mp35": Mp35(root, f, prof); return true;
                case "mp41": Mp41(root, f, prof); return true;
                case "mg15": Mg15(root, f, prof); return true;
                case "mg30": Mg30(root, f, prof); return true;
                case "gew98": BoltAction(root, f, prof, Wood, false, 0.98f, 0.026f); b("LangeSight", 0, 0.034f, 0.2f, 0.026f, 0.014f, 0.1f, Blued); b("BandMid", 0, -0.006f, 0.55f, 0.054f, 0.056f, 0.016f, Blued); return true;
                case "g3340": BoltAction(root, f, prof, WoodDark, false, 0.6f, 0.026f); b("ButtCup", 0, -0.075f, -0.455f, 0.044f, 0.11f, 0.014f, Steel); b("StockDisc", 0.024f, -0.04f, -0.3f, 0.004f, 0.026f, 0.026f, Steel); return true;
                case "fedorov": Fedorov(root, f, prof); return true;
                case "sks": Sks(root, f, prof); return true;
                case "ppd34": Ppd34(root, f, prof); return true;
                case "rp46": Rp46(root, f, prof); return true;
                case "dpm": Dpm(root, f, prof); return true;
                case "m38": BoltAction(root, f, prof, Wood, true, 0.6f, 0.028f); b("MagBox", 0, -0.085f, 0.055f, 0.026f, 0.03f, 0.07f, Blued, 8); b("HoodSides", 0, 0.05f, 0.57f, 0.03f, 0.03f, 0.03f, Blued); return true;
            }
            return false;
        }

        // ---------- MP 35 (Бергманн): деревянная ложа, кожух с отверстиями, магазин справа, рукоять затвора сзади ----------
        static void Mp35(GameObject root, Faction f, WeaponAnimProfile prof)
        {
            const float bore = 0.018f;
            b("StockFore", 0, -0.03f, 0.05f, 0.044f, 0.054f, 0.2f, Wood);
            b("StockWrist", 0, -0.05f, -0.12f, 0.038f, 0.05f, 0.16f, Wood, -15);
            b("StockButt", 0, -0.075f, -0.3f, 0.042f, 0.11f, 0.24f, Wood, -5);
            b("Buttplate", 0, -0.086f, -0.425f, 0.044f, 0.116f, 0.012f, Steel);
            cz("Receiver", 0, bore, 0.0f, 0.02f, 0.24f, Blued, 10);
            // Рукоять затвора — как у винтовки, в задней части коробки
            var prev = _t; _t = grp("BoltHandle", 0, bore, -0.1f);
            cy("Stem", 0.022f, 0, 0, 0.005f, 0.04f, Bright, 0, 0, 90, 6);
            Part(_t, "Knob", MeshUtil.Ico(), new Vector3(0.044f, 0, 0), Vector3.one * 0.018f, Bright, default, false, 0.6f, 0.85f);
            _t = prev;
            cz("Jacket", 0, bore, 0.24f, 0.017f, 0.22f, Blued, 12);
            for (int i = 0; i < 6; i++) cy("Hole" + i, 0.0168f, bore, 0.15f + i * 0.036f, 0.0055f, 0.003f, new Color(0.03f, 0.03f, 0.03f), 0, 0, 90, 8);
            cz("MuzzleNut", 0, bore, 0.36f, 0.012f, 0.02f, Blued, 8);
            b("MagHousing", 0.03f, bore, 0.1f, 0.028f, 0.03f, 0.045f, Blued);
            grpBox("Mag_Standard", 0.13f, bore, 0.1f, 0.19f, 0.022f, 0.034f, Blued);
            TriggerGroup(-0.02f, -0.06f);
            RearSight(0.0f, bore + 0.026f, 0.05f);
            FrontSight(0.34f, bore + 0.012f, 0.026f, false);
            HandRight(f, 0, -0.065f, -0.12f, -15);
            HandLeft(f, 0, -0.06f, 0.1f);
            var s = Sockets(root, 0.38f, bore, bore + 0.038f, new Vector3(0.13f, bore, 0.1f), 0.24f, 0.0f, bore + 0.03f);
            s.shellEjectPoint.localPosition = new Vector3(-0.025f, bore + 0.01f, 0.05f);
            prof.style = WeaponAnimProfile.Style.SideMag; prof.casingLength = 0.019f; prof.casingRadius = 0.005f; prof.magOut = new Vector3(0.3f, 0, 0);
        }

        // ---------- MP 41: ствольная коробка MP-40 в деревянной ложе, без складного приклада ----------
        static void Mp41(GameObject root, Faction f, WeaponAnimProfile prof)
        {
            const float bore = 0.022f;
            b("StockFore", 0, -0.02f, 0.04f, 0.044f, 0.05f, 0.18f, WoodLight);
            b("StockWrist", 0, -0.045f, -0.12f, 0.038f, 0.05f, 0.16f, WoodLight, -15);
            b("StockButt", 0, -0.07f, -0.3f, 0.042f, 0.11f, 0.24f, WoodLight, -5);
            b("Buttplate", 0, -0.082f, -0.425f, 0.044f, 0.116f, 0.012f, Steel);
            cz("Receiver", 0, bore, 0.06f, 0.02f, 0.3f, Blued, 10);
            for (int i = 0; i < 6; i++) cz("Rib" + i, 0, bore, -0.04f + i * 0.04f, 0.0205f, 0.006f, Parker, 10);   // рифление коробки
            cz("Barrel", 0, bore, 0.3f, 0.009f, 0.2f, Blued, 8);
            cz("MuzzleNut", 0, bore, 0.405f, 0.012f, 0.02f, Blued, 8);
            b("ChargingHandle", -0.024f, bore, 0.1f, 0.018f, 0.008f, 0.01f, Bright);
            b("MagHousing", 0, -0.035f, 0.14f, 0.032f, 0.07f, 0.045f, Blued);
            grpBox("Mag_Standard", 0, -0.17f, 0.14f, 0.022f, 0.21f, 0.034f, Blued);
            TriggerGroup(-0.01f, -0.04f);
            b("RearSight", 0, bore + 0.03f, 0.0f, 0.016f, 0.02f, 0.012f, Blued);
            FrontSight(0.38f, bore + 0.01f, 0.026f, true);
            HandRight(f, 0, -0.07f, -0.12f, -15);
            HandLeft(f, 0, -0.1f, 0.14f, 0, 0, 0);
            var s = Sockets(root, 0.42f, bore, bore + 0.04f, new Vector3(0, -0.17f, 0.14f), 0.26f, 0.02f, bore + 0.03f);
            s.shellEjectPoint.localPosition = new Vector3(0.025f, bore + 0.01f, 0.08f);
            prof.style = WeaponAnimProfile.Style.Magazine; prof.casingLength = 0.019f; prof.casingRadius = 0.005f;
        }

        // ---------- MG 15: бывший авиапулемёт — двойной седловидный барабан сверху, каркасный приклад, голый ствол ----------
        static void Mg15(GameObject root, Faction f, WeaponAnimProfile prof)
        {
            const float bore = 0.02f;
            b("Receiver", 0, 0.015f, 0.02f, 0.046f, 0.06f, 0.3f, Blued);
            b("PistolGrip", 0, -0.075f, -0.07f, 0.032f, 0.1f, 0.044f, Bakelite, 12);
            TriggerGroup(-0.03f, -0.04f);
            cz("StockRod", 0, 0.0f, -0.24f, 0.007f, 0.26f, Steel, 6);
            b("StockStrut", 0, -0.045f, -0.22f, 0.008f, 0.008f, 0.24f, Steel, -16);
            b("StockPad", 0, -0.03f, -0.37f, 0.03f, 0.1f, 0.016f, Bakelite);
            cz("Barrel", 0, bore, 0.38f, 0.012f, 0.44f, Blued, 10);
            fz("FlashCone", 0, bore, 0.62f, 0.014f, 0.06f, 1.8f, Blued, 10);
            cz("GasTube", 0, bore - 0.024f, 0.3f, 0.007f, 0.26f, Blued, 6);
            // Седловидный барабан на 75: две половины по бокам над коробкой
            var prev = _t; _t = grp("Mag_Standard", 0, 0.07f, 0.04f);
            cy("DrumL", -0.05f, -0.01f, 0, 0.055f, 0.05f, Blued, 0, 0, 90, 14);
            cy("DrumR", 0.05f, -0.01f, 0, 0.055f, 0.05f, Blued, 0, 0, 90, 14);
            b("Saddle", 0, 0.03f, 0, 0.06f, 0.02f, 0.05f, Blued);
            b("SaddleHandle", 0, 0.05f, 0, 0.012f, 0.012f, 0.06f, Steel);
            _t = prev;
            b("ChargingHandle", 0.03f, -0.0f, 0.08f, 0.016f, 0.012f, 0.03f, Bright);
            b("RearSight", 0, 0.05f, -0.1f, 0.02f, 0.03f, 0.01f, Blued);
            FrontSight(0.56f, bore + 0.02f, 0.05f, false);
            Bipod(0.42f, -0.004f, 0.12f, 0.25f);
            HandRight(f, 0, -0.075f, -0.07f, 12);
            HandLeft(f, 0, -0.04f, 0.2f);
            var s = Sockets(root, 0.66f, bore, bore + 0.08f, new Vector3(0, 0.07f, 0.04f), 0.32f, -0.02f, 0.11f);
            s.shellEjectPoint.localPosition = new Vector3(0, -0.02f, 0.05f);
            prof.style = WeaponAnimProfile.Style.PanTop; prof.magOut = new Vector3(0, 0.26f, -0.06f); prof.ejectDir = new Vector3(0.1f, -1f, 0);
        }

        // ---------- MG 30 (Солотурн): длинная коробка, деревянный приклад, коробчатый магазин слева ----------
        static void Mg30(GameObject root, Faction f, WeaponAnimProfile prof)
        {
            const float bore = 0.022f;
            b("Receiver", 0, 0.018f, 0.06f, 0.046f, 0.06f, 0.4f, Blued);
            b("Butt", 0, -0.02f, -0.27f, 0.04f, 0.1f, 0.26f, Wood, -4);
            b("Buttplate", 0, -0.025f, -0.405f, 0.042f, 0.11f, 0.012f, Steel);
            b("PistolGrip", 0, -0.08f, -0.08f, 0.032f, 0.1f, 0.044f, Wood, 12);
            TriggerGroup(-0.03f, -0.05f);
            cz("BarrelSleeve", 0, bore, 0.34f, 0.018f, 0.16f, Blued, 10);
            cz("Barrel", 0, bore, 0.52f, 0.011f, 0.3f, Blued, 10);
            fz("FlashCone", 0, bore, 0.7f, 0.013f, 0.06f, 1.7f, Blued, 10);
            // Изогнутый магазин слева на 25 патронов
            var prev = _t; _t = grp("Mag_Standard", -0.03f, 0.02f, 0.08f, 0, 0, 90);
            for (int i = 0; i < 4; i++) b("Seg" + i, 0, -0.03f - i * 0.04f, i * 0.006f, 0.03f, 0.044f, 0.06f, Blued, -4 * i);
            _t = prev;
            b("ChargingHandle", 0.03f, 0.02f, 0.12f, 0.016f, 0.012f, 0.03f, Bright);
            b("RearSight", 0, 0.06f, -0.06f, 0.018f, 0.028f, 0.01f, Blued);
            FrontSight(0.64f, bore + 0.016f, 0.036f, true);
            Bipod(0.48f, -0.004f, 0.12f, 0.25f);
            HandRight(f, 0, -0.08f, -0.08f, 12);
            HandLeft(f, -0.01f, -0.055f, 0.24f);
            var s = Sockets(root, 0.74f, bore, bore + 0.05f, new Vector3(-0.03f, 0.02f, 0.08f), 0.34f, 0.0f, 0.08f);
            s.shellEjectPoint.localPosition = new Vector3(0.03f, 0.02f, 0.06f);
            prof.style = WeaponAnimProfile.Style.SideMag; prof.magOut = new Vector3(-0.3f, 0.02f, 0); prof.casingLength = 0.057f;
        }

        // ---------- Автомат Фёдорова: деревянная ложа, передняя рукоять, кожух ствола с рёбрами, изогнутый магазин ----------
        static void Fedorov(GameObject root, Faction f, WeaponAnimProfile prof)
        {
            const float bore = 0.016f;
            WoodStock(Wood, 0.26f, -0.42f);
            b("Receiver", 0, bore, 0.0f, 0.036f, 0.044f, 0.22f, Blued);
            b("ReceiverTop", 0, bore + 0.026f, -0.02f, 0.03f, 0.01f, 0.14f, Blued);
            b("ChargingHandle", 0.026f, bore + 0.01f, 0.03f, 0.018f, 0.01f, 0.014f, Bright);
            b("RibbedJacket", 0, bore, 0.32f, 0.034f, 0.034f, 0.18f, Parker);
            for (int i = 0; i < 7; i++) b("Rib" + i, 0, bore + 0.019f, 0.24f + i * 0.026f, 0.036f, 0.006f, 0.006f, Blued);
            cz("Barrel", 0, bore, 0.335f, 0.009f, 0.47f, Blued, 8);   // от коробки — без разрыва
            b("Foregrip", 0, -0.045f, 0.24f, 0.026f, 0.08f, 0.032f, Wood, 8);
            CurvedMag(0, -0.01f, 0.07f, 0.028f, 0.06f, 0.16f, 14, Blued);
            TriggerGroup(-0.02f, -0.04f);
            RearSight(0.1f, bore + 0.03f, 0.05f);
            FrontSight(0.55f, bore + 0.01f, 0.03f, true);
            HandRight(f, 0, -0.07f, -0.13f, -16);
            HandLeft(f, 0, -0.09f, 0.24f);
            var s = Sockets(root, 0.58f, bore, bore + 0.044f, new Vector3(0, -0.01f, 0.07f), 0.3f, 0.0f, bore + 0.04f);
            s.shellEjectPoint.localPosition = new Vector3(0.025f, bore + 0.02f, 0.0f);
            prof.style = WeaponAnimProfile.Style.Magazine; prof.casingLength = 0.051f; prof.magOut = new Vector3(0, -0.3f, 0.04f);
        }

        // ---------- СКС: деревянная ложа, неотъёмный магазин, откидной штык под стволом, газовая трубка ----------
        static void Sks(GameObject root, Faction f, WeaponAnimProfile prof)
        {
            const float bore = 0.015f;
            WoodStock(WoodLight, 0.36f, -0.44f);
            b("Receiver", 0, bore, 0.0f, 0.034f, 0.04f, 0.2f, Blued);
            var prev = _t; _t = grp("BoltHandle", 0.022f, bore + 0.012f, 0.02f);
            b("BoltCarrier", -0.022f, 0.006f, -0.02f, 0.026f, 0.014f, 0.12f, Bright);
            b("BoltKnob", 0.006f, 0, 0, 0.014f, 0.012f, 0.016f, Bright);
            _t = prev;
            b("GasTube", 0, bore + 0.026f, 0.3f, 0.022f, 0.02f, 0.22f, WoodLight);
            cz("Barrel", 0, bore, 0.48f, 0.0095f, 0.44f, Blued, 8);
            b("Mag_Standard", 0, -0.05f, 0.06f, 0.03f, 0.05f, 0.09f, Blued);
            b("BayonetFolded", 0, -0.016f, 0.48f, 0.008f, 0.012f, 0.3f, Bright);
            b("BayonetHinge", 0, -0.012f, 0.64f, 0.018f, 0.022f, 0.024f, Blued);
            TriggerGroup(-0.02f, -0.05f);
            RearSight(0.12f, bore + 0.028f, 0.06f);
            FrontSight(0.68f, bore + 0.01f, 0.03f, true);
            b("Sling", -0.03f, -0.045f, 0.0f, 0.004f, 0.026f, 0.6f, Leather, -2.5f);
            HandRight(f, 0, -0.07f, -0.13f, -16);
            HandLeft(f, 0, -0.07f, 0.26f);
            var s = Sockets(root, 0.71f, bore, bore + 0.04f, new Vector3(0, -0.05f, 0.06f), 0.3f, 0.0f, bore + 0.034f);
            s.shellEjectPoint.localPosition = new Vector3(0.024f, bore + 0.02f, 0.0f);
            prof.style = WeaponAnimProfile.Style.Clip; prof.showClip = true; prof.boltTravel = 0.07f; prof.boltLift = 0f;
            prof.casingLength = 0.039f; prof.casingRadius = 0.0055f; prof.clipHandPos = new Vector3(0.01f, 0.1f, -0.03f);
        }

        // ---------- ППД-34/38: деревянная ложа, кожух с овальными окнами, изогнутый коробчатый магазин ----------
        static void Ppd34(GameObject root, Faction f, WeaponAnimProfile prof)
        {
            const float bore = 0.018f;
            b("StockFore", 0, -0.03f, 0.06f, 0.044f, 0.05f, 0.2f, WoodDark);
            b("StockWrist", 0, -0.05f, -0.11f, 0.038f, 0.05f, 0.16f, WoodDark, -14);
            b("StockButt", 0, -0.075f, -0.29f, 0.042f, 0.11f, 0.24f, WoodDark, -5);
            b("Buttplate", 0, -0.086f, -0.415f, 0.044f, 0.116f, 0.012f, Steel);
            cz("Receiver", 0, bore, 0.0f, 0.02f, 0.22f, Blued, 10);
            cz("Jacket", 0, bore, 0.24f, 0.018f, 0.24f, Blued, 12);
            for (int i = 0; i < 4; i++)
            {
                b("SlotL" + i, -0.0178f, bore, 0.16f + i * 0.05f, 0.002f, 0.01f, 0.03f, new Color(0.03f, 0.03f, 0.03f));
                b("SlotR" + i, 0.0178f, bore, 0.16f + i * 0.05f, 0.002f, 0.01f, 0.03f, new Color(0.03f, 0.03f, 0.03f));
            }
            cz("MuzzleRing", 0, bore, 0.365f, 0.019f, 0.012f, Blued, 12);
            b("ChargingHandle", 0.022f, bore + 0.006f, 0.04f, 0.014f, 0.008f, 0.012f, Bright);
            TriggerGroup(-0.04f, -0.06f);
            b("MagWell", 0, -0.04f, 0.08f, 0.034f, 0.03f, 0.05f, Blued);
            CurvedMag(0, -0.055f, 0.08f, 0.026f, 0.046f, 0.18f, 16, Blued);
            RearSight(-0.02f, bore + 0.026f, 0.04f);
            FrontSight(0.35f, bore + 0.014f, 0.024f, true);
            HandRight(f, 0, -0.065f, -0.11f, -15);
            HandLeft(f, 0, -0.06f, 0.2f);
            var s = Sockets(root, 0.39f, bore, bore + 0.04f, new Vector3(0, -0.055f, 0.08f), 0.24f, 0.0f, bore + 0.032f);
            s.shellEjectPoint.localPosition = new Vector3(0.024f, bore + 0.01f, 0.05f);
            prof.style = WeaponAnimProfile.Style.Magazine; prof.casingLength = 0.025f; prof.casingRadius = 0.005f;
        }

        // ---------- РП-46: ротный пулемёт — утяжелённый ствол, ручка для переноски, приёмник ленты сбоку ----------
        static void Rp46(GameObject root, Faction f, WeaponAnimProfile prof)
        {
            Dpm(root, f, prof);
            cz("HeavyBarrel", 0, 0.022f, 0.46f, 0.016f, 0.22f, Blued, 10);
            b("CarryHandle", 0.045f, 0.03f, 0.34f, 0.012f, 0.03f, 0.12f, Wood);
            b("CarryClamp", 0.03f, 0.03f, 0.34f, 0.02f, 0.012f, 0.02f, Steel);
            b("BeltFeed", -0.045f, 0.02f, 0.05f, 0.04f, 0.04f, 0.08f, Blued);
            b("BeltBox", -0.09f, -0.04f, 0.05f, 0.05f, 0.1f, 0.12f, new Color(0.3f, 0.32f, 0.24f));
            b("BeltRounds", -0.06f, 0.02f, 0.05f, 0.04f, 0.012f, 0.06f, Brass);
        }

        // ---------- ДПМ: модернизированный ДП — пистолетная рукоять и пружина в трубке за коробкой ----------
        static void Dpm(GameObject root, Faction f, WeaponAnimProfile prof)
        {
            Dp28(root, f, prof);
            b("PistolGripM", 0, -0.08f, -0.09f, 0.032f, 0.1f, 0.044f, Wood, 12);
            cz("SpringTube", 0, 0.03f, -0.16f, 0.012f, 0.12f, Blued, 8);
            b("SafetyLever", 0.026f, -0.02f, -0.04f, 0.006f, 0.012f, 0.03f, Steel);
        }
    }
}
