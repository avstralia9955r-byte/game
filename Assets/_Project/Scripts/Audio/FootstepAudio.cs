using UnityEngine;

namespace Frontline
{
    /// <summary>
    /// Звуки игрока: шаги (трава/снег, громкость от бега/приседа), приземление.
    /// PlayerController шлёт сообщение OnFootstep на каждый шаг.
    /// </summary>
    public class FootstepAudio : MonoBehaviour
    {
        public PlayerController player;
        public AudioSource source;
        public bool snow;
        AudioClip _land, _jump;
        bool _wasGrounded = true;
        PlayerHealth _hp;
        float _heartbeat;

        void Start()
        {
            _land = SoundSynth.Land(); _jump = SoundSynth.Jump();
            _hp = GetComponent<PlayerHealth>();
            if (_hp) _hp.Damaged += OnHurt;
        }

        void OnDestroy() { if (_hp) _hp.Damaged -= OnHurt; }

        void OnHurt(Vector3 dir, float dmg)
        {
            if (source) source.PlayOneShot(SoundSynth.Impact(SoundSynth.ImpactKind.Flesh, Random.Range(0, 2)), Mathf.Clamp01(dmg / 40f) * 0.9f + 0.2f);
        }

        SoundSynth.Surface Ground()
        {
            var o = player.transform.position + Vector3.up * 0.3f;
            return Physics.Raycast(o, Vector3.down, out var hit, 1.2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                ? SurfaceUtil.Step(hit.collider) : (snow ? SoundSynth.Surface.Snow : SoundSynth.Surface.Grass);
        }

        void OnFootstep()
        {
            if (!source) return;
            float vol = player.IsSprinting ? 0.55f : player.IsCrouched ? 0.15f : 0.35f;
            source.pitch = Random.Range(0.9f, 1.1f);
            source.PlayOneShot(SoundSynth.Footstep(Ground(), Random.Range(0, 4)), vol);
        }

        void Update()
        {
            if (!player || !source) return;
            if (!_wasGrounded && player.IsGrounded) source.PlayOneShot(_land, 0.5f);
            if (_wasGrounded && !player.IsGrounded && player.Input.jumpPressed) source.PlayOneShot(_jump, 0.4f);
            _wasGrounded = player.IsGrounded;

            // Сердцебиение при низком здоровье
            if (_hp && _hp.Alive && _hp.Health < 18f)
            {
                _heartbeat -= Time.deltaTime;
                if (_heartbeat <= 0) { _heartbeat = Mathf.Lerp(0.55f, 0.9f, _hp.Health / 18f); source.PlayOneShot(SoundSynth.Heartbeat(), 0.55f); }
            }
        }
    }
}
