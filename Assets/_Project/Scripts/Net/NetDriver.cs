using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using Frontline.Bots;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Frontline.Net
{
    /// <summary>
    /// Сетевой «двигатель» кооператива по локальной сети: голый UDP (System.Net.Sockets), без пакетов Unity.
    ///
    /// ХОСТ (авторитетный): ведёт весь мир как обычно; 20 раз в секунду рассылает снимок — время, счёт, точки,
    /// положения всех бойцов (по 9 байт), здоровье подключённых игроков, технику, выстрелы, убийства, взрывы.
    /// Раз в секунду объявляет игру широковещательно (порт 27016) — клиенты видят её в списке.
    /// КЛИЕНТ: строит ту же карту (генерация детерминирована), ботов создаёт тем же зерном, но их ИИ выключен —
    /// боты-«куклы» двигаются по снимкам с интерполяцией; свой игрок полностью локальный (стрельба без задержки),
    /// а урон, который он нанёс, отправляется хосту событиями урона. Здоровье клиента считает хост.
    /// До 3 подключённых игроков. Техника и стройка клиенту видны, но сесть в танк/джип клиент пока не может.
    /// </summary>
    public class NetDriver : MonoBehaviour
    {
        public static NetDriver I { get; private set; }

        UdpClient _sock, _disc;
        MatchConfig _cfg;
        float _snapTimer, _discTimer, _sendTimer;

        // ---------------- Хост ----------------
        class Remote
        {
            public IPEndPoint ep; public int slot; public float lastSeen; public string name;
            public Vector3 pos, vel; public float yaw;
            public bool crouched, aiming, reloading, carrying, alive, clientAlive, ackDead = true;
            public byte team = 1, cls;
            public float health = 50f, lastHit = -99f, deadAt = -99f;
            public RemoteSoldier view;
        }
        readonly Remote[] _remotes = new Remote[NetSession.MaxRemote];

        struct ShotEv { public int shooter; public Vector3 from, to; public byte team; public bool supp; }
        readonly List<ShotEv> _shots = new();
        readonly List<KillEvent> _kills = new();
        readonly List<(Vector3 p, float r, byte k)> _expl = new();

        // ---------------- Клиент ----------------
        IPEndPoint _host;
        bool _welcomed; float _lastFromHost, _joinTimer; int _joinTries;
        Action<string> _onFail;
        readonly List<DamageEvent> _outDamage = new();
        readonly List<ShotEv> _outShots = new();
        readonly List<(Vector3 p, float r, byte k)> _outExpl = new();
        public static bool HostStarted { get; private set; }
        float3[] _from, _to; float[] _fromYaw, _toYaw; byte[] _flags; bool[] _known;
        float _snapT, _snapPrevT, _snapInterval = 0.05f;
        readonly Dictionary<int, RemoteSoldier> _views = new();
        readonly Dictionary<int, (Tank tank, float seen)> _tanks = new();
        readonly float[] _remoteHealth = new float[NetSession.MaxRemote];
        readonly byte[] _remoteCls = new byte[NetSession.MaxRemote];
        byte _hostCls;

        static readonly MemoryStream _ms = new(65536);
        static readonly BinaryWriter _w = new(_ms);

        // =====================================================================
        // Запуск
        // =====================================================================

        static NetDriver Make()
        {
            if (I) Destroy(I.gameObject);
            var go = new GameObject("[NetDriver]");
            DontDestroyOnLoad(go);
            return I = go.AddComponent<NetDriver>();
        }

        /// <summary>Открыть матч для локальной сети (вызывается перед стартом матча).</summary>
        public static bool StartHost(MatchConfig cfg)
        {
            var d = Make();
            try
            {
                d._sock = new UdpClient(new IPEndPoint(IPAddress.Any, NetSession.GamePort)) { EnableBroadcast = true };
                d._sock.Client.Blocking = false;
                NoReset(d._sock);
            }
            catch (Exception e)
            {
                NetSession.Status = "Порт " + NetSession.GamePort + " занят: " + e.Message;
                Destroy(d.gameObject); return false;
            }
            d._cfg = cfg;
            NetSession.Role = NetRole.Host;
            NetSession.Seed = (uint)UnityEngine.Random.Range(1, int.MaxValue);
            NetSession.Status = "Игра открыта в локальной сети";
            return true;
        }

        /// <summary>Подключиться к хосту по адресу. При успехе сам запускает матч (GameFlow.StartMatch).</summary>
        public static void Join(string address, Action<string> onFail)
        {
            var d = Make();
            try
            {
                if (!IPAddress.TryParse(address.Trim(), out var ip))
                {
                    var hostEntry = Dns.GetHostAddresses(address.Trim());
                    ip = Array.Find(hostEntry, a => a.AddressFamily == AddressFamily.InterNetwork);
                    if (ip == null) throw new Exception("адрес не найден");
                }
                d._host = new IPEndPoint(ip, NetSession.GamePort);
                d._sock = new UdpClient(0);
                d._sock.Client.Blocking = false;
                NoReset(d._sock);
            }
            catch (Exception e) { Destroy(d.gameObject); onFail?.Invoke("Не удалось подключиться: " + e.Message); return; }
            d._onFail = onFail;
            NetSession.Role = NetRole.Client;
            NetSession.Status = "Подключение к " + address + "…";
            d._joinTimer = 0; d._joinTries = 0;
            HostStarted = false;
        }

        /// <summary>Слушать объявления хостов в локальной сети (экран «По сети»).</summary>
        public static void StartDiscovery()
        {
            if (I && I._disc != null) return;
            if (!I) Make();
            try
            {
                var u = new UdpClient();
                u.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                u.Client.Bind(new IPEndPoint(IPAddress.Any, NetSession.DiscoveryPort));
                u.Client.Blocking = false;
                I._disc = u;
            }
            catch (Exception e) { NetSession.Status = "Поиск игр недоступен: " + e.Message; }
        }

        void OnEnable() => BotRegistry.KillHappened += OnKillHost;
        void OnDisable() => BotRegistry.KillHappened -= OnKillHost;

        void OnDestroy()
        {
            try
            {
                if (_sock != null)
                {
                    if (NetSession.IsClient && _host != null) { Begin('Q'); SendTo(_host); }
                    if (NetSession.IsHost) foreach (var r in _remotes) if (r != null) { Begin('Q'); SendTo(r.ep); }
                    _sock.Close();
                }
                _disc?.Close();
            }
            catch { }
            foreach (var v in _views.Values) if (v) Destroy(v.gameObject);
            foreach (var r in _remotes) if (r?.view) Destroy(r.view.gameObject);
            if (I == this) I = null;
        }

        // =====================================================================
        // Пакеты
        // =====================================================================

        /// <summary>Windows: ICMP «порт недоступен» иначе роняет приём UDP (WSAECONNRESET) — отключаем.</summary>
        static void NoReset(UdpClient u)
        {
            try
            {
                if (Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor)
                    u.Client.IOControl(-1744830452, new byte[] { 0, 0, 0, 0 }, null);
            }
            catch { }
        }

        static void Begin(char type) { _ms.SetLength(0); _w.Write(NetSession.Magic); _w.Write((byte)NetSession.Version); _w.Write((byte)type); }

        void SendTo(IPEndPoint ep)
        {
            if (_sock == null || ep == null) return;
            try { _sock.Send(_ms.GetBuffer(), (int)_ms.Length, ep); } catch { }
        }

        static short Q(float v) => (short)Mathf.Clamp(Mathf.RoundToInt(v * 20f), short.MinValue, short.MaxValue);
        static float DQ(short v) => v / 20f;
        static void WV(Vector3 v) { _w.Write(Q(v.x)); _w.Write(Q(v.y)); _w.Write(Q(v.z)); }
        static Vector3 RV(BinaryReader r) => new(DQ(r.ReadInt16()), DQ(r.ReadInt16()), DQ(r.ReadInt16()));
        static void WF(Vector3 v) { _w.Write(v.x); _w.Write(v.y); _w.Write(v.z); }
        static Vector3 RF(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

        void Update()
        {
            Receive(_sock, false);
            Receive(_disc, true);
            if (NetSession.IsHost) HostUpdate();
            else if (NetSession.IsClient) ClientUpdate();
            // Забываем исчезнувшие объявления
            NetSession.Found.RemoveAll(g => Time.unscaledTime - g.seenAt > 4f);
        }

        void Receive(UdpClient u, bool discovery)
        {
            if (u == null) return;
            for (int guard = 0; guard < 400; guard++)
            {
                byte[] data; IPEndPoint ep = new(IPAddress.Any, 0);
                try { if (u.Available <= 0) return; data = u.Receive(ref ep); }
                catch (SocketException) { return; }
                catch (ObjectDisposedException) { return; }
                try
                {
                    using var r = new BinaryReader(new MemoryStream(data));
                    if (r.ReadString() != NetSession.Magic || r.ReadByte() != NetSession.Version) continue;
                    char type = (char)r.ReadByte();
                    if (discovery) { if (type == 'D') OnDiscovery(r, ep); continue; }
                    if (NetSession.IsHost) HostPacket(type, r, ep);
                    else if (NetSession.IsClient) ClientPacket(type, r, ep);
                }
                catch (Exception e) { Debug.LogWarning("[Net] Битый пакет: " + e.Message); }
            }
        }

        static void OnDiscovery(BinaryReader r, IPEndPoint ep)
        {
            var g = new LanGame
            {
                address = ep.Address.ToString(), hostName = r.ReadString(), winter = r.ReadBoolean(), mode = r.ReadString(),
                bots = r.ReadUInt16(), minutes = r.ReadByte(), players = r.ReadByte(), seenAt = Time.unscaledTime
            };
            int i = NetSession.Found.FindIndex(x => x.address == g.address);
            if (i >= 0) NetSession.Found[i] = g; else NetSession.Found.Add(g);
        }

        // =====================================================================
        // ХОСТ
        // =====================================================================

        void HostPacket(char type, BinaryReader r, IPEndPoint ep)
        {
            Remote rem = null;
            foreach (var x in _remotes) if (x != null && x.ep.Equals(ep)) rem = x;
            switch (type)
            {
                case 'J':
                {
                    string name = r.ReadString();
                    if (!GameModeManager.I || GameModeManager.I.Mode == null) return;   // карта ещё строится — клиент повторит запрос
                    if (rem == null)
                    {
                        int idx = Array.IndexOf(_remotes, null);
                        if (idx < 0) return;                          // мест нет
                        rem = _remotes[idx] = new Remote { ep = ep, slot = NetSession.FirstRemoteId + idx, name = name, lastSeen = Time.unscaledTime };
                        Debug.Log($"[Net] Подключился {name} ({ep}) — слот {rem.slot}");
                    }
                    SendWelcome(rem);
                    break;
                }
                case 'C':
                    if (rem == null) return;
                    rem.lastSeen = Time.unscaledTime;
                    ReadClientState(rem, r);
                    break;
                case 'Q':
                    if (rem != null) Drop(rem);
                    break;
            }
        }

        void SendWelcome(Remote rem)
        {
            var c = GameModeManager.I ? GameModeManager.I.Config : _cfg;
            Begin('W');
            _w.Write((short)rem.slot);
            _w.Write(NetSession.Seed);
            _w.Write(c.map ? c.map.id : "normandy");
            _w.Write((byte)c.mode); _w.Write((ushort)c.botCount); _w.Write(c.timeLimitMinutes);
            _w.Write((byte)c.timeOfDay); _w.Write((byte)c.weather); _w.Write((byte)c.difficulty);
            _w.Write((byte)MatchBridge.PlayerFaction);
            SendTo(rem.ep);
        }

        void ReadClientState(Remote rem, BinaryReader r)
        {
            rem.pos = RF(r); rem.yaw = r.ReadSingle(); rem.vel = RF(r);
            byte fl = r.ReadByte();
            rem.clientAlive = (fl & 1) != 0; rem.crouched = (fl & 2) != 0; rem.aiming = (fl & 4) != 0; rem.reloading = (fl & 16) != 0; rem.carrying = (fl & 32) != 0;
            rem.team = r.ReadByte(); rem.cls = r.ReadByte();
            if (!rem.clientAlive) rem.ackDead = true;

            // Урон, нанесённый клиентом: применяем как свой (стрелок — слот клиента)
            int nd = r.ReadByte();
            for (int i = 0; i < nd; i++)
            {
                int target = r.ReadInt16(); float amount = r.ReadSingle();
                byte f = r.ReadByte(); Vector3 dir = RF(r), point = RF(r);
                if (!rem.alive && target != rem.slot) continue;
                CombatWorld.Damage.Enqueue(new DamageEvent
                {
                    targetActorId = target, sourceActorId = rem.slot, amount = amount, headshot = (f & 1) != 0, explosive = (f & 2) != 0,
                    direction = dir, hitPoint = point
                });
            }
            // Выстрелы клиента — звук и трассер у хоста и у остальных клиентов
            int ns = r.ReadByte();
            for (int i = 0; i < ns; i++)
            {
                Vector3 from = RV(r), to = RV(r); bool supp = r.ReadBoolean();
                PlayShotFx(from, to, rem.team, supp);
                if (rem.view) rem.view.Shot();
                _shots.Add(new ShotEv { shooter = rem.slot, from = from, to = to, team = rem.team, supp = supp });
            }
            int ne = r.ReadByte();
            for (int i = 0; i < ne; i++)
            {
                Vector3 p = RV(r); float rad = r.ReadByte() * 0.25f; byte k = r.ReadByte();
                Explosions.VisualOnly(p, rad, (Explosions.Kind)k);
                _expl.Add((p, rad, k));
            }
        }

        void Drop(Remote rem)
        {
            Debug.Log($"[Net] {rem.name} отключился");
            int idx = Array.IndexOf(_remotes, rem);
            if (idx >= 0) _remotes[idx] = null;
            if (rem.view) Destroy(rem.view.gameObject);
            if (CombatWorld.Initialized && rem.slot < CombatWorld.Actors.Length)
            {
                var a = CombatWorld.Actors[rem.slot]; a.alive = false; CombatWorld.Actors[rem.slot] = a;
            }
        }

        /// <summary>Урон по подключённому игроку (зовёт BotDamageSystem у хоста). Возвращает true, если цель — сетевой игрок.</summary>
        public static bool ApplyRemoteDamage(in DamageEvent e)
        {
            if (!I || !NetSession.IsHost || !NetSession.IsRemoteSlot(e.targetActorId)) return false;
            var rem = I._remotes[e.targetActorId - NetSession.FirstRemoteId];
            if (rem == null || !rem.alive) return true;
            // Огонь по своим не проходит (кроме собственной гранаты)
            if (e.sourceActorId != rem.slot && e.sourceActorId >= 0 && e.sourceActorId < CombatWorld.Actors.Length
                && CombatWorld.Actors[e.sourceActorId].team == rem.team) return true;
            rem.health -= e.amount; rem.lastHit = Time.time;
            if (rem.health <= 0f)
            {
                rem.health = 0f; rem.alive = false; rem.ackDead = false; rem.deadAt = Time.time;
                BotRegistry.Kills.Enqueue(new KillEvent { killerActorId = e.sourceActorId, victimActorId = rem.slot, victimTeam = rem.team, headshot = e.headshot });
            }
            return true;
        }

        public static void RecordShot(int shooter, Vector3 from, Vector3 to, byte team, bool supp)
        {
            if (!I || !NetSession.IsHost || I._shots.Count >= 200) return;
            I._shots.Add(new ShotEv { shooter = shooter, from = from, to = to, team = team, supp = supp });
        }

        public static void RecordExplosion(Vector3 p, float radius, Explosions.Kind kind)
        {
            if (!I || !NetSession.IsHost || I._expl.Count >= 40) return;
            I._expl.Add((p, radius, (byte)kind));
        }

        void OnKillHost(KillEvent k) { if (NetSession.IsHost && _kills.Count < 100) _kills.Add(k); }

        void HostUpdate()
        {
            float now = Time.unscaledTime;
            for (int i = 0; i < _remotes.Length; i++)
            {
                var rem = _remotes[i];
                if (rem == null) continue;
                if (now - rem.lastSeen > 6f) { Drop(rem); continue; }
                // Возрождение: клиент выбрал высадку (жив у себя) после того, как узнал о гибели
                if (!rem.alive && rem.clientAlive && rem.ackDead && Time.time - rem.deadAt > 1.5f) { rem.alive = true; rem.health = 50f; }
                if (rem.alive && rem.health < 50f && Time.time - rem.lastHit > 6f) rem.health = Mathf.Min(50f, rem.health + 6f * Time.deltaTime);
                if (CombatWorld.Initialized && rem.slot < CombatWorld.Actors.Length)
                    CombatWorld.Actors[rem.slot] = new ActorSnapshot
                    {
                        position = rem.pos, velocity = rem.vel, actorId = rem.slot, team = rem.team,
                        alive = rem.alive && rem.clientAlive, crouched = rem.crouched
                    };
                if (!rem.view) rem.view = RemoteSoldier.Create(null, rem.name);
                rem.view.SetState(rem.pos, rem.yaw, rem.alive && rem.clientAlive, rem.crouched, rem.aiming, rem.team, (SoldierClass)rem.cls);
                rem.view.Reloading = rem.reloading; rem.view.Carrying = rem.carrying;
            }

            _snapTimer -= Time.unscaledDeltaTime;
            if (_snapTimer <= 0f)
            {
                _snapTimer = 0.05f;
                bool any = false; foreach (var x in _remotes) any |= x != null;
                if (any)
                {
                    BuildSnapshot();
                    foreach (var x in _remotes) if (x != null) SendTo(x.ep);
                }
                _shots.Clear(); _kills.Clear(); _expl.Clear();
            }

            _discTimer -= Time.unscaledDeltaTime;
            if (_discTimer <= 0f)
            {
                _discTimer = 1f;
                var c = GameModeManager.I ? GameModeManager.I.Config : _cfg;
                if (c != null)
                {
                    int players = 1; foreach (var x in _remotes) if (x != null) players++;
                    Begin('D');
                    _w.Write(Environment.MachineName); _w.Write(c.map && c.map.theme == MapTheme.Winter);
                    _w.Write(c.mode == GameModeType.TerritoryControl ? "Захват точек" : "Командный бой");
                    _w.Write((ushort)c.botCount); _w.Write((byte)Mathf.RoundToInt(c.timeLimitMinutes)); _w.Write((byte)players);
                    SendTo(new IPEndPoint(IPAddress.Broadcast, NetSession.DiscoveryPort));
                }
            }
        }

        void BuildSnapshot()
        {
            var gm = GameModeManager.I;
            var mode = gm ? gm.Mode : null;
            Begin('S');
            _w.Write(Time.time);
            _w.Write(mode != null ? mode.TimeLeft : 0f);
            _w.Write(mode != null ? mode.Score[0] : 0); _w.Write(mode != null ? mode.Score[1] : 0);
            _w.Write(mode != null && mode.Over); _w.Write((byte)(mode?.Winner == null ? 255 : (int)mode.Winner.Value));
            _w.Write((byte)MatchBridge.PlayerFaction); _w.Write(MatchBridge.MatchRunning);
            _w.Write((byte)(gm ? gm.Config.playerClass : SoldierClass.Assault));

            var pts = WorldStores.CapturePoints;
            int np = pts.IsCreated ? pts.Length : 0;
            _w.Write((byte)np);
            for (int i = 0; i < np; i++) { _w.Write(pts[i].progress); _w.Write(pts[i].owner); _w.Write(pts[i].contested); }

            // Бойцы: флаги, позиция, курс
            var em = World.DefaultGameObjectInjectionWorld?.EntityManager;
            var actors = CombatWorld.Actors;
            int na = CombatWorld.Initialized ? actors.Length : 0;
            _w.Write((ushort)na);
            var pc = PlayerBridge.Controller;
            for (int i = 0; i < na; i++)
            {
                var a = actors[i];
                float yaw = 0; bool aim = false, rel = false, car = false;
                if (i == 0 && pc) { yaw = pc.transform.eulerAngles.y; aim = pc.Aiming; rel = PlayerBody.LocalReloadOrDraw; car = pc.CarryingShell; }
                else if (NetSession.IsRemoteSlot(i)) { var rem = _remotes[i - NetSession.FirstRemoteId]; if (rem != null) { yaw = rem.yaw; aim = rem.aiming; rel = rem.reloading; car = rem.carrying; } }
                else if (em.HasValue && i < BotRegistry.ActorToEntity.Length)
                {
                    var e = BotRegistry.ActorToEntity[i];
                    if (e != Entity.Null && em.Value.Exists(e))
                    {
                        yaw = math.degrees(em.Value.GetComponentData<BotMotion>(e).yaw);
                        aim = em.Value.GetComponentData<BotPerception>(e).targetVisible;
                    }
                }
                byte fl = (byte)((a.alive ? 1 : 0) | (a.crouched ? 2 : 0) | (aim ? 4 : 0) | (a.team == 1 ? 8 : 0) | (rel ? 16 : 0) | (car ? 32 : 0));
                _w.Write(fl); WV(a.position);
                _w.Write((byte)Mathf.RoundToInt(Mathf.Repeat(yaw, 360f) / 360f * 255f));
            }
            // Здоровье и класс подключённых игроков
            for (int i = 0; i < NetSession.MaxRemote; i++)
            {
                var rem = _remotes[i];
                _w.Write(rem != null ? rem.health : 0f); _w.Write(rem != null); _w.Write(rem != null ? rem.cls : (byte)0);
            }
            // Техника
            int nt = 0; foreach (var t in Tank.All) if (t) nt++;
            _w.Write((byte)Mathf.Min(nt, 32));
            int written = 0;
            foreach (var t in Tank.All)
            {
                if (!t || written >= 32) continue;
                written++;
                _w.Write((short)t.index);
                _w.Write((byte)((t.Team == 1 ? 1 : 0) | (t.IsLight ? 2 : 0) | (t.Alive ? 4 : 0) | (t.crew != Tank.Crew.None ? 8 : 0)));
                WF(t.transform.position); _w.Write(t.transform.eulerAngles.y); _w.Write(t.TurretYaw); _w.Write(t.GunPitch);
            }
            // Самолёты
            int npl = 0; foreach (var a in Aircraft.All) if (a) npl++;
            npl = Mathf.Min(npl, 64);
            _w.Write((byte)npl);
            int wpl = 0;
            foreach (var a in Aircraft.All)
            {
                if (!a || wpl >= npl) continue;
                wpl++;
                _w.Write((short)a.index);
                _w.Write((byte)((a.Team == 1 ? 1 : 0) | (a.Alive ? 2 : 0) | (a.Airborne ? 4 : 0) | (a.HasPilot ? 8 : 0)));
                WF(a.transform.position); var aeu = a.transform.eulerAngles; _w.Write(aeu.x); _w.Write(aeu.y); _w.Write(aeu.z); _w.Write(a.Throttle);
            }
            // Орудия и зенитки
            int ng = 0; foreach (var g in ArtilleryGun.All) if (g) ng++;
            ng = Mathf.Min(ng, 64);
            _w.Write((byte)ng);
            int wg = 0;
            foreach (var g in ArtilleryGun.All)
            {
                if (!g || wg >= ng) continue;
                wg++;
                _w.Write((short)g.index);
                _w.Write((byte)((g.Team == 1 ? 1 : 0) | (g.Alive ? 2 : 0) | (g.IsAA ? 4 : 0) | (g.Loaded ? 8 : 0) | (g.crewBot > 0 || g.PlayerAboard ? 16 : 0)));
                WF(g.BasePosition); _w.Write(g.BaseYaw); _w.Write(g.YawOffset); _w.Write(g.Elevation); _w.Write(g.Recoil01);
            }
            // Кучи снарядов
            for (int tm = 0; tm < 2; tm++)
            {
                var pile = AmmoPile.Of((byte)tm);
                _w.Write(pile != null);
                if (pile) { WF(pile.transform.position); _w.Write(pile.transform.eulerAngles.y); _w.Write((byte)pile.Count); }
            }
            // Выстрелы, убийства, взрывы
            int ns = Mathf.Min(_shots.Count, 255);
            _w.Write((byte)ns);
            for (int i = 0; i < ns; i++) { var s = _shots[i]; _w.Write((short)s.shooter); WV(s.from); WV(s.to); _w.Write(s.team); _w.Write(s.supp); }
            int nk = Mathf.Min(_kills.Count, 255);
            _w.Write((byte)nk);
            for (int i = 0; i < nk; i++) { var k = _kills[i]; _w.Write(k.killerActorId); _w.Write((short)k.victimActorId); _w.Write(k.victimTeam); _w.Write(k.headshot); }
            int ne = Mathf.Min(_expl.Count, 255);
            _w.Write((byte)ne);
            for (int i = 0; i < ne; i++) { WV(_expl[i].p); _w.Write((byte)Mathf.Clamp(_expl[i].r * 4f, 0, 255)); _w.Write(_expl[i].k); }
        }

        static void PlayShotFx(Vector3 from, Vector3 to, byte team, bool supp)
        {
            BotAudio.Shot(from, team, supp);
            if (FxPool.I)
            {
                Vector3 d = (to - from); float len = d.magnitude;
                if (len > 0.5f) { FxPool.I.Tracer(from + d / len * 0.8f, to, 700f); FxPool.I.MuzzleFlashAt(from + d / len * 0.9f, d / len); }
            }
        }

        // =====================================================================
        // КЛИЕНТ
        // =====================================================================

        void ClientPacket(char type, BinaryReader r, IPEndPoint ep)
        {
            if (!ep.Address.Equals(_host.Address)) return;
            _lastFromHost = Time.unscaledTime;
            switch (type)
            {
                case 'W': OnWelcome(r); break;
                case 'S': if (_welcomed) OnSnapshot(r); break;
                case 'Q': Disconnect("Хост завершил игру"); break;
            }
        }

        void OnWelcome(BinaryReader r)
        {
            if (_welcomed) return;
            NetSession.MyRemoteId = r.ReadInt16();
            NetSession.Seed = r.ReadUInt32();
            string mapId = r.ReadString();
            var mode = (GameModeType)r.ReadByte(); int bots = r.ReadUInt16(); float minutes = r.ReadSingle();
            var tod = (TimeOfDay)r.ReadByte(); var weather = (Weather)r.ReadByte(); var diff = (BotDifficulty)r.ReadByte();
            NetSession.HostFaction = (Faction)r.ReadByte();
            var db = GameDatabase.Instance;
            MapDefinition map = null;
            foreach (var m in db.maps) if (m && m.id == mapId) { map = m; break; }
            if (!map && db.maps.Count > 0) map = db.maps[0];
            _cfg = new MatchConfig
            {
                map = map, mode = mode, playerFaction = NetSession.HostFaction == Faction.USSR ? Faction.Germany : Faction.USSR,
                playerClass = SoldierClass.Assault, botCount = bots, difficulty = diff, timeOfDay = tod, weather = weather,
                timeLimitMinutes = minutes, scoreLimit = 1000, killLimit = 50
            };
            _welcomed = true;
            NetSession.Status = "Подключено";
            Debug.Log($"[Net] Принят хостом: слот {NetSession.MyRemoteId}, ботов {bots}");
            if (GameFlow.I) GameFlow.I.StartMatch(_cfg);
        }

        void EnsureBuffers(int n)
        {
            if (_from != null && _from.Length >= n) return;
            _from = new float3[n]; _to = new float3[n]; _fromYaw = new float[n]; _toYaw = new float[n]; _flags = new byte[n]; _known = new bool[n];
        }

        void OnSnapshot(BinaryReader r)
        {
            float hostTime = r.ReadSingle();
            float timeLeft = r.ReadSingle(); int s0 = r.ReadInt32(), s1 = r.ReadInt32();
            bool over = r.ReadBoolean(); byte winner = r.ReadByte();
            NetSession.HostFaction = (Faction)r.ReadByte(); HostStarted = r.ReadBoolean();
            _hostCls = r.ReadByte();
            var gm = GameModeManager.I;
            var mode = gm ? gm.Mode : null;
            if (mode != null)
            {
                mode.TimeLeft = timeLeft; mode.Score[0] = s0; mode.Score[1] = s1;
                if (over && !mode.Over) { mode.Over = true; mode.Winner = winner == 255 ? null : (Faction?)(Faction)winner; }
            }

            int np = r.ReadByte();
            var pts = WorldStores.CapturePoints;
            for (int i = 0; i < np; i++)
            {
                float prog = r.ReadSingle(); byte own = r.ReadByte(), con = r.ReadByte();
                if (pts.IsCreated && i < pts.Length)
                {
                    var d = pts[i]; d.progress = prog; d.owner = own; d.contested = con; pts[i] = d;
                    if (gm && i < gm.capturePoints.Length && gm.capturePoints[i]) gm.capturePoints[i].Visualize(d);
                }
            }

            int na = r.ReadUInt16();
            EnsureBuffers(na);
            _snapPrevT = _snapT; _snapT = Time.unscaledTime;
            _snapInterval = Mathf.Clamp(_snapT - _snapPrevT, 0.03f, 0.2f);
            for (int i = 0; i < na; i++)
            {
                byte fl = r.ReadByte(); Vector3 p = RV(r); float yaw = r.ReadByte() / 255f * 360f;
                _from[i] = _known[i] ? Lerp(i, 1f) : (float3)p; _fromYaw[i] = _known[i] ? _toYaw[i] : yaw;
                _to[i] = p; _toYaw[i] = yaw; _flags[i] = fl; _known[i] = true;
            }
            for (int i = 0; i < NetSession.MaxRemote; i++) { _remoteHealth[i] = r.ReadSingle(); r.ReadBoolean(); _remoteCls[i] = r.ReadByte(); }

            int nt = r.ReadByte();
            float now = Time.unscaledTime;
            for (int i = 0; i < nt; i++)
            {
                int idx = r.ReadInt16(); byte tf = r.ReadByte();
                Vector3 pos = RF(r); float yaw = r.ReadSingle(), ty = r.ReadSingle(), gp = r.ReadSingle();
                ApplyTank(idx, tf, pos, yaw, ty, gp, now);
            }
            var stale = new List<int>();
            foreach (var kv in _tanks) if (now - kv.Value.seen > 5f) stale.Add(kv.Key);
            foreach (var k in stale) { if (_tanks[k].tank) Destroy(_tanks[k].tank.gameObject); _tanks.Remove(k); }

            int npl = r.ReadByte();
            for (int i = 0; i < npl; i++)
            {
                int idx = r.ReadInt16(); byte f = r.ReadByte(); Vector3 pos = RF(r);
                Vector3 eu = new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); float thr = r.ReadSingle();
                ApplyPlane(idx, f, pos, eu, thr, now);
            }
            stale.Clear();
            foreach (var kv in _planes) if (now - kv.Value.seen > 5f) stale.Add(kv.Key);
            foreach (var k in stale) { if (_planes[k].plane) Destroy(_planes[k].plane.gameObject); _planes.Remove(k); }

            int ng = r.ReadByte();
            for (int i = 0; i < ng; i++)
            {
                int idx = r.ReadInt16(); byte f = r.ReadByte(); Vector3 pos = RF(r);
                float by = r.ReadSingle(), yo = r.ReadSingle(), el = r.ReadSingle(), rc = r.ReadSingle();
                ApplyGun(idx, f, pos, by, yo, el, rc, now);
            }
            stale.Clear();
            foreach (var kv in _guns) if (now - kv.Value.seen > 5f) stale.Add(kv.Key);
            foreach (var k in stale) { if (_guns[k].gun) Destroy(_guns[k].gun.gameObject); _guns.Remove(k); }

            for (int tm = 0; tm < 2; tm++)
            {
                if (!r.ReadBoolean()) continue;
                Vector3 pp = RF(r); float py = r.ReadSingle(); int pcnt = r.ReadByte();
                var pile = AmmoPile.Of((byte)tm);
                if (!pile) pile = AmmoPile.Create((byte)tm, pp, py, null, ProceduralModels.Theme);
                if (pile.Count != pcnt) pile.SetCount(pcnt);
            }

            int ns = r.ReadByte();
            for (int i = 0; i < ns; i++)
            {
                int shooter = NetSession.HostToLocal(r.ReadInt16()); Vector3 from = RV(r), to = RV(r); byte team = r.ReadByte(); bool supp = r.ReadBoolean();
                if (shooter == 0) continue;                          // свой выстрел уже показан локально
                PlayShotFx(from, to, team, supp);
                if (_views.TryGetValue(shooter, out var v) && v) v.Shot();
                MarkBotShot(shooter);
            }
            int nk = r.ReadByte();
            for (int i = 0; i < nk; i++)
            {
                int killer = r.ReadInt32(); int victim = r.ReadInt16(); byte vt = r.ReadByte(); bool head = r.ReadBoolean();
                if (killer >= 0) killer = NetSession.HostToLocal(killer);
                BotRegistry.Kills.Enqueue(new KillEvent { killerActorId = killer, victimActorId = NetSession.HostToLocal(victim), victimTeam = vt, headshot = head });
            }
            if (nk > 0) BotRegistry.FlushKills();
            int ne = r.ReadByte();
            for (int i = 0; i < ne; i++) { Vector3 p = RV(r); float rad = r.ReadByte() * 0.25f; byte k = r.ReadByte(); Explosions.VisualOnly(p, rad, (Explosions.Kind)k); }
        }

        float3 Lerp(int i, float k) => math.lerp(_from[i], _to[i], k);

        readonly Dictionary<int, (Aircraft plane, float seen)> _planes = new();
        readonly Dictionary<int, (ArtilleryGun gun, float seen)> _guns = new();

        /// <summary>Самолёт хоста у клиента: кукла с позой, винтом и звуком; сбит у хоста — взрыв и обломки у нас.</summary>
        void ApplyPlane(int idx, byte f, Vector3 pos, Vector3 euler, float thr, float now)
        {
            bool alive = (f & 2) != 0;
            if (!_planes.TryGetValue(idx, out var e) || !e.plane)
            {
                if (!alive) return;
                int layer = LayerMask.NameToLayer("World"); if (layer < 0) layer = 0;
                var a = Aircraft.Create((f & 1) != 0 ? Faction.USSR : Faction.Germany, ProceduralModels.Theme, pos, euler.y, idx, null, layer);
                a.enabled = false;
                e = (a, now);
            }
            e.seen = now; _planes[idx] = e;
            var pl = e.plane;
            if (pl.Alive)
            {
                pl.ApplyNetPose(pos, Quaternion.Euler(euler), thr, (f & 8) != 0, (f & 4) != 0);
                if (!alive) pl.TakeDamage(1e6f, -1, pos);
            }
        }

        /// <summary>Орудие или зенитка хоста у клиента.</summary>
        void ApplyGun(int idx, byte f, Vector3 pos, float baseYaw, float yawOff, float elev, float recoil, float now)
        {
            bool alive = (f & 2) != 0;
            if (!_guns.TryGetValue(idx, out var e) || !e.gun)
            {
                if (!alive) return;
                int layer = LayerMask.NameToLayer("World"); if (layer < 0) layer = 0;
                var fac = (f & 1) != 0 ? Faction.USSR : Faction.Germany;
                var g = (f & 4) != 0 ? ArtilleryGun.CreateAA(fac, ProceduralModels.Theme, pos, baseYaw, idx, null, layer)
                                     : ArtilleryGun.Create(fac, ProceduralModels.Theme, pos, baseYaw, idx, null, layer);
                g.enabled = false;
                e = (g, now);
            }
            e.seen = now; _guns[idx] = e;
            var gun = e.gun;
            if (gun.Alive)
            {
                gun.ApplyNetPose(yawOff, elev, recoil, (f & 8) != 0, (f & 16) != 0);
                if (!alive) gun.TakeDamage(1e6f, -1, pos);
            }
        }

        void ApplyTank(int idx, byte tf, Vector3 pos, float yaw, float ty, float gp, float now)
        {
            bool alive = (tf & 4) != 0;
            if (!_tanks.TryGetValue(idx, out var e) || !e.tank)
            {
                if (!alive) return;
                int layer = LayerMask.NameToLayer("World"); if (layer < 0) layer = 0;
                var t = Tank.Create((tf & 1) != 0 ? Faction.USSR : Faction.Germany, ProceduralModels.Theme, pos, yaw, idx, null, layer, (tf & 2) != 0);
                t.enabled = false;                                   // кукла: своя логика выключена
                e = (t, now);
            }
            e.seen = now;
            _tanks[idx] = e;
            var tk = e.tank;
            tk.crew = (tf & 8) != 0 ? Tank.Crew.Bot : Tank.Crew.None;
            if (tk.Alive)
            {
                tk.ApplyNetPose(pos, yaw, ty, gp);
                if (!alive) tk.TakeDamage(1e6f, -1, tk.Center, true);   // уничтожен у хоста — взрыв и обломки у нас
            }
        }

        void MarkBotShot(int actorId)
        {
            if (actorId <= 0 || actorId >= BotRegistry.ActorToEntity.Length) return;
            var world = World.DefaultGameObjectInjectionWorld; if (world == null) return;
            var em = world.EntityManager;
            var e = BotRegistry.ActorToEntity[actorId];
            if (e == Entity.Null || !em.Exists(e)) return;
            var w = em.GetComponentData<BotWeapon>(e);
            w.nextFire = (float)world.Time.ElapsedTime + w.fireInterval;      // отдача в позе
            em.SetComponentData(e, w);
        }

        // ---- Отправка своего состояния ----

        public static void QueueDamage(in DamageEvent e)
        {
            if (!I || !NetSession.IsClient || I._outDamage.Count >= 120) return;
            I._outDamage.Add(e);
        }

        public static void SendShot(Vector3 from, Vector3 to, byte team, bool supp)
        {
            if (!I || !NetSession.IsClient || I._outShots.Count >= 60) return;
            I._outShots.Add(new ShotEv { from = from, to = to, team = team, supp = supp });
        }

        public static void SendExplosion(Vector3 p, float radius, Explosions.Kind kind)
        {
            if (!I || !NetSession.IsClient || I._outExpl.Count >= 20) return;
            I._outExpl.Add((p, radius, (byte)kind));
        }

        void ClientUpdate()
        {
            float now = Time.unscaledTime;
            if (!_welcomed)
            {
                _joinTimer -= Time.unscaledDeltaTime;
                if (_joinTimer <= 0f)
                {
                    _joinTimer = 0.5f; _joinTries++;
                    if (_joinTries > 60) { var f = _onFail; NetSession.Stop(); f?.Invoke("Хост не отвечает. Проверьте адрес и брандмауэр (UDP 27015)."); return; }
                    Begin('J'); _w.Write(NetSession.PlayerName); SendTo(_host);
                }
                return;
            }
            if (now - _lastFromHost > 6f) { Disconnect("Связь с хостом потеряна"); return; }

            ApplyActors();
            SyncHealth();

            _sendTimer -= Time.unscaledDeltaTime;
            if (_sendTimer <= 0f)
            {
                _sendTimer = 1f / 30f;
                var pc = PlayerBridge.Controller;
                var hp = pc ? pc.GetComponent<PlayerHealth>() : null;
                Begin('C');
                WF(pc ? pc.transform.position : Vector3.zero);
                _w.Write(pc ? pc.transform.eulerAngles.y : 0f);
                WF(pc && CombatWorld.Initialized && CombatWorld.Actors.Length > 0 ? (Vector3)CombatWorld.Actors[0].velocity : Vector3.zero);
                bool alive = pc && hp && hp.Alive && !(GameModeManager.I && GameModeManager.I.AwaitingDeploy);
                _w.Write((byte)((alive ? 1 : 0) | (pc && pc.IsCrouched ? 2 : 0) | (pc && pc.Aiming ? 4 : 0) | (PlayerBody.LocalReloadOrDraw ? 16 : 0) | (pc && pc.CarryingShell ? 32 : 0)));
                _w.Write((byte)(pc ? pc.Team : 1));
                _w.Write((byte)(GameModeManager.I ? GameModeManager.I.Config.playerClass : SoldierClass.Assault));
                int nd = Mathf.Min(_outDamage.Count, 255);
                _w.Write((byte)nd);
                for (int i = 0; i < nd; i++)
                {
                    var e = _outDamage[i];
                    _w.Write((short)NetSession.LocalToHost(e.targetActorId)); _w.Write(e.amount);
                    _w.Write((byte)((e.headshot ? 1 : 0) | (e.explosive ? 2 : 0)));
                    WF(e.direction); WF(e.hitPoint);
                }
                int ns = Mathf.Min(_outShots.Count, 255);
                _w.Write((byte)ns);
                for (int i = 0; i < ns; i++) { WV(_outShots[i].from); WV(_outShots[i].to); _w.Write(_outShots[i].supp); }
                int ne = Mathf.Min(_outExpl.Count, 255);
                _w.Write((byte)ne);
                for (int i = 0; i < ne; i++) { WV(_outExpl[i].p); _w.Write((byte)Mathf.Clamp(_outExpl[i].r * 4f, 0, 255)); _w.Write(_outExpl[i].k); }
                SendTo(_host);
                _outDamage.Clear(); _outShots.Clear(); _outExpl.Clear();
            }
        }

        void Disconnect(string why)
        {
            NetSession.Status = why;
            Debug.LogWarning("[Net] " + why);
            var flow = GameFlow.I;
            NetSession.Stop();
            if (flow) flow.ReturnToMenu();
        }

        /// <summary>Здоровье клиента считает хост: уменьшение — как попадание (эффекты, смерть), рост — регенерация.</summary>
        void SyncHealth()
        {
            int idx = NetSession.MyRemoteId - NetSession.FirstRemoteId;
            if (idx < 0 || idx >= NetSession.MaxRemote) return;
            var pc = PlayerBridge.Controller; if (!pc) return;
            var hp = pc.GetComponent<PlayerHealth>(); if (!hp || !hp.Alive) return;
            if (GameModeManager.I && GameModeManager.I.AwaitingDeploy) return;
            float host = _remoteHealth[idx];
            if (host < hp.Health - 0.5f)
                hp.ApplyDamage(new DamageEvent { targetActorId = 0, sourceActorId = -1, amount = host <= 0f ? 9999f : hp.Health - host, direction = -pc.transform.forward });
            else if (host > hp.Health + 0.5f) hp.SyncFromHost(host);
        }

        /// <summary>Боты-куклы и другие игроки: интерполяция между двумя последними снимками (~50 мс позади хоста).</summary>
        void ApplyActors()
        {
            if (_from == null || !CombatWorld.Initialized) return;
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;
            var em = world.EntityManager;
            var actors = CombatWorld.Actors;
            float k = Mathf.Clamp01((Time.unscaledTime - _snapT) / _snapInterval);
            float t = (float)world.Time.ElapsedTime;
            int bots = Mathf.Clamp(MatchBridge.BotCount, 0, BotRegistry.MaxBots);
            int need = NetSession.FirstRemoteId + NetSession.MaxRemote;
            if (actors.Length < need) actors.Resize(need, Unity.Collections.NativeArrayOptions.ClearMemory);
            for (int hi = 0; hi < _from.Length; hi++)
            {
                if (!_known[hi]) continue;
                int li = NetSession.HostToLocal(hi);
                if (li == 0 || li >= actors.Length) continue;
                byte fl = _flags[hi];
                bool alive = (fl & 1) != 0, crouch = (fl & 2) != 0, aim = (fl & 4) != 0, rel = (fl & 16) != 0, car = (fl & 32) != 0; byte team = (byte)((fl & 8) != 0 ? 1 : 0);
                float3 pos = Lerp(hi, k);
                float yaw = Mathf.LerpAngle(_fromYaw[hi], _toYaw[hi], k);
                float3 vel = (_to[hi] - _from[hi]) / _snapInterval;
                actors[li] = new ActorSnapshot { position = pos, velocity = vel, actorId = li, team = team, alive = alive, crouched = crouch };

                if (li >= 1 && li <= bots && li < BotRegistry.ActorToEntity.Length)
                {
                    var e = BotRegistry.ActorToEntity[li];
                    if (e == Entity.Null || !em.Exists(e)) continue;
                    var hp = em.GetComponentData<BotHealth>(e);
                    bool wasAlive = hp.value > 0;
                    var m = em.GetComponentData<BotMotion>(e);
                    if (alive)
                    {
                        if (!wasAlive)
                        {
                            hp.value = hp.max; em.SetComponentData(e, hp);
                            em.SetComponentEnabled<BotDead>(e, false);
                            if (em.HasComponent<BotRagdoll>(e)) em.SetComponentData(e, new BotRagdoll());
                            m.lastPos = pos;
                        }
                        em.SetComponentData(e, LocalTransform.FromPositionRotation(pos, quaternion.RotateY(math.radians(yaw))));
                        m.velocity = vel; m.yaw = math.radians(yaw); m.crouched = crouch; m.lastPos = pos;
                        em.SetComponentData(e, m);
                        var p = em.GetComponentData<BotPerception>(e);
                        p.targetVisible = aim;
                        p.targetPos = pos + new float3(math.sin(m.yaw), 0, math.cos(m.yaw)) * 30f;
                        em.SetComponentData(e, p);
                    }
                    else if (wasAlive)
                    {
                        hp.value = 0; hp.lastHitTime = t; em.SetComponentData(e, hp);
                        m.velocity = float3.zero; em.SetComponentData(e, m);
                        em.SetComponentEnabled<BotDead>(e, true);
                        if (em.HasComponent<BotRagdoll>(e)) em.SetComponentData(e, new BotRagdoll());
                    }
                }
                else if (li > bots)
                {
                    // Хост и другие подключённые игроки
                    if (!_views.TryGetValue(li, out var v) || !v)
                    {
                        if (!alive) continue;
                        v = RemoteSoldier.Create(null, NetSession.NameOf(li) ?? "Игрок");
                        _views[li] = v;
                    }
                    byte cls = li == NetSession.MyRemoteId ? _hostCls : (li - NetSession.FirstRemoteId >= 0 && li - NetSession.FirstRemoteId < NetSession.MaxRemote ? _remoteCls[li - NetSession.FirstRemoteId] : (byte)0);
                    v.SetState(pos, yaw, alive, crouch, aim, team, (SoldierClass)cls);
                    v.Reloading = rel; v.Carrying = car;
                }
            }
        }
    }
}
