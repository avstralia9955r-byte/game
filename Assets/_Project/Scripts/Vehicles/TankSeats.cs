using Frontline.Bots;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Frontline
{
    /// <summary>Места в джипе: водитель, стрелок за пулемётом, пассажир со своим оружием.</summary>
    public enum JeepSeat : byte { None, Driver, Gunner, Passenger }

    /// <summary>
    /// Экипаж джипа. Любое место может занять игрок или бот-боец (настоящий ECS-бот, а не «виртуальный» экипаж):
    /// бот на время поездки выключается из ИИ (BotInVehicle), его тело прячется, а вместо него в машине видна фигурка.
    /// Водитель-бот ведёт машину к цели (ThinkAI), стрелок-бот ведёт огонь из пулемёта, пассажир-бот стреляет из
    /// своего оружия. Игрок-пассажир смотрит и стреляет из своего оружия сам. [X] — пересесть на свободное место.
    /// Гибель бойца в машине освобождает место; при уничтожении машины гибнут все, кто внутри.
    /// </summary>
    public partial class Tank
    {
        public JeepSeat PlayerSeat { get; private set; }
        public bool PlayerAboard => PlayerSeat != JeepSeat.None;
        [System.NonSerialized] public int driverBot = -1, gunnerBot = -1, passengerBot = -1;

        Transform _seatGunner, _seatPassenger, _figGunner, _figPassenger;
        float _paxNext, _paxThink, _paxIdle;
        int _paxTarget = -1;
        Vector3 _prevPos;
        /// <summary>На месте пулемётчика стоит второй игрок (сетевой клиент) — место занято (считает хост).</summary>
        [System.NonSerialized] public bool RemoteRider;
        int _buddy = -1; float _buddyUntil, _buddyRetry, _buddyRefresh;
        internal static readonly System.Collections.Generic.HashSet<int> Claimed = new();   // бойцы, уже бегущие к какой-то машине

        void ReleaseBuddy() { if (_buddy >= 0) { FrontPlanner.Release(_buddy); Claimed.Remove(_buddy); } _buddy = -1; }

        /// <summary>Кто сейчас стреляет из пулемёта (для засчитывания убийств).</summary>
        int MgShooterId =>
            PlayerSeat == JeepSeat.Gunner || (crew == Crew.Player && gunnerBot < 0) ? CombatWorld.PlayerActorId
            : gunnerBot >= 0 ? gunnerBot : DriverActorId;

        /// <summary>Ремонт гаечным ключом инженера.</summary>
        public void Repair(float hp) { if (Alive) Health = Mathf.Min(maxHealth, Health + hp); }

        public int BotCount => (driverBot >= 0 ? 1 : 0) + (gunnerBot >= 0 ? 1 : 0) + (passengerBot >= 0 ? 1 : 0);

        void InitSeats()
        {
            _seatGunner = rig.hull.Find("SeatGunner");
            _seatPassenger = rig.hull.Find("SeatPassenger");
            _figGunner = rig.turret ? rig.turret.Find("CrewGunner") : null;
            _figPassenger = rig.hull.Find("CrewPassenger");
            if (_figGunner) _figGunner.gameObject.SetActive(false);
            if (_figPassenger) _figPassenger.gameObject.SetActive(false);
            _prevPos = transform.position;
        }

        public Transform SeatTransform(JeepSeat s) => s switch
        {
            JeepSeat.Gunner => _seatGunner ? _seatGunner : rig.seat,
            JeepSeat.Passenger => _seatPassenger ? _seatPassenger : rig.seat,
            _ => rig.seat
        };

        public bool SeatFree(JeepSeat s) => s switch
        {
            JeepSeat.Driver => crew == Crew.None && driverBot < 0 && PlayerSeat != JeepSeat.Driver,
            JeepSeat.Gunner => IsLight && gunnerBot < 0 && PlayerSeat != JeepSeat.Gunner && !RemoteRider,
            JeepSeat.Passenger => false,                         // в джипе два места: водитель и пулемётчик
            _ => false
        };

        public bool AnySeatFree => SeatFree(JeepSeat.Driver) || SeatFree(JeepSeat.Gunner) || SeatFree(JeepSeat.Passenger);

        public JeepSeat FirstFreeSeat()
        {
            if (SeatFree(JeepSeat.Driver)) return JeepSeat.Driver;
            if (SeatFree(JeepSeat.Gunner)) return JeepSeat.Gunner;
            if (SeatFree(JeepSeat.Passenger)) return JeepSeat.Passenger;
            return JeepSeat.None;
        }

        public static string SeatName(JeepSeat s) => s switch
        {
            JeepSeat.Driver => "водитель", JeepSeat.Gunner => "пулемётчик", JeepSeat.Passenger => "пассажир", _ => ""
        };

        static EntityManager? Em()
        {
            var w = World.DefaultGameObjectInjectionWorld;
            return w != null && w.IsCreated ? w.EntityManager : null;
        }

        static Entity BotEntity(int actorId) =>
            BotRegistry.ActorToEntity.IsCreated && actorId > 0 && actorId < BotRegistry.ActorToEntity.Length ? BotRegistry.ActorToEntity[actorId] : Entity.Null;

        /// <summary>Посадить бота на свободное место (водитель → пулемёт → пассажир). Только у хоста/в одиночной игре.</summary>
        public bool TryBoardBot(int actorId) => TryBoardBotAt(actorId, FirstFreeSeat());

        public bool TryBoardBotAt(int actorId, JeepSeat seat)
        {
            if (!Alive || actorId <= 0 || seat == JeepSeat.None || !SeatFree(seat)) return false;
            if (!IsLight && seat != JeepSeat.Driver) return false;            // в танке бот — один, за рычагами и пушкой
            if (BotCount >= 1) return false;                     // в машину садится только один бот
            var em = Em(); if (em == null) return false;
            var e = BotEntity(actorId);
            if (e == Entity.Null || !em.Value.Exists(e) || VehicleSeats.IsBoarded(em.Value, e)) return false;
            if (em.Value.HasComponent<BotDead>(e) && em.Value.IsComponentEnabled<BotDead>(e)) return false;
            VehicleSeats.Board(em.Value, e, index, (byte)seat);
            switch (seat)
            {
                case JeepSeat.Driver: driverBot = actorId; crew = Crew.Bot; break;
                case JeepSeat.Gunner: gunnerBot = actorId; break;
                default: passengerBot = actorId; _paxIdle = 0; break;
            }
            return true;
        }

        // ---------------- Цикл джипа ----------------

        void JeepControl(float dt)
        {
            UpdateBotSeats(dt);
            var pc = PlayerBridge.Controller;
            if (PlayerAboard && (!pc || pc.Vehicle != this)) { if (PlayerSeat == JeepSeat.Driver && crew == Crew.Player) crew = Crew.None; PlayerSeat = JeepSeat.None; }

            bool playerMg = PlayerSeat == JeepSeat.Gunner || (crew == Crew.Player && gunnerBot < 0);
            float pThr = 0, pSteer = 0; bool pFire = false;
            if (PlayerSeat == JeepSeat.Driver || PlayerSeat == JeepSeat.Gunner)
            {
                PlayerInputUpdate();
                pThr = throttle; pSteer = steer; pFire = fireMg;
                if (!playerMg) Zoomed = false;
            }
            else if (PlayerSeat == JeepSeat.Passenger && pc)
            {
                var inp = pc.Input;
                if (inp.interactPressed && Time.frameCount > _enterFrame + 1) VehicleManager.EjectPlayer(this, false, -1);
                else if (inp.seatPressed && Time.frameCount > _enterFrame + 1) TrySwitchSeat();
            }
            if (!PlayerAboard) Zoomed = false;

            Vector3 savedAim = _aimPoint;
            if (crew == Crew.Bot || gunnerBot >= 0) ThinkAI(dt);
            else { throttle = steer = 0; fireMg = false; }

            if (crew == Crew.Player) { throttle = pThr; steer = pSteer; }
            else if (crew != Crew.Bot) { throttle = steer = 0; }

            if (playerMg) { fireMg = pFire; _aimPoint = savedAim; }
            else if (gunnerBot < 0) { fireMg = false; if (crew != Crew.Bot) _aimPoint = rig.muzzle.position + transform.forward * 60f; }
            fireCannon = false;

            if (passengerBot >= 0) PassengerFire(dt);
            BuddyBoarding();

            // Фигурки экипажа
            bool showG = gunnerBot >= 0, showP = passengerBot >= 0;
            if (_figGunner && _figGunner.gameObject.activeSelf != showG) _figGunner.gameObject.SetActive(showG);
            if (_figPassenger && _figPassenger.gameObject.activeSelf != showP) _figPassenger.gameObject.SetActive(showP);
        }

        /// <summary>Боты на местах: живы ли, держим их тела (для хитбоксов и снимка позиций) на сиденьях.</summary>
        void UpdateBotSeats(float dt)
        {
            Vector3 vel = dt > 0 ? (transform.position - _prevPos) / dt : Vector3.zero;
            _prevPos = transform.position;
            if (driverBot < 0 && gunnerBot < 0 && passengerBot < 0)
            {
                if (crew == Crew.Bot) { crew = Crew.None; EmptySince = Time.time; }
                return;
            }
            var em = Em(); if (em == null) return;
            var m = em.Value;
            Keep(m, ref driverBot, JeepSeat.Driver, vel);
            Keep(m, ref gunnerBot, JeepSeat.Gunner, vel);
            Keep(m, ref passengerBot, JeepSeat.Passenger, vel);
            if (driverBot < 0 && crew == Crew.Bot) { crew = Crew.None; EmptySince = Time.time; }

            // Пассажир выходит, если машина долго стоит (приехали / застряли) — дальше пешком
            if (passengerBot >= 0)
            {
                _paxIdle = vel.magnitude < 0.6f ? _paxIdle + dt : 0f;
                if (_paxIdle > 9f) { DismountBot(m, ref passengerBot, JeepSeat.Passenger); _paxIdle = 0; }
            }
        }

        void Keep(EntityManager m, ref int id, JeepSeat seat, Vector3 vel)
        {
            if (id < 0) return;
            var e = BotEntity(id);
            var actors = CombatWorld.Actors;
            bool alive = id < actors.Length && actors[id].alive;
            if (e == Entity.Null || !m.Exists(e) || !VehicleSeats.IsBoarded(m, e) || !alive)
            {
                if (e != Entity.Null && m.Exists(e)) VehicleSeats.Unboard(m, e);
                id = -1; return;
            }
            var st = SeatTransform(seat);
            if (m.HasComponent<LocalTransform>(e))
            {
                var lt = m.GetComponentData<LocalTransform>(e);
                lt.Position = st.position - Vector3.up * 0.35f;
                lt.Rotation = quaternion.RotateY(math.radians(seat == JeepSeat.Gunner ? transform.eulerAngles.y + _turretYaw : transform.eulerAngles.y));
                m.SetComponentData(e, lt);
            }
            if (m.HasComponent<BotMotion>(e))
            {
                var mo = m.GetComponentData<BotMotion>(e);
                mo.velocity = vel; mo.speed = 0;
                m.SetComponentData(e, mo);
            }
        }

        void DismountBot(EntityManager m, ref int id, JeepSeat seat)
        {
            var e = BotEntity(id);
            id = -1;
            if (e == Entity.Null || !m.Exists(e)) return;
            if (m.HasComponent<LocalTransform>(e))
            {
                var lt = m.GetComponentData<LocalTransform>(e);
                lt.Position = ExitPoint(seat);
                m.SetComponentData(e, lt);
            }
            VehicleSeats.Unboard(m, e);
        }

        Vector3 ExitPoint(JeepSeat seat)
        {
            Vector3 side = seat == JeepSeat.Passenger ? transform.right : seat == JeepSeat.Driver ? -transform.right : -transform.forward;
            Vector3 p = transform.position + side * (!IsLight ? 3.6f : seat == JeepSeat.Gunner ? 3.2f : 2.2f);
            var t = Terrain.activeTerrain;
            if (t) p.y = t.SampleHeight(p) + t.transform.position.y + 0.05f;
            return p;
        }

        /// <summary>Машина уничтожена: все боты внутри погибают (засчитывается уничтожившему).</summary>
        void UnboardAll(int killer)
        {
            var em = Em();
            int[] ids = { driverBot, gunnerBot, passengerBot };
            driverBot = gunnerBot = passengerBot = -1;
            if (_figGunner) _figGunner.gameObject.SetActive(false);
            if (_figPassenger) _figPassenger.gameObject.SetActive(false);
            if (em == null) return;
            for (int k = 0; k < 3; k++)
            {
                int id = ids[k]; if (id < 0) continue;
                var e = BotEntity(id);
                if (e == Entity.Null || !em.Value.Exists(e)) continue;
                var seat = (JeepSeat)(k + 1);
                Vector3 p = ExitPoint(seat);
                if (em.Value.HasComponent<LocalTransform>(e))
                {
                    var lt = em.Value.GetComponentData<LocalTransform>(e);
                    lt.Position = p; em.Value.SetComponentData(e, lt);
                }
                VehicleSeats.Unboard(em.Value, e);
                CombatWorld.Damage.Enqueue(new DamageEvent
                {
                    targetActorId = id, sourceActorId = killer, amount = 9999f, explosive = true,
                    hitPoint = p, direction = Vector3.up
                });
            }
        }

        /// <summary>Игрок в джипе нажал 3 — свободное место займёт ближайший боец (иначе оно ждёт игрока).</summary>
        [System.NonSerialized] public bool BuddyCalled;

        /// <summary>Танк без игрока: держим бота-водителя на месте, проверяем, жив ли он, и набираем экипаж.</summary>
        void TankCrewUpdate(float dt)
        {
            if (driverBot >= 0) UpdateBotSeats(dt);
            BuddyBoarding();
        }

        /// <summary>Игрок садится в танк, где сидит бот, — бот выходит наружу.</summary>
        void EjectBotDriver()
        {
            if (driverBot < 0) return;
            var em = Em(); if (em == null) { driverBot = -1; return; }
            DismountBot(em.Value, ref driverBot, JeepSeat.Driver);
        }

        /// <summary>Бот в танке под бронёй — осколки взрывов снаружи его не задевают.</summary>
        public static bool IsArmoredCrew(int actorId)
        {
            if (actorId <= 0) return false;
            foreach (var t in All) if (t && !t.IsLight && t.driverBot == actorId) return true;
            return false;
        }

        /// <summary>
        /// Игрок сел в машину, а второе место свободно — ближайший свой боец бегом подбегает и садится
        /// (если в настройках матча второе место отдано боту, а не второму игроку).
        /// </summary>
        void BuddyBoarding()
        {
            float now = Time.time;
            // Джип: боец идёт, только если игрок внутри позвал его клавишей 3. Танк: пустой — экипаж набирается сам.
            bool want = !Net.NetSession.IsClient && BotCount == 0 && AnySeatFree && Alive &&
                        (IsLight ? PlayerAboard && BuddyCalled : !PlayerAboard && crew == Crew.None && Time.time > EmptySince + 1.5f);
            var actors = CombatWorld.Actors;
            if (!want || !actors.IsCreated) { ReleaseBuddy(); return; }
            if (_buddy >= 0)
            {
                bool ok = _buddy < actors.Length && actors[_buddy].alive && now < _buddyUntil;
                if (!ok) { ReleaseBuddy(); _buddyRetry = now + 2f; return; }
                Vector3 bp = actors[_buddy].position;
                if (Vector3.Distance(bp, transform.position) < 3.6f)
                {
                    int id = _buddy; ReleaseBuddy();
                    TryBoardBot(id);
                    return;
                }
                if (now > _buddyRefresh) { _buddyRefresh = now + 0.8f; FrontPlanner.Force(_buddy, new float2(transform.position.x, transform.position.z)); }
                return;
            }
            if (now < _buddyRetry) return;
            _buddyRetry = now + 1.5f;
            var em = Em();
            float best = 70f; int pick = -1;
            for (int i = 1; i < actors.Length; i++)
            {
                var a = actors[i];
                if (!a.alive || a.team != Team || Net.NetSession.IsRemoteSlot(a.actorId) || Claimed.Contains(a.actorId)) continue;
                float d = Vector3.Distance(a.position, transform.position);
                if (d >= best) continue;
                var e = BotEntity(a.actorId);
                if (em == null || e == Entity.Null || !em.Value.Exists(e) || VehicleSeats.IsBoarded(em.Value, e)) continue;
                best = d; pick = a.actorId;
            }
            if (pick < 0) return;
            _buddy = pick; _buddyUntil = now + 25f; _buddyRefresh = now + 0.8f; Claimed.Add(pick);
            FrontPlanner.Force(pick, new float2(transform.position.x, transform.position.z));
        }

        // ---------------- Игрок ----------------

        public void OnPlayerExit()
        {
            BuddyCalled = false;
            if (PlayerSeat == JeepSeat.Driver || crew == Crew.Player) { crew = driverBot >= 0 ? Crew.Bot : Crew.None; EmptySince = Time.time; }
            PlayerSeat = JeepSeat.None;
            Zoomed = false;
        }

        /// <summary>[X] — пересесть на следующее свободное место по кругу.</summary>
        public void TrySwitchSeat()
        {
            if (!IsLight || !PlayerAboard) return;
            var pc = PlayerBridge.Controller; if (!pc) return;
            var cur = PlayerSeat;
            for (int k = 1; k <= 3; k++)
            {
                var next = (JeepSeat)(((int)cur - 1 + k) % 2 + 1);    // водитель ↔ пулемётчик
                if (!SeatFree(next)) continue;
                if (cur == JeepSeat.Driver) { crew = driverBot >= 0 ? Crew.Bot : Crew.None; EmptySince = Time.time; }
                PlayerSeat = next;
                if (next == JeepSeat.Driver) crew = Crew.Player;
                _enterFrame = Time.frameCount; Zoomed = false;
                _camYaw = transform.eulerAngles.y + _turretYaw; _camPitch = 8f;
                pc.SetPassenger(next == JeepSeat.Passenger);
                BotAudio.PlayAt(SoundSynth.Hatch(), transform.position + Vector3.up * 1.5f, 0.4f);
                return;
            }
        }

        // ---------------- Пассажир-бот стреляет из своего оружия ----------------

        void PassengerFire(float dt)
        {
            var actors = CombatWorld.Actors;
            Vector3 eye = SeatTransform(JeepSeat.Passenger).position + Vector3.up * 1.1f;
            _paxThink -= dt;
            if (_paxThink <= 0)
            {
                _paxThink = 0.6f;
                _paxTarget = -1;
                float bd = 140f; int checks = 0;
                for (int i = 0; i < actors.Length && checks < 5; i++)
                {
                    var a = actors[i];
                    if (!a.alive || a.team == Team) continue;
                    float d = Vector3.Distance(a.position, eye);
                    if (d >= bd) continue;
                    checks++;
                    if (Visible(eye, (Vector3)a.position + Vector3.up * 1.2f, null)) { bd = d; _paxTarget = a.actorId; }
                }
            }
            if (_paxTarget < 0 || _paxTarget >= actors.Length || !actors[_paxTarget].alive) return;
            if (Time.time < _paxNext) return;
            _paxNext = Time.time + Random.Range(0.35f, 0.7f);    // с качающейся машины — редкие выстрелы

            var tg = actors[_paxTarget];
            Vector3 aim = (Vector3)tg.position + Vector3.up * Random.Range(0.9f, 1.5f);
            float dist = Vector3.Distance(eye, aim);
            bool hit = Random.value < Mathf.Lerp(0.38f, 0.12f, dist / 140f) * (Mathf.Abs(_speed) > 5f ? 0.6f : 1f);
            Vector3 end = hit ? aim : aim + Random.insideUnitSphere * Mathf.Max(0.8f, dist * 0.04f);
            Vector3 dir = (end - eye).normalized;
            if (hit)
            {
                CombatWorld.Damage.Enqueue(new DamageEvent
                {
                    targetActorId = _paxTarget, sourceActorId = passengerBot, amount = 30f,
                    hitPoint = aim, direction = dir, headshot = Random.value < 0.12f
                });
                if (FxPool.I) FxPool.I.Blood(aim, dir);
            }
            if (FxPool.I) { FxPool.I.Tracer(eye, end, 700f); FxPool.I.MuzzleFlashAt(eye + dir * 0.6f, dir); }
            BotAudio.Shot(eye, Team, false);
            if (Net.NetSession.IsHost) Net.NetDriver.RecordShot(passengerBot, eye, end, Team, false);
            CombatWorld.EmitNoise(eye, 160f, passengerBot, Team, 0);
        }
    }
}
