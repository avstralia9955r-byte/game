using UnityEngine;

namespace Frontline
{
    /// <summary>
    /// Детальные low-poly модели оружия (вид от первого лица). Каждая деталь — отдельная часть с фаской:
    /// ложа с шейкой и затыльником, ствольные кольца, мушка с намушником, целик, спусковая скоба и крючок,
    /// затвор, кожухи с окнами, сошки, антабки, ремни. Кисти рук — с пальцами и манжетами.
    /// Оси: +Z — вперёд (ствол), +Y — вверх, начало координат — у спускового крючка.
    /// </summary>
    public static partial class ProceduralModels
    {
        public static readonly Color Parker = new(0.24f, 0.25f, 0.25f), Leather = new(0.27f, 0.17f, 0.1f), Brass = new(0.72f, 0.56f, 0.24f),
            WoodLight = new(0.52f, 0.34f, 0.18f), Glove = new(0.2f, 0.18f, 0.15f), Bright = new(0.7f, 0.72f, 0.75f);

        static Transform _t;

        static void Surf(Color c, out float smooth, out float metal)
        {
            if (c == Blued || c == Steel || c == Parker || c == Bright) { smooth = c == Bright ? 0.7f : 0.45f; metal = 0.85f; }
            else if (c == Brass) { smooth = 0.6f; metal = 0.9f; }
            else if (c == Wood || c == WoodDark || c == WoodLight) { smooth = 0.32f; metal = 0; }
            else if (c == Bakelite) { smooth = 0.5f; metal = 0; }
            else { smooth = 0.15f; metal = 0; }
        }

        // ---- Мини-DSL ----
        // Все детали — с текстурой по типу поверхности (UV в метрах), цилиндры — гладкие и с большим числом граней.
        static DetailKind KindOf(Color c)
        {
            if (c == Blued || c == Steel || c == Parker || c == Bright || c == Brass) return DetailKind.Metal;
            if (c == Wood || c == WoodDark || c == WoodLight) return DetailKind.Wood;
            if (c == Bakelite) return DetailKind.Bakelite;
            if (c == Leather) return DetailKind.Leather;
            if (c == Skin || c == Glove) return DetailKind.Skin;
            if (Near(c, SleeveDe) || Near(c, SleeveSu) || Near(c, SleeveWinter)) return DetailKind.Fabric;
            return DetailKind.Paint;
        }

        static bool Near(Color c, Color s)
        {
            // Рукав и его затемнённые варианты (манжета = рукав × 0.85)
            for (float k = 0.8f; k <= 1.001f; k += 0.05f)
                if (Mathf.Abs(c.r - s.r * k) < 0.02f && Mathf.Abs(c.g - s.g * k) < 0.02f && Mathf.Abs(c.b - s.b * k) < 0.02f) return true;
            return false;
        }

        static int Seg(int seg) => seg >= 8 ? Mathf.RoundToInt(seg * 1.75f) : seg;

        static GameObject DP(string n, Mesh mesh, Vector3 pos, Vector3 scale, Color c, Vector3 euler)
        {
            Surf(c, out float s, out float m);
            return DetailPart(_t, n, mesh, pos, scale, c, euler, KindOf(c), s, m);
        }

        /// <summary>Коробка: металл — с фаской (чёткие грани), дерево, кожа рук, бакелит, ремни — скруглённые.</summary>
        static GameObject b(string n, float x, float y, float z, float sx, float sy, float sz, Color c, float rx = 0, float ry = 0, float rz = 0)
        {
            var k = KindOf(c);
            Mesh mesh = k switch
            {
                DetailKind.Skin => MeshDetail.RoundBox(0.42f),
                DetailKind.Wood or DetailKind.Bakelite => MeshDetail.RoundBox(0.22f),
                DetailKind.Leather or DetailKind.Fabric => MeshDetail.RoundBox(0.3f),
                _ => MeshUtil.Bevel(Mathf.Min(sx, Mathf.Min(sy, sz)) < 0.012f ? 0.12f : 0.1f)
            };
            return DP(n, mesh, new Vector3(x, y, z), new Vector3(sx, sy, sz), c, new Vector3(rx, ry, rz));
        }

        /// <summary>Цилиндр вдоль Z.</summary>
        static GameObject cz(string n, float x, float y, float z, float r, float len, Color c, int seg = 8)
            => DP(n, MeshUtil.Cylinder(Seg(seg)), new Vector3(x, y, z), new Vector3(r * 2, len, r * 2), c, new Vector3(90, 0, 0));

        /// <summary>Цилиндр с произвольным поворотом (ось — локальная Y до поворота).</summary>
        static GameObject cy(string n, float x, float y, float z, float r, float len, Color c, float rx, float ry, float rz, int seg = 8)
            => DP(n, MeshUtil.Cylinder(Seg(seg)), new Vector3(x, y, z), new Vector3(r * 2, len, r * 2), c, new Vector3(rx, ry, rz));

        /// <summary>Усечённый конус вдоль Z: радиус r сзади, r·top спереди.</summary>
        static GameObject fz(string n, float x, float y, float z, float r, float len, float top, Color c, int seg = 8)
            => DP(n, MeshUtil.Frustum(Seg(seg), top), new Vector3(x, y, z), new Vector3(r * 2, len, r * 2), c, new Vector3(90, 0, 0));

        static Transform grp(string n, float x, float y, float z, float rx = 0, float ry = 0, float rz = 0)
        {
            var g = Empty(_t, n, new Vector3(x, y, z));
            g.localEulerAngles = new Vector3(rx, ry, rz);
            return g;
        }

        // ---- Руки ----
        static Color SleeveCol(Faction f) => Theme == MapTheme.Winter ? SleeveWinter : f == Faction.Germany ? SleeveDe : SleeveSu;

        /// <summary>
        /// Правая кисть, обхватывающая пистолетную рукоять / шейку ложи: ладонь справа, четыре пальца по две фаланги
        /// обхватывают рукоять спереди, указательный — на спуске, большой — сверху слева. Манжета рукава.
        /// Предплечье и плечо достраивает ViewModelArms (IK от плеча к точке «Wrist»).
        /// </summary>
        static Transform HandRight(Faction f, float x, float y, float z, float rx = 0, float ry = 0, float rz = 0)
        {
            var prev = _t; var h = grp("HandR", x, y, z, rx, ry, rz); _t = h;
            Color skin = Theme == MapTheme.Winter ? Glove : Skin, sl = SleeveCol(f);
            b("Palm", 0.026f, -0.008f, -0.006f, 0.032f, 0.088f, 0.066f, skin);
            b("PalmHeel", 0.018f, -0.044f, -0.022f, 0.034f, 0.03f, 0.05f, skin);
            for (int i = 0; i < 3; i++)          // средний, безымянный, мизинец
            {
                float fy = 0.006f - i * 0.022f, w = 0.019f - i * 0.0015f;
                b("FingerP" + i, 0.014f, fy, 0.03f, 0.03f, w, 0.02f, skin, 0, -32, 0);
                b("FingerM" + i, -0.006f, fy, 0.036f, 0.022f, w * 0.95f, 0.018f, skin, 0, -78, 0);
                b("FingerD" + i, -0.02f, fy, 0.026f, 0.016f, w * 0.9f, 0.016f, skin, 0, -125, 0);
            }
            b("IndexP", 0.016f, 0.03f, 0.035f, 0.016f, 0.017f, 0.032f, skin, 6, -12, 0);
            b("IndexD", 0.008f, 0.024f, 0.06f, 0.015f, 0.015f, 0.026f, skin, 30, -20, 0);
            b("ThumbP", 0.004f, 0.04f, -0.016f, 0.022f, 0.02f, 0.036f, skin, 0, 25, 8);
            b("ThumbD", -0.018f, 0.044f, 0.004f, 0.018f, 0.017f, 0.03f, skin, 0, 60, 8);
            b("Knuckles", 0.03f, 0.022f, 0.026f, 0.026f, 0.016f, 0.03f, skin);
            b("Wrist", 0.03f, -0.03f, -0.05f, 0.05f, 0.055f, 0.05f, skin, -18, -10);
            b("Cuff", 0.034f, -0.04f, -0.08f, 0.078f, 0.082f, 0.05f, sl * 0.85f, -18, -10);
            Empty(_t, "WristPoint", new Vector3(0.038f, -0.05f, -0.1f));
            _t = prev; return h;
        }

        /// <summary>Левая кисть под цевьём: ладонь снизу, пальцы по фалангам обхватывают правый бок, большой — левый.</summary>
        static Transform HandLeft(Faction f, float x, float y, float z, float rx = 0, float ry = 0, float rz = 0)
        {
            var prev = _t; var h = grp("HandL", x, y, z, rx, ry, rz); _t = h;
            Color skin = Theme == MapTheme.Winter ? Glove : Skin, sl = SleeveCol(f);
            b("Palm", -0.004f, -0.016f, 0f, 0.064f, 0.03f, 0.09f, skin);
            for (int i = 0; i < 4; i++)
            {
                float fz = 0.034f - i * 0.022f, w = 0.019f - (i == 3 ? 0.004f : 0);
                b("FingerP" + i, 0.034f, -0.006f, fz, 0.02f, 0.034f, w, skin, 0, 0, -20);
                b("FingerM" + i, 0.04f, 0.018f, fz, 0.018f, 0.024f, w * 0.95f, skin, 0, 0, 15);
                b("FingerD" + i, 0.032f, 0.034f, fz, 0.016f, 0.018f, w * 0.9f, skin, 0, 0, 55);
            }
            b("ThumbP", -0.034f, 0.0f, 0.02f, 0.022f, 0.032f, 0.032f, skin, 0, 0, 18);
            b("ThumbD", -0.03f, 0.024f, 0.034f, 0.018f, 0.024f, 0.026f, skin, 20, 0, -12);
            b("Wrist", -0.016f, -0.03f, -0.055f, 0.052f, 0.05f, 0.05f, skin, -20, 24);
            b("Cuff", -0.024f, -0.04f, -0.082f, 0.078f, 0.082f, 0.05f, sl * 0.85f, -20, 24);
            // Наручные часы (ремешок, корпус, стекло)
            cy("WatchBand", -0.016f, -0.028f, -0.062f, 0.029f, 0.016f, Leather, 70, 24, 0, 12);
            cy("WatchCase", -0.03f, -0.006f, -0.064f, 0.012f, 0.007f, Steel, -20, 24, -40, 12);
            cy("WatchGlass", -0.032f, -0.002f, -0.064f, 0.01f, 0.003f, new Color(0.85f, 0.85f, 0.78f), -20, 24, -40, 12);
            Empty(_t, "WristPoint", new Vector3(-0.03f, -0.05f, -0.1f));
            _t = prev; return h;
        }

        // ---- Общие узлы ----
        static void WoodStock(Color w, float foreEnd, float buttZ = -0.46f)
        {
            b("StockFore", 0, -0.034f, (foreEnd - 0.06f) * 0.5f, 0.048f, 0.058f, foreEnd + 0.06f, w);
            b("StockWrist", 0, -0.054f, -0.135f, 0.04f, 0.05f, 0.17f, w, -16);
            b("StockButt", 0, -0.078f, (buttZ - 0.19f) * 0.5f, 0.044f, 0.118f, -buttZ - 0.17f, w, -5);
            b("Buttplate", 0, -0.09f, buttZ, 0.046f, 0.124f, 0.012f, Steel);
            TriggerGroup(-0.02f, -0.066f);
        }

        static void TriggerGroup(float z, float y)
        {
            b("TriggerGuard", 0, y - 0.012f, z, 0.01f, 0.005f, 0.085f, Blued);
            b("TriggerGuardF", 0, y, z + 0.042f, 0.01f, 0.024f, 0.005f, Blued);
            b("TriggerGuardR", 0, y, z - 0.042f, 0.01f, 0.024f, 0.005f, Blued);
            b("Trigger", 0, y + 0.006f, z - 0.004f, 0.005f, 0.024f, 0.006f, Blued, -18);
        }

        static void FrontSight(float z, float baseY, float h, bool hood)
        {
            b("IronSights", 0, baseY + h * 0.5f, z, 0.004f, h, 0.006f, Blued);
            b("FrontSightBase", 0, baseY - 0.004f, z, 0.016f, 0.012f, 0.03f, Blued);
            if (hood) { b("HoodL", -0.011f, baseY + h * 0.5f, z, 0.003f, h * 1.2f, 0.026f, Blued); b("HoodR", 0.011f, baseY + h * 0.5f, z, 0.003f, h * 1.2f, 0.026f, Blued); b("HoodT", 0, baseY + h * 1.1f, z, 0.025f, 0.003f, 0.026f, Blued); }
        }

        static void RearSight(float z, float y, float len = 0.06f)
        {
            b("RearSightBase", 0, y, z, 0.022f, 0.01f, len, Blued);
            b("RearSightLeaf", 0, y + 0.008f, z + len * 0.1f, 0.016f, 0.004f, len * 0.9f, Blued, -4);
            b("RearNotchL", -0.007f, y + 0.014f, z - len * 0.4f, 0.006f, 0.008f, 0.004f, Blued);
            b("RearNotchR", 0.007f, y + 0.014f, z - len * 0.4f, 0.006f, 0.008f, 0.004f, Blued);
        }

        static void Bipod(float z, float y, float spread = 0.09f, float len = 0.22f)
        {
            b("BipodYoke", 0, y, z, 0.05f, 0.016f, 0.02f, Steel);
            cy("BipodL", -spread * 0.5f, y - len * 0.42f, z + 0.02f, 0.006f, len, Steel, 12, 0, -22, 6);
            cy("BipodR", spread * 0.5f, y - len * 0.42f, z + 0.02f, 0.006f, len, Steel, 12, 0, 22, 6);
        }

        /// <summary>Изогнутый магазин из сегментов (StG-44, АВС).</summary>
        static Transform CurvedMag(float x, float y, float z, float w, float depth, float len, float curveDeg, Color c)
        {
            var prev = _t; var g = grp("Mag_Standard", x, y, z); _t = g;
            int n = 4; float seg = len / n;
            Vector3 p = Vector3.zero; float ang = -8;
            for (int i = 0; i < n; i++)
            {
                var rot = Quaternion.Euler(ang, 0, 0);
                Vector3 c0 = p + rot * new Vector3(0, -seg * 0.5f, 0);
                b("Seg" + i, c0.x, c0.y, c0.z, w, seg * 1.08f, depth, c, ang);
                p += rot * new Vector3(0, -seg, 0);
                ang -= curveDeg / n;
            }
            b("BasePlate", p.x, p.y - 0.004f, p.z, w * 1.1f, 0.008f, depth * 1.08f, Steel, ang);
            _t = prev; return g;
        }

        static WeaponSockets Sockets(GameObject root, float muzzleZ, float boreY, float sightY, Vector3 mag, float underZ, float opticZ = 0.03f, float opticY = -1)
        {
            var t = root.transform;
            var s = root.AddComponent<WeaponSockets>();
            s.muzzle = Empty(t, "Socket_Muzzle", new Vector3(0, boreY, muzzleZ));
            s.magazine = Empty(t, "Socket_Mag", mag);
            s.optic = Empty(t, "Socket_Optic", new Vector3(0, opticY < 0 ? sightY - 0.012f : opticY, opticZ));
            s.underbarrel = Empty(t, "Socket_Under", new Vector3(0, boreY - 0.045f, underZ));
            s.laser = Empty(t, "Socket_Laser", new Vector3(0.034f, boreY - 0.005f, underZ + 0.03f));
            s.muzzleFlashPoint = Empty(t, "MuzzleFlash", new Vector3(0, boreY, muzzleZ + 0.02f));
            s.shellEjectPoint = Empty(t, "ShellEject", new Vector3(0.03f, boreY + 0.015f, 0.0f));
            s.adsAnchor = Empty(t, "ADS_Iron", new Vector3(0, sightY, -0.02f));
            s.adsAnchorOptic = Empty(t, "ADS_Optic", new Vector3(0, (opticY < 0 ? sightY - 0.012f : opticY) + 0.035f, -0.02f));
            return s;
        }

        // =====================================================================================

        public static GameObject Weapon(string id, WeaponCategory cat, Faction f)
        {
            var root = new GameObject("VM_" + id);
            root.transform.SetParent(Root, false);
            _t = root.transform;
            var prof = root.AddComponent<WeaponAnimProfile>();

            switch (id)
            {
                case "kar98k": Kar98k(root, f, prof); break;
                case "mosin": Mosin(root, f, prof); break;
                case "stg44": Stg44(root, f, prof); break;
                case "avt40": Svt(root, f, prof, false); break;
                case "avs36": Svt(root, f, prof, true); break;
                case "fg42": Fg42(root, f, prof); break;
                case "mp40": Mp40(root, f, prof); break;
                case "ppsh41": Ppsh(root, f, prof); break;
                case "mg42": Mg42(root, f, prof); break;
                case "dp28": Dp28(root, f, prof); break;
                case "p38": P38(root, f, prof); break;
                case "p08": Luger(root, f, prof); break;
                case "tt33": Tt33(root, f, prof); break;
                case "nagant": Nagant(root, f, prof); break;
                case "panzerfaust": Panzerfaust(root, f, prof); break;
                case "ptrd": Ptrd(root, f, prof); break;
                default:
                    if (BuildExtra(id, root, f, prof) || BuildMore(id, root, f, prof) || BuildWW2(id, root, f, prof)) break;
                    if (cat == WeaponCategory.Knife) Knife(root, f, prof);
                    else Stg44(root, f, prof);
                    break;
            }
            FinishWeapon(root, cat);
            _t = null;
            return root;
        }

        // ---------- Противотанковое ----------

        /// <summary>Panzerfaust 60: труба с откидным прицелом и спусковым рычагом, надкалиберная граната (прячется после выстрела).</summary>
        static void Panzerfaust(GameObject root, Faction f, WeaponAnimProfile prof)
        {
            Color tube = new(0.3f, 0.33f, 0.24f), head = new(0.34f, 0.37f, 0.26f), red = new(0.72f, 0.12f, 0.08f);
            cz("Tube", 0, 0, -0.12f, 0.024f, 0.86f, tube, 10);
            b("TubeBand", 0, 0, 0.12f, 0.054f, 0.054f, 0.02f, Blued);
            b("TubeBandRear", 0, 0, -0.48f, 0.054f, 0.054f, 0.02f, Blued);
            b("SightLeaf", 0, 0.06f, 0.13f, 0.034f, 0.05f, 0.004f, Blued);
            b("SightPin", 0, 0.034f, 0.13f, 0.008f, 0.014f, 0.01f, Blued);
            b("TriggerLever", 0, 0.03f, 0.04f, 0.012f, 0.012f, 0.1f, Blued, -14);
            b("TriggerButton", 0, 0.036f, -0.01f, 0.014f, 0.014f, 0.014f, red);
            b("Warning", 0.025f, 0, -0.25f, 0.002f, 0.035f, 0.28f, red);
            var prev = _t; _t = grp("Warhead", 0, 0, 0.3f);
            cz("Boom", 0, 0, 0.02f, 0.026f, 0.07f, Blued, 8);
            fz("Rear", 0, 0, 0.1f, 0.03f, 0.1f, 2.5f, head, 12);
            cz("Body", 0, 0, 0.19f, 0.075f, 0.08f, head, 12);
            fz("Nose", 0, 0, 0.29f, 0.075f, 0.13f, 0.25f, head, 12);
            _t = prev;
            HandRight(f, 0.0f, -0.05f, -0.03f, -10);
            HandLeft(f, 0f, -0.046f, 0.2f);
            Sockets(root, 0.64f, 0f, 0.086f, new Vector3(0, -0.05f, 0), 0.2f);
            prof.style = WeaponAnimProfile.Style.Launcher; prof.ejectsCasings = false; prof.boltTravel = 0f;
        }

        /// <summary>ПТРД-41: длинный ствол с коробчатым дульным тормозом, сошки, рукоятка для переноски, однозарядный затвор.</summary>
        static void Ptrd(GameObject root, Faction f, WeaponAnimProfile prof)
        {
            const float bore = 0.02f;
            cz("Receiver", 0, bore, 0.02f, 0.03f, 0.3f, Blued, 10);
            cz("Barrel", 0, bore, 0.75f, 0.017f, 1.15f, Blued, 10);
            cz("BarrelRing", 0, bore, 0.35f, 0.027f, 0.08f, Blued, 10);
            b("Brake", 0, bore, 1.38f, 0.075f, 0.05f, 0.12f, Blued);
            b("BrakeTop", 0, bore + 0.024f, 1.38f, 0.04f, 0.012f, 0.12f, Blued);
            cz("StockTube", 0, bore - 0.01f, -0.3f, 0.024f, 0.4f, Blued, 8);
            b("ShoulderPad", 0, -0.02f, -0.52f, 0.05f, 0.17f, 0.06f, Leather);
            b("Cheek", 0, bore + 0.03f, -0.32f, 0.035f, 0.04f, 0.2f, Wood);
            b("PistolGrip", 0, -0.06f, -0.1f, 0.03f, 0.1f, 0.045f, Wood, 15);
            TriggerGroup(-0.06f, -0.03f);
            // Рукоятка для переноски — сбоку от ствола: сверху она перекрывала линию прицеливания
            b("CarryHandle", 0.045f, bore + 0.005f, 0.42f, 0.04f, 0.014f, 0.13f, Wood);
            Bipod(0.62f, bore - 0.02f, 0.2f, 0.34f);
            FrontSight(0.84f, bore + 0.018f, 0.032f, true);
            RearSight(0.16f, bore + 0.034f, 0.05f);
            b("Mag_Standard", 0, -0.02f, 0.03f, 0.02f, 0.01f, 0.05f, Blued);
            var prev = _t; _t = grp("BoltHandle", 0, bore, -0.05f);
            cz("BoltBody", 0, 0, -0.02f, 0.019f, 0.12f, Bright, 8);
            cy("Stem", 0.036f, 0, 0, 0.006f, 0.06f, Bright, 0, 0, 90, 6);
            Part(_t, "Knob", MeshUtil.Ico(), new Vector3(0.07f, 0, 0), Vector3.one * 0.026f, Bright, default, false, 0.6f, 0.85f);
            _t = prev;
            HandRight(f, 0f, -0.08f, -0.14f, -10);
            HandLeft(f, 0f, -0.03f, 0.33f);
            var s = Sockets(root, 1.44f, bore, bore + 0.018f + 0.032f, new Vector3(0, -0.02f, 0.03f), 0.4f);
            s.shellEjectPoint.localPosition = new Vector3(0.03f, bore + 0.02f, 0.0f);
            prof.style = WeaponAnimProfile.Style.Clip; prof.showClip = false; prof.boltTravel = 0.1f; prof.boltLift = 60f;
            prof.casingLength = 0.11f; prof.casingRadius = 0.011f; prof.clipHandPos = new Vector3(0.03f, 0.07f, -0.02f);
        }

        // ---------- Винтовки с продольно-скользящим затвором ----------

        static void BoltAction(GameObject root, Faction f, WeaponAnimProfile prof, Color wood, bool straightBolt, float barrelEnd, float sightH)
        {
            const float bore = 0.014f;
            WoodStock(wood, barrelEnd - 0.22f);
            b("Handguard", 0, 0.004f, 0.3f, 0.036f, 0.024f, 0.3f, wood);
            cz("Receiver", 0, bore, 0.0f, 0.017f, 0.21f, Blued, straightBolt ? 6 : 10);
            b("ReceiverBridge", 0, bore + 0.012f, -0.07f, 0.03f, 0.012f, 0.03f, Blued);
            cz("Barrel", 0, bore, (0.1f + barrelEnd) * 0.5f, 0.0095f, barrelEnd - 0.1f, Blued, 8);
            b("BandRear", 0, -0.006f, barrelEnd - 0.34f, 0.054f, 0.056f, 0.016f, Blued);
            b("BandFront", 0, -0.006f, barrelEnd - 0.2f, 0.052f, 0.052f, 0.02f, Blued);
            cz("CleaningRod", 0, -0.028f, barrelEnd - 0.1f, 0.003f, 0.2f, Bright, 5);
            b("SlingFront", -0.028f, -0.03f, barrelEnd - 0.34f, 0.005f, 0.028f, 0.04f, Leather);
            b("Sling", -0.03f, -0.045f, 0.02f, 0.004f, 0.026f, 0.62f, Leather, -2.5f);
            b("Mag_Standard", 0, -0.07f, 0.05f, 0.03f, straightBolt ? 0.03f : 0.012f, 0.085f, Blued);
            RearSight(0.15f, bore + 0.022f, 0.07f);
            FrontSight(barrelEnd - 0.03f, bore + 0.008f, sightH, !straightBolt);

            // Затвор: тело + рукоять. Корень группы — ось затвора (вращение вокруг Z = подъём рукояти).
            var prev = _t; _t = grp("BoltHandle", 0, bore, -0.06f);
            cz("BoltBody", 0, 0, -0.03f, 0.0105f, 0.1f, Bright, 8);
            b("BoltShroud", 0, 0, -0.085f, 0.022f, 0.022f, 0.02f, Blued);
            if (straightBolt) { cy("Stem", 0.03f, 0, 0, 0.005f, 0.05f, Bright, 0, 0, 90, 6); Part(_t, "Knob", MeshUtil.Ico(), new Vector3(0.058f, 0, 0), Vector3.one * 0.022f, Bright, default, false, 0.6f, 0.85f); }
            else { cy("Stem", 0.026f, -0.012f, 0, 0.005f, 0.05f, Bright, 0, 0, 55, 6); Part(_t, "Knob", MeshUtil.Ico(), new Vector3(0.046f, -0.028f, 0), Vector3.one * 0.022f, Bright, default, false, 0.6f, 0.85f); }
            _t = prev;

            HandRight(f, 0.0f, -0.07f, -0.13f, -16);
            HandLeft(f, 0f, -0.07f, 0.28f);
            var s = Sockets(root, barrelEnd, bore, bore + 0.008f + sightH, new Vector3(0, -0.07f, 0.05f), 0.3f, 0.02f, bore + 0.03f);
            s.shellEjectPoint.localPosition = new Vector3(0.02f, bore + 0.02f, -0.02f);
            prof.style = WeaponAnimProfile.Style.Clip; prof.boltTravel = 0.085f; prof.boltLift = straightBolt ? 85 : 70;
            prof.casingLength = 0.057f; prof.casingRadius = 0.006f; prof.clipHandPos = new Vector3(0.01f, 0.1f, -0.03f);
        }

        static void Kar98k(GameObject r, Faction f, WeaponAnimProfile p) => BoltAction(r, f, p, Wood, false, 0.74f, 0.026f);

        static void Mosin(GameObject r, Faction f, WeaponAnimProfile p)
        {
            BoltAction(r, f, p, WoodDark, true, 0.8f, 0.028f);
            b("MagBox", 0, -0.085f, 0.055f, 0.026f, 0.03f, 0.07f, Blued, 8);   // выступающая магазинная коробка
            b("Bayonet", 0.014f, 0.014f, 1.0f, 0.006f, 0.006f, 0.42f, Bright);  // игольчатый штык
            b("BayonetSocket", 0.008f, 0.014f, 0.79f, 0.022f, 0.024f, 0.05f, Blued);
        }

        // ---------- StG-44 ----------

        static void Stg44(GameObject root, Faction f, WeaponAnimProfile prof)
        {
            const float bore = 0.02f;
            b("Receiver", 0, 0.018f, 0.06f, 0.042f, 0.062f, 0.34f, Parker);
            b("ReceiverRib", 0.022f, 0.02f, 0.06f, 0.003f, 0.02f, 0.3f, Parker);
            b("EjectionPort", 0.022f, 0.032f, 0.05f, 0.002f, 0.016f, 0.06f, Blued);
            b("LowerReceiver", 0, -0.028f, 0.0f, 0.036f, 0.035f, 0.16f, Parker);
            b("MagWell", 0, -0.04f, 0.115f, 0.036f, 0.06f, 0.06f, Parker);
            b("PistolGrip", 0, -0.09f, -0.055f, 0.034f, 0.1f, 0.046f, Bakelite, 14);
            b("Stock", 0, 0.0f, -0.24f, 0.038f, 0.085f, 0.24f, Wood, -3);
            b("StockButt", 0, -0.012f, -0.365f, 0.04f, 0.12f, 0.02f, Steel);
            b("Handguard", 0, 0.004f, 0.3f, 0.044f, 0.05f, 0.15f, Parker);
            cz("GasTube", 0, bore + 0.026f, 0.36f, 0.009f, 0.3f, Parker, 6);
            cz("Barrel", 0, bore, 0.47f, 0.0095f, 0.26f, Blued, 8);
            cz("MuzzleNut", 0, bore, 0.595f, 0.012f, 0.03f, Blued, 8);
            b("ChargingHandle", -0.026f, 0.03f, 0.1f, 0.016f, 0.01f, 0.012f, Blued);
            TriggerGroup(-0.005f, -0.052f);
            RearSight(0.1f, 0.054f, 0.07f);
            FrontSight(0.52f, bore + 0.024f, 0.03f, true);
            CurvedMag(0, -0.065f, 0.115f, 0.028f, 0.05f, 0.2f, 22, Blued);
            b("SlingLoop", -0.022f, 0.01f, 0.33f, 0.006f, 0.02f, 0.012f, Steel);
            HandRight(f, 0, -0.09f, -0.055f, 14);
            HandLeft(f, 0, -0.045f, 0.3f);
            var s = Sockets(root, 0.61f, bore, bore + 0.054f, new Vector3(0, -0.065f, 0.115f), 0.32f, 0.03f, 0.056f);
            s.shellEjectPoint.localPosition = new Vector3(0.03f, 0.035f, 0.05f);
            prof.style = WeaponAnimProfile.Style.Magazine; prof.casingLength = 0.033f; prof.casingRadius = 0.0055f;
        }

        // ---------- АВТ-40 / АВС-36 ----------

        static void Svt(GameObject root, Faction f, WeaponAnimProfile prof, bool avs)
        {
            const float bore = 0.014f;
            WoodStock(avs ? WoodLight : WoodDark, 0.36f, -0.44f);
            b("Receiver", 0, 0.014f, 0.01f, 0.036f, 0.04f, 0.22f, Blued);
            b("ReceiverCover", 0, 0.036f, -0.02f, 0.03f, 0.012f, 0.16f, Blued);
            b("ChargingHandle", 0.024f, 0.022f, 0.03f, 0.016f, 0.008f, 0.012f, Bright);
            b("HandguardMetal", 0, 0.012f, 0.3f, 0.04f, 0.03f, 0.16f, Parker);
            for (int i = 0; i < 4; i++) b("Vent" + i, 0.0205f, 0.014f, 0.24f + i * 0.036f, 0.002f, 0.01f, 0.022f, Blued);
            b("HandguardWood", 0, 0.004f, 0.46f, 0.038f, 0.026f, 0.14f, avs ? WoodLight : WoodDark);
            cz("Barrel", 0, bore, 0.425f, 0.0095f, 0.63f, Blued, 8);   // от коробки до тормоза — без разрыва
            cz("GasBlock", 0, bore + 0.012f, 0.6f, 0.01f, 0.08f, Blued, 6);
            // Многокамерный дульный тормоз
            b("Brake", 0, bore, 0.775f, 0.026f, 0.028f, 0.075f, Blued);
            for (int i = 0; i < 3; i++) b("BrakeSlot" + i, 0, bore, 0.752f + i * 0.022f, 0.03f, 0.012f, 0.008f, Parker);
            RearSight(0.12f, bore + 0.024f, 0.06f);
            FrontSight(0.7f, bore + 0.008f, 0.03f, true);
            float magLen = avs ? 0.12f : 0.085f;
            _ = avs ? CurvedMag(0, -0.062f, 0.06f, 0.03f, 0.065f, magLen, 10, Blued)
                    : grpBox("Mag_Standard", 0, -0.062f - magLen * 0.5f, 0.06f, 0.03f, magLen, 0.068f, Blued);
            cz("CleaningRod", 0.024f, -0.02f, 0.4f, 0.003f, 0.5f, Bright, 5);
            b("Sling", -0.03f, -0.045f, 0.0f, 0.004f, 0.026f, 0.6f, Leather, -2.5f);
            HandRight(f, 0, -0.07f, -0.13f, -16);
            HandLeft(f, 0, -0.07f, 0.26f);
            var s = Sockets(root, 0.815f, bore, bore + 0.038f, new Vector3(0, -0.062f, 0.06f), 0.3f, 0.0f, 0.045f);
            s.shellEjectPoint.localPosition = new Vector3(0.025f, 0.035f, 0.0f);
            prof.style = WeaponAnimProfile.Style.Magazine; prof.casingLength = 0.054f; prof.magOut = new Vector3(0, -0.3f, 0.05f);
        }

        static Transform grpBox(string n, float x, float y, float z, float sx, float sy, float sz, Color c) => b(n, x, y, z, sx, sy, sz, c).transform;

        // ---------- FG-42 ----------

        static void Fg42(GameObject root, Faction f, WeaponAnimProfile prof)
        {
            const float bore = 0.02f;
            b("Receiver", 0, 0.02f, 0.06f, 0.04f, 0.056f, 0.36f, Parker);
            b("TopRail", 0, 0.05f, 0.04f, 0.018f, 0.008f, 0.2f, Blued);
            b("Stock", 0, 0.012f, -0.24f, 0.034f, 0.07f, 0.24f, Parker);
            b("StockRib", 0, 0.012f, -0.24f, 0.038f, 0.02f, 0.2f, Blued);
            b("Buttplate", 0, 0.012f, -0.365f, 0.036f, 0.09f, 0.014f, Steel);
            b("PistolGrip", 0, -0.075f, -0.035f, 0.032f, 0.115f, 0.042f, Bakelite, 32);   // характерный наклонный
            b("Handguard", 0, 0.002f, 0.32f, 0.042f, 0.046f, 0.2f, Wood);
            cz("Barrel", 0, bore, 0.52f, 0.0095f, 0.34f, Blued, 8);
            fz("MuzzleBrake", 0, bore, 0.71f, 0.012f, 0.06f, 1.5f, Blued, 8);
            b("Bayonet", 0, bore - 0.03f, 0.56f, 0.005f, 0.005f, 0.24f, Bright);
            TriggerGroup(0.0f, -0.035f);
            grpBox("Mag_Standard", -0.085f, 0.02f, 0.09f, 0.13f, 0.03f, 0.055f, Blued);   // магазин слева
            b("MagHousing", -0.026f, 0.02f, 0.09f, 0.016f, 0.036f, 0.062f, Parker);
            b("ChargingHandle", 0.024f, 0.012f, 0.14f, 0.016f, 0.008f, 0.012f, Bright);
            b("RearSightFold", 0, 0.062f, -0.03f, 0.014f, 0.022f, 0.006f, Blued);
            FrontSight(0.66f, bore + 0.008f, 0.034f, false);
            Bipod(0.42f, -0.02f, 0.08f, 0.2f);
            HandRight(f, 0, -0.075f, -0.04f, 30);
            HandLeft(f, 0, -0.045f, 0.3f);
            var s = Sockets(root, 0.745f, bore, bore + 0.042f, new Vector3(-0.085f, 0.02f, 0.09f), 0.3f, 0.04f, 0.056f);
            s.shellEjectPoint.localPosition = new Vector3(0.028f, 0.03f, 0.08f);
            prof.style = WeaponAnimProfile.Style.SideMag; prof.magOut = new Vector3(-0.3f, -0.03f, 0); prof.casingLength = 0.057f;
        }

        // ---------- MP-40 ----------

        static void Mp40(GameObject root, Faction f, WeaponAnimProfile prof)
        {
            const float bore = 0.022f;
            cz("Receiver", 0, bore, 0.06f, 0.02f, 0.3f, Blued, 10);
            cz("ReceiverCap", 0, bore, -0.1f, 0.021f, 0.03f, Blued, 10);
            b("Lower", 0, -0.012f, 0.02f, 0.034f, 0.04f, 0.2f, Bakelite);
            b("PistolGrip", 0, -0.075f, -0.05f, 0.032f, 0.1f, 0.042f, Bakelite, 12);
            b("MagHousing", 0, -0.035f, 0.14f, 0.032f, 0.07f, 0.045f, Blued);
            grpBox("Mag_Standard", 0, -0.17f, 0.14f, 0.022f, 0.21f, 0.034f, Blued);
            cz("Barrel", 0, bore, 0.3f, 0.009f, 0.2f, Blued, 8);
            b("RestingBar", 0, bore - 0.02f, 0.3f, 0.01f, 0.018f, 0.12f, Bakelite);   // упор под стволом
            cz("MuzzleNut", 0, bore, 0.405f, 0.012f, 0.02f, Blued, 8);
            b("ChargingHandle", -0.024f, bore, 0.1f, 0.018f, 0.008f, 0.01f, Bright);
            b("ChargingSlot", -0.0205f, bore, 0.07f, 0.002f, 0.008f, 0.14f, Parker);
            // Сложенный под оружием приклад
            cz("StockArmL", -0.014f, -0.036f, 0.02f, 0.004f, 0.3f, Steel, 5);
            cz("StockArmR", 0.014f, -0.036f, 0.02f, 0.004f, 0.3f, Steel, 5);
            b("StockPlate", 0, -0.04f, 0.18f, 0.04f, 0.012f, 0.06f, Steel);
            TriggerGroup(0.0f, -0.04f);
            b("RearSight", 0, bore + 0.03f, 0.0f, 0.016f, 0.02f, 0.012f, Blued);
            FrontSight(0.38f, bore + 0.01f, 0.026f, true);
            HandRight(f, 0, -0.075f, -0.05f, 12);
            HandLeft(f, 0, -0.1f, 0.14f, 0, 0, 0);
            var s = Sockets(root, 0.42f, bore, bore + 0.04f, new Vector3(0, -0.17f, 0.14f), 0.26f, 0.02f, bore + 0.03f);
            s.shellEjectPoint.localPosition = new Vector3(0.025f, bore + 0.01f, 0.08f);
            prof.style = WeaponAnimProfile.Style.Magazine; prof.casingLength = 0.019f; prof.casingRadius = 0.005f;
        }

        // ---------- ППШ-41 ----------

        static void Ppsh(GameObject root, Faction f, WeaponAnimProfile prof)
        {
            const float bore = 0.02f;
            b("StockFore", 0, -0.034f, 0.02f, 0.046f, 0.058f, 0.2f, Wood);
            b("StockWrist", 0, -0.054f, -0.135f, 0.04f, 0.05f, 0.17f, Wood, -16);
            b("StockButt", 0, -0.08f, -0.31f, 0.044f, 0.118f, 0.25f, Wood, -5);
            b("Buttplate", 0, -0.092f, -0.44f, 0.046f, 0.124f, 0.012f, Steel);
            TriggerGroup(-0.02f, -0.066f);
            b("Receiver", 0, bore, 0.02f, 0.04f, 0.044f, 0.24f, Blued);
            b("ReceiverTop", 0, bore + 0.024f, 0.0f, 0.034f, 0.01f, 0.18f, Blued);
            b("ChargingHandle", 0.024f, bore + 0.006f, 0.04f, 0.016f, 0.008f, 0.012f, Bright);
            // Кожух ствола с овальными окнами и скошенным компенсатором
            b("Jacket", 0, bore, 0.27f, 0.042f, 0.044f, 0.26f, Blued);
            for (int i = 0; i < 4; i++)
            {
                b("SlotR" + i, 0.0215f, bore, 0.18f + i * 0.055f, 0.002f, 0.014f, 0.034f, Parker);
                b("SlotL" + i, -0.0215f, bore, 0.18f + i * 0.055f, 0.002f, 0.014f, 0.034f, Parker);
                b("SlotT" + i, 0, bore + 0.0225f, 0.18f + i * 0.055f, 0.014f, 0.002f, 0.034f, Parker);
            }
            b("Compensator", 0, bore + 0.004f, 0.415f, 0.042f, 0.05f, 0.04f, Blued, -22);
            cz("Barrel", 0, bore, 0.3f, 0.008f, 0.26f, Parker, 6);
            // Барабанный магазин
            var prev = _t; _t = grp("Mag_Standard", 0, -0.085f, 0.1f);
            cy("Drum", 0, 0, 0, 0.078f, 0.05f, Blued, 0, 0, 90, 12);
            cy("DrumCap", 0.027f, 0, 0, 0.03f, 0.006f, Parker, 0, 0, 90, 8);
            b("DrumNeck", 0, 0.07f, 0, 0.03f, 0.04f, 0.04f, Blued);
            b("DrumWinder", 0.031f, 0, 0, 0.004f, 0.05f, 0.01f, Bright, 35);
            _t = prev;
            b("RearSight", 0, bore + 0.036f, 0.06f, 0.02f, 0.014f, 0.02f, Blued);
            FrontSight(0.38f, bore + 0.022f, 0.022f, true);
            b("Sling", -0.028f, -0.05f, -0.1f, 0.004f, 0.026f, 0.5f, Leather, -3);
            HandRight(f, 0, -0.07f, -0.13f, -16);
            HandLeft(f, 0.005f, -0.175f, 0.1f);
            var s = Sockets(root, 0.44f, bore, bore + 0.044f, new Vector3(0, -0.085f, 0.1f), 0.26f, 0.0f, bore + 0.034f);
            s.shellEjectPoint.localPosition = new Vector3(0.01f, bore + 0.04f, 0.04f);
            prof.style = WeaponAnimProfile.Style.Magazine; prof.casingLength = 0.022f; prof.casingRadius = 0.005f;
            prof.ejectDir = new Vector3(0.3f, 1f, -0.2f); prof.magOut = new Vector3(0, -0.34f, 0.02f);
        }

        // ---------- MG-42 ----------

        static void Mg42(GameObject root, Faction f, WeaponAnimProfile prof)
        {
            const float bore = 0.026f;
            b("Receiver", 0, 0.02f, 0.03f, 0.056f, 0.075f, 0.36f, Parker);
            // Откидная крышка лентоприёмника (ось — у заднего края)
            var prev = _t; _t = grp("TopCover", 0, 0.06f, -0.06f);
            b("Cover", 0, 0.008f, 0.1f, 0.058f, 0.018f, 0.2f, Parker);
            b("CoverLatch", 0, 0.02f, 0.01f, 0.02f, 0.01f, 0.03f, Blued);
            _t = prev;
            b("FeedTray", 0, 0.05f, 0.04f, 0.09f, 0.008f, 0.07f, Blued);
            // Приклад «рыбий хвост»
            b("StockNeck", 0, 0.01f, -0.2f, 0.034f, 0.05f, 0.12f, Bakelite);
            b("StockTail", 0, -0.005f, -0.32f, 0.036f, 0.11f, 0.14f, Bakelite, -4);
            b("Buttplate", 0, -0.008f, -0.395f, 0.038f, 0.12f, 0.012f, Steel);
            b("PistolGrip", 0, -0.085f, -0.07f, 0.034f, 0.1f, 0.044f, Bakelite, 15);
            TriggerGroup(-0.02f, -0.045f);
            // Прямоугольный кожух с окнами охлаждения
            b("Jacket", 0, bore, 0.42f, 0.05f, 0.056f, 0.42f, Parker);
            for (int i = 0; i < 5; i++)
            {
                b("VentL" + i, -0.0255f, bore, 0.27f + i * 0.072f, 0.002f, 0.026f, 0.046f, Blued);
                b("VentR" + i, 0.0255f, bore + 0.006f, 0.27f + i * 0.072f, 0.002f, 0.02f, 0.046f, Blued);
            }
            b("BarrelChangeSlot", 0.0255f, bore - 0.01f, 0.42f, 0.003f, 0.016f, 0.3f, Blued);
            fz("MuzzleBooster", 0, bore, 0.66f, 0.017f, 0.07f, 1.7f, Blued, 10);
            cz("Barrel", 0, bore, 0.45f, 0.011f, 0.4f, Blued, 6);
            b("ChargingHandle", 0.034f, 0.0f, 0.06f, 0.016f, 0.014f, 0.04f, Bakelite);
            // Барабан с лентой (Gurttrommel) слева + видимый отрезок ленты
            _t = grp("Mag_Standard", -0.085f, -0.01f, 0.04f);
            cy("BeltDrum", 0, 0, 0, 0.062f, 0.09f, new Color(0.3f, 0.33f, 0.26f), 90, 0, 0, 10);
            b("DrumLatch", 0.04f, 0.04f, 0, 0.03f, 0.02f, 0.03f, Parker);
            for (int i = 0; i < 4; i++) cy("Round" + i, 0.05f + i * 0.012f, 0.062f, 0, 0.005f, 0.06f, Brass, 90, 0, 0, 5);
            _t = prev;
            b("RearSight", 0, 0.085f, -0.02f, 0.02f, 0.03f, 0.008f, Blued);
            FrontSight(0.6f, bore + 0.03f, 0.03f, false);
            Bipod(0.52f, -0.008f, 0.12f, 0.26f);
            HandRight(f, 0, -0.085f, -0.07f, 15);
            HandLeft(f, -0.01f, -0.06f, 0.22f);
            var s = Sockets(root, 0.7f, bore, bore + 0.064f, new Vector3(-0.085f, -0.01f, 0.04f), 0.36f, 0.02f, 0.09f);
            s.shellEjectPoint.localPosition = new Vector3(0.0f, -0.03f, 0.05f);
            prof.style = WeaponAnimProfile.Style.Belt; prof.magOut = new Vector3(-0.22f, -0.2f, 0); prof.ejectDir = new Vector3(0.15f, -1f, 0);
            prof.casingLength = 0.057f;
        }

        // ---------- ДП-28 ----------

        static void Dp28(GameObject root, Faction f, WeaponAnimProfile prof)
        {
            const float bore = 0.022f;
            b("Receiver", 0, 0.014f, 0.03f, 0.05f, 0.06f, 0.3f, Blued);
            b("StockWrist", 0, -0.03f, -0.17f, 0.04f, 0.055f, 0.14f, Wood, -12);
            b("StockButt", 0, -0.055f, -0.33f, 0.042f, 0.12f, 0.2f, Wood, -6);
            b("Buttplate", 0, -0.064f, -0.435f, 0.044f, 0.125f, 0.012f, Steel);
            b("GripSafety", 0, -0.06f, -0.09f, 0.012f, 0.03f, 0.05f, Blued);
            TriggerGroup(-0.03f, -0.045f);
            // Кожух ствола с продольными окнами и конический пламегаситель
            cz("Jacket", 0, bore, 0.38f, 0.026f, 0.36f, Blued, 10);
            for (int i = 0; i < 4; i++) { b("SlotT" + i, 0, bore + 0.0255f, 0.25f + i * 0.08f, 0.012f, 0.003f, 0.05f, Parker); b("SlotS" + i, 0.0255f, bore, 0.25f + i * 0.08f, 0.003f, 0.012f, 0.05f, Parker); }
            cz("Barrel", 0, bore, 0.6f, 0.01f, 0.12f, Blued, 8);
            fz("FlashHider", 0, bore, 0.7f, 0.012f, 0.09f, 2.2f, Blued, 10);
            cz("GasTube", 0, bore - 0.03f, 0.36f, 0.008f, 0.36f, Blued, 6);
            // Дисковый магазин сверху: корпус, рёбра, центральная гайка
            var prev = _t; _t = grp("Mag_Standard", 0, 0.062f, 0.06f);
            cy("Pan", 0, 0, 0, 0.135f, 0.022f, Blued, 0, 0, 0, 14);
            cy("PanTop", 0, 0.012f, 0, 0.12f, 0.004f, Parker, 0, 0, 0, 14);
            cy("PanHub", 0, 0.017f, 0, 0.03f, 0.01f, Blued, 0, 0, 0, 8);
            for (int i = 0; i < 6; i++) b("Rib" + i, 0, 0.015f, 0, 0.006f, 0.004f, 0.22f, Blued, 0, i * 30);
            _t = prev;
            b("ChargingHandle", 0.03f, -0.005f, 0.06f, 0.018f, 0.01f, 0.014f, Bright);
            b("RearSight", 0, 0.05f, -0.09f, 0.02f, 0.05f, 0.01f, Blued);
            FrontSight(0.62f, bore + 0.03f, 0.06f, true);
            Bipod(0.5f, -0.012f, 0.12f, 0.27f);
            HandRight(f, 0, -0.06f, -0.12f, -14);
            HandLeft(f, 0, -0.06f, 0.2f);
            var s = Sockets(root, 0.75f, bore, bore + 0.092f, new Vector3(0, 0.062f, 0.06f), 0.34f, -0.02f, 0.1f);
            s.shellEjectPoint.localPosition = new Vector3(0, -0.02f, 0.06f);
            prof.style = WeaponAnimProfile.Style.PanTop; prof.magOut = new Vector3(-0.1f, 0.26f, -0.08f); prof.ejectDir = new Vector3(0.1f, -1f, 0);
            prof.casingLength = 0.054f;
        }

        // ---------- Пистолеты ----------

        static void PistolHands(Faction f, float gripZ)
        {
            // Кисть повторяет наклон рукояти (низ рукояти отведён назад)
            HandRight(f, 0, -0.055f, gripZ, 12);
            HandLeft(f, -0.006f, -0.1f, gripZ - 0.012f, 12, 0, -20);
        }

        static void P38(GameObject root, Faction f, WeaponAnimProfile prof)
        {
            const float bore = 0.026f;
            b("Frame", 0, 0.004f, 0.03f, 0.026f, 0.022f, 0.15f, Blued);
            b("Grip", 0, -0.052f, -0.022f, 0.03f, 0.1f, 0.044f, Bakelite, 14);
            for (int i = 0; i < 5; i++) b("GripRib" + i, 0.0155f, -0.03f - i * 0.014f, -0.016f - i * 0.0035f, 0.002f, 0.005f, 0.036f, Blued, 14);
            var prev = _t; _t = grp("Slide", 0, bore, 0.03f);
            b("SlideL", -0.011f, 0, 0, 0.008f, 0.03f, 0.15f, Blued);
            b("SlideR", 0.011f, 0, 0, 0.008f, 0.03f, 0.15f, Blued);
            b("SlideRear", 0, 0, -0.055f, 0.03f, 0.03f, 0.04f, Blued);
            b("RearSightP", 0, 0.018f, -0.06f, 0.014f, 0.006f, 0.008f, Blued);
            for (int i = 0; i < 5; i++) b("Serration" + i, 0.0155f, 0, -0.07f + i * 0.007f, 0.002f, 0.026f, 0.003f, Parker);
            _t = prev;
            cz("Barrel", 0, bore, 0.085f, 0.0075f, 0.13f, Blued, 8);   // открытый ствол
            b("IronSights", 0, bore + 0.016f, 0.14f, 0.004f, 0.012f, 0.008f, Blued);
            b("Hammer", 0, bore + 0.004f, -0.05f, 0.008f, 0.02f, 0.012f, Blued, -25);
            TriggerGroup(0.035f, -0.022f);
            grpBox("Mag_Standard", 0, -0.105f, -0.034f, 0.024f, 0.014f, 0.04f, Steel);
            b("Lanyard", -0.016f, -0.09f, -0.045f, 0.004f, 0.012f, 0.012f, Steel);
            PistolHands(f, -0.022f);
            var s = Sockets(root, 0.15f, bore, bore + 0.024f, new Vector3(0, -0.105f, -0.034f), 0.08f, 0.0f, bore + 0.022f);
            s.shellEjectPoint.localPosition = new Vector3(-0.01f, bore + 0.02f, 0.0f);
            prof.style = WeaponAnimProfile.Style.Magazine; prof.magOut = new Vector3(0.04f, -0.2f, -0.03f); prof.ejectDir = new Vector3(-0.6f, 1f, 0);
            prof.casingLength = 0.019f; prof.casingRadius = 0.005f; prof.boltTravel = 0.035f;
        }

        static void Tt33(GameObject root, Faction f, WeaponAnimProfile prof)
        {
            const float bore = 0.026f;
            b("Frame", 0, 0.002f, 0.03f, 0.026f, 0.022f, 0.16f, Blued);
            b("Grip", 0, -0.054f, -0.02f, 0.03f, 0.1f, 0.042f, Blued, 8);
            b("GripPanel", 0.0155f, -0.054f, -0.02f, 0.003f, 0.075f, 0.034f, Bakelite, 8);
            cy("GripStar", 0.0175f, -0.05f, -0.02f, 0.01f, 0.002f, Blued, 0, 0, 90, 5);
            var prev = _t; _t = grp("Slide", 0, bore, 0.035f);
            b("SlideBody", 0, 0, 0, 0.03f, 0.03f, 0.19f, Blued);
            b("RearSightP", 0, 0.018f, -0.08f, 0.014f, 0.006f, 0.008f, Blued);
            for (int i = 0; i < 6; i++) b("Serration" + i, 0.0155f, 0, -0.085f + i * 0.007f, 0.002f, 0.024f, 0.003f, Parker);
            b("EjectionPort", 0.0155f, 0.006f, 0.0f, 0.002f, 0.012f, 0.03f, Parker);
            _t = prev;
            cz("BarrelBushing", 0, bore, 0.133f, 0.009f, 0.012f, Parker, 8);
            b("IronSights", 0, bore + 0.018f, 0.12f, 0.004f, 0.01f, 0.008f, Blued);
            b("Hammer", 0, bore + 0.002f, -0.065f, 0.008f, 0.018f, 0.012f, Blued, -30);
            TriggerGroup(0.035f, -0.022f);
            grpBox("Mag_Standard", 0, -0.106f, -0.027f, 0.024f, 0.014f, 0.04f, Steel);
            b("LanyardLoop", 0, -0.11f, -0.05f, 0.012f, 0.012f, 0.004f, Steel);
            PistolHands(f, -0.02f);
            var s = Sockets(root, 0.14f, bore, bore + 0.024f, new Vector3(0, -0.106f, -0.027f), 0.08f, 0.0f, bore + 0.022f);
            s.shellEjectPoint.localPosition = new Vector3(0.02f, bore + 0.012f, 0.03f);
            prof.style = WeaponAnimProfile.Style.Magazine; prof.magOut = new Vector3(0.04f, -0.2f, -0.02f);
            prof.casingLength = 0.025f; prof.casingRadius = 0.005f; prof.boltTravel = 0.04f;
        }

        static void Luger(GameObject root, Faction f, WeaponAnimProfile prof)
        {
            const float bore = 0.026f;
            b("Frame", 0, 0.0f, 0.0f, 0.026f, 0.026f, 0.13f, Blued);
            b("Grip", 0, -0.056f, -0.035f, 0.03f, 0.105f, 0.042f, WoodDark, 34);   // характерный сильный наклон
            for (int i = 0; i < 4; i++) b("Checker" + i, 0.0155f, -0.035f - i * 0.016f, -0.022f - i * 0.011f, 0.002f, 0.006f, 0.03f, Wood, 34);
            b("ReceiverFork", 0, bore, -0.005f, 0.028f, 0.024f, 0.1f, Blued);
            fz("Barrel", 0, bore, 0.1f, 0.009f, 0.11f, 0.75f, Blued, 8);   // конический ствол
            b("IronSights", 0, bore + 0.014f, 0.148f, 0.004f, 0.012f, 0.008f, Blued);
            // Коленчато-рычажный затвор: при выстреле «ломается» вверх
            var prev = _t; _t = grp("Slide", 0, bore + 0.014f, -0.03f);
            b("ToggleFront", 0, 0, 0.02f, 0.02f, 0.012f, 0.045f, Blued);
            b("ToggleRear", 0, 0, -0.02f, 0.02f, 0.012f, 0.04f, Blued);
            cy("ToggleKnobL", -0.016f, 0.002f, 0, 0.011f, 0.008f, Parker, 0, 0, 90, 8);
            cy("ToggleKnobR", 0.016f, 0.002f, 0, 0.011f, 0.008f, Parker, 0, 0, 90, 8);
            b("RearSightP", 0, 0.01f, -0.04f, 0.012f, 0.006f, 0.006f, Blued);
            _t = prev;
            TriggerGroup(0.02f, -0.022f);
            grpBox("Mag_Standard", 0, -0.11f, -0.068f, 0.024f, 0.014f, 0.036f, Wood);
            PistolHands(f, -0.032f);
            var s = Sockets(root, 0.158f, bore, bore + 0.024f, new Vector3(0, -0.11f, -0.068f), 0.07f, -0.01f, bore + 0.03f);
            s.shellEjectPoint.localPosition = new Vector3(0, bore + 0.03f, -0.02f);
            prof.style = WeaponAnimProfile.Style.Magazine; prof.magOut = new Vector3(0.03f, -0.18f, -0.1f); prof.ejectDir = new Vector3(0.1f, 1f, -0.3f);
            prof.casingLength = 0.019f; prof.casingRadius = 0.005f; prof.boltTravel = 0.02f;
        }

        static void Nagant(GameObject root, Faction f, WeaponAnimProfile prof)
        {
            const float bore = 0.03f;
            b("Frame", 0, 0.012f, 0.0f, 0.024f, 0.05f, 0.1f, Blued);
            b("TopStrap", 0, bore + 0.022f, 0.02f, 0.014f, 0.008f, 0.09f, Blued);
            b("Grip", 0, -0.05f, -0.045f, 0.03f, 0.1f, 0.04f, WoodDark, 22);
            b("GripCap", 0, -0.1f, -0.066f, 0.03f, 0.012f, 0.042f, Blued, 22);
            cy("LanyardRing", 0, -0.112f, -0.07f, 0.008f, 0.003f, Steel, 90, 0, 0, 6);
            // Барабан на 7 камор с долами
            var prev = _t; _t = grp("Cylinder", 0, bore, 0.025f);
            cy("Drum", 0, 0, 0, 0.021f, 0.04f, Blued, 90, 0, 0, 7);
            for (int i = 0; i < 7; i++)
            {
                float a = i * Mathf.PI * 2 / 7;
                cy("Flute" + i, Mathf.Cos(a) * 0.02f, Mathf.Sin(a) * 0.02f, 0.004f, 0.004f, 0.026f, Parker, 90, 0, 0, 4);
                cy("Rim" + i, Mathf.Cos(a + 0.45f) * 0.014f, Mathf.Sin(a + 0.45f) * 0.014f, -0.021f, 0.0045f, 0.003f, Brass, 90, 0, 0, 5);
            }
            _t = prev;
            cz("Barrel", 0, bore + 0.006f, 0.105f, 0.008f, 0.11f, Blued, 6);   // гранёный
            cz("EjectorRod", 0.006f, bore - 0.01f, 0.1f, 0.003f, 0.09f, Bright, 5);
            b("IronSights", 0, bore + 0.02f, 0.152f, 0.004f, 0.012f, 0.008f, Blued);
            b("RearNotch", 0, bore + 0.028f, -0.03f, 0.012f, 0.006f, 0.006f, Blued);
            b("Hammer", 0, bore + 0.012f, -0.05f, 0.008f, 0.026f, 0.014f, Blued, -30);
            b("LoadingGate", 0.014f, bore, -0.012f, 0.004f, 0.03f, 0.016f, Blued);
            TriggerGroup(0.015f, -0.022f);
            b("Mag_Standard", 0, bore, 0.025f, 0.001f, 0.001f, 0.001f, Blued);   // фиктивная часть: у револьвера магазина нет
            PistolHands(f, -0.045f);
            var s = Sockets(root, 0.165f, bore + 0.006f, bore + 0.036f, new Vector3(0, -0.08f, 0), 0.08f, 0.02f, bore + 0.036f);
            prof.style = WeaponAnimProfile.Style.Revolver; prof.ejectsCasings = false; prof.clipHandPos = new Vector3(0.04f, 0.03f, -0.02f);
        }

        // ---------- Нож, шпатель, граната ----------

        static void Knife(GameObject root, Faction f, WeaponAnimProfile prof)
        {
            // Обратный хват: рукоять вертикально в кулаке, навершие над большим пальцем, клинок вниз из-под мизинца,
            // лезвие смотрит вперёд. KnifeBody вращается отдельно от руки (флип ножа на F).
            var prevK = _t; _t = grp("KnifeBody", 0.004f, -0.004f, 0.014f, 90, 0, 0);
            b("Handle", 0, 0, 0, 0.026f, 0.034f, 0.115f, WoodDark);
            for (int i = 0; i < 3; i++) b("Rivet" + i, 0.0135f, 0, -0.035f + i * 0.035f, 0.002f, 0.008f, 0.008f, Brass);
            b("Pommel", 0, 0, -0.064f, 0.028f, 0.036f, 0.016f, Steel);
            b("Guard", 0, 0, 0.062f, 0.014f, 0.075f, 0.012f, Steel);
            b("Blade", 0, 0.004f, 0.17f, 0.005f, 0.028f, 0.2f, Bright);
            b("Fuller", 0.003f, 0.008f, 0.16f, 0.002f, 0.006f, 0.15f, Steel);
            Part(_t, "Tip", MeshUtil.Prism(), new Vector3(0, 0.004f, 0.285f), new Vector3(0.005f, 0.03f, 0.028f), Bright, new Vector3(90, 0, 0), false, 0.7f, 0.85f);
            _t = prevK;
            HandRight(f, 0.0f, -0.005f, -0.005f, 0, 0, 0);
            var s = root.AddComponent<WeaponSockets>();
            s.muzzleFlashPoint = Empty(root.transform, "TipPoint", new Vector3(0.004f, -0.3f, 0.014f));
            prof.ejectsCasings = false;
        }

        /// <summary>Обойма на 5 патронов (в левой руке при заряжании Kar98k / Мосина).</summary>
        public static GameObject StripperClip(Transform parent)
        {
            var root = new GameObject("StripperClip");
            root.transform.SetParent(parent, false);
            var prev = _t; _t = root.transform;
            b("ClipStrip", 0, 0, -0.03f, 0.012f, 0.062f, 0.004f, Steel);
            for (int i = 0; i < 5; i++)
            {
                cz("Case" + i, 0, -0.024f + i * 0.012f, 0.0f, 0.0058f, 0.056f, Brass, 6);
                fz("Bullet" + i, 0, -0.024f + i * 0.012f, 0.04f, 0.0045f, 0.026f, 0.25f, new Color(0.6f, 0.38f, 0.22f), 6);
            }
            _t = prev;
            return root;
        }

        public static GameObject Attachment(AttachmentType type)
        {
            var root = new GameObject("ATT_" + type);
            root.transform.SetParent(Root, false);
            _t = root.transform;
            switch (type)
            {
                case AttachmentType.Suppressor:
                    cz("Can", 0, 0, 0.11f, 0.02f, 0.2f, Parker, 10);
                    cz("RingRear", 0, 0, 0.012f, 0.022f, 0.016f, Blued, 10);
                    cz("RingFront", 0, 0, 0.205f, 0.021f, 0.012f, Blued, 10);
                    for (int i = 0; i < 3; i++) cz("Groove" + i, 0, 0, 0.06f + i * 0.05f, 0.0208f, 0.004f, Blued, 10);
                    Empty(_t, "Tip", new Vector3(0, 0, 0.215f));
                    break;
                case AttachmentType.MuzzleBrake:
                    b("Brake", 0, 0, 0.04f, 0.036f, 0.03f, 0.075f, Blued);
                    for (int i = 0; i < 3; i++) b("Port" + i, 0, 0, 0.018f + i * 0.022f, 0.04f, 0.012f, 0.008f, Parker);
                    Empty(_t, "Tip", new Vector3(0, 0, 0.08f));
                    break;
                case AttachmentType.ExtendedMag:
                    b("MagExt", 0, -0.05f, 0, 0.034f, 0.26f, 0.055f, Blued);
                    b("BasePlate", 0, -0.185f, 0, 0.04f, 0.014f, 0.062f, Steel);
                    b("Witness", 0.0175f, -0.05f, 0, 0.002f, 0.16f, 0.008f, Parker);
                    break;
                case AttachmentType.Scope:
                    cz("Tube", 0, 0.036f, 0, 0.014f, 0.2f, Blued, 10);
                    fz("Objective", 0, 0.036f, 0.12f, 0.016f, 0.05f, 1.35f, Blued, 10);
                    fz("Ocular", 0, 0.036f, -0.12f, 0.02f, 0.05f, 0.75f, Blued, 10);
                    cz("LensFront", 0, 0.036f, 0.146f, 0.019f, 0.002f, new Color(0.2f, 0.35f, 0.5f), 10);
                    cy("TurretTop", 0, 0.056f, 0, 0.01f, 0.014f, Parker, 0, 0, 0, 8);
                    cy("TurretSide", 0.02f, 0.036f, 0, 0.01f, 0.014f, Parker, 0, 0, 90, 8);
                    b("RingF", 0, 0.03f, 0.06f, 0.034f, 0.04f, 0.014f, Parker);
                    b("RingR", 0, 0.03f, -0.06f, 0.034f, 0.04f, 0.014f, Parker);
                    b("Mount", 0, 0.008f, 0, 0.022f, 0.016f, 0.15f, Blued);
                    break;
                case AttachmentType.TacticalGrip:
                    b("Clamp", 0, -0.004f, 0, 0.04f, 0.016f, 0.05f, Parker);
                    b("Grip", 0, -0.06f, 0, 0.03f, 0.1f, 0.034f, Wood, 6);
                    b("GripCap", 0, -0.112f, -0.004f, 0.032f, 0.01f, 0.036f, Steel, 6);
                    break;
                case AttachmentType.FlashHider:
                    // Конический пламегаситель с продольными прорезями
                    fz("Cone", 0, 0, 0.045f, 0.015f, 0.075f, 1.3f, Blued, 12);
                    cz("Collar", 0, 0, 0.008f, 0.017f, 0.018f, Parker, 12);
                    for (int i = 0; i < 4; i++)
                    {
                        float a = i * Mathf.PI * 0.5f + 0.4f;
                        b("Slot" + i, Mathf.Cos(a) * 0.017f, Mathf.Sin(a) * 0.017f, 0.055f, 0.004f, 0.004f, 0.05f, new Color(0.03f, 0.03f, 0.03f), 0, 0, a * Mathf.Rad2Deg);
                    }
                    Empty(_t, "Tip", new Vector3(0, 0, 0.085f));
                    break;
                case AttachmentType.Bayonet:
                    // Штык под стволом: кольцо, рукоять, клинок с долом — вспышка остаётся у дула
                    cz("Ring", 0, 0, 0.0f, 0.016f, 0.02f, Blued, 10);
                    b("Socket", 0, -0.026f, -0.03f, 0.016f, 0.026f, 0.07f, Blued);
                    b("Grip", 0, -0.028f, -0.11f, 0.018f, 0.03f, 0.09f, Wood);
                    b("Guard", 0, -0.026f, 0.008f, 0.012f, 0.05f, 0.008f, Steel);
                    b("Blade", 0, -0.028f, 0.13f, 0.004f, 0.026f, 0.24f, Bright);
                    b("Fuller", 0.0022f, -0.026f, 0.12f, 0.0008f, 0.008f, 0.18f, Steel);
                    Part(_t, "Point", MeshUtil.Prism(), new Vector3(0, -0.028f, 0.265f), new Vector3(0.004f, 0.026f, 0.04f), Bright, new Vector3(0, 0, 0), false, 0.6f, 0.85f);
                    Empty(_t, "Tip", new Vector3(0, 0, 0.0f));
                    break;
                case AttachmentType.DrumMag:
                    // Барабанный магазин: горловина и барабан с заводным ключом
                    b("Neck", 0, -0.04f, 0, 0.032f, 0.08f, 0.05f, Blued);
                    cy("Drum", 0, -0.13f, 0.01f, 0.085f, 0.07f, Blued, 0, 0, 90, 18);
                    cy("DrumCap", 0.037f, -0.13f, 0.01f, 0.07f, 0.006f, Parker, 0, 0, 90, 18);
                    cy("Key", 0.045f, -0.13f, 0.01f, 0.012f, 0.014f, Steel, 0, 0, 90, 8);
                    b("KeyWing", 0.053f, -0.13f, 0.01f, 0.004f, 0.04f, 0.008f, Steel);
                    break;
                case AttachmentType.ScopeShort:
                    // Короткая «разведывательная» оптика 1,5× на длинном кронштейне (большой вынос зрачка)
                    cz("Tube", 0, 0.04f, 0, 0.013f, 0.14f, Blued, 12);
                    fz("Objective", 0, 0.04f, 0.085f, 0.015f, 0.035f, 1.25f, Blued, 12);
                    fz("Ocular", 0, 0.04f, -0.085f, 0.017f, 0.035f, 0.85f, Blued, 12);
                    cz("LensFront", 0, 0.04f, 0.103f, 0.018f, 0.002f, new Color(0.25f, 0.4f, 0.5f), 12);
                    b("RingF", 0, 0.032f, 0.04f, 0.032f, 0.034f, 0.012f, Parker);
                    b("RingR", 0, 0.032f, -0.04f, 0.032f, 0.034f, 0.012f, Parker);
                    b("Rail", 0, 0.012f, 0, 0.02f, 0.014f, 0.12f, Blued);
                    break;
                case AttachmentType.Bipod:
                    // Сошки: хомут и две сложенные вперёд ноги с упорами
                    b("Clamp", 0, -0.004f, 0, 0.044f, 0.02f, 0.04f, Parker);
                    for (int s = -1; s <= 1; s += 2)
                    {
                        cy("Leg" + s, s * 0.02f, -0.012f, 0.1f, 0.0055f, 0.2f, Blued, 90, 0, 0, 8);
                        b("Foot" + s, s * 0.02f, -0.02f, 0.2f, 0.012f, 0.01f, 0.02f, Steel);
                        cy("Hinge" + s, s * 0.02f, -0.01f, 0.0f, 0.007f, 0.012f, Steel, 0, 0, 90, 8);
                    }
                    break;
                case AttachmentType.LaserSight:
                    b("Body", 0, 0, 0, 0.024f, 0.024f, 0.075f, Parker);
                    cz("Lens", 0, 0, 0.039f, 0.007f, 0.004f, new Color(1f, 0.12f, 0.1f), 8);
                    b("Clamp", -0.014f, 0, 0, 0.008f, 0.03f, 0.03f, Blued);
                    b("Switch", 0, 0.014f, -0.02f, 0.01f, 0.006f, 0.014f, Bakelite);
                    break;
            }
            _t = null;
            return root;
        }

        public static GameObject Trowel()
        {
            var root = new GameObject("VM_Trowel");
            root.transform.SetParent(Root, false);
            _t = root.transform;
            b("Handle", 0, 0, 0, 0.03f, 0.03f, 0.12f, Wood);
            b("Ferrule", 0, 0, 0.062f, 0.024f, 0.024f, 0.02f, Brass);
            cy("Shank", 0, 0.012f, 0.085f, 0.005f, 0.05f, Steel, 60, 0, 0, 6);
            Part(_t, "Blade", MeshUtil.Prism(), new Vector3(0, 0.022f, 0.18f), new Vector3(0.11f, 0.004f, 0.17f), Bright, new Vector3(0, 0, 0), false, 0.6f, 0.85f);
            b("Mortar", 0, 0.03f, 0.17f, 0.07f, 0.012f, 0.09f, new Color(0.6f, 0.6f, 0.57f));
            HandRight(Faction.USSR, 0, -0.005f, -0.005f);
            // Кирпич в левой руке
            var prev = _t; _t = grp("BrickHand", -0.26f, -0.06f, 0.1f, 0, 25, 0);
            b("Brick", 0, 0.03f, 0, 0.2f, 0.09f, 0.095f, new Color(0.62f, 0.27f, 0.2f));
            HandLeft(Faction.USSR, 0, -0.02f, 0);
            _t = prev;
            _t = null;
            return root;
        }
    }
}
