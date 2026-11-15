using System;
using Frontline.Bots;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Frontline
{

    /// <summary>
    /// Оба режима идут строго по времени: пока идёт таймер — идёт бой, досрочно матч не заканчивается.
    /// Score[] — убийства команды (индекс = (byte)Faction). Когда таймер вышел, побеждает команда с бо́льшим числом
    /// убийств; поровну — ничья. Уничтоженный вражеский танк засчитывается как убийство.
    /// </summary>
    public abstract class GameMode
    {
        public readonly int[] Score = new int[2];       // убийства команды
        public int StartTickets { get; protected set; } // не используется (совместимость HUD)
        public event Action<int, int> TicketsLost;       // совместимость: (команда, очки) — теперь «команда набрала»
        public float TimeLeft;
        public bool Over;
        public Faction? Winner;
        protected MatchConfig Cfg;

        protected GameMode(MatchConfig cfg) { Cfg = cfg; TimeLeft = Mathf.Max(1f, cfg.timeLimitMinutes) * 60f; }

        public virtual void Tick(float dt)
        {
            if (Over) return;
            TimeLeft -= dt;
            if (TimeLeft <= 0) Finish(Score[0] == Score[1] ? (Faction?)null : Score[0] > Score[1] ? Faction.Germany : Faction.USSR);
        }

        /// <summary>Убийство: очко команде убийцы. Самоубийство и огонь по своим очков не дают.</summary>
        public virtual void OnKill(in KillEvent k)
        {
            if (Over) return;
            int victimTeam = k.victimTeam;
            int killerTeam = TeamOf(k.killerActorId);
            if (killerTeam == victimTeam || k.killerActorId == k.victimActorId) return;
            AddKill(1 - victimTeam, 1);
        }

        /// <summary>Уничтоженный вражеский танк — одно убийство (джип — тоже: экипаж погиб).</summary>
        public virtual void OnVehicleLost(int team) => AddKill(1 - team, 1);

        protected void AddKill(int team, int amount)
        {
            if (Over || amount <= 0 || team < 0 || team > 1) return;
            Score[team] += amount;
            TicketsLost?.Invoke(team, amount);
        }

        static int TeamOf(int actorId)
        {
            if (actorId >= 0 && actorId < CombatWorld.Actors.Length) return CombatWorld.Actors[actorId].team;
            if (actorId <= -10) { int ti = -10 - actorId; foreach (var t in Tank.All) if (t && t.index == ti) return t.Team; }
            return -1;   // неизвестный источник (взрыв обломков и т. п.) — очко противнику погибшего
        }

        protected void Finish(Faction? winner) { Over = true; Winner = winner; TimeLeft = Mathf.Max(0, TimeLeft); }

        public abstract string Describe();
    }

    /// <summary>
    /// «Захват» (Territory Control): 5 точек. Захват — когда в радиусе больше бойцов одной команды (скорость ∝ перевесу,
    /// максимум ×3). Точки дают места высадки и цели наступления; победу решают убийства к концу таймера.
    /// </summary>
    public class TerritoryControlMode : GameMode
    {
        readonly CapturePoint[] _points;
        public const float CaptureSeconds = 10f;     // один боец, нейтральная → своя
        public const int PointsPerTickPerPoint = 1;

        public TerritoryControlMode(MatchConfig cfg, CapturePoint[] points) : base(cfg)
        {
            _points = points;
        }

        public override void Tick(float dt)
        {
            if (Over) return;
            var data = WorldStores.CapturePoints;
            var actors = CombatWorld.Actors;

            for (int i = 0; i < data.Length; i++)
            {
                var d = data[i];
                int g = 0, s = 0;
                float r2 = d.radius * d.radius;
                for (int a = 0; a < actors.Length; a++)
                {
                    var act = actors[a];
                    if (!act.alive || math.distancesq(act.position, d.position) > r2) continue;
                    if (act.team == 0) g++; else s++;
                }
                // Техника с экипажем захватывает точки наравне с пехотой (танк — как два бойца, джип — как один)
                foreach (var tk in Tank.All)
                {
                    if (!tk || !tk.Alive || tk.crew == Tank.Crew.None) continue;
                    Vector3 tp = tk.transform.position;
                    if (math.distancesq((float3)tp, d.position) > r2 * 1.3f) continue;
                    int w = tk.IsLight ? 1 : 2;
                    if (tk.Team == 0) g += w; else s += w;
                }
                d.contested = (byte)(g > 0 && s > 0 ? 1 : 0);
                int diff = s - g;                       // + к СССР, - к Германии
                if (diff != 0)
                {
                    float rate = Mathf.Min(3, Mathf.Abs(diff)) / CaptureSeconds * Mathf.Sign(diff);
                    d.progress = Mathf.Clamp(d.progress + rate * dt, -1f, 1f);
                }
                if (d.progress >= 1f) d.owner = 1;
                else if (d.progress <= -1f) d.owner = 0;
                else if ((d.owner == 1 && d.progress <= 0) || (d.owner == 0 && d.progress >= 0)) d.owner = 255; // нейтрализована
                data[i] = d;
                if (i < _points.Length && _points[i]) _points[i].Visualize(d);
            }

            base.Tick(dt);
        }

        public override string Describe() => "Захват точек · по убийствам";
    }

    /// <summary>Командный бой: только на время, побеждает команда с бо́льшим числом убийств к концу таймера.</summary>
    public class TeamDeathmatchMode : GameMode
    {
        public TeamDeathmatchMode(MatchConfig cfg) : base(cfg) { }

        public override string Describe() => "Командный бой · по убийствам";
    }
}
