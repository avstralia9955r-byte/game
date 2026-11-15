using System.Collections.Generic;
using UnityEngine;

namespace Frontline
{
    /// <summary>Один кирпич. Префаб: MeshRenderer (общий материал, SRP Batcher/GPU Instancing) + BoxCollider, слой "Bricks".</summary>
    public class Brick : MonoBehaviour
    {
        public float maxHealth = 100f;
        public float Health { get; private set; }
        public BrickWall Wall { get; set; }
        public Vector2Int Cell { get; set; }

        void OnEnable() => Health = maxHealth;

        public void Damage(float amount)
        {
            if (Health <= 0) return;
            Health -= amount;
            if (Health <= 0) Wall.RemoveBrick(this, true);
        }
    }
}
