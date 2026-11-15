using Frontline.Bots;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace Frontline
{
    /// <summary>
    /// «Засветка»: если свой боец видит вражеского пехотинца, тот 4 секунды отмечается на мини-карте и в 3D-окнах.
    /// Опрос раз в 0,4 с по восприятию ботов (BotPerception.targetVisible).
    /// </summary>
    public static class Spotting
    {
        static float[] _seen = new float[0];
        static float _next;
        static EntityQuery _q;
        static bool _qOk;

        public static bool Spotted(int actorId) => actorId >= 0 && actorId < _seen.Length && Time.time - _seen[actorId] < 4f;

        public static void Mark(int actorId)
        {
            if (actorId < 0) return;
            if (actorId >= _seen.Length) System.Array.Resize(ref _seen, Mathf.Max(actorId + 1, _seen.Length * 2, 64));
            _seen[actorId] = Time.time;
        }

        public static void Tick(byte myTeam)
        {
            if (Time.time < _next) return;
            _next = Time.time + 0.4f;
            var w = World.DefaultGameObjectInjectionWorld;
            if (w == null || !w.IsCreated) return;
            var em = w.EntityManager;
            if (!_qOk) { _q = em.CreateEntityQuery(ComponentType.ReadOnly<BotPerception>(), ComponentType.ReadOnly<BotIdentity>()); _qOk = true; }
            var percs = _q.ToComponentDataArray<BotPerception>(Allocator.Temp);
            var ids = _q.ToComponentDataArray<BotIdentity>(Allocator.Temp);
            for (int i = 0; i < percs.Length; i++)
                if (ids[i].team == myTeam && percs[i].targetVisible && percs[i].targetActorId > 0) Mark(percs[i].targetActorId);
            percs.Dispose(); ids.Dispose();
        }
    }
}
