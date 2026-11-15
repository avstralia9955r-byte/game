using UnityEngine;

namespace Frontline
{
    /// <summary>Фон: ветер (зимой резче), далёкий бой за горизонтом, птицы летом днём.</summary>
    public class AmbientAudio : MonoBehaviour
    {
        public bool winter, day = true;
        AudioSource _wind, _rumble, _birds;
        float _nextBird, _nextArty;
        AudioSource _arty;

        void Start()
        {
            _wind = Loop(SoundSynth.Wind(winter), winter ? 0.35f : 0.22f);
            _rumble = Loop(SoundSynth.BattleRumble(), 0.25f);
            _birds = gameObject.AddComponent<AudioSource>(); _birds.spatialBlend = 0; _birds.volume = 0.25f;
            _nextBird = Time.time + 3f;
            _arty = gameObject.AddComponent<AudioSource>(); _arty.spatialBlend = 0; _arty.volume = 0.5f;
            _nextArty = Time.time + Random.Range(10f, 25f);
            if (!day) Loop(SoundSynth.Crickets(), winter ? 0f : 0.18f);
        }

        AudioSource Loop(AudioClip c, float vol)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.clip = c; s.loop = true; s.volume = vol; s.spatialBlend = 0; s.Play();
            return s;
        }

        void Update()
        {
            if (Time.time >= _nextArty)
            {
                _nextArty = Time.time + Random.Range(14f, 40f);
                _arty.panStereo = Random.Range(-0.8f, 0.8f);
                _arty.PlayOneShot(SoundSynth.ArtilleryFar(Random.Range(0, 3)), Random.Range(0.4f, 0.9f));
            }
            if (winter || !day || Time.time < _nextBird) return;
            _nextBird = Time.time + Random.Range(5f, 14f);
            _birds.pitch = Random.Range(0.85f, 1.2f);
            _birds.PlayOneShot(SoundSynth.Birds(), Random.Range(0.4f, 1f));
        }
    }
}
