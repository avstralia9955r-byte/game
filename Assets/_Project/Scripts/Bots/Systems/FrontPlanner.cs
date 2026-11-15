using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;

namespace Frontline.Bots
{
    /// <summary>Строй армий: широкая сплошная полоса или отдельные полки-колонны (выбирается в меню паузы).</summary>
    public enum FrontStyle : byte { BroadLine = 0, Regiments = 1 }

    /// <summary>
    /// Планировщик фронта (главный поток, раз в 0,3 с, O(N log N) на 512 ботов — доли миллисекунды).
    /// Для каждой стороны: ось наступления «своя база → база врага», линия фронта — среднее продвижение бойцов,
    /// не отставших от неё более чем на 80 м. Бойцы раскладываются по ширине фронта в порядке их текущего бокового
    /// положения (чтобы никто не перебегал через весь строй).
    /// • Широкая полоса: у каждого своё место в сплошной линии (интервал 6–9 м), цель — в 28 м впереди линии.
    /// • Полки: сторона делится на 2–6 полков; полк идёт колонной по 4 в ряд в своём коридоре, у каждого полка свой темп.
    /// Результат — точка-цель на бойца: Goals[actorId] = (x, z, отставание от линии, 1).
    /// </summary>
    public static class FrontPlanner
    {
        public static FrontStyle Style = FrontStyle.BroadLine;
        public static NativeArray<float4> Goals;
        static float _next;
        static readonly float[] _progress = new float[2];
        static readonly bool[] _init = new bool[2];
        static readonly List<(float lat, int id, float along)> _team = new();
        static readonly float[] _regProg = new float[16];
        static readonly int[] _regCount = new int[16];

        /// <summary>Принудительные цели «бегом сюда» (actorId → точка), поверх строя: w = 2.</summary>
        public static readonly Dictionary<int, float2> Forced = new();
        static readonly List<int> _released = new();

        public static void Reset() { _init[0] = _init[1] = false; _next = 0; Forced.Clear(); }

        public static void Force(int actorId, float2 p)
        {
            Forced[actorId] = p;
            _next = 0;      // применится на ближайшем шаге планировщика (он пишет Goals в безопасный момент)
        }

        public static void Release(int actorId)
        {
            if (Forced.Remove(actorId)) { _released.Add(actorId); _next = 0; }
        }

        public static void Ensure()
        {
            if (!Goals.IsCreated) { Goals = new NativeArray<float4>(BotRegistry.MaxBots + 8, Allocator.Persistent); UnityEngine.Application.quitting += Dispose; }
        }

        public static void Dispose() { if (Goals.IsCreated) Goals.Dispose(); }

        static float3 Base(NativeArray<float3> s)
        {
            if (!s.IsCreated || s.Length == 0) return float3.zero;
            float3 c = 0; for (int i = 0; i < s.Length; i++) c += s[i];
            return c / s.Length;
        }

        public static void Update(float now)
        {
            Ensure();
            if (now < _next || !CombatWorld.Initialized || !WorldStores.Ready) return;
            _next = now + 0.3f;
            var actors = CombatWorld.Actors;
            float3 bG = Base(WorldStores.SpawnsGermany), bU = Base(WorldStores.SpawnsUSSR);
            for (int team = 0; team < 2; team++)
            {
                float3 own = team == 0 ? bG : bU, enemy = team == 0 ? bU : bG;
                float2 f = math.normalizesafe((enemy - own).xz, new float2(0, 1)), side = new float2(-f.y, f.x);
                float axisLen = math.length((enemy - own).xz);
                _team.Clear();
                for (int i = 1; i < actors.Length; i++)
                {
                    var a = actors[i];
                    if (!a.alive || a.team != team || a.actorId >= Goals.Length || Net.NetSession.IsRemoteSlot(a.actorId)) continue;
                    float2 rel = a.position.xz - own.xz;
                    _team.Add((math.dot(rel, side), a.actorId, math.dot(rel, f)));
                }
                int n = _team.Count;
                if (n == 0) continue;
                // Линия фронта: среднее продвижение тех, кто не отстал (свежие с базы её не тянут назад)
                float prev = _init[team] ? _progress[team] : 0f;
                float sum = 0; int cnt = 0;
                foreach (var b in _team) if (b.along > prev - 80f) { sum += b.along; cnt++; }
                float prog = cnt > 0 ? sum / cnt : prev;
                prog = math.clamp(prog, 30f, axisLen - 60f);
                _progress[team] = _init[team] ? math.lerp(prev, prog, 0.5f) : prog;
                _init[team] = true;

                _team.Sort((x, y) => x.lat.CompareTo(y.lat));
                float width = math.clamp(n * (Style == FrontStyle.BroadLine ? 8f : 11f), 120f, 660f);
                float lim = MapGenerator.Zone - 25f;

                if (Style == FrontStyle.BroadLine)
                {
                    for (int r = 0; r < n; r++)
                    {
                        var b = _team[r];
                        float u = (r + 0.5f) / n - 0.5f;
                        float wob = math.sin(b.id * 1.7f) * 2.5f;                          // «рваная» линия
                        float2 g = own.xz + f * (_progress[team] + 28f + math.sin(b.id * 0.9f) * 4f) + side * (u * width + wob);
                        g = math.clamp(g, -lim, lim);
                        Goals[b.id] = new float4(g.x, g.y, _progress[team] - b.along, 1f);
                    }
                }
                else
                {
                    int R = math.clamp(n / 36 + 1, 2, 6);
                    for (int k = 0; k < R; k++) { _regProg[k] = 0; _regCount[k] = 0; }
                    for (int r = 0; r < n; r++)
                    {
                        int reg = math.min(R - 1, r * R / n);
                        if (_team[r].along > _progress[team] - 100f) { _regProg[reg] += _team[r].along; _regCount[reg]++; }
                    }
                    for (int k = 0; k < R; k++)
                        _regProg[k] = _regCount[k] > 0 ? math.clamp(_regProg[k] / _regCount[k], 30f, axisLen - 60f) : _progress[team];
                    for (int r = 0; r < n; r++)
                    {
                        var b = _team[r];
                        int reg = math.min(R - 1, r * R / n);
                        int first = (reg * n + R - 1) / R, j = r - first;                // место внутри полка
                        float center = ((reg + 0.5f) / R - 0.5f) * width;
                        float lat = center + ((j % 4) - 1.5f) * 4.5f;
                        float depth = (j / 4) * 3.5f;
                        float2 g = own.xz + f * (_regProg[reg] + 24f - depth) + side * lat;
                        g = math.clamp(g, -lim, lim);
                        Goals[b.id] = new float4(g.x, g.y, _regProg[reg] - depth - b.along, 1f);
                    }
                }
            }
            foreach (int id in _released) if (id >= 0 && id < Goals.Length && !Forced.ContainsKey(id)) Goals[id] = new float4(Goals[id].x, Goals[id].y, 0, 0f);
            _released.Clear();
            foreach (var kv in Forced) if (kv.Key >= 0 && kv.Key < Goals.Length) Goals[kv.Key] = new float4(kv.Value.x, kv.Value.y, 0, 2f);
        }
    }
}
