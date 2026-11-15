using UnityEngine;

namespace Frontline
{
    /// <summary>
    /// Ручная граната (M24 / РГД-33): бросок на G, запал 3.5 с. Урон акторам с затуханием и проверкой укрытия,
    /// разрушение кирпичных стен, громкий шум для ботов (радиус 250 м), взрыв + дым из пула эффектов.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Grenade : MonoBehaviour
    {
        public float fuse = 3.5f, radius = 7f, damage = 140f, brickDamage = 160f, noiseRadius = 250f, armorDamage = 70f;
        public AudioClip explosionSound;

        int _thrower; byte _team; float _t;

        public void Launch(Vector3 velocity, int throwerActorId, byte team)
        {
            _thrower = throwerActorId; _team = team; _t = fuse;
            var rb = GetComponent<Rigidbody>();
            rb.linearVelocity = velocity;
            rb.angularVelocity = Random.insideUnitSphere * 8f;
        }

        void Update()
        {
            _t -= Time.deltaTime;
            if (_t <= 0) Explode();
        }

        void Explode()
        {
            Vector3 c = transform.position + Vector3.up * 0.2f;
            Explosions.Detonate(c, radius, damage, armorDamage, _thrower, _team, Explosions.Kind.Grenade, true, brickDamage);
            Destroy(gameObject);
        }
    }

}
