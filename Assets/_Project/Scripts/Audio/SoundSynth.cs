using System.Collections.Generic;
using UnityEngine;

namespace Frontline
{
    /// <summary>
    /// Процедурный синтез звука v2 (без аудиофайлов), стерео с реверберацией.
    /// • Выстрелы: сверхзвуковой щелчок + дульный хлопок + «тело» + низкий удар + механика затвора +
    ///   эхо от опушки (дискретные отражения) + диффузный хвост (Шрёдер: 4 гребенчатых + 2 всепропускающих фильтра на канал).
    /// • Механика оружия по фазам: магазин вынут/вставлен, затвор вверх/назад/вперёд/вниз, взвод, обойма, патрон, крышка, барабан.
    /// • Шаги: трава, снег, камень/доски, гравий; приземление; попадания по земле, дереву/камню, металлу, по телу.
    /// • Мир: ветер, птицы, сверчки, далёкий бой, артиллерия вдали; интерфейс; музыка главного меню.
    /// Клипы кэшируются по ключу. Настоящие звуки, назначенные в WeaponDefinition / BotAudio, имеют приоритет.
    /// </summary>
    public static partial class SoundSynth
    {
        const int Rate = 44100;
        static readonly Dictionary<string, AudioClip> _cache = new();

        public delegate float Gen(float t, System.Random r, ref State s);
        public struct State { public float lp1, lp2, lp3, lp4, hp, phase, phase2, env; }

        // ======================= Ядро =======================

        static float N(System.Random r) => (float)(r.NextDouble() * 2 - 1);
        static float LP(ref float st, float x, float a) => st += (x - st) * a;
        static float Env(float t, float d) => t < 0 ? 0 : Mathf.Exp(-t / d);
        static float Click(float t, float at, float len, System.Random r, ref float lp, float tone)
            => t >= at && t < at + len ? LP(ref lp, N(r), tone) * Env(t - at, len * 0.25f) : 0f;

        /// <summary>Моно-сигнал → стерео с реверберацией (wet 0 — сухой) и дискретным эхом.</summary>
        /// <summary>
        /// Настоящая запись вместо синтеза: если в Assets/_Project/Resources/Sounds/ лежит клип с именем ключа
        /// (например shot2_kar98k.wav, step_Grass0.wav, mag_in.wav), берётся он. Нет файла — звук синтезируется как раньше.
        /// </summary>
        static AudioClip Ext(string key)
        {
            var clip = Resources.Load<AudioClip>("Sounds/" + key);
            if (clip && clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
            return clip;
        }

        static AudioClip Make(string key, float dur, int seed, Gen g, float gain = 0.95f, float wet = 0f, float room = 0.82f, float echoDelay = 0f, float echoGain = 0f, float width = 0.12f)
        {
            if (_cache.TryGetValue(key, out var c) && c) return c;
            var ext = Ext(key); if (ext) return _cache[key] = ext;
            int n = Mathf.CeilToInt(dur * Rate);
            var dry = new float[n];
            var r = new System.Random(seed); var st = new State();
            for (int i = 0; i < n; i++) dry[i] = g(i / (float)Rate, r, ref st);

            var L = new float[n]; var R = new float[n];
            int ed = Mathf.RoundToInt(echoDelay * Rate), ed2 = Mathf.RoundToInt(echoDelay * 1.63f * Rate);
            float eLp = 0, eLp2 = 0;
            for (int i = 0; i < n; i++)
            {
                float x = dry[i];
                float e1 = ed > 0 && i >= ed ? LP(ref eLp, dry[i - ed], 0.25f) * echoGain : 0;
                float e2 = ed2 > 0 && i >= ed2 ? LP(ref eLp2, dry[i - ed2], 0.15f) * echoGain * 0.6f : 0;
                L[i] = x + e1 + e2 * 0.6f;
                R[i] = x + e1 * 0.6f + e2;
            }
            if (wet > 0)
            {
                Reverb(dry, L, wet, room, 0);
                Reverb(dry, R, wet, room, 23);
            }
            // Небольшое расширение базы: задержка 0.4 мс на одном канале для части сигнала
            int wd = 18;
            for (int i = n - 1; i >= wd; i--) R[i] = R[i] * (1 - width) + R[i - wd] * width;

            float peak = 1e-4f;
            for (int i = 0; i < n; i++) peak = Mathf.Max(peak, Mathf.Max(Mathf.Abs(L[i]), Mathf.Abs(R[i])));
            float k = gain / peak;
            var data = new float[n * 2];
            int f = Mathf.Min(n / 4, 96);
            for (int i = 0; i < n; i++)
            {
                float fade = i < f ? i / (float)f : i > n - 1 - f ? (n - 1 - i) / (float)f : 1f;
                data[i * 2] = SoftClip(L[i] * k) * fade;
                data[i * 2 + 1] = SoftClip(R[i] * k) * fade;
            }
            c = AudioClip.Create(key, n, 2, Rate, false);
            c.SetData(data, 0);
            return _cache[key] = c;
        }

        static float SoftClip(float x) => x > 0.95f ? 0.95f + (x - 0.95f) * 0.2f : x < -0.95f ? -0.95f + (x + 0.95f) * 0.2f : x;

        static readonly int[] Combs = { 1116, 1188, 1277, 1356 }, Alls = { 556, 225 };

        /// <summary>Реверберация Шрёдера (как во Freeverb), результат добавляется в out.</summary>
        static void Reverb(float[] input, float[] output, float wet, float room, int spread)
        {
            int n = input.Length;
            var acc = new float[n];
            foreach (var d0 in Combs)
            {
                int d = d0 + spread; var buf = new float[d]; int idx = 0; float damp = 0;
                for (int i = 0; i < n; i++)
                {
                    float y = buf[idx];
                    damp = y * 0.6f + damp * 0.4f;
                    buf[idx] = input[i] * 0.25f + damp * room;
                    idx = (idx + 1) % d;
                    acc[i] += y;
                }
            }
            foreach (var d0 in Alls)
            {
                int d = d0 + spread / 2; var buf = new float[d]; int idx = 0;
                for (int i = 0; i < n; i++)
                {
                    float b = buf[idx]; float y = -acc[i] + b;
                    buf[idx] = acc[i] + b * 0.5f; idx = (idx + 1) % d;
                    acc[i] = y;
                }
            }
            for (int i = 0; i < n; i++) output[i] += acc[i] * wet;
        }

        /// <summary>Петля с кроссфейдом конца в начало (моно → стерео).</summary>
        static AudioClip Loop(string key, float dur, int seed, Gen g, float gain, int rate = Rate, Gen gR = null)
        {
            if (_cache.TryGetValue(key, out var c) && c) return c;
            var ext = Ext(key); if (ext) return _cache[key] = ext;
            int n = Mathf.CeilToInt(dur * rate), xf = rate / 2;
            var rawL = new float[n + xf]; var rawR = new float[n + xf];
            var rl = new System.Random(seed); var rr = new System.Random(seed + 77);
            var sl = new State(); var sr = new State(); float peak = 1e-4f;
            for (int i = 0; i < rawL.Length; i++)
            {
                float t = i / (float)rate;
                rawL[i] = g(t, rl, ref sl); rawR[i] = (gR ?? g)(t, rr, ref sr);
                peak = Mathf.Max(peak, Mathf.Max(Mathf.Abs(rawL[i]), Mathf.Abs(rawR[i])));
            }
            var data = new float[n * 2];
            for (int i = 0; i < n; i++)
            {
                float l = rawL[i], r = rawR[i];
                if (i < xf) { float k = i / (float)xf; l = l * k + rawL[n + i] * (1 - k); r = r * k + rawR[n + i] * (1 - k); }
                data[i * 2] = l * gain / peak; data[i * 2 + 1] = r * gain / peak;
            }
            c = AudioClip.Create(key, n, 2, rate, false);
            c.SetData(data, 0);
            return _cache[key] = c;
        }

        // ======================= Оружие =======================

        public struct ShotProfile { public float crack, body, thumpHz, tail, bright, mech, echo; }

        public static ShotProfile Profile(WeaponCategory c) => c switch
        {
            WeaponCategory.SniperRifle => new ShotProfile { crack = 0.022f, body = 0.16f, thumpHz = 62, tail = 0.55f, bright = 0.55f, mech = 0f, echo = 0.32f },
            WeaponCategory.AssaultRifle => new ShotProfile { crack = 0.016f, body = 0.11f, thumpHz = 85, tail = 0.38f, bright = 0.5f, mech = 0.35f, echo = 0.24f },
            WeaponCategory.BattleRifle => new ShotProfile { crack = 0.018f, body = 0.13f, thumpHz = 75, tail = 0.45f, bright = 0.52f, mech = 0.35f, echo = 0.28f },
            WeaponCategory.SubmachineGun => new ShotProfile { crack = 0.01f, body = 0.07f, thumpHz = 115, tail = 0.26f, bright = 0.42f, mech = 0.5f, echo = 0.18f },
            WeaponCategory.MachineGun => new ShotProfile { crack = 0.013f, body = 0.09f, thumpHz = 78, tail = 0.32f, bright = 0.5f, mech = 0.3f, echo = 0.26f },
            _ => new ShotProfile { crack = 0.008f, body = 0.06f, thumpHz = 135, tail = 0.22f, bright = 0.38f, mech = 0.45f, echo = 0.16f },
        };

        public static AudioClip GunshotLegacy(string id, WeaponCategory cat)
        {
            var p = Profile(cat);
            float tone = (Mathf.Abs(id.GetHashCode()) % 100) / 100f;   // индивидуальный оттенок образца
            float thumpHz = p.thumpHz * (0.9f + tone * 0.2f);
            return Make("shot_" + id, 0.35f + p.tail * 2.4f, id.GetHashCode(), (float t, System.Random r, ref State s) =>
            {
                float n = N(r);
                float crack = (n - LP(ref s.lp4, n, 0.5f)) * Env(t, p.crack * 0.5f) * 1.4f;                 // N-волна пули
                float blast = n * Env(t, p.crack) * 1.1f;                                                      // дульный хлопок
                float body = LP(ref s.lp1, n, p.bright) * Env(t, p.body) * 1.4f;
                float thump = Mathf.Sin(2 * Mathf.PI * thumpHz * t * (1 - t * 1.5f)) * Env(t, 0.065f) * 0.95f;
                float tail = LP(ref s.lp2, LP(ref s.lp3, n, 0.08f), 0.4f) * Env(Mathf.Max(0, t - 0.02f), p.tail) * 2.0f * Mathf.Clamp01(t * 40);
                float mech = p.mech > 0 ? Click(t, 0.035f + tone * 0.01f, 0.012f, r, ref s.hp, 0.7f) * p.mech : 0;   // ход затворной рамы
                return crack + blast + body + thump + tail + mech;
            }, 0.95f, 0.22f, 0.86f, p.echo, 0.35f);
        }

        public static AudioClip Suppressed(string id) => Make("supp_" + id, 0.5f, id.GetHashCode() + 7, (float t, System.Random r, ref State s) =>
        {
            float n = N(r);
            float thup = LP(ref s.lp1, n, 0.12f) * Env(t, 0.035f) * 2f;
            float pop = Mathf.Sin(2 * Mathf.PI * 160 * t) * Env(t, 0.02f) * 0.6f;
            float mech = Click(t, 0.02f, 0.012f, r, ref s.lp2, 0.6f) * 0.7f + Click(t, 0.06f, 0.01f, r, ref s.lp3, 0.5f) * 0.5f;
            return thup + pop + mech;
        }, 0.6f, 0.08f, 0.6f);

        public static AudioClip Distant(int v) => Make("distant" + v, 2.2f, 100 + v, (float t, System.Random r, ref State s) =>
        {
            float n = N(r);
            float x = LP(ref s.lp2, LP(ref s.lp1, n, 0.035f), 0.2f);
            float att = Mathf.Clamp01(t * 90);
            return x * Env(t, 0.22f + v * 0.06f) * 3f * att;
        }, 0.8f, 0.5f, 0.9f, 0.3f + v * 0.08f, 0.5f, 0.3f);

        public static AudioClip Explosion() => Make("explosion", 4f, 9, (float t, System.Random r, ref State s) =>
        {
            float n = N(r);
            float boom = Mathf.Sin(2 * Mathf.PI * 36 * t * (1 - t * 0.25f)) * Env(t, 0.5f) * 1.3f;
            float body = LP(ref s.lp1, n, 0.08f) * Env(t, 0.55f) * 2f;
            float crack = n * Env(t, 0.025f) * 1.2f;
            if (r.NextDouble() < 0.002 * Env(t, 0.9f)) s.env = 1f;      // разлетающиеся обломки
            s.env *= 0.9985f;
            float debris = LP(ref s.lp2, N(r), 0.45f) * s.env * 0.7f;
            return boom + body + crack + debris;
        }, 0.97f, 0.35f, 0.9f, 0.45f, 0.5f, 0.25f);

        /// <summary>Далёкий артиллерийский разрыв (для эмбиента).</summary>
        public static AudioClip ArtilleryFar(int v) => Make("arty" + v, 3.5f, 200 + v, (float t, System.Random r, ref State s) =>
        {
            float boom = LP(ref s.lp2, LP(ref s.lp1, N(r), 0.012f), 0.1f) * Env(t, 0.7f) * Mathf.Clamp01(t * 30) * 6;
            return boom + Mathf.Sin(2 * Mathf.PI * 28 * t) * Env(t, 0.6f) * 0.5f;
        }, 0.7f, 0.5f, 0.92f, 0.6f, 0.4f, 0.3f);

        // ---------- Механика оружия по фазам ----------

        static AudioClip Mech(string key, int seed, float dur, (float at, float len, float tone, float amp)[] ev, float slide = 0, float slideFrom = 0, float slideTo = 0, float ring = 0, float ringHz = 1800)
            => Make(key, dur, seed, (float t, System.Random r, ref State s) =>
            {
                float x = 0;
                foreach (var e in ev)
                    if (t >= e.at && t < e.at + e.len * 3)
                    {
                        float tt = t - e.at;
                        x += (N(r) * Env(tt, e.len * 0.15f) + LP(ref s.lp1, N(r), e.tone) * Env(tt, e.len)) * e.amp;
                        if (ring > 0) x += Mathf.Sin(2 * Mathf.PI * ringHz * tt) * Env(tt, e.len * 1.5f) * ring * e.amp;
                    }
                if (slide > 0 && t > slideFrom && t < slideTo)
                    x += (LP(ref s.lp2, N(r), 0.15f) - LP(ref s.lp3, N(r), 0.03f)) * slide * Mathf.Sin(Mathf.InverseLerp(slideFrom, slideTo, t) * Mathf.PI);
                return x;
            }, 0.7f, 0.04f, 0.5f);

        public static AudioClip MagOut() => Mech("mag_out", 301, 0.45f, new[] { (0.01f, 0.012f, 0.6f, 1f), (0.09f, 0.02f, 0.25f, 0.6f) }, 0.15f, 0.02f, 0.2f, 0.2f, 2400);
        public static AudioClip MagIn() => Mech("mag_in", 302, 0.4f, new[] { (0.02f, 0.015f, 0.35f, 0.8f), (0.07f, 0.01f, 0.7f, 1.2f) }, 0.12f, 0f, 0.06f, 0.25f, 2100);
        public static AudioClip ChargeBack() => Mech("charge", 303, 0.45f, new[] { (0.01f, 0.01f, 0.6f, 0.9f), (0.12f, 0.014f, 0.5f, 1.1f), (0.24f, 0.01f, 0.7f, 1f) }, 0.2f, 0.01f, 0.13f, 0.3f, 2600);
        public static AudioClip BoltUp() => Mech("bolt_up", 304, 0.2f, new[] { (0.01f, 0.01f, 0.65f, 1f) }, 0, 0, 0, 0.2f, 3000);
        public static AudioClip BoltBack() => Mech("bolt_back", 305, 0.3f, new[] { (0.13f, 0.012f, 0.55f, 1f) }, 0.3f, 0.0f, 0.14f, 0.25f, 2200);
        public static AudioClip BoltForward() => Mech("bolt_fwd", 306, 0.3f, new[] { (0.1f, 0.012f, 0.5f, 1.1f) }, 0.25f, 0.0f, 0.1f, 0.25f, 2000);
        public static AudioClip BoltDown() => Mech("bolt_down", 307, 0.2f, new[] { (0.01f, 0.012f, 0.6f, 1f) }, 0, 0, 0, 0.3f, 2800);
        public static AudioClip ClipIn() => Mech("clip_in", 308, 0.55f, new[] { (0.02f, 0.01f, 0.6f, 0.8f), (0.34f, 0.015f, 0.4f, 0.9f) }, 0.35f, 0.04f, 0.32f, 0.3f, 3400);
        public static AudioClip RoundIn() => Mech("round_in", 309, 0.25f, new[] { (0.02f, 0.01f, 0.65f, 0.8f), (0.08f, 0.008f, 0.75f, 0.5f) }, 0.08f, 0.02f, 0.08f, 0.35f, 3800);
        public static AudioClip CoverOpen() => Mech("cover_open", 310, 0.4f, new[] { (0.01f, 0.015f, 0.5f, 1f), (0.12f, 0.02f, 0.3f, 0.7f) }, 0, 0, 0, 0.3f, 1500);
        public static AudioClip CoverClose() => Mech("cover_close", 311, 0.35f, new[] { (0.01f, 0.02f, 0.4f, 1.3f) }, 0, 0, 0, 0.35f, 1300);
        public static AudioClip PanOff() => Mech("pan_off", 312, 0.5f, new[] { (0.02f, 0.012f, 0.6f, 0.8f), (0.25f, 0.025f, 0.3f, 0.6f) }, 0.1f, 0.05f, 0.25f, 0.4f, 900);
        public static AudioClip CylinderTick() => Mech("cyl", 313, 0.15f, new[] { (0.005f, 0.006f, 0.8f, 0.8f) }, 0, 0, 0, 0.2f, 4200);
        public static AudioClip DryFire() => Mech("dry", 41, 0.15f, new[] { (0.005f, 0.006f, 0.7f, 1f) }, 0, 0, 0, 0.15f, 3500);
        public static AudioClip Reload(bool bolt) => bolt ? ClipIn() : MagIn();
        public static AudioClip Bolt() => BoltBack();

        public static AudioClip ShellTink(int v) => Make("tink" + v, 0.35f, 400 + v, (float t, System.Random r, ref State s) =>
        {
            float f = 3200 + v * 600;
            float x = 0;
            for (int k = 0; k < 3; k++)                 // 2–3 подскока гильзы
            {
                float at = k * (0.07f + v * 0.015f);
                float tt = t - at;
                if (tt >= 0) x += (Mathf.Sin(2 * Mathf.PI * f * tt) + Mathf.Sin(2 * Mathf.PI * f * 2.76f * tt) * 0.5f) * Env(tt, 0.03f) * Mathf.Pow(0.5f, k);
            }
            return x;
        }, 0.4f, 0.03f, 0.5f);

        // ======================= Игрок и мир =======================

        public enum Surface { Grass, Snow, Stone, Wood, Gravel }

        public static AudioClip Footstep(bool snow, int v) => Footstep(snow ? Surface.Snow : Surface.Grass, v);

        public static AudioClip Footstep(Surface surf, int v) => Make("step_" + surf + v, 0.3f, 50 + v + (int)surf * 10, (float t, System.Random r, ref State s) =>
        {
            float n = N(r);
            switch (surf)
            {
                case Surface.Snow:
                {
                    float gate = r.NextDouble() < 0.22 ? 1 : 0.12f;
                    return LP(ref s.lp1, n, 0.5f) * gate * Env(t, 0.07f) * Mathf.Clamp01(t / 0.012f);
                }
                case Surface.Stone:
                    return (LP(ref s.lp1, n, 0.35f) * Env(t, 0.012f) * 1.6f + Mathf.Sin(2 * Mathf.PI * (180 + v * 20) * t) * Env(t, 0.02f) * 0.5f) + (n - LP(ref s.lp2, n, 0.4f)) * Env(t, 0.03f) * 0.25f;
                case Surface.Wood:
                    return LP(ref s.lp1, n, 0.2f) * Env(t, 0.02f) * 1.5f + Mathf.Sin(2 * Mathf.PI * (95 + v * 12) * t) * Env(t, 0.06f) * 0.9f;
                case Surface.Gravel:
                {
                    float gate = r.NextDouble() < 0.35 ? 1 : 0.2f;
                    return (LP(ref s.lp1, n, 0.6f) * gate * 0.8f + LP(ref s.lp2, n, 0.08f)) * Env(t, 0.05f);
                }
                default:
                {
                    float thud = LP(ref s.lp1, n, 0.08f) * Env(t, 0.03f) * 2;
                    float rustle = (n - LP(ref s.lp2, n, 0.3f)) * Env(t - 0.01f, 0.06f) * 0.45f;
                    return thud + rustle;
                }
            }
        }, 0.5f, 0.03f, 0.4f);

        public static AudioClip Land() => Make("land", 0.4f, 61, (float t, System.Random r, ref State s) =>
            LP(ref s.lp1, N(r), 0.06f) * Env(t, 0.06f) * 3 + Mathf.Sin(2 * Mathf.PI * 70 * t) * Env(t, 0.05f) + LP(ref s.lp2, N(r), 0.4f) * Env(t - 0.08f, 0.04f) * 0.5f, 0.6f);

        public static AudioClip Jump() => Make("jump", 0.25f, 62, (float t, System.Random r, ref State s) =>
            (N(r) - LP(ref s.lp1, N(r), 0.2f)) * Env(t, 0.05f) * 0.6f + LP(ref s.lp2, N(r), 0.1f) * Env(t, 0.03f), 0.35f);

        public static AudioClip Heartbeat() => Make("heart", 0.45f, 63, (float t, System.Random r, ref State s) =>
            Mathf.Sin(2 * Mathf.PI * 55 * t) * Env(t, 0.05f) + Mathf.Sin(2 * Mathf.PI * 50 * (t - 0.17f)) * Env(t - 0.17f, 0.06f) * 0.7f, 0.8f);

        public static AudioClip KnifeSwish() => Make("swish", 0.35f, 71, (float t, System.Random r, ref State s) =>
        {
            float env = Mathf.Sin(Mathf.Clamp01(t / 0.25f) * Mathf.PI);
            float a = Mathf.Lerp(0.05f, 0.55f, Mathf.Clamp01(t / 0.25f));
            float n = N(r);
            return (LP(ref s.lp1, n, a) - LP(ref s.lp2, n, a * 0.3f)) * env * 2;
        }, 0.5f, 0.05f, 0.5f);

        public static AudioClip Thud() => Make("thud", 0.3f, 72, (float t, System.Random r, ref State s) =>
            LP(ref s.lp1, N(r), 0.05f) * Env(t, 0.04f) * 3 + Mathf.Sin(2 * Mathf.PI * 95 * t) * Env(t, 0.05f), 0.7f);

        public static AudioClip BrickLay() => Make("brick", 0.6f, 81, (float t, System.Random r, ref State s) =>
        {
            float n = N(r);
            float scrape = t < 0.3f ? (n - LP(ref s.lp1, n, 0.2f)) * 0.25f * Mathf.Sin(t / 0.3f * Mathf.PI) : 0;
            float tap = t > 0.32f ? LP(ref s.lp2, n, 0.25f) * Env(t - 0.32f, 0.02f) * 2 + Mathf.Sin(2 * Mathf.PI * 420 * t) * Env(t - 0.32f, 0.03f) * 0.4f : 0;
            return scrape + tap;
        }, 0.6f, 0.06f, 0.5f);

        public static AudioClip Whiz(int v) => Make("whiz" + v, 0.4f, 90 + v, (float t, System.Random r, ref State s) =>
        {
            float n = N(r);
            float band = LP(ref s.lp1, n, 0.45f) - LP(ref s.lp2, n, 0.15f);
            float env = Mathf.Exp(-Mathf.Pow((t - 0.08f) / 0.05f, 2)) + (t > 0.08f ? Env(t - 0.08f, 0.04f) * 0.4f : 0);
            float crack = (t > 0.075f && t < 0.08f) ? n * 0.9f : 0;
            return band * env * 3 + crack;
        }, 0.6f, 0.1f, 0.6f, 0, 0, 0.3f);

        public static AudioClip HitTick() => Make("tick", 0.08f, 95, (float t, System.Random r, ref State s) =>
            Mathf.Sin(2 * Mathf.PI * 2600 * t) * Env(t, 0.012f) + Mathf.Sin(2 * Mathf.PI * 1300 * t) * Env(t, 0.02f) * 0.4f, 0.5f);

        public static AudioClip Headshot() => Make("headshot", 0.2f, 96, (float t, System.Random r, ref State s) =>
            Mathf.Sin(2 * Mathf.PI * 3400 * t) * Env(t, 0.03f) + Mathf.Sin(2 * Mathf.PI * 5100 * t) * Env(t, 0.02f) * 0.5f + LP(ref s.lp1, N(r), 0.4f) * Env(t, 0.008f), 0.55f, 0.05f, 0.4f);

        /// <summary>Подтверждение убийства: глухой удар + два коротких металлических «динь» (как в Polyfield/Battlefield).</summary>
        public static AudioClip KillConfirm() => Make("kill_confirm", 0.55f, 97, (float t, System.Random r, ref State s) =>
        {
            float thump = Mathf.Sin(2 * Mathf.PI * (140 - 60 * t) * t) * Env(t, 0.05f) * 0.8f;
            float d1 = Mathf.Sin(2 * Mathf.PI * 1480 * t) * Env(t, 0.09f) + Mathf.Sin(2 * Mathf.PI * 2960 * t) * Env(t, 0.04f) * 0.3f;
            float t2 = t - 0.085f;
            float d2 = t2 > 0 ? Mathf.Sin(2 * Mathf.PI * 1975 * t2) * Env(t2, 0.12f) + Mathf.Sin(2 * Mathf.PI * 3950 * t2) * Env(t2, 0.05f) * 0.25f : 0f;
            return thump + d1 * 0.55f + d2 * 0.6f;
        }, 0.6f, 0.08f, 0.5f);

        public enum ImpactKind { Dirt, Snow, Stone, Wood, Metal, Flesh }

        public static AudioClip Impact(ImpactKind k, int v) => Make("imp_" + k + v, 0.45f, 500 + (int)k * 10 + v, (float t, System.Random r, ref State s) =>
        {
            float n = N(r);
            switch (k)
            {
                case ImpactKind.Metal:
                    return (Mathf.Sin(2 * Mathf.PI * (1900 + v * 300) * t) + Mathf.Sin(2 * Mathf.PI * (3100 + v * 250) * t) * 0.6f) * Env(t, 0.08f) + n * Env(t, 0.004f);
                case ImpactKind.Stone:
                    return n * Env(t, 0.006f) * 1.2f + LP(ref s.lp1, n, 0.3f) * Env(t, 0.03f) + (r.NextDouble() < 0.01 * Env(t, 0.1f) ? 0.6f : 0);
                case ImpactKind.Wood:
                    return LP(ref s.lp1, n, 0.25f) * Env(t, 0.02f) * 1.5f + Mathf.Sin(2 * Mathf.PI * (240 + v * 40) * t) * Env(t, 0.04f) * 0.6f;
                case ImpactKind.Flesh:
                    return LP(ref s.lp1, n, 0.07f) * Env(t, 0.03f) * 2.5f + Mathf.Sin(2 * Mathf.PI * 110 * t) * Env(t, 0.04f);
                case ImpactKind.Snow:
                    return LP(ref s.lp1, n, 0.2f) * Env(t, 0.04f) * 1.4f;
                default:
                    return LP(ref s.lp1, n, 0.12f) * Env(t, 0.035f) * 1.8f + n * Env(t, 0.004f) * 0.5f + LP(ref s.lp2, N(r), 0.5f) * Env(t - 0.05f, 0.08f) * 0.25f;
            }
        }, 0.6f, 0.12f, 0.6f);

        public static AudioClip Wind(bool winter) => Loop(winter ? "wind_w" : "wind_s", 12f, 111, (float t, System.Random r, ref State s) =>
        {
            float gust = 0.5f + 0.5f * Mathf.Sin(t * 0.52f) * Mathf.Sin(t * 0.21f + 1);
            float n = N(r);
            float whistle = winter ? Mathf.Sin(2 * Mathf.PI * (520 + 80 * Mathf.Sin(t * 0.3f)) * t) * 0.04f * gust * gust : 0;
            return LP(ref s.lp2, LP(ref s.lp1, n, winter ? 0.05f + gust * 0.03f : 0.025f), 0.3f) * gust + whistle;
        }, 0.35f, 22050, (float t, System.Random r, ref State s) =>
        {
            float gust = 0.5f + 0.5f * Mathf.Sin(t * 0.47f + 2) * Mathf.Sin(t * 0.23f);
            return LP(ref s.lp2, LP(ref s.lp1, N(r), winter ? 0.05f : 0.025f), 0.3f) * gust;
        });

        public static AudioClip BattleRumble() => Loop("rumble", 14f, 121, (float t, System.Random r, ref State s) =>
        {
            if (r.NextDouble() < 0.00035) s.env = 1f;           // далёкий выстрел/разрыв
            if (r.NextDouble() < 0.00008) s.phase2 = 1f;        // далёкая пулемётная очередь
            s.env *= 0.99975f; s.phase2 *= 0.99993f;
            float mg = s.phase2 > 0.05f && (int)(t * 18) % 2 == 0 ? s.phase2 * 0.8f : 0;
            return LP(ref s.lp2, LP(ref s.lp1, N(r), 0.02f), 0.1f) * (0.12f + s.env * 3f + mg);
        }, 0.4f, 22050);

        public static AudioClip Birds() => Make("birds", 2.6f, 131, (float t, System.Random r, ref State s) =>
        {
            float x = 0;
            for (int k = 0; k < 5; k++)
            {
                float st = 0.2f + k * 0.33f + (k % 2) * 0.05f, d = 0.09f + (k % 3) * 0.03f;
                if (t > st && t < st + d)
                {
                    float u = (t - st) / d;
                    float f = 2600 + 1800 * Mathf.Sin(u * Mathf.PI) + k * 180;
                    s.phase += 2 * Mathf.PI * f / Rate;
                    x += Mathf.Sin(s.phase) * Mathf.Sin(u * Mathf.PI);
                }
            }
            return x;
        }, 0.35f, 0.15f, 0.7f);

        public static AudioClip Crickets() => Loop("crickets", 8f, 141, (float t, System.Random r, ref State s) =>
        {
            float chirp = ((int)(t * 15) % 3 != 0 && (t % 1.7f) < 0.9f) ? 1 : 0;
            return Mathf.Sin(2 * Mathf.PI * 4300 * t) * chirp * (0.5f + 0.5f * Mathf.Sin(t * 95)) * 0.5f;
        }, 0.15f, 22050);

        // ======================= Интерфейс и музыка =======================

        public static AudioClip UiClick() => Make("ui_click", 0.12f, 601, (float t, System.Random r, ref State s) =>
            Mathf.Sin(2 * Mathf.PI * 1600 * t) * Env(t, 0.012f) + LP(ref s.lp1, N(r), 0.5f) * Env(t, 0.004f) * 0.6f, 0.4f);

        public static AudioClip UiHover() => Make("ui_hover", 0.08f, 602, (float t, System.Random r, ref State s) =>
            Mathf.Sin(2 * Mathf.PI * 900 * t) * Env(t, 0.015f), 0.15f);

        /// <summary>
        /// Музыка меню: медленный марш в ре миноре — струнный «пэд» (аккорды Dm–B♭–Gm–A), низкая медь с мелодией,
        /// малый барабан с дробью и литавры. 32 такта по 2 с, бесшовная петля.
        /// </summary>
        public static AudioClip MenuMusicLegacy()
        {
            const float bar = 2f; const int bars = 16;
            float[][] chords = { new[] { 146.8f, 174.6f, 220f }, new[] { 116.5f, 146.8f, 174.6f }, new[] { 98f, 116.5f, 146.8f }, new[] { 110f, 138.6f, 164.8f } };
            float[] melody = { 293.7f, 0, 349.2f, 329.6f, 293.7f, 0, 261.6f, 233.1f, 220f, 0, 233.1f, 261.6f, 277.2f, 0, 0, 0,
                               293.7f, 0, 349.2f, 392f, 440f, 0, 392f, 349.2f, 392f, 0, 349.2f, 329.6f, 293.7f, 0, 0, 0 };
            Gen music(int chan) => (float t, System.Random r, ref State s) =>
            {
                int b = (int)(t / bar) % bars;
                var ch = chords[(b / 2) % 4];
                float x = 0, tb = t % bar;
                // Струнные: три голоса, слегка расстроенные пилообразные волны через ФНЧ, мягкая атака каждого аккорда
                float attack = Mathf.Clamp01(((t % (bar * 2)) / 0.6f));
                for (int i = 0; i < 3; i++)
                {
                    float f = ch[i] * (1 + (chan == 0 ? 0.002f : -0.002f) * (i + 1));
                    x += ((t * f) % 1f * 2 - 1) * 0.22f + ((t * f * 1.004f) % 1f * 2 - 1) * 0.12f;
                }
                x = LP(ref s.lp1, x, 0.05f) * attack;
                // Мелодия (медь): квадратная волна с вибрато, через ФНЧ
                float beat = t / (bar / 2);
                float mf = melody[(int)beat % melody.Length];
                if (mf > 0)
                {
                    float nt = beat % 1f;
                    float vib = 1 + 0.004f * Mathf.Sin(t * 33);
                    float sq = Mathf.Sign(Mathf.Sin(2 * Mathf.PI * mf * vib * t)) * 0.5f + Mathf.Sin(2 * Mathf.PI * mf * 2 * t) * 0.2f;
                    x += LP(ref s.lp2, sq, 0.08f) * Mathf.Clamp01(nt / 0.05f) * Mathf.Clamp01((1 - nt) / 0.2f) * 0.55f;
                }
                // Малый барабан: доли 2 и 4 + дробь в конце каждой фразы
                float q = bar / 4; float tq = tb % q; int qi = (int)(tb / q);
                bool roll = b % 4 == 3 && qi == 3;
                if (qi == 1 || qi == 3 || roll)
                {
                    float tr = roll ? tq % (q / 6) : tq;
                    x += (N(r) - LP(ref s.lp3, N(r), 0.3f)) * Env(tr, roll ? 0.02f : 0.05f) * (roll ? 0.35f : 0.55f);
                }
                // Литавры на первую долю каждого второго такта
                if (b % 2 == 0 && tb < 1.2f) x += Mathf.Sin(2 * Mathf.PI * (ch[0] * 0.5f) * tb) * Env(tb, 0.45f) * 0.9f;
                return x;
            };
            return Loop("menu_music", bar * bars, 777, music(0), 0.55f, 22050, music(1));
        }
    }
}
