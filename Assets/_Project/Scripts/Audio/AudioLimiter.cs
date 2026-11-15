using UnityEngine;

namespace Frontline
{
    /// <summary>
    /// Общий ограничитель громкости на слушателе. Когда одновременно звучат десятки выстрелов и взрывов, их сумма
    /// выходит за 1 и звук «рвётся» (жёсткий клиппинг). Ограничитель плавно убирает усиление на пиках
    /// (быстрая атака, медленное восстановление) и мягко скругляет остаток — громко, но без треска.
    /// Плюс при запуске увеличивается число одновременно звучащих голосов.
    /// </summary>
    public class AudioLimiter : MonoBehaviour
    {
        float _env, _gain = 1f;
        const float Threshold = 0.72f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            try
            {
                var cfg = AudioSettings.GetConfiguration();
                int want = Application.isMobilePlatform ? 48 : 96;
                if (cfg.numRealVoices < want) { cfg.numRealVoices = want; cfg.numVirtualVoices = Mathf.Max(cfg.numVirtualVoices, 512); AudioSettings.Reset(cfg); }
            }
            catch { }
        }

        /// <summary>Повесить ограничитель на активный слушатель (вызывается периодически — слушатель меняется).</summary>
        public static void Ensure()
        {
            var l = Object.FindAnyObjectByType<AudioListener>();
            if (l && !l.GetComponent<AudioLimiter>()) l.gameObject.AddComponent<AudioLimiter>();
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            float env = _env, gain = _gain;
            for (int i = 0; i < data.Length; i += channels)
            {
                float peak = 0f;
                for (int c = 0; c < channels; c++) { float a = data[i + c]; if (a < 0) a = -a; if (a > peak) peak = a; }
                env = peak > env ? peak : env * 0.9997f;                      // атака мгновенная, спад ~0,1 с
                float target = env > Threshold ? Threshold / env : 1f;
                gain = target < gain ? target : gain + (target - gain) * 0.0005f;   // восстановление плавное
                for (int c = 0; c < channels; c++)
                {
                    float x = data[i + c] * gain;
                    data[i + c] = x / (1f + (x < 0 ? -x : x) * 0.25f);           // мягкое скругление
                }
            }
            _env = env; _gain = gain;
        }
    }
}
