using UnityEngine;

namespace Frontline
{
    /// <summary>
    /// Звуки мира в 3D: выстрелы ботов и танков, попадания, рикошеты, свист пуль.
    ///
    /// Два раздельных пула голосов:
    ///   • «выстрелы» — только звуки, которые рождаются в точке выстрела (дульный хлопок, далёкий бой);
    ///   • «эффекты» — попадания, рикошеты, свист, обломки.
    /// Каждый голос играет РОВНО ОДИН клип через clip + Play() (не PlayOneShot). Раньше голос с ещё звучащим
    /// хвостом выстрела (PlayOneShot нельзя остановить, а isPlaying для него всегда false) переиспользовался для
    /// звука попадания и переносился в точку попадания — и выстрел «звучал» там, куда прилетела пуля.
    /// Теперь: занятый голос не трогается; если свободных нет — замещается самый тихий для слушателя (дальний и
    /// старый), и только если новый звук громче; иначе новый звук просто не играет.
    /// </summary>
    public class BotAudio : MonoBehaviour
    {
        static BotAudio I;

        [SerializeField] AudioClip[] germanyShots, ussrShots, distantShots, suppressedShots;
        [SerializeField] int voices = 56;
        [SerializeField] float farDistance = 170f, maxDistance = 750f;
        [SerializeField] UnityEngine.Audio.AudioMixerGroup sfxGroup;

        class Pool
        {
            public AudioSource[] src;
            public AudioLowPassFilter[] lp;
            public float[] endTime, startTime, gain;
        }

        Pool _shots, _fx;
        Transform _listener;
        static AudioClip[] _whiz;

        void Awake()
        {
            I = this;
            if (Application.isMobilePlatform) voices = Mathf.Min(voices, 28);   // телефон: меньше одновременных 3D-голосов
            int fxVoices = Mathf.Max(12, voices / 3);
            _shots = MakePool("ShotVoice", Mathf.Max(16, voices - fxVoices));
            foreach (var a in _shots.src) a.minDistance = 8f;   // выстрелы вдвое громче на любой дистанции (лог. затухание ∝ min/d)
            _fx = MakePool("FxVoice", fxVoices);
        }

        Pool MakePool(string name, int n)
        {
            var p = new Pool { src = new AudioSource[n], lp = new AudioLowPassFilter[n], endTime = new float[n], startTime = new float[n], gain = new float[n] };
            for (int i = 0; i < n; i++)
            {
                var go = new GameObject(name + i);
                go.transform.SetParent(transform);
                var a = go.AddComponent<AudioSource>();
                a.spatialBlend = 1f; a.rolloffMode = AudioRolloffMode.Logarithmic;
                a.minDistance = 4f; a.maxDistance = maxDistance; a.dopplerLevel = 0; a.playOnAwake = false; a.loop = false;
                a.outputAudioMixerGroup = sfxGroup;
                a.spread = 40f;
                p.lp[i] = go.AddComponent<AudioLowPassFilter>();
                p.src[i] = a;
            }
            return p;
        }

        float _limiterCheck;
        void LateUpdate()
        {
            if (Time.unscaledTime < _limiterCheck) return;
            _limiterCheck = Time.unscaledTime + 1f;
            AudioLimiter.Ensure();
            if (_listener && !_listener.gameObject.activeInHierarchy) _listener = null;
            Tank.AudioBudget(ListenerPos());
        }

        Vector3 ListenerPos()
        {
            if (!_listener)
            {
                // Слушатель — активная камера игрока (обзорная камера высадки удаляется после первой высадки)
                foreach (var l in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
                    if (l.isActiveAndEnabled) { _listener = l.transform; break; }
            }
            return _listener ? _listener.position : Vector3.zero;
        }

        /// <summary>Оценка громкости у слушателя (логарифмический спад) — для приоритета голосов.</summary>
        float Audible(Vector3 pos, float vol)
        {
            float d = Vector3.Distance(pos, ListenerPos());
            return vol * 4f / Mathf.Max(4f, d);
        }

        /// <summary>
        /// Голос для нового звука или −1, если все голоса заняты более громкими звуками.
        /// Занятый голос перед повторным использованием останавливается (Stop работает, т.к. играем через Play).
        /// </summary>
        static int Voice(Pool p, float length, float gain)
        {
            float now = Time.time;
            int pick = -1; float weakest = float.MaxValue;
            for (int i = 0; i < p.src.Length; i++)
            {
                if (now >= p.endTime[i] && !p.src[i].isPlaying) { pick = i; weakest = -1; break; }
                // Текущая громкость затухающего звука: чем дольше звучит, тем тише хвост
                float age = now - p.startTime[i];
                float cur = p.gain[i] / (1f + age * 3f);
                if (cur < weakest) { weakest = cur; pick = i; }
            }
            if (pick < 0) return -1;
            if (weakest >= 0 && weakest > gain) return -1;     // все звучащие громче — новый пропускаем
            var s = p.src[pick];
            s.Stop();
            p.startTime[pick] = now; p.endTime[pick] = now + length + 0.05f; p.gain[pick] = gain;
            return pick;
        }

        static void Play(Pool p, int idx, AudioClip clip, Vector3 pos, float vol, float pitch, float cutoff)
        {
            // Чем больше одновременно звучит голосов пула, тем тише каждый новый — сумма не «рвётся»
            float now = Time.time; int active = 0;
            for (int i = 0; i < p.src.Length; i++) if (now < p.endTime[i]) active++;
            vol *= 1f / Mathf.Sqrt(Mathf.Max(1f, active / 6f));
            var s = p.src[idx];
            s.transform.position = pos;
            s.clip = clip; s.volume = vol; s.pitch = pitch;
            p.lp[idx].cutoffFrequency = cutoff; p.lp[idx].lowpassResonanceQ = 1f;
            s.Play();
        }

        /// <summary>Разовый 3D-звук эффекта (попадания, рикошеты, обломки, люки) — в отдельном пуле.</summary>
        public static void PlayAt(AudioClip clip, Vector3 pos, float vol = 1f, float pitchVar = 0.08f)
        {
            if (!I || !clip) return;
            int idx = Voice(I._fx, clip.length, I.Audible(pos, vol));
            if (idx < 0) return;
            Play(I._fx, idx, clip, pos, vol, Random.Range(1 - pitchVar, 1 + pitchVar), 22000f);
        }

        /// <summary>Звук, рождающийся в точке выстрела (пушка танка, пулемёт, ПТРД, фаустпатрон) — в пуле выстрелов.</summary>
        public static void ShotAt(AudioClip clip, Vector3 pos, float vol = 1f, float pitchVar = 0.05f)
        {
            if (!I || !clip) return;
            float d = Vector3.Distance(pos, I.ListenerPos());
            int idx = Voice(I._shots, clip.length, I.Audible(pos, vol));
            if (idx < 0) return;
            Play(I._shots, idx, clip, pos, vol, Random.Range(1 - pitchVar, 1 + pitchVar),
                 Mathf.Lerp(22000f, 1200f, Mathf.Pow(Mathf.Clamp01(d / I.maxDistance), 0.6f)));
        }

        /// <summary>Свист пули рядом с игроком (пролёт без попадания).</summary>
        public static void Whiz(Vector3 pos)
        {
            if (!I) return;
            _whiz ??= new[] { SoundSynth.Whiz(0), SoundSynth.Whiz(1), SoundSynth.Whiz(2) };
            var clip = _whiz[Random.Range(0, _whiz.Length)];
            int idx = Voice(I._fx, clip.length, 1f);
            if (idx < 0) return;
            Play(I._fx, idx, clip, pos, 1f, Random.Range(0.9f, 1.15f), 22000f);
        }

        /// <summary>Выстрел бота: звук всегда в точке выстрела (дуле), никогда — в точке попадания.</summary>
        public static void Shot(Vector3 pos, byte team, bool suppressed)
        {
            if (!I) return;
            float d = Vector3.Distance(pos, I.ListenerPos());
            if (d > I.maxDistance) return;

            AudioClip[] set = suppressed ? I.suppressedShots
                            : d > I.farDistance ? I.distantShots
                            : team == (byte)Faction.Germany ? I.germanyShots : I.ussrShots;
            if (set == null || set.Length == 0) return;
            var clip = set[Random.Range(0, set.Length)];
            if (!clip) return;

            float vol = suppressed ? 0.3f : 1f;
            int idx = Voice(I._shots, clip.length, I.Audible(pos, vol));
            if (idx < 0) return;
            // Воздух съедает верха: 22 кГц вблизи → ~1.2 кГц на 450 м; средняя дистанция — тот же выстрел, но глуше
            Play(I._shots, idx, clip, pos, vol, Random.Range(0.94f, 1.06f) * (d > I.farDistance ? 0.92f : 1f),
                 Mathf.Lerp(22000f, 1200f, Mathf.Pow(Mathf.Clamp01(d / I.maxDistance), 0.6f)));
        }
    }
}
