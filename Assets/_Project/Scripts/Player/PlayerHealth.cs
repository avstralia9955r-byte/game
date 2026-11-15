using System;
using UnityEngine;

namespace Frontline
{
    public class PlayerHealth : MonoBehaviour
    {
        public float maxHealth = 50f;     // вдвое меньше прежнего — бои быстрее
        public float regenDelay = 6f, regenPerSecond = 6f;

        public float Health { get; private set; }
        public bool Alive => Health > 0;
        /// <summary>Тяжело ранен (меньше 10 HP): лежит и ползёт, промахивается, сердце колотится. Поднимает аптечка.</summary>
        public bool Downed => Alive && Health < FieldTools.DownedHp;

        /// <summary>Аптечка: +amount HP.</summary>
        public void Heal(float amount) { if (Alive) Health = Mathf.Min(maxHealth, Health + amount); }
        float _regenT;
        /// <summary>В танке пули и осколки достаются броне; гибель экипажа — отдельным событием при уничтожении танка.</summary>
        public bool Invulnerable { get; set; }
        /// <summary>Момент гибели и направление, откуда пришёл смертельный урон (камера падает в обратную сторону).</summary>
        public float DeathTime { get; private set; } = -999f;
        public Vector3 DeathDir { get; private set; }

        /// <summary>Мировое направление на источник урона — для HUD-индикатора.</summary>
        public event Action<Vector3, float> Damaged;
        public event Action<int> Died;     // actorId убийцы

        float _lastHit;
        PlayerController _pc;

        void Awake() { _pc = GetComponent<PlayerController>(); Health = maxHealth; }

        /// <summary>Неуязвимость после появления (10 с).</summary>
        public float ProtectedUntil { get; private set; }
        public bool SpawnProtected => Time.time < ProtectedUntil;

        public void ResetHealth() { Health = maxHealth; enabled = true; ProtectedUntil = Time.time + 10f; }

        public void ApplyDamage(in DamageEvent e)
        {
            if (!Alive || ((Invulnerable || SpawnProtected) && e.amount < 9000f)) return;
            float dmg = e.amount; // множитель головы уже учтён стрелком
            Health = Mathf.Max(0, Health - dmg);
            _lastHit = Time.time;
            Vector3 fromDir = -(Vector3)e.direction;
            Damaged?.Invoke(fromDir, dmg);
            if (Health <= 0)
            {
                DeathTime = Time.time;
                DeathDir = fromDir;
                _pc.PublishSnapshot(false);
                AudioDuck.Duck(3.5f);
                Died?.Invoke(e.sourceActorId);
            }
        }

        /// <summary>Сетевая игра (клиент): здоровье считает хост — подтягиваем восстановление.</summary>
        public void SyncFromHost(float hp) { if (Alive) Health = Mathf.Clamp(hp, 0.01f, maxHealth); }

        void Update()
        {
            if (Net.NetSession.IsClient) return;   // у клиента восстановление идёт на хосте
            // Своё восстановление — медленное: 1 HP раз в 10 секунд (быстро поднимает только аптечка)
            if (Alive && Health < maxHealth && (_regenT += Time.deltaTime) >= 10f) { _regenT = 0f; Health = Mathf.Min(maxHealth, Health + 1f); }
        }
    }
}
