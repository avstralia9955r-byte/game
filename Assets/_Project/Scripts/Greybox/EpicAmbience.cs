using Frontline.Bots;
using UnityEngine;

namespace Frontline
{
    /// <summary>
    /// «Большая война вокруг»: каждые 6–14 с где-то в 150–380 м от игрока ложится дальний артналёт (2–4 разрыва
    /// без урона, вдали от бойцов), над полем иногда взлетают осветительные ракеты. Дёшево: эффекты из пула.
    /// </summary>
    public class EpicAmbience : MonoBehaviour
    {
        float _next = 8f, _flareNext = 20f;
        int _left; float _salvoT; Vector3 _salvoC;

        void Update()
        {
            var pc = PlayerBridge.Controller;
            if (!pc || !MatchBridge.MatchRunning) return;
            float t = Time.time;
            if (_left > 0 && t >= _salvoT)
            {
                _left--; _salvoT = t + Random.Range(0.25f, 0.9f);
                Vector2 o = Random.insideUnitCircle * 22f;
                Vector3 p = _salvoC + new Vector3(o.x, 0, o.y);
                p.y = GameModeManager.GroundAt(p).y;
                if (Clear(p, 45f)) Explosions.VisualOnly(p, 9f, Random.value < 0.35f ? Explosions.Kind.Artillery : Explosions.Kind.Shell);
            }
            if (t >= _next)
            {
                _next = t + Random.Range(6f, 14f);
                Vector2 d = Random.insideUnitCircle.normalized * Random.Range(150f, 380f);
                Vector3 c = pc.transform.position + new Vector3(d.x, 0, d.y);
                float lim = MapGenerator.Zone - 20f;
                c.x = Mathf.Clamp(c.x, -lim, lim); c.z = Mathf.Clamp(c.z, -lim, lim);
                if (Vector3.Distance(c, pc.transform.position) > 120f) { _salvoC = c; _left = Random.Range(2, 5); _salvoT = t; }
            }
            if (t >= _flareNext && FxPool.I)
            {
                _flareNext = t + Random.Range(18f, 40f);
                Vector2 d = Random.insideUnitCircle * 300f;
                FxPool.I.MuzzleFlashAt(new Vector3(d.x, GameModeManager.GroundAt(new Vector3(d.x, 0, d.y)).y + 60f, d.y), Vector3.down);
            }
        }

        /// <summary>Рядом с точкой никого нет — разрыв только «для картинки».</summary>
        static bool Clear(Vector3 p, float r)
        {
            if (!CombatWorld.Initialized) return true;
            var a = CombatWorld.Actors;
            for (int i = 0; i < a.Length; i++)
                if (a[i].alive && ((Vector3)a[i].position - p).sqrMagnitude < r * r) return false;
            return true;
        }
    }
}
