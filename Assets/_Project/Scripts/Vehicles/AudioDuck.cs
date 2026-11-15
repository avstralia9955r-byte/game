using UnityEngine;

namespace Frontline
{
    /// <summary>Глушение мира после близкого взрыва (общая громкость плавно возвращается).</summary>
    public class AudioDuck : MonoBehaviour
    {
        static AudioDuck _i;
        float _until, _depth;

        public static void Duck(float seconds)
        {
            if (!_i) { var go = new GameObject("[AudioDuck]"); DontDestroyOnLoad(go); _i = go.AddComponent<AudioDuck>(); }
            _i._until = Mathf.Max(_i._until, Time.unscaledTime + seconds);
            _i._depth = 0.75f;
        }

        void Update()
        {
            float master = SaveSystem.Settings != null ? SaveSystem.Settings.masterVolume : 1f;
            float left = _until - Time.unscaledTime;
            float k = left > 0 ? Mathf.Lerp(1f, 1f - _depth, Mathf.Clamp01(left / 0.8f)) : 1f;
            AudioListener.volume = master * k;
        }
    }
}
