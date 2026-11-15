using System.Collections.Generic;
using UnityEngine;

namespace Frontline
{
    /// <summary>
    /// Реалистичные выстрелы и музыка меню.
    /// Выстрел собирается как в акустике открытого пространства:
    ///  • дульная волна Фридлендера (резкий фронт, экспоненциальный спад, отрицательная фаза) + турбулентный шум газов;
    ///  • сверхзвуковая N-волна пули у винтовочных патронов;
    ///  • резонансы ствола/ствольной коробки (затухающие парциалы), механика автоматики;
    ///  • отражение от земли (3 мс) — «плотность» звука;
    ///  • ранние отражения от опушек и строений (20–260 мс, разные для левого и правого уха, всё глуше с задержкой);
    ///  • поздний «раскат» — диффузный низкочастотный хвост 1–2 с, как у выстрела в поле.
    /// Музыка меню — медленная фортепианная пьеса ре минор со струнными и виолончелью, с реверберацией зала.
    /// </summary>
    public static partial class SoundSynth
    {
        struct Shot2 { public float blastMs, noiseMs, crack, boomHz, boomDecay, mech, tail, early, late, bright; }

        static Shot2 Prof2(WeaponCategory c) => c switch
        {
            WeaponCategory.SniperRifle or WeaponCategory.BattleRifle =>
                new Shot2 { blastMs = 0.62f, noiseMs = 9f, crack = 1f, boomHz = 82f, boomDecay = 0.075f, mech = 0f, tail = 2.0f, early = 0.5f, late = 0.32f, bright = 0.55f },
            WeaponCategory.AssaultRifle =>
                new Shot2 { blastMs = 0.48f, noiseMs = 7f, crack = 0.8f, boomHz = 104f, boomDecay = 0.055f, mech = 0.35f, tail = 1.6f, early = 0.45f, late = 0.27f, bright = 0.5f },
            WeaponCategory.MachineGun =>
                new Shot2 { blastMs = 0.56f, noiseMs = 8f, crack = 0.9f, boomHz = 92f, boomDecay = 0.06f, mech = 0.28f, tail = 1.7f, early = 0.48f, late = 0.3f, bright = 0.52f },
            WeaponCategory.SubmachineGun =>
                new Shot2 { blastMs = 0.32f, noiseMs = 4.5f, crack = 0.12f, boomHz = 150f, boomDecay = 0.04f, mech = 0.55f, tail = 1.15f, early = 0.38f, late = 0.2f, bright = 0.42f },
            WeaponCategory.Launcher =>
                new Shot2 { blastMs = 0.9f, noiseMs = 14f, crack = 1f, boomHz = 60f, boomDecay = 0.1f, mech = 0f, tail = 2.3f, early = 0.55f, late = 0.36f, bright = 0.6f },
            _ =>   // пистолеты
                new Shot2 { blastMs = 0.26f, noiseMs = 3.2f, crack = 0.08f, boomHz = 185f, boomDecay = 0.03f, mech = 0.5f, tail = 0.95f, early = 0.32f, late = 0.17f, bright = 0.4f },
        };

        static float Loud(float x) => (float)System.Math.Tanh(x * 2f) / 0.964f * 0.97f;

        public static AudioClip Gunshot(string id, WeaponCategory cat)
        {
            string key = "shot3_" + id;
            if (_cache.TryGetValue(key, out var cached) && cached) return cached;
            var ext = Ext(key); if (ext) return _cache[key] = ext;
            var p = Prof2(cat);
            int seed = id.GetHashCode();
            var rnd = new System.Random(seed);
            float tone = (Mathf.Abs(seed) % 100) / 100f;
            const int sr = Rate;

            // ---------- Сухой сигнал (0.3 с) ----------
            int nd = (int)(0.3f * sr);
            var dry = new float[nd];
            float tp = p.blastMs / 1000f, boom = p.boomHz * (0.92f + tone * 0.16f);
            float lpN = 0, lpN2 = 0, hpPrev = 0, hpOut = 0, mlp = 0;
            for (int i = 0; i < nd; i++)
            {
                float t = i / (float)sr;
                // Волна Фридлендера: положительная фаза длительностью tp и затяжная отрицательная
                float fr = t < tp * 6f ? (1f - t / tp) * Mathf.Exp(-1.8f * t / tp) : 0f;
                // Турбулентный шум пороховых газов
                float n = N(rnd);
                lpN += (n - lpN) * p.bright;
                float turb = lpN * Env(t, p.noiseMs / 1000f) * 0.9f;
                // Сверхзвуковая N-волна пули (у стрелка совпадает с хлопком)
                float crack = 0;
                if (p.crack > 0 && t < 0.0004f) crack = (1f - 2f * t / 0.0004f) * p.crack * 0.9f;
                // Резонансы ствола и коробки
                float body = Mathf.Sin(2 * Mathf.PI * boom * t) * Env(t, p.boomDecay) * 0.55f
                           + Mathf.Sin(2 * Mathf.PI * boom * 2.37f * t + 0.6f) * Env(t, p.boomDecay * 0.6f) * 0.3f
                           + Mathf.Sin(2 * Mathf.PI * boom * 4.11f * t + 1.1f) * Env(t, p.boomDecay * 0.35f) * 0.18f;
                // Хлёсткая высокочастотная составляющая (дифференцированный шум) — первые миллисекунды
                hpOut = 0.97f * (hpOut + n - hpPrev); hpPrev = n;
                float snap = hpOut * Env(t, 0.0018f) * 0.5f;
                // Механика автоматики: удар затворной рамы и отражателя
                float mech = 0;
                if (p.mech > 0)
                {
                    float m1 = t - (0.018f + tone * 0.006f), m2 = t - (0.046f + tone * 0.008f);
                    if (m1 > 0 && m1 < 0.01f) { mlp += (N(rnd) - mlp) * 0.6f; mech += mlp * Env(m1, 0.0025f); }
                    if (m2 > 0 && m2 < 0.012f) { mlp += (N(rnd) - mlp) * 0.45f; mech += mlp * Env(m2, 0.003f) * 0.8f; }
                    mech += Mathf.Sin(2 * Mathf.PI * (2300 + tone * 600) * t) * (m1 > 0 ? Env(m1, 0.012f) : 0) * 0.15f;
                    mech *= p.mech;
                }
                lpN2 += (fr - lpN2) * 0.5f;
                dry[i] = lpN2 * 2.2f + turb + crack + body + snap + mech;
            }

            // Тёмные копии для отражений
            var dark1 = new float[nd]; var dark2 = new float[nd];
            float a1 = 0, a2 = 0, b2 = 0;
            for (int i = 0; i < nd; i++)
            {
                a1 += (dry[i] - a1) * 0.22f; dark1[i] = a1;
                a2 += (dry[i] - a2) * 0.07f; b2 += (a2 - b2) * 0.07f; dark2[i] = b2 * 1.8f;
            }
            float energy = 0; for (int i = 0; i < nd; i++) energy += dry[i] * dry[i];
            energy = Mathf.Sqrt(energy / nd);

            // ---------- Стерео: прямой звук + земля + ранние отражения + раскат ----------
            int n2 = (int)((0.3f + p.tail) * sr);
            var L = new float[n2]; var R = new float[n2];
            for (int i = 0; i < nd; i++) { L[i] += dry[i]; R[i] += dry[i]; }
            int g = (int)(0.0031f * sr);
            for (int i = 0; i < nd && i + g < n2; i++) { L[i + g] += dark1[i] * 0.45f; R[i + g + 3] += dark1[i] * 0.42f; }

            void Taps(float[] outp, int s)
            {
                var tr = new System.Random(seed + s);
                for (int k = 0; k < 26; k++)
                {
                    float delay = 0.02f + (float)tr.NextDouble() * 0.24f * (0.3f + k / 26f);
                    float amp = p.early * Mathf.Exp(-delay / 0.11f) * (0.5f + (float)tr.NextDouble() * 0.6f) * (tr.NextDouble() < 0.15 ? -1 : 1);
                    int off = (int)(delay * sr);
                    var src = delay > 0.07f ? dark2 : dark1;
                    for (int i = 0; i < nd && i + off < n2; i++) outp[i + off] += src[i] * amp;
                }
            }
            Taps(L, 11); Taps(R, 29);

            // Поздний раскат: отфильтрованный шум с медленной огибающей (подъём ~60 мс, спад p.tail)
            void Late(float[] outp, int s)
            {
                var tr = new System.Random(seed + s);
                float l1 = 0, l2 = 0, mod = 0;
                for (int i = 0; i < n2; i++)
                {
                    float t = i / (float)sr;
                    float env = Mathf.Clamp01(t / 0.06f) * Mathf.Exp(-t / (p.tail * 0.42f));
                    float x = (float)(tr.NextDouble() * 2 - 1);
                    float cutoff = Mathf.Lerp(0.09f, 0.025f, Mathf.Clamp01(t / p.tail));      // со временем глуше
                    l1 += (x - l1) * cutoff; l2 += (l1 - l2) * cutoff;
                    mod += ((float)tr.NextDouble() - 0.5f) * 0.02f; mod *= 0.999f;            // «перекаты» по опушкам
                    outp[i] += l2 * env * energy * p.late * 14f * (1f + mod * 3f);
                }
            }
            Late(L, 101); Late(R, 211);

            float peak = 1e-4f;
            for (int i = 0; i < n2; i++) peak = Mathf.Max(peak, Mathf.Max(Mathf.Abs(L[i]), Mathf.Abs(R[i])));
            float k2 = 0.97f / peak;
            var data = new float[n2 * 2];
            int fade = sr / 20;
            for (int i = 0; i < n2; i++)
            {
                float f = i > n2 - fade ? (n2 - i) / (float)fade : 1f;
                // Громкость ×2: усиление в 2 раза с мягким ограничителем (tanh) — пик не клиппует,
                // а «тело» выстрела и хвост становятся вдвое громче
                data[i * 2] = Loud(L[i] * k2) * f;
                data[i * 2 + 1] = Loud(R[i] * k2) * f;
            }
            var c = AudioClip.Create(key, n2, 2, sr, false);
            c.SetData(data, 0);
            return _cache[key] = c;
        }

        // ======================= Смена оружия =======================

        /// <summary>Убрать оружие: шорох ремня и ткани, лёгкий стук о снаряжение.</summary>
        public static AudioClip Holster() => Make("holster", 0.45f, 901, (float t, System.Random r, ref State s) =>
        {
            float n = N(r);
            float rustle = (n - LP(ref s.lp1, n, 0.25f)) * Mathf.Sin(Mathf.Clamp01(t / 0.3f) * Mathf.PI) * 0.5f;
            float tap = t > 0.26f ? LP(ref s.lp2, N(r), 0.5f) * Env(t - 0.26f, 0.01f) * 0.9f : 0;
            return rustle + tap;
        }, 0.5f, 0.06f, 0.5f);

        /// <summary>Достать оружие: шорох, затем металлический «клац» — тяжелее для больших образцов.</summary>
        public static AudioClip Draw(WeaponCategory c)
        {
            float heavy = c switch { WeaponCategory.MachineGun or WeaponCategory.Launcher => 1f, WeaponCategory.SniperRifle or WeaponCategory.BattleRifle or WeaponCategory.AssaultRifle => 0.65f,
                                     WeaponCategory.SubmachineGun => 0.45f, WeaponCategory.Knife => 0f, _ => 0.25f };
            return Make("draw_" + c, 0.6f, 911 + (int)c, (float t, System.Random r, ref State s) =>
            {
                float n = N(r);
                float rustle = (n - LP(ref s.lp1, n, 0.3f)) * Mathf.Sin(Mathf.Clamp01(t / 0.25f) * Mathf.PI) * 0.45f;
                float at = 0.22f + heavy * 0.12f;
                float clack = t > at ? (LP(ref s.lp2, N(r), 0.55f - heavy * 0.25f) * Env(t - at, 0.012f + heavy * 0.01f) * 1.1f
                             + Mathf.Sin(2 * Mathf.PI * (1900 - heavy * 900) * (t - at)) * Env(t - at, 0.05f) * 0.25f) : 0;
                if (c == WeaponCategory.Knife) clack = t > 0.15f ? Mathf.Sin(2 * Mathf.PI * (3200 + 1500 * (t - 0.15f)) * t) * Env(t - 0.15f, 0.12f) * 0.3f : 0;
                return rustle + clack;
            }, 0.55f, 0.06f, 0.5f);
        }

        // ======================= Музыка меню =======================

        static float Midi(int m) => 440f * Mathf.Pow(2f, (m - 69) / 12f);

        /// <summary>Нота фортепиано: негармоничные парциалы, двухступенчатое затухание, удар молоточка.</summary>
        static float[] PianoNote(int midi, float dur, int sr)
        {
            int n = (int)(dur * sr);
            var buf = new float[n];
            float f0 = Midi(midi);
            const float B = 0.00035f;
            var rnd = new System.Random(midi * 13 + 5);
            float hl = 0;
            for (int k = 1; k <= 8; k++)
            {
                float fk = k * f0 * Mathf.Sqrt(1 + B * k * k);
                if (fk > sr * 0.45f) break;
                float amp = 1f / Mathf.Pow(k, 1.25f) * (k == 2 ? 0.8f : 1f);
                float d = Mathf.Lerp(3.2f, 1.2f, (midi - 36) / 60f) / Mathf.Pow(k, 0.65f);
                float w = 2 * Mathf.PI * fk / sr, ph = (float)rnd.NextDouble() * 6.28f;
                // Рекурсивный осциллятор и мультипликативные огибающие — быстро и без накопления ошибки фазы
                double cw = System.Math.Cos(w), sw = System.Math.Sin(w), x = System.Math.Cos(ph), y = System.Math.Sin(ph);
                float e1 = 0.65f * amp, e2 = 0.35f * amp;
                float c1 = Mathf.Exp(-1f / (d * 0.18f * sr)), c2 = Mathf.Exp(-1f / (d * sr));
                for (int i = 0; i < n; i++)
                {
                    buf[i] += (float)y * (e1 + e2);
                    e1 *= c1; e2 *= c2;
                    double nx = x * cw - y * sw; y = x * sw + y * cw; x = nx;
                    if (e1 + e2 < 1e-5f) break;
                }
            }
            for (int i = 0; i < Mathf.Min(n, sr / 50); i++)
            {
                hl += ((float)(rnd.NextDouble() * 2 - 1) - hl) * 0.35f;
                buf[i] += hl * Mathf.Exp(-i / (sr * 0.004f)) * 0.25f;
            }
            for (int i = 0; i < Mathf.Min(n, 64); i++) buf[i] *= i / 64f;
            for (int i = 0; i < n; i++) { float t = i / (float)sr; if (t > dur - 0.25f) buf[i] *= (dur - t) / 0.25f; }
            return buf;
        }

        /// <summary>
        /// «Поле после боя»: ре минор, 80 уд/мин. Фортепиано — арпеджио левой руки и мелодия правой, струнная подложка,
        /// виолончель на басу, зал. 16 тактов по 3 с, вторая половина — мелодия выше и шире. Бесшовная петля.
        /// </summary>
        public static AudioClip MenuMusic()
        {
            const string key = "menu_music2";
            if (_cache.TryGetValue(key, out var cached) && cached) return cached;
            var ext = Ext(key); if (ext) return _cache[key] = ext;
            const int sr = 32000; const float bar = 3f; const int bars = 16;
            float total = bar * bars, tailT = 3f;
            int n = (int)((total + tailT) * sr);
            var mono = new float[n]; var padL = new float[n]; var padR = new float[n];

            int[][] chords =
            {
                new[] { 50, 57, 62, 65, 69 }, new[] { 46, 53, 58, 62, 65 }, new[] { 41, 48, 53, 57, 60 }, new[] { 48, 55, 60, 64, 67 },
                new[] { 43, 50, 55, 58, 62 }, new[] { 50, 57, 62, 65, 69 }, new[] { 46, 53, 58, 62, 65 }, new[] { 45, 52, 57, 61, 64 }
            };
            int[] mel1 = { 77, 76, 74, 72, 72, 69, 67, 72, 70, 74, 74, 72, 70, 69, 69, -1 };
            int[] mel2 = { 81, 79, 77, 74, 76, 72, 72, 76, 79, 77, 77, 74, 74, 73, 76, -1 };
            int[] arp = { 0, 1, 2, 3, 4, 3, 2, 1 };
            var notes = new Dictionary<int, float[]>();
            float[] Note(int m) { if (!notes.TryGetValue(m, out var b)) notes[m] = b = PianoNote(m, 4.5f, sr); return b; }
            void Put(float[] src, float at, float vel)
            {
                int o = (int)(at * sr);
                for (int i = 0; i < src.Length && o + i < n; i++) mono[o + i] += src[i] * vel;
            }

            for (int b = 0; b < bars; b++)
            {
                var ch = chords[b % 8];
                float t0 = b * bar;
                Put(Note(ch[0] - 12), t0, 0.5f);                                      // бас
                for (int e = 0; e < 8; e++)
                    Put(Note(ch[arp[e]]), t0 + e * bar / 8f + (e % 2 == 1 ? 0.012f : 0f), e == 0 ? 0.32f : 0.22f);
                var mel = b < 8 ? mel1 : mel2;
                for (int h = 0; h < 2; h++)
                {
                    int m = mel[(b % 8) * 2 + h];
                    if (m > 0) Put(Note(m), t0 + h * bar / 2f, 0.5f);
                }
            }

            // Струнные и виолончель: пила с расстройкой через ФНЧ, медленная атака, вибрато
            for (int b = 0; b < bars + 1; b++)
            {
                var ch = chords[b % 8];
                float t0 = b * bar;
                int i0 = (int)(t0 * sr), i1 = Mathf.Min(n, (int)((t0 + bar + 0.8f) * sr));
                float lpL = 0, lpR = 0;
                for (int i = i0; i < i1; i++)
                {
                    float t = (i - i0) / (float)sr;
                    float env = Mathf.Clamp01(t / 1.1f) * Mathf.Clamp01((bar + 0.8f - t) / 0.8f);
                    float vib = 1 + 0.003f * Mathf.Sin(2 * Mathf.PI * 5.2f * (i / (float)sr));
                    float xl = 0, xr = 0;
                    for (int v = 1; v <= 3; v++)
                    {
                        float f = Midi(ch[v]) * vib;
                        float tt = i / (float)sr;
                        xl += ((tt * f * 1.002f) % 1f * 2 - 1) + ((tt * f * 0.997f) % 1f * 2 - 1) * 0.6f;
                        xr += ((tt * f * 0.998f) % 1f * 2 - 1) + ((tt * f * 1.003f) % 1f * 2 - 1) * 0.6f;
                    }
                    float cello = Mathf.Sin(2 * Mathf.PI * Midi(ch[0] - 12) * vib * (i / (float)sr)) * 1.2f;
                    lpL += (xl - lpL) * 0.035f; lpR += (xr - lpR) * 0.035f;
                    padL[i] += (lpL * 0.07f + cello * 0.08f) * env;
                    padR[i] += (lpR * 0.07f + cello * 0.08f) * env;
                }
            }

            // Зал: реверберация фортепиано и струнных
            var L = new float[n]; var R = new float[n];
            for (int i = 0; i < n; i++) { L[i] = mono[i] * 0.8f + padL[i]; R[i] = mono[i] * 0.8f + padR[i]; }
            var mix = new float[n];
            for (int i = 0; i < n; i++) mix[i] = mono[i] + (padL[i] + padR[i]) * 0.5f;
            Reverb(mix, L, 0.28f, 0.9f, 0);
            Reverb(mix, R, 0.28f, 0.9f, 23);

            // Петля: хвост поверх начала
            int loopN = (int)(total * sr), tailN = n - loopN;
            for (int i = 0; i < tailN; i++) { L[i] += L[loopN + i]; R[i] += R[loopN + i]; }
            float peak = 1e-4f;
            for (int i = 0; i < loopN; i++) peak = Mathf.Max(peak, Mathf.Max(Mathf.Abs(L[i]), Mathf.Abs(R[i])));
            var data = new float[loopN * 2];
            for (int i = 0; i < loopN; i++) { data[i * 2] = L[i] / peak * 0.8f; data[i * 2 + 1] = R[i] / peak * 0.8f; }
            var c = AudioClip.Create(key, loopN, 2, sr, false);
            c.SetData(data, 0);
            return _cache[key] = c;
        }
    }
}
