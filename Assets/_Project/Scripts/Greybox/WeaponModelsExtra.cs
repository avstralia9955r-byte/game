using UnityEngine;

namespace Frontline
{
    /// <summary>
    /// Дополнительные образцы (на выбор в кастомизации): Gewehr 43, MG-34, MP-28 — Германия;
    /// ППС-43, карабин обр. 1944 (М44), ДТ — СССР. Те же правила деталировки, что у основного арсенала:
    /// узнаваемые силуэты, подвижные детали (затвор, магазин, крышка), открытые прицелы и сокеты обвесов.
    /// </summary>
    public static partial class ProceduralModels
    {
        public static bool BuildExtra(string id, GameObject root, Faction f, WeaponAnimProfile prof)
        {
            switch (id)
            {
                case "g43": G43(root, f, prof); return true;
                case "mg34": Mg34(root, f, prof); return true;
                case "mp28": Mp28(root, f, prof); return true;
                case "pps43": Pps43(root, f, prof); return true;
                case "m44": M44(root, f, prof); return true;
                case "dt": Dt(root, f, prof); return true;
            }
            return false;
        }

        // ---------- Gewehr 43: самозарядная винтовка с газовым поршнем над стволом и коробчатым магазином ----------
        static void G43(GameObject root, Faction f, WeaponAnimProfile prof)
        {
            const float bore = 0.016f;
            WoodStock(WoodDark, 0.34f, -0.45f);
            b("Receiver", 0, 0.016f, 0.0f, 0.036f, 0.042f, 0.22f, Blued);
            b("BoltCarrier", 0, 0.04f, -0.01f, 0.026f, 0.012f, 0.13f, Blued);
            b("ChargingHandle", -0.024f, 0.036f, 0.04f, 0.018f, 0.01f, 0.012f, Bright);   // рукоятка слева
            b("RailMount", 0.024f, 0.02f, 0.02f, 0.006f, 0.022f, 0.09f, Parker);           // боковая планка под ZF4
            b("HandguardTop", 0, 0.034f, 0.26f, 0.034f, 0.014f, 0.26f, WoodDark);
            cz("GasCyl", 0, bore + 0.028f, 0.45f, 0.008f, 0.18f, Blued, 8);
            cz("Barrel", 0, bore, 0.415f, 0.0092f, 0.63f, Blued, 8);   // от коробки — без разрыва
            b("BarrelBand", 0, bore + 0.004f, 0.4f, 0.042f, 0.05f, 0.016f, Steel);
            cz("MuzzleCap", 0, bore, 0.735f, 0.011f, 0.03f, Blued, 8);
            grpBox("Mag_Standard", 0, -0.07f, 0.06f, 0.03f, 0.11f, 0.06f, Blued);
            b("MagRelease", 0.018f, -0.03f, 0.03f, 0.006f, 0.012f, 0.012f, Steel);
            b("Sling", -0.03f, -0.045f, 0.0f, 0.004f, 0.026f, 0.6f, Leather, -2.5f);
            TriggerGroup(-0.02f, -0.05f);
            RearSight(0.11f, bore + 0.028f, 0.06f);
            FrontSight(0.68f, bore + 0.008f, 0.032f, true);
            HandRight(f, 0, -0.07f, -0.13f, -16);
            HandLeft(f, 0, -0.07f, 0.26f);
            var s = Sockets(root, 0.75f, bore, bore + 0.042f, new Vector3(0, -0.07f, 0.06f), 0.3f, 0.0f, 0.05f);
            s.shellEjectPoint.localPosition = new Vector3(0.025f, 0.04f, 0.0f);
            prof.style = WeaponAnimProfile.Style.Magazine; prof.casingLength = 0.057f; prof.magOut = new Vector3(0, -0.3f, 0.04f);
        }

        // ---------- MG-34: круглый дырчатый кожух, приклад, барабан-«улитка» сбоку ----------
        static void Mg34(GameObject root, Faction f, WeaponAnimProfile prof)
        {
            const float bore = 0.024f;
            b("Receiver", 0, 0.02f, 0.03f, 0.05f, 0.064f, 0.3f, Blued);
            var prev = _t; _t = grp("TopCover", 0, 0.054f, -0.05f);
            b("Cover", 0, 0.008f, 0.09f, 0.052f, 0.018f, 0.18f, Blued);
            b("CoverLatch", 0, 0.02f, 0.0f, 0.018f, 0.01f, 0.03f, Steel);
            _t = prev;
            b("FeedTray", 0, 0.046f, 0.04f, 0.085f, 0.008f, 0.06f, Blued);
            b("Stock", 0, 0.0f, -0.26f, 0.036f, 0.09f, 0.26f, Bakelite, -3);
            b("StockRidge", 0, 0.042f, -0.2f, 0.03f, 0.012f, 0.16f, Bakelite);
            b("Buttplate", 0, -0.006f, -0.395f, 0.038f, 0.11f, 0.012f, Steel);
            b("PistolGrip", 0, -0.08f, -0.06f, 0.032f, 0.1f, 0.044f, Bakelite, 14);
            b("TriggerTwin", 0, -0.04f, -0.02f, 0.006f, 0.03f, 0.012f, Steel);           // двойной спуск: одиночный/очередь
            TriggerGroup(-0.02f, -0.045f);
            // Круглый кожух с рядами отверстий
            cz("Jacket", 0, bore, 0.38f, 0.026f, 0.4f, Blued, 12);
            for (int r = 0; r < 4; r++)
                for (int i = 0; i < 6; i++)
                {
                    float a = r * Mathf.PI * 0.5f + 0.4f;
                    cy("Hole" + r + "_" + i, Mathf.Cos(a) * 0.026f, bore + Mathf.Sin(a) * 0.026f, 0.22f + i * 0.06f, 0.0075f, 0.004f,
                       new Color(0.03f, 0.03f, 0.03f), 0, 0, a * Mathf.Rad2Deg - 90f, 8);
                }
            cz("Barrel", 0, bore, 0.6f, 0.01f, 0.06f, Blued, 8);
            fz("MuzzleBooster", 0, bore, 0.62f, 0.016f, 0.06f, 1.5f, Blued, 10);
            b("ChargingHandle", 0.032f, 0.0f, 0.05f, 0.016f, 0.014f, 0.04f, Steel);
            // Барабан на 50 патронов («улитка») слева
            _t = grp("Mag_Standard", -0.075f, -0.01f, 0.04f);
            cy("Drum", 0, 0, 0, 0.055f, 0.08f, new Color(0.28f, 0.3f, 0.24f), 90, 0, 0, 12);
            fz("DrumNose", 0.04f, 0.0f, 0, 0.03f, 0.03f, 0.6f, new Color(0.28f, 0.3f, 0.24f), 8);
            b("DrumHandle", 0, 0.06f, 0, 0.012f, 0.012f, 0.05f, Steel);
            for (int i = 0; i < 3; i++) cy("Round" + i, 0.05f + i * 0.012f, 0.055f, 0, 0.005f, 0.06f, Brass, 90, 0, 0, 5);
            _t = prev;
            b("RearSight", 0, 0.08f, -0.02f, 0.018f, 0.028f, 0.008f, Blued);
            FrontSight(0.56f, bore + 0.028f, 0.03f, false);
            Bipod(0.5f, -0.006f, 0.12f, 0.25f);
            HandRight(f, 0, -0.08f, -0.06f, 14);
            HandLeft(f, -0.01f, -0.055f, 0.2f);
            var s = Sockets(root, 0.66f, bore, bore + 0.06f, new Vector3(-0.075f, -0.01f, 0.04f), 0.34f, 0.02f, 0.085f);
            s.shellEjectPoint.localPosition = new Vector3(0.0f, -0.03f, 0.05f);
            prof.style = WeaponAnimProfile.Style.Belt; prof.magOut = new Vector3(-0.22f, -0.2f, 0); prof.ejectDir = new Vector3(0.15f, -1f, 0);
        }

        // ---------- MP-28: деревянная ложа, дырчатый кожух, магазин слева ----------
        static void Mp28(GameObject root, Faction f, WeaponAnimProfile prof)
        {
            const float bore = 0.018f;
            b("StockFore", 0, -0.03f, 0.04f, 0.044f, 0.054f, 0.18f, WoodLight);
            b("StockWrist", 0, -0.05f, -0.12f, 0.038f, 0.05f, 0.16f, WoodLight, -15);
            b("StockButt", 0, -0.075f, -0.3f, 0.042f, 0.11f, 0.24f, WoodLight, -5);
            b("Buttplate", 0, -0.086f, -0.425f, 0.044f, 0.116f, 0.012f, Steel);
            cz("Receiver", 0, bore, 0.02f, 0.02f, 0.24f, Blued, 10);
            cz("ReceiverCap", 0, bore, -0.105f, 0.021f, 0.02f, Blued, 10);
            b("ChargingHandle", 0.024f, bore + 0.004f, 0.06f, 0.016f, 0.008f, 0.012f, Bright);
            cz("Jacket", 0, bore, 0.25f, 0.017f, 0.22f, Blued, 10);
            for (int i = 0; i < 5; i++) cy("Hole" + i, 0.0168f, bore, 0.16f + i * 0.042f, 0.006f, 0.003f, new Color(0.03f, 0.03f, 0.03f), 0, 0, 90, 8);
            cz("MuzzleNut", 0, bore, 0.37f, 0.012f, 0.02f, Blued, 8);
            // Магазин слева, горизонтально
            b("MagHousing", -0.03f, bore, 0.1f, 0.028f, 0.03f, 0.045f, Blued);
            grpBox("Mag_Standard", -0.13f, bore, 0.1f, 0.19f, 0.022f, 0.034f, Blued);
            TriggerGroup(-0.02f, -0.06f);
            RearSight(0.02f, bore + 0.026f, 0.04f);
            FrontSight(0.35f, bore + 0.012f, 0.026f, false);
            HandRight(f, 0, -0.065f, -0.12f, -15);
            HandLeft(f, 0, -0.06f, 0.1f);
            var s = Sockets(root, 0.39f, bore, bore + 0.038f, new Vector3(-0.13f, bore, 0.1f), 0.24f, 0.0f, bore + 0.03f);
            s.shellEjectPoint.localPosition = new Vector3(0.025f, bore + 0.01f, 0.06f);
            prof.style = WeaponAnimProfile.Style.SideMag; prof.casingLength = 0.019f; prof.casingRadius = 0.005f;
            prof.magOut = new Vector3(-0.3f, 0.0f, 0.0f);
        }

        // ---------- ППС-43: штампованная коробка, складной металлический приклад, кожух с окнами ----------
        static void Pps43(GameObject root, Faction f, WeaponAnimProfile prof)
        {
            const float bore = 0.022f;
            b("Receiver", 0, 0.012f, 0.02f, 0.036f, 0.05f, 0.26f, Parker);
            b("ReceiverTop", 0, 0.04f, -0.02f, 0.032f, 0.008f, 0.16f, Parker);
            b("ChargingHandle", 0.022f, 0.03f, 0.03f, 0.016f, 0.008f, 0.012f, Steel);
            b("PistolGrip", 0, -0.065f, -0.07f, 0.032f, 0.09f, 0.04f, WoodDark, 12);
            b("TriggerGuardPlate", 0, -0.026f, -0.02f, 0.03f, 0.008f, 0.1f, Parker);
            TriggerGroup(-0.04f, -0.04f);
            // Кожух ствола с овальными окнами и компенсатор
            b("Jacket", 0, bore, 0.24f, 0.034f, 0.036f, 0.2f, Parker);
            for (int i = 0; i < 3; i++)
            {
                b("SlotL" + i, -0.0175f, bore, 0.17f + i * 0.06f, 0.002f, 0.014f, 0.034f, Blued);
                b("SlotR" + i, 0.0175f, bore, 0.17f + i * 0.06f, 0.002f, 0.014f, 0.034f, Blued);
            }
            b("Compensator", 0, bore, 0.36f, 0.036f, 0.038f, 0.04f, Parker, 12);
            cz("Barrel", 0, bore, 0.3f, 0.008f, 0.1f, Blued, 8);
            // Сложенный вверх и вперёд приклад из прутка
            cz("StockRodL", -0.02f, 0.06f, 0.06f, 0.004f, 0.24f, Steel, 5);
            cz("StockRodR", 0.02f, 0.06f, 0.06f, 0.004f, 0.24f, Steel, 5);
            b("StockPad", 0, 0.06f, 0.18f, 0.044f, 0.012f, 0.04f, Steel);
            // Изогнутый коробчатый магазин на 35
            CurvedMag(0, -0.04f, 0.11f, 0.026f, 0.048f, 0.2f, 18, Blued);
            b("MagWell", 0, -0.026f, 0.11f, 0.036f, 0.03f, 0.056f, Parker);
            b("RearSightFlip", 0, 0.05f, -0.06f, 0.018f, 0.02f, 0.006f, Blued);
            FrontSight(0.34f, bore + 0.018f, 0.028f, true);
            HandRight(f, 0, -0.065f, -0.07f, 12);
            HandLeft(f, 0, -0.08f, 0.11f);
            var s = Sockets(root, 0.39f, bore, bore + 0.038f, new Vector3(0, -0.04f, 0.11f), 0.24f, 0.0f, bore + 0.026f);
            s.shellEjectPoint.localPosition = new Vector3(0.022f, bore + 0.012f, 0.05f);
            prof.style = WeaponAnimProfile.Style.Magazine; prof.casingLength = 0.025f; prof.casingRadius = 0.005f;
        }

        // ---------- Карабин обр. 1944 (М44): укороченная трёхлинейка с откидным штыком ----------
        static void M44(GameObject r, Faction f, WeaponAnimProfile p)
        {
            BoltAction(r, f, p, WoodLight, true, 0.66f, 0.028f);
            b("MagBox", 0, -0.085f, 0.055f, 0.026f, 0.03f, 0.07f, Blued, 8);
            // Откидной игольчатый штык, сложенный вдоль ложи справа
            b("BayonetHinge", 0.02f, 0.01f, 0.64f, 0.016f, 0.02f, 0.03f, Blued);
            b("BayonetFolded", 0.024f, 0.0f, 0.42f, 0.006f, 0.006f, 0.38f, Bright);
        }

        // ---------- ДТ: танковый Дегтярёв — пистолетная рукоять, выдвижной приклад, двухрядный диск ----------
        static void Dt(GameObject root, Faction f, WeaponAnimProfile prof)
        {
            const float bore = 0.022f;
            b("Receiver", 0, 0.014f, 0.03f, 0.05f, 0.06f, 0.3f, Blued);
            b("PistolGrip", 0, -0.075f, -0.08f, 0.034f, 0.1f, 0.044f, Bakelite, 12);
            TriggerGroup(-0.03f, -0.045f);
            // Выдвижной приклад из металла
            b("StockTube", 0, -0.01f, -0.2f, 0.024f, 0.03f, 0.2f, Parker);
            b("StockSlide", 0, -0.01f, -0.31f, 0.02f, 0.024f, 0.06f, Steel);
            b("StockPlate", 0, -0.03f, -0.35f, 0.032f, 0.1f, 0.012f, Parker);
            // Голый тяжёлый ствол (без кожуха) с плавником охлаждения
            cz("Barrel", 0, bore, 0.38f, 0.014f, 0.5f, Blued, 10);
            for (int i = 0; i < 6; i++) cz("Fin" + i, 0, bore, 0.2f + i * 0.045f, 0.02f, 0.006f, Blued, 10);
            fz("FlashHider", 0, bore, 0.66f, 0.014f, 0.08f, 2f, Blued, 10);
            cz("GasTube", 0, bore - 0.028f, 0.32f, 0.008f, 0.3f, Blued, 6);
            // Двухрядный диск на 63 патрона — толще, чем у ДП
            var prev = _t; _t = grp("Mag_Standard", 0, 0.068f, 0.06f);
            cy("Pan", 0, 0, 0, 0.11f, 0.044f, Blued, 0, 0, 0, 14);
            cy("PanTop", 0, 0.024f, 0, 0.1f, 0.004f, Parker, 0, 0, 0, 14);
            cy("PanHub", 0, 0.03f, 0, 0.026f, 0.012f, Blued, 0, 0, 0, 8);
            for (int i = 0; i < 6; i++) b("Rib" + i, 0, 0.026f, 0, 0.006f, 0.004f, 0.18f, Blued, 0, i * 30);
            _t = prev;
            b("ChargingHandle", 0.03f, -0.005f, 0.06f, 0.018f, 0.01f, 0.014f, Bright);
            b("RearSight", 0, 0.05f, -0.1f, 0.02f, 0.05f, 0.01f, Blued);
            FrontSight(0.6f, bore + 0.032f, 0.07f, false);
            Bipod(0.46f, -0.01f, 0.12f, 0.25f);
            HandRight(f, 0, -0.075f, -0.08f, 12);
            HandLeft(f, 0, -0.04f, 0.2f);
            var s = Sockets(root, 0.72f, bore, bore + 0.1f, new Vector3(0, 0.068f, 0.06f), 0.32f, -0.02f, 0.11f);
            s.shellEjectPoint.localPosition = new Vector3(0, -0.02f, 0.06f);
            prof.style = WeaponAnimProfile.Style.PanTop; prof.magOut = new Vector3(-0.1f, 0.26f, -0.08f); prof.ejectDir = new Vector3(0.1f, -1f, 0);
        }
    }
}
