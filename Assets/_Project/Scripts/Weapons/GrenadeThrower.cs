using UnityEngine;

namespace Frontline
{
    /// <summary>Бросок гранаты игроком: G, лимит по лоадауту класса.</summary>
    public class GrenadeThrower : MonoBehaviour
    {
        public PlayerController player;
        public Grenade grenadePrefab;
        public float throwSpeed = 16f, cooldown = 1.2f;
        public int Count { get; private set; }
        public event System.Action Thrown;
        float _next;

        public int Max { get; private set; }
        public void Refill(int count) { Count = count; Max = Mathf.Max(Max, count); }
        public void RefillToMax() => Count = Max;

        void Update()
        {
            if (!player.InputEnabled || Count <= 0 || Time.time < _next) return;
            if (!player.Input.grenadePressed) return;
            _next = Time.time + cooldown; Count--;
            Thrown?.Invoke();
            var cam = player.worldCamera.transform;
            var g = Instantiate(grenadePrefab, cam.position + cam.forward * 0.6f, Random.rotation);
            g.Launch(cam.forward * throwSpeed + Vector3.up * 3f + (Vector3)player.GetComponent<CharacterController>().velocity, CombatWorld.PlayerActorId, player.Team);
        }
    }
}
