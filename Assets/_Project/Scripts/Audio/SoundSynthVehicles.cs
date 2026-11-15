using UnityEngine;

namespace Frontline
{
    /// <summary>
    /// Звуки техники и противотанкового оружия: дизель Т-34 / бензиновый Maybach Pz.IV (петли), лязг гусениц,
    /// привод башни, выстрел танковой пушки, разрыв фугаса, пуск фаустпатрона и шипение ракеты в полёте,
    /// выстрел ПТРД, рикошет от брони, ломающиеся доски забора, подбор боеприпасов, звон в ушах после взрыва.
    /// </summary>
    public static partial class SoundSynth
    {
        /// <summary>Двигатель на холостых: гармоники вспышек цилиндров + шум впуска. Высота задаётся pitch источника.</summary>
        public static AudioClip Engine(bool diesel) => Loop(diesel ? "engine_diesel" : "engine_petrol", 4f, diesel ? 701 : 702, (float t, System.Random r, ref State s) =>
        {
            float f = diesel ? 38f : 52f;                              // частота вспышек, Гц
            float wob = 1f + 0.015f * Mathf.Sin(t * 2.1f) + 0.01f * Mathf.Sin(t * 5.3f);
            s.phase += f * wob / 22050f; if (s.phase > 1) s.phase -= 1;
            float pulse = Mathf.Pow(Mathf.Max(0, Mathf.Sin(s.phase * Mathf.PI * 2)), diesel ? 6 : 3);
            float rumble = LP(ref s.lp1, pulse + N(r) * 0.15f, diesel ? 0.05f : 0.08f) * 2.5f;
            float harm = Mathf.Sin(s.phase * Mathf.PI * 4) * 0.25f + Mathf.Sin(s.phase * Mathf.PI * 6) * (diesel ? 0.18f : 0.1f);
            float knock = diesel ? LP(ref s.lp2, N(r), 0.4f) * pulse * 0.35f : 0;
            float intake = LP(ref s.lp3, N(r), 0.02f) * 0.6f;
            return rumble + harm * 0.6f + knock + intake;
        }, 0.6f, 22050);

        /// <summary>Лязг траков: удары звеньев о ведущее колесо, скрежет. Темп ∝ pitch источника.</summary>
        public static AudioClip Tracks() => Loop("tracks", 3f, 711, (float t, System.Random r, ref State s) =>
        {
            float rate = 9f;
            float ph = t * rate % 1f;
            float clank = (ph < 0.03f ? N(r) : 0) * 0.9f;
            float ring = Mathf.Sin(2 * Mathf.PI * 820 * ph) * Mathf.Exp(-ph * 60) * 0.35f + Mathf.Sin(2 * Mathf.PI * 1330 * ph) * Mathf.Exp(-ph * 80) * 0.2f;
            float squeal = Mathf.Sin(2 * Mathf.PI * (2400 + 300 * Mathf.Sin(t * 0.7f)) * t) * 0.03f;
            float grind = LP(ref s.lp1, N(r), 0.18f) * 0.35f;
            return clank + ring + squeal + grind;
        }, 0.5f, 22050);

        /// <summary>Электропривод башни (жужжание с редуктором).</summary>
        public static AudioClip TurretTraverse() => Loop("turret", 2f, 721, (float t, System.Random r, ref State s) =>
            Mathf.Sin(2 * Mathf.PI * 180 * t) * 0.3f + Mathf.Sin(2 * Mathf.PI * 360 * t) * 0.2f + Mathf.Sin(2 * Mathf.PI * 1440 * t) * 0.06f
            + LP(ref s.lp1, N(r), 0.1f) * 0.2f, 0.35f, 22050);

        /// <summary>Выстрел 75/85-мм пушки: удар, раскат, эхо и звон гильзы в башне.</summary>
        public static AudioClip TankCannon(bool german) => Make(german ? "cannon_de" : "cannon_su", 4.5f, german ? 731 : 732, (float t, System.Random r, ref State s) =>
        {
            float n = N(r);
            float blast = n * Env(t, 0.03f) * 1.6f;
            float boom = Mathf.Sin(2 * Mathf.PI * (german ? 44 : 38) * t * (1 - t * 0.3f)) * Env(t, 0.35f) * 1.6f;
            float body = LP(ref s.lp1, n, 0.06f) * Env(t, 0.45f) * 2.4f;
            float roll = LP(ref s.lp3, LP(ref s.lp2, N(r), 0.03f), 0.2f) * Env(Mathf.Max(0, t - 0.15f), 1.2f) * 3f * Mathf.Clamp01((t - 0.1f) * 8);
            float casing = t > 0.9f && t < 1.6f ? Mathf.Sin(2 * Mathf.PI * 1250 * t) * Env(t - 0.9f, 0.12f) * 0.12f : 0;
            return blast + boom + body + roll + casing;
        }, 0.98f, 0.3f, 0.92f, 0.55f, 0.45f, 0.3f);

        /// <summary>Разрыв фугасного снаряда — тяжелее гранаты, с долгим грохотом.</summary>
        public static AudioClip ShellExplosion() => Make("shell_expl", 5f, 741, (float t, System.Random r, ref State s) =>
        {
            float n = N(r);
            float crack = n * Env(t, 0.02f) * 1.4f;
            float boom = Mathf.Sin(2 * Mathf.PI * 30 * t * (1 - t * 0.2f)) * Env(t, 0.7f) * 1.5f;
            float body = LP(ref s.lp1, n, 0.06f) * Env(t, 0.8f) * 2.6f;
            if (r.NextDouble() < 0.003 * Env(t, 1.2f)) s.env = 1f;
            s.env *= 0.9988f;
            float debris = LP(ref s.lp2, N(r), 0.5f) * s.env * 0.8f;
            return crack + boom + body + debris;
        }, 0.98f, 0.4f, 0.92f, 0.5f, 0.5f, 0.3f);

        /// <summary>Пуск фаустпатрона: хлопок вышибного заряда + рёв струи назад.</summary>
        public static AudioClip RocketLaunch() => Make("rocket_launch", 2.2f, 751, (float t, System.Random r, ref State s) =>
        {
            float n = N(r);
            float pop = n * Env(t, 0.015f) * 1.3f + Mathf.Sin(2 * Mathf.PI * 90 * t) * Env(t, 0.08f);
            float roar = LP(ref s.lp1, n, 0.25f) * Env(t, 0.35f) * 1.6f * Mathf.Clamp01(t * 60);
            float hiss = (n - LP(ref s.lp2, n, 0.3f)) * Env(t, 0.5f) * 0.6f;
            return pop + roar + hiss;
        }, 0.95f, 0.25f, 0.85f, 0.4f, 0.35f, 0.3f);

        /// <summary>Шипение реактивного снаряда в полёте (петля, 3D).</summary>
        public static AudioClip RocketFlight() => Loop("rocket_fly", 1.5f, 752, (float t, System.Random r, ref State s) =>
        {
            float n = N(r);
            return (n - LP(ref s.lp1, n, 0.2f)) * 0.7f + LP(ref s.lp2, n, 0.05f) * 0.6f;
        }, 0.5f, 22050);

        /// <summary>ПТРД-41: 14,5 мм — очень громкий плоский удар с длинным хвостом.</summary>
        public static AudioClip AntiTankRifle() => Make("ptrd_shot", 3f, 761, (float t, System.Random r, ref State s) =>
        {
            float n = N(r);
            float crack = (n - LP(ref s.lp4, n, 0.5f)) * Env(t, 0.014f) * 1.8f;
            float blast = n * Env(t, 0.03f) * 1.4f;
            float thump = Mathf.Sin(2 * Mathf.PI * 52 * t * (1 - t)) * Env(t, 0.12f) * 1.2f;
            float tail = LP(ref s.lp2, LP(ref s.lp3, n, 0.06f), 0.4f) * Env(Mathf.Max(0, t - 0.02f), 0.8f) * 2.2f * Mathf.Clamp01(t * 30);
            return crack + blast + thump + tail;
        }, 0.97f, 0.28f, 0.9f, 0.4f, 0.45f, 0.3f);

        /// <summary>Пуля/осколок о броню: звон + визг рикошета.</summary>
        public static AudioClip Ricochet(int v) => Make("ricochet" + v, 0.7f, 771 + v, (float t, System.Random r, ref State s) =>
        {
            float ping = (Mathf.Sin(2 * Mathf.PI * (1500 + v * 220) * t) + Mathf.Sin(2 * Mathf.PI * (2650 + v * 180) * t) * 0.5f) * Env(t, 0.06f);
            float f = 3800 - t * 2600 + v * 200;
            s.phase += f / 44100f;
            float whine = Mathf.Sin(s.phase * Mathf.PI * 2) * Env(Mathf.Max(0, t - 0.02f), 0.18f) * 0.45f * Mathf.Clamp01((t - 0.02f) * 50);
            return ping + whine + N(r) * Env(t, 0.003f);
        }, 0.6f, 0.1f, 0.6f);

        /// <summary>Попадание снаряда в броню (глухой удар корпуса).</summary>
        public static AudioClip ArmorHit() => Make("armor_hit", 1.6f, 781, (float t, System.Random r, ref State s) =>
        {
            float n = N(r);
            return n * Env(t, 0.01f) * 1.2f + Mathf.Sin(2 * Mathf.PI * 210 * t) * Env(t, 0.25f) * 0.8f
                 + Mathf.Sin(2 * Mathf.PI * 487 * t) * Env(t, 0.4f) * 0.4f + LP(ref s.lp1, n, 0.08f) * Env(t, 0.3f) * 1.5f;
        }, 0.9f, 0.25f, 0.85f, 0.3f, 0.3f);

        /// <summary>Ломающиеся доски забора.</summary>
        public static AudioClip WoodBreak(int v) => Make("wood_break" + v, 0.9f, 791 + v, (float t, System.Random r, ref State s) =>
        {
            float n = N(r);
            if (r.NextDouble() < 0.004 * Env(t, 0.25f)) s.env = 1f;
            s.env *= 0.995f;
            float snap = LP(ref s.lp1, n, 0.4f) * Env(t, 0.015f) * 1.6f;
            float crackle = LP(ref s.lp2, n, 0.3f) * s.env;
            float body = Mathf.Sin(2 * Mathf.PI * (170 + v * 30) * t) * Env(t, 0.08f) * 0.7f + LP(ref s.lp3, n, 0.1f) * Env(t, 0.12f);
            return snap + crackle + body;
        }, 0.8f, 0.12f, 0.7f);

        /// <summary>Подбор боеприпасов из ящика.</summary>
        public static AudioClip AmmoPickup() => Make("ammo_pick", 0.7f, 801, (float t, System.Random r, ref State s) =>
        {
            float x = 0;
            foreach (float at in new[] { 0.0f, 0.12f, 0.2f, 0.33f })
                if (t >= at) x += (Mathf.Sin(2 * Mathf.PI * (2100 + at * 900) * (t - at)) * 0.4f + N(r) * 0.5f) * Env(t - at, 0.025f);
            return x + LP(ref s.lp1, N(r), 0.2f) * Env(t - 0.4f, 0.08f) * (t > 0.4f ? 0.8f : 0);
        }, 0.6f, 0.05f, 0.5f);

        /// <summary>Звон в ушах после близкого разрыва (тиннитус).</summary>
        public static AudioClip Tinnitus() => Make("tinnitus", 3.5f, 811, (float t, System.Random r, ref State s) =>
            Mathf.Sin(2 * Mathf.PI * 3950 * t) * Mathf.Clamp01(t * 8) * Mathf.Exp(-t / 1.4f) * 0.5f
            + Mathf.Sin(2 * Mathf.PI * 4210 * t) * Mathf.Clamp01(t * 5) * Mathf.Exp(-t / 1.1f) * 0.2f, 0.25f);

        /// <summary>Посадка в танк / вылезание: стук люка.</summary>
        public static AudioClip Hatch() => Mech("hatch", 821, 0.6f, new[] { (0.02f, 0.03f, 0.25f, 1.2f), (0.3f, 0.04f, 0.2f, 1.4f) }, 0.08f, 0.1f, 0.25f, 0.5f, 380);
    }
}
