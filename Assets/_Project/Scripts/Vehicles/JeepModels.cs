using UnityEngine;

namespace Frontline
{
    /// <summary>
    /// Лёгкая машина с пулемётом на турели (как джип в Ravenfield):
    /// Германия — Kübelwagen с MG-34 на штыре, СССР — ГАЗ-64 с ДП на штыре.
    /// Открытый кузов, четыре колеса, лобовое стекло, сиденья, запаска, водитель в кабине.
    /// Ригу те же узлы, что у танка: hull / turret (поворот турели) / gun (наклон пулемёта) / muzzle / seat.
    /// </summary>
    public static class JeepModels
    {
        static Transform _p;

        static GameObject B(string n, Vector3 pos, Vector3 size, Color c, Vector3 euler = default, float bevel = 0.08f, float smooth = 0.25f, float metal = 0.2f)
            => ProceduralModels.Part(_p, n, MeshUtil.Bevel(bevel), pos, size, c, euler, false, smooth, metal);
        static GameObject C(string n, Vector3 pos, float r, float len, Color c, Vector3 euler, int seg = 14, float smooth = 0.3f, float metal = 0.3f)
            => ProceduralModels.Part(_p, n, MeshUtil.Cylinder(seg), pos, new Vector3(r * 2, len, r * 2), c, euler, false, smooth, metal);
        static Transform G(string n, Vector3 pos)
        {
            var t = new GameObject(n).transform; t.SetParent(_p, false); t.localPosition = pos; return t;
        }

        public static TankModels.Rig Build(Faction f, MapTheme theme, Transform parent)
        {
            bool de = f == Faction.Germany, winter = theme == MapTheme.Winter;
            Color paint = de ? new Color(0.5f, 0.5f, 0.44f) : new Color(0.3f, 0.36f, 0.22f);
            if (winter) paint = new Color(0.84f, 0.85f, 0.84f);
            Color dark = Color.Lerp(paint, new Color(0.1f, 0.1f, 0.1f), 0.5f);
            Color tyre = new(0.09f, 0.09f, 0.09f), steel = new(0.3f, 0.3f, 0.31f), blued = ProceduralModels.Blued,
                  seat = de ? new Color(0.25f, 0.2f, 0.14f) : new Color(0.22f, 0.24f, 0.18f), glass = new Color(0.5f, 0.6f, 0.65f);

            var rig = new TankModels.Rig();
            var root = new GameObject(de ? "Jeep_Kuebelwagen" : "Jeep_GAZ64");
            root.transform.SetParent(parent, false);
            rig.root = root; _p = root.transform;
            var hull = G("Hull", Vector3.zero); rig.hull = hull; _p = hull;

            float len = de ? 3.75f : 3.6f, w = 1.55f;
            // ---- Колёса и подвеска ----
            float wx = w * 0.5f, wz = len * 0.5f - 0.62f;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    var p = new Vector3(sx * wx, 0.36f, sz * wz);
                    C("Tyre", p, 0.36f, 0.24f, tyre, new Vector3(0, 0, 90), 16, 0.1f, 0f);
                    C("Hub", p + new Vector3(sx * 0.125f, 0, 0), 0.17f, 0.03f, steel, new Vector3(0, 0, 90), 10);
                    B("Fender", new Vector3(sx * wx, 0.78f, sz * wz), new Vector3(0.36f, 0.06f, 0.95f), paint, new Vector3(0, 0, sx * -8f));
                }
            B("Chassis", new Vector3(0, 0.45f, 0), new Vector3(w * 0.75f, 0.14f, len - 0.3f), dark, default, 0.03f);

            // ---- Кузов ----
            if (de)
            {
                // Kübelwagen: рубленый корпус с рёбрами, покатый нос, двери-«корыта»
                B("Body", new Vector3(0, 0.82f, -0.15f), new Vector3(w, 0.62f, len - 1.2f), paint, default, 0.05f);
                B("Nose", new Vector3(0, 0.86f, len * 0.5f - 0.45f), new Vector3(w * 0.92f, 0.42f, 0.9f), paint, new Vector3(14, 0, 0), 0.05f);
                for (int i = 0; i < 4; i++) B("Rib" + i, new Vector3(0, 1.08f, len * 0.5f - 0.3f - i * 0.18f), new Vector3(w * 0.8f, 0.03f, 0.04f), dark, default, 0.01f);
                B("Rear", new Vector3(0, 0.86f, -len * 0.5f + 0.28f), new Vector3(w * 0.96f, 0.6f, 0.6f), paint, new Vector3(-10, 0, 0), 0.05f);
            }
            else
            {
                // ГАЗ-64: открытый кузов, капот с решёткой, широкие крылья
                B("Body", new Vector3(0, 0.82f, -0.35f), new Vector3(w, 0.55f, len - 1.5f), paint, default, 0.05f);
                B("Hood", new Vector3(0, 0.96f, len * 0.5f - 0.62f), new Vector3(w * 0.72f, 0.42f, 1.15f), paint, default, 0.05f);
                B("Grille", new Vector3(0, 0.9f, len * 0.5f - 0.04f), new Vector3(w * 0.66f, 0.46f, 0.06f), dark, default, 0.01f);
                for (int i = 0; i < 7; i++) B("Slat" + i, new Vector3(-0.27f + i * 0.09f, 0.9f, len * 0.5f - 0.0f), new Vector3(0.025f, 0.4f, 0.03f), steel, default, 0.005f);
            }
            for (int s = -1; s <= 1; s += 2)
                C("Headlight", new Vector3(s * 0.45f, 1.08f, len * 0.5f - 0.15f), 0.08f, 0.1f, glass, new Vector3(90, 0, 0), 12, 0.8f, 0.4f);
            // Лобовое стекло в рамке (откинуто чуть назад)
            var ws = G("Windshield", new Vector3(0, 1.15f, 0.42f)); ws.localEulerAngles = new Vector3(-12, 0, 0);
            var prev = _p; _p = ws;
            B("FrameTop", new Vector3(0, 0.42f, 0), new Vector3(w * 0.95f, 0.05f, 0.05f), dark, default, 0.01f);
            B("FrameL", new Vector3(-w * 0.47f, 0.2f, 0), new Vector3(0.05f, 0.44f, 0.05f), dark, default, 0.01f);
            B("FrameR", new Vector3(w * 0.47f, 0.2f, 0), new Vector3(0.05f, 0.44f, 0.05f), dark, default, 0.01f);
            ProceduralModels.Part(_p, "Glass", ProceduralModels.CubeMesh, new Vector3(0, 0.2f, 0), new Vector3(w * 0.9f, 0.38f, 0.01f), glass, default, false, 0.9f, 0.3f);
            _p = prev;
            // Салон: сиденья, руль, запаска
            B("SeatFrontL", new Vector3(-0.38f, 0.98f, 0.0f), new Vector3(0.5f, 0.14f, 0.5f), seat, default, 0.08f, 0.15f, 0f);
            B("SeatBackL", new Vector3(-0.38f, 1.25f, -0.24f), new Vector3(0.5f, 0.45f, 0.1f), seat, new Vector3(-10, 0, 0), 0.08f, 0.15f, 0f);
            B("SeatFrontR", new Vector3(0.38f, 0.98f, 0.0f), new Vector3(0.5f, 0.14f, 0.5f), seat, default, 0.08f, 0.15f, 0f);
            B("SeatBackR", new Vector3(0.38f, 1.25f, -0.24f), new Vector3(0.5f, 0.45f, 0.1f), seat, new Vector3(-10, 0, 0), 0.08f, 0.15f, 0f);
            B("RearBench", new Vector3(0, 0.98f, -0.95f), new Vector3(w * 0.85f, 0.14f, 0.5f), seat, default, 0.08f, 0.15f, 0f);
            C("SteeringCol", new Vector3(-0.38f, 1.25f, 0.28f), 0.025f, 0.45f, steel, new Vector3(-55, 0, 0), 8);
            C("Wheel", new Vector3(-0.38f, 1.42f, 0.18f), 0.17f, 0.025f, dark, new Vector3(-55, 0, 0), 16);
            C("Spare", new Vector3(0, 1.0f, -len * 0.5f - 0.05f), 0.33f, 0.2f, tyre, new Vector3(90, 0, 0), 16, 0.1f, 0f);

            // Водитель (виден, пока машина занята)
            var crew = G("Crew", new Vector3(-0.38f, 0.95f, 0.0f)); prev = _p; _p = crew;
            BotBody.Palette(f, theme, out var uni, out _, out var helm, out _, out var skin);
            ProceduralModels.Part(_p, "Torso", BotBody.TorsoMesh(SoldierClass.Assault), new Vector3(0, 0.08f, -0.05f), Vector3.one, uni, new Vector3(-8, 0, 0));
            ProceduralModels.Part(_p, "Head", BotBody.HeadMesh(), new Vector3(0, 0.82f, 0.02f), Vector3.one, skin);
            ProceduralModels.Part(_p, "Helmet", BotBody.HelmetMesh(f), new Vector3(0, 0.88f, 0.02f), Vector3.one, helm);
            for (int s = -1; s <= 1; s += 2)
                ProceduralModels.Part(_p, "Arm", BotBody.LimbMesh("upper", 0.115f, BotBody.Upper, 0), new Vector3(s * 0.2f, 0.6f, 0), Vector3.one, uni, new Vector3(-60, 0, 0));
            _p = prev;
            // Пассажир справа (с винтовкой), места для посадки
            var pax = G("CrewPassenger", new Vector3(0.38f, 0.95f, 0.0f)); prev = _p; _p = pax;
            ProceduralModels.Part(_p, "Torso", BotBody.TorsoMesh(SoldierClass.Assault), new Vector3(0, 0.08f, -0.05f), Vector3.one, uni, new Vector3(-4, 0, 0));
            ProceduralModels.Part(_p, "Head", BotBody.HeadMesh(), new Vector3(0, 0.82f, 0.02f), Vector3.one, skin);
            ProceduralModels.Part(_p, "Helmet", BotBody.HelmetMesh(f), new Vector3(0, 0.88f, 0.02f), Vector3.one, helm);
            for (int s = -1; s <= 1; s += 2)
                ProceduralModels.Part(_p, "Arm", BotBody.LimbMesh("upper", 0.115f, BotBody.Upper, 0), new Vector3(s * 0.2f, 0.6f, 0.02f), Vector3.one, uni, new Vector3(-75, s * 12f, 0));
            B("Rifle", new Vector3(0.05f, 0.55f, 0.35f), new Vector3(0.04f, 0.06f, 0.95f), ProceduralModels.Wood, new Vector3(-20, -6, 0), 0.01f, 0.35f, 0f);
            _p = prev;
            rig.seat = G("Seat", new Vector3(-0.38f, 0.6f, 0.0f));
            G("SeatPassenger", new Vector3(0.38f, 0.6f, 0.0f));
            G("SeatGunner", new Vector3(0f, 0.75f, -1.2f));
            rig.exhaust = G("Exhaust", new Vector3(0.5f, 0.45f, -len * 0.5f));

            // ---- Турель с пулемётом за передними сиденьями ----
            _p = hull;
            C("Post", new Vector3(0, 1.35f, -0.62f), 0.04f, 0.75f, steel, default, 10);
            var turret = G("Turret", new Vector3(0, 1.72f, -0.62f)); rig.turret = turret; _p = turret;
            B("Cradle", new Vector3(0, 0.03f, 0), new Vector3(0.12f, 0.06f, 0.2f), blued, default, 0.02f, 0.4f, 0.8f);
            var gun = G("Gun", new Vector3(0, 0.1f, 0)); rig.gun = gun; _p = gun;
            if (de)
            {   // MG-34: дырчатый кожух ствола, конический приклад, барабан-«улитка»
                B("Receiver", new Vector3(0, 0, 0.05f), new Vector3(0.06f, 0.08f, 0.38f), blued, default, 0.015f, 0.45f, 0.85f);
                C("Jacket", new Vector3(0, 0.005f, 0.48f), 0.026f, 0.5f, blued, new Vector3(90, 0, 0), 10, 0.45f, 0.85f);
                for (int i = 0; i < 6; i++) C("Hole" + i, new Vector3(0.024f, 0.005f, 0.3f + i * 0.07f), 0.008f, 0.01f, new Color(0.03f, 0.03f, 0.03f), new Vector3(0, 0, 90), 6);
                B("Stock", new Vector3(0, -0.03f, -0.32f), new Vector3(0.045f, 0.11f, 0.3f), ProceduralModels.Bakelite, new Vector3(-6, 0, 0), 0.02f, 0.4f, 0f);
                C("Drum", new Vector3(-0.07f, -0.02f, 0.08f), 0.06f, 0.07f, blued, new Vector3(0, 0, 90), 12, 0.45f, 0.85f);
            }
            else
            {   // ДП: тарельчатый диск сверху, кожух с рёбрами, деревянный приклад
                B("Receiver", new Vector3(0, 0, 0.05f), new Vector3(0.06f, 0.07f, 0.34f), blued, default, 0.015f, 0.45f, 0.85f);
                C("Shroud", new Vector3(0, 0.005f, 0.45f), 0.028f, 0.45f, blued, new Vector3(90, 0, 0), 10, 0.45f, 0.85f);
                C("Pan", new Vector3(0, 0.07f, 0.06f), 0.13f, 0.025f, blued, default, 18, 0.45f, 0.85f);
                B("Stock", new Vector3(0, -0.04f, -0.3f), new Vector3(0.045f, 0.12f, 0.28f), ProceduralModels.Wood, new Vector3(-8, 0, 0), 0.02f, 0.35f, 0f);
            }
            B("Grip", new Vector3(0, -0.07f, -0.06f), new Vector3(0.03f, 0.09f, 0.04f), ProceduralModels.Bakelite, new Vector3(-15, 0, 0), 0.01f, 0.4f, 0f);
            B("Shield", new Vector3(0, 0.02f, 0.2f), new Vector3(0.42f, 0.32f, 0.015f), paint, default, 0.01f, 0.3f, 0.4f);   // щиток стрелка
            rig.muzzle = G("Muzzle", new Vector3(0, 0.005f, 0.76f));
            // Пулемётчик стоит за турелью и поворачивается вместе с ней
            _p = turret;
            var gnr = G("CrewGunner", new Vector3(0, -0.62f, -0.62f)); _p = gnr;
            ProceduralModels.Part(_p, "Torso", BotBody.TorsoMesh(SoldierClass.Support), new Vector3(0, 0.1f, 0), Vector3.one, uni, new Vector3(6, 0, 0));
            ProceduralModels.Part(_p, "Head", BotBody.HeadMesh(), new Vector3(0, 0.84f, 0.06f), Vector3.one, skin);
            ProceduralModels.Part(_p, "Helmet", BotBody.HelmetMesh(f), new Vector3(0, 0.9f, 0.06f), Vector3.one, helm);
            for (int s = -1; s <= 1; s += 2)
                ProceduralModels.Part(_p, "Arm", BotBody.LimbMesh("upper", 0.115f, BotBody.Upper, 0), new Vector3(s * 0.2f, 0.6f, 0.05f), Vector3.one, uni, new Vector3(-80, -s * 15f, 0));
            _p = gun;
            rig.coaxMuzzle = rig.muzzle;

            rig.hullSize = new Vector3(w + 0.1f, 1.1f, len);
            rig.hullCenterY = 0.75f;
            rig.turretSize = new Vector3(0.45f, 0.35f, 0.9f);
            rig.turretY = 1.72f;
            _p = null;
            // Склейка деталей в меши по узлам (корпус, турель, пулемёт, фигурки экипажа) — меньше draw calls
            var groups = new System.Collections.Generic.HashSet<Transform> { root.transform, hull, turret, gun };
            foreach (var n in new[] { "Crew", "CrewPassenger" }) { var t = hull.Find(n); if (t) groups.Add(t); }
            var cg = turret.Find("CrewGunner"); if (cg) groups.Add(cg);
            foreach (var g in new[] { gun, turret }) MeshMerge.Merge(g, groups);
            foreach (var g in groups) if (g != gun && g != turret && g != hull && g != root.transform) MeshMerge.Merge(g, groups);
            MeshMerge.Merge(hull, groups);
            return rig;
        }
    }
}
