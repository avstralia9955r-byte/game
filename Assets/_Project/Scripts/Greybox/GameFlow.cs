using System.Collections;
using System.Reflection;
using Frontline.Bots;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Frontline
{
    /// <summary>
    /// Игровой цикл без сцен и ассетов: главное меню → генерация карты 2×2 км → матч → обратно в меню.
    /// Включается сам, если в загруженной сцене нет GameModeManager и MainMenu (например, пустая SampleScene).
    /// Как только появятся собранные вручную сцены — молча отключается.
    /// </summary>
    public class GameFlow : MonoBehaviour
    {
        public static GameFlow I { get; private set; }

        GameObject _menu, _match;
        public bool InMatch => _match;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoStart()
        {
            if (Object.FindAnyObjectByType<GameModeManager>() || Object.FindAnyObjectByType<MainMenu>()) return;
            var go = new GameObject("[GameFlow]");
            DontDestroyOnLoad(go);
            I = go.AddComponent<GameFlow>();
            I.ShowMenu();
        }

        void Awake()
        {
            if (SaveSystem.Settings == null) SaveSystem.LoadAll();
            GameInput.Init();
            CombatWorld.Init();
            BotRegistry.Init();
            if (GraphicsSettings.currentRenderPipeline == null)
                Debug.LogError("[Frontline] Проект на Built-in — материалы будут розовыми, боты не отрисуются. Меню Frontline → Fix Pink Materials (URP).");
            // Стартовый бонус, чтобы сразу попробовать обвесы.
        }

        public void ShowMenu()
        {
            GameInput.SetEnabled(false);
            if (_menu) Destroy(_menu);
            EnsureLight();
            _menu = RuntimeMenu.Create(this);
            // После матча камера игрока (вместе со слушателем звука) удалена, а камера сцены выключена при старте матча:
            // без AudioListener Unity каждый кадр пишет предупреждение, и музыка меню «подвисает». Даём меню свой слушатель.
            if (!FindAnyObjectByType<AudioListener>() && _menu) _menu.AddComponent<AudioListener>();
            AudioListener.volume = SaveSystem.Settings != null ? SaveSystem.Settings.masterVolume : 1f;
        }

        public void StartMatch(MatchConfig cfg) => StartCoroutine(StartRoutine(cfg));
        public void ReturnToMenu() => StartCoroutine(MenuRoutine());

        IEnumerator StartRoutine(MatchConfig cfg)
        {
            RuntimeMenu.ShowLoading(_menu, "Генерация карты 1×1 км…");
            yield return null; yield return null;           // дать отрисоваться экрану загрузки
            Teardown();
            yield return null;
            MatchSession.Current = cfg;
            Build(cfg);
            if (_menu) Destroy(_menu);
        }

        IEnumerator MenuRoutine()
        {
            if (Net.NetSession.Active) Net.NetSession.Stop();   // выход в меню завершает сетевую игру
            Teardown();
            yield return null;
            ShowMenu();
        }

        // ======================= Матч =======================

        void Build(MatchConfig cfg)
        {
            var theme = cfg.map ? cfg.map.theme : MapTheme.Summer;
            ProceduralModels.Theme = theme;
            int world = Layer("World", 0), bricks = Layer("Bricks", world);
            foreach (var cam in FindObjectsByType<Camera>()) if (!cam.targetTexture) cam.gameObject.SetActive(false);

            _match = new GameObject("[Match]");
            MapGenerator.TrenchMap = cfg.map && cfg.map.id == "rzhev";      // третья карта — сплошные линии окопов
            var layout = MapGenerator.Generate(theme, MapGenerator.TrenchMap ? 1942_08 : theme == MapTheme.Winter ? 1944 : 1944_06, world);
            layout.root.transform.SetParent(_match.transform);

            // Точки захвата
            var cps = new CapturePoint[layout.points.Length];
            for (int i = 0; i < cps.Length; i++)
            {
                var go = new GameObject("Point_" + (char)('A' + i));
                go.transform.SetParent(_match.transform); go.transform.position = layout.points[i];
                var cp = go.AddComponent<CapturePoint>();
                cp.letter = (char)('A' + i); cp.radius = 14f;
                ProceduralModels.Box(go.transform, "Pole", new Vector3(0, 5, 0), new Vector3(0.18f, 10, 0.18f), new Color(0.55f, 0.55f, 0.57f), false, default, 0.5f, 0.8f);
                var flag = new GameObject("Flag").transform; flag.SetParent(go.transform, false);
                var cloth = ProceduralModels.Box(flag, "Cloth", new Vector3(1f, 0, 0), new Vector3(1.9f, 1.2f, 0.05f), Color.white);
                cp.flag = flag; cp.flagRenderer = cloth.GetComponent<Renderer>(); cp.flagLow = 1.5f; cp.flagHigh = 9.2f;
                cps[i] = cp;
            }
            var spawnsDe = ToTransforms(layout.spawnsDe, "SpawnDe"); var spawnsSu = ToTransforms(layout.spawnsSu, "SpawnSu");

            // Кирпичи
            var brickTpl = new GameObject("Brick");
            brickTpl.transform.SetParent(ProceduralModels.Root, false);
            brickTpl.layer = bricks;
            var brickSize = new Vector3(0.6f, 0.3f, 0.3f);
            brickTpl.AddComponent<BoxCollider>().size = brickSize;
            ProceduralModels.Box(brickTpl.transform, "Mesh", Vector3.zero, brickSize * 0.97f, theme == MapTheme.Winter ? new Color(0.58f, 0.32f, 0.26f) : new Color(0.64f, 0.28f, 0.2f));
            var brick = brickTpl.AddComponent<Brick>();

            var managers = new GameObject("[Managers]");
            managers.SetActive(false);
            managers.transform.SetParent(_match.transform);
            var bs = managers.AddComponent<BrickSystem>();
            bs.brickPrefab = brick; bs.brickSize = brickSize; bs.groundMask = 1 << world;

            var gm = managers.AddComponent<GameModeManager>();
            gm.capturePoints = cps; gm.spawnsGermany = spawnsDe; gm.spawnsUSSR = spawnsSu;
            gm.playerPrefab = BuildPlayerTemplate(theme, cfg);

            var ff = managers.AddComponent<FlowFieldBaker>();
            float zh = MapGenerator.Zone + 20;
            ff.boundsMin = new Vector2(-zh, -zh); ff.boundsMax = new Vector2(zh, zh);
            ff.cellSize = 2f; ff.rayTop = 400f; ff.groundMask = 1 << world;
            ff.capturePoints = System.Array.ConvertAll(cps, c => c.transform);
            ff.baseGermany = spawnsDe[0]; ff.baseUSSR = spawnsSu[0];

            var cov = managers.AddComponent<CoverPointBaker>();
            cov.obstacleMask = 1 << world; cov.cellStride = 3;

            managers.AddComponent<GameHUD>();
            Net.NetSystems.SetClientMode(Net.NetSession.IsClient);
            // Телефон: экранные кнопки и джойстик; дальность «детального» поведения ботов вдвое короче
            if (Application.isMobilePlatform) TouchUI.Create(managers.transform);
            BotLod.Scale = Application.isMobilePlatform ? 0.5f : cfg.botCount > 256 ? 0.75f : 1f;
            PerformanceOptimizer.BotLodBase = BotLod.Scale;
            if (FxPool.I) FxPool.I.ClearDecals();
            managers.AddComponent<PerformanceOptimizer>();
            managers.AddComponent<EpicAmbience>();                      // дальние артналёты и осветительные ракеты — эпичнее и динамичнее

            // Техника, ящики с боеприпасами, тактика ботов (гранаты, противотанковое)
            Vector3 Avg(Transform[] t) { Vector3 c = Vector3.zero; foreach (var x in t) c += x.position; return t.Length > 0 ? c / t.Length : Vector3.zero; }
            Vector3 baseDe = Avg(spawnsDe), baseSu = Avg(spawnsSu);
            var vm = managers.AddComponent<VehicleManager>();
            vm.theme = theme; vm.worldLayer = world; vm.baseGermany = baseDe; vm.baseUSSR = baseSu;
            int tanks = Mathf.Clamp(cfg.tanksTotal, 0, 128);             // ползунок «Танков» в «Новой игре»
            vm.tanksGermany = (tanks + 1) / 2; vm.tanksUssr = tanks / 2;
            vm.tanksPerTeam = vm.tanksGermany;
            vm.jeepsPerTeam = 1;                                         // всего 2 машины — по одной у стороны
            var air = managers.AddComponent<AirManager>();               // самолёты: ползунок «Количество самолётов»
            air.theme = theme; air.worldLayer = world; air.baseGermany = baseDe; air.baseUSSR = baseSu;
            int planes = Mathf.Clamp(cfg.planesTotal, 0, 32);
            air.planesGermany = (planes + 1) / 2; air.planesUssr = planes / 2;
            var art = managers.AddComponent<ArtilleryManager>();         // гаубицы: ползунок «Количество орудий»
            art.theme = theme; art.worldLayer = world; art.baseGermany = baseDe; art.baseUSSR = baseSu;
            int guns = Mathf.Clamp(cfg.artilleryTotal, 0, 24);
            art.gunsGermany = (guns + 1) / 2; art.gunsUssr = guns / 2;
            art.aaPerTeam = planes > 0 ? 2 : 0;                           // зенитные пулемёты — когда в небе есть самолёты
            var tactics = managers.AddComponent<BotTactics>();
            tactics.grenadePrefab = gm.playerPrefab.GetComponent<GrenadeThrower>().grenadePrefab;
            var crates = new GameObject("AmmoCrates").transform; crates.SetParent(_match.transform);
            foreach (var b in new[] { baseDe, baseSu })
            {
                Vector3 toC = -b; toC.y = 0; toC.Normalize();
                Vector3 side = Vector3.Cross(Vector3.up, toC);
                foreach (float s in new[] { -1f, 1f })
                {
                    var p = GameModeManager.GroundAt(b + toC * 14f + side * (8f * s));
                    AmmoCrate.Create(crates, p, Mathf.Atan2(toC.x, toC.z) * Mathf.Rad2Deg + 90f, theme, world);
                }
            }
            for (int i = 0; i < cps.Length; i++)
            {
                var c = cps[i].transform.position;
                float a = i * 1.7f + 0.6f;
                var p = GameModeManager.GroundAt(c + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 6.5f);
                AmmoCrate.Create(crates, p, a * Mathf.Rad2Deg, theme, world);
            }
            Environment(theme, cfg.timeOfDay, cfg.weather);

            // Эффекты, постобработка, звук
            ProceduralFx.Create(_match.transform, theme);
            if (GraphicsSettings.currentRenderPipeline != null) PostFx.Create(_match.transform, theme, cfg.timeOfDay);
            var audioGo = new GameObject("Audio"); audioGo.SetActive(false); audioGo.transform.SetParent(_match.transform);
            var ba = audioGo.AddComponent<BotAudio>();
            var dbw = GameDatabase.Instance;
            AudioClip[] Shots(FactionArsenal a) => System.Array.ConvertAll(
                new[] { WeaponCategory.AssaultRifle, WeaponCategory.SubmachineGun, WeaponCategory.MachineGun, WeaponCategory.SniperRifle },
                c => a.Get(c) ? a.Get(c).fireSound : null);
            Set(ba, "germanyShots", Shots(dbw.germany));
            Set(ba, "ussrShots", Shots(dbw.ussr));
            Set(ba, "distantShots", new[] { SoundSynth.Distant(0), SoundSynth.Distant(1), SoundSynth.Distant(2) });
            Set(ba, "suppressedShots", new[] { SoundSynth.Suppressed("bot_a"), SoundSynth.Suppressed("bot_b") });
            var amb = audioGo.AddComponent<AmbientAudio>();
            amb.winter = theme == MapTheme.Winter; amb.day = cfg.timeOfDay == TimeOfDay.Day;
            audioGo.SetActive(true);
            managers.SetActive(true);

            CreateBotPrefabs(theme);
        }

        /// <summary>Полная очистка матча: объекты, ECS-боты, статические буферы.</summary>
        void Teardown()
        {
            MatchBridge.MatchRunning = false;
            MatchBridge.SpawnRequested = false;
            if (GameModeManager.I && GameModeManager.I.Player) Destroy(GameModeManager.I.Player.gameObject);
            if (_match) Destroy(_match);
            _match = null;
            foreach (var g in FindObjectsByType<Grenade>()) Destroy(g.gameObject);
            foreach (var pr in FindObjectsByType<Projectile>()) Destroy(pr.gameObject);

            var w = World.DefaultGameObjectInjectionWorld;
            if (w != null && w.IsCreated)
            {
                var em = w.EntityManager;
                // Сначала корни (вместе с ними удаляются части тела через LinkedEntityGroup), потом остатки.
                DestroyAll(em, new EntityQueryBuilder(Allocator.Temp).WithAll<BotIdentity>().WithOptions(EntityQueryOptions.IncludePrefab));
                DestroyAll(em, new EntityQueryBuilder(Allocator.Temp).WithAll<BotLimb>().WithOptions(EntityQueryOptions.IncludePrefab));
                DestroyAll(em, new EntityQueryBuilder(Allocator.Temp).WithAll<BotPrefabs>());
            }
            if (CombatWorld.Initialized)
            {
                CombatWorld.Actors.Clear(); CombatWorld.Damage.Clear(); CombatWorld.Noise.Clear(); CombatWorld.NoiseWrite.Clear();
                FrontPlanner.Reset();
            }
            BotRegistry.BotCount = 0;
            if (BotRegistry.Kills.IsCreated) BotRegistry.Kills.Clear();
            if (BotRegistry.BuildRequests.IsCreated) BotRegistry.BuildRequests.Clear();
            if (BotCombatSystem.Shots.IsCreated) BotCombatSystem.Shots.Clear();
            GameInput.SetEnabled(false);
        }

        static void DestroyAll(EntityManager em, EntityQueryBuilder b)
        {
            var q = em.CreateEntityQuery(b);
            var arr = q.ToEntityArray(Allocator.Temp);
            em.DestroyEntity(arr);
            arr.Dispose(); q.Dispose(); b.Dispose();
        }

        // ======================= Окружение =======================

        void Environment(MapTheme theme, TimeOfDay time, Weather weather)
        {
            var env = new GameObject("Environment").AddComponent<EnvironmentController>();
            env.transform.SetParent(_match.transform);
            env.sun = EnsureLight();
            EnvironmentController.LightingPreset P(Vector3 e, Color sun, float i, Color amb, Color fog, float dens) =>
                new() { sunEuler = e, sunColor = sun, sunIntensity = i, ambient = amb, fogColor = fog, fogDensity = dens };
            // Пасмурнее и мрачнее: солнце ниже и тусклее, серое небо, плотная серая дымка
            env.summerDay = P(new(34, -35, 0), new Color(0.95f, 0.9f, 0.82f), 1.55f, new Color(0.5f, 0.53f, 0.56f), new Color(0.6f, 0.63f, 0.64f), 0.0024f);
            env.summerEvening = P(new(9, 250, 0), new Color(0.92f, 0.66f, 0.48f), 1.15f, new Color(0.36f, 0.35f, 0.4f), new Color(0.58f, 0.5f, 0.47f), 0.0022f);
            env.winterDay = P(new(28, -20, 0), new Color(0.9f, 0.92f, 0.95f), 1.3f, new Color(0.58f, 0.62f, 0.68f), new Color(0.7f, 0.73f, 0.76f), 0.0026f);
            env.winterEvening = P(new(7, 240, 0), new Color(0.9f, 0.7f, 0.58f), 0.9f, new Color(0.4f, 0.42f, 0.5f), new Color(0.5f, 0.52f, 0.58f), 0.0028f);
            env.rain = Precipitation(env.transform, false);
            env.snow = Precipitation(env.transform, true);
            env.Apply(theme, time, weather);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = RenderSettings.ambientLight * 1.15f;
            RenderSettings.ambientEquatorColor = RenderSettings.ambientLight;
            RenderSettings.ambientGroundColor = RenderSettings.ambientLight * 0.55f;
        }

        static ParticleSystem Precipitation(Transform parent, bool snow)
        {
            var go = new GameObject(snow ? "Snow" : "Rain");
            go.transform.SetParent(parent);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.startLifetime = snow ? 7f : 1.2f; main.startSpeed = snow ? 0.5f : 22f;
            main.startSize = snow ? 0.06f : 0.025f; main.maxParticles = snow ? 6000 : 4000;
            main.gravityModifier = snow ? 0.06f : 1.2f; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = snow ? new Color(1, 1, 1, 0.9f) : new Color(0.75f, 0.8f, 0.9f, 0.45f);
            var em = ps.emission; em.rateOverTime = snow ? 900 : 2500;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(70, 70, 1);   // после поворота на 90° по X: 70×70 по горизонтали
            go.transform.rotation = Quaternion.Euler(90, 0, 0);
            if (snow) { var noise = ps.noise; noise.enabled = true; noise.strength = 0.6f; noise.frequency = 0.3f; }
            var r = go.GetComponent<ParticleSystemRenderer>();
            var sh2 = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Particles/Standard Unlit");
            if (sh2) r.sharedMaterial = new Material(sh2) { color = main.startColor.color };
            if (!snow) { r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.04f; r.lengthScale = 1f; }
            go.SetActive(false);
            ps.Play();
            return ps;
        }

        static Light EnsureLight()
        {
            foreach (var l in FindObjectsByType<Light>())
                if (l.type == LightType.Directional && !l.GetComponentInParent<MenuStage>()) return l;
            var go = new GameObject("Sun");
            DontDestroyOnLoad(go);
            var sun = go.AddComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 1.6f; sun.shadows = LightShadows.Soft;
            go.transform.rotation = Quaternion.Euler(50, -30, 0);
            return sun;
        }

        // ======================= Игрок =======================

        public const int ViewModelLayer = 31;   // оружие от первого лица рисует отдельная оверлей-камера

        static void SetLayer(Transform t, int layer) { foreach (var c in t.GetComponentsInChildren<Transform>(true)) c.gameObject.layer = layer; }

        static PlayerController BuildPlayerTemplate(MapTheme theme, MatchConfig cfg)
        {
            var p = new GameObject("Player");
            p.transform.SetParent(ProceduralModels.Root, false);
            p.layer = 2;
            var cc = p.AddComponent<CharacterController>();
            cc.height = 1.8f; cc.center = new Vector3(0, 0.9f, 0); cc.radius = 0.35f; cc.stepOffset = 0.45f; cc.slopeLimit = 50;

            var pivot = new GameObject("CameraPivot").transform; pivot.SetParent(p.transform, false); pivot.localPosition = new Vector3(0, 1.68f, 0);
            var camGo = new GameObject("PlayerCamera"); camGo.tag = "MainCamera";
            camGo.transform.SetParent(pivot, false);
            var cam = camGo.AddComponent<Camera>(); cam.nearClipPlane = 0.02f; cam.farClipPlane = 1100f; cam.fieldOfView = 70;
            camGo.AddComponent<AudioListener>();
            // Руки и оружие рисует сама камера мира (слой 31 включён): без стека камер, который мог не собраться после
            // Instantiate шаблона, — оружие видно всегда, освещено и затенено как мир, проходит ту же постобработку.
            // От проваливания в стены защищает отвод оружия у препятствий (ViewModelAnimator).
            cam.cullingMask = ~0;
            Camera vmCam = null;
            if (GraphicsSettings.currentRenderPipeline != null)
            {
                var acd = camGo.AddComponent<UniversalAdditionalCameraData>();
                acd.renderPostProcessing = true; acd.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            }

            var viewRoot = new GameObject("ViewModel").transform; viewRoot.SetParent(camGo.transform, false);
            var assembler = viewRoot.gameObject.AddComponent<WeaponVisualAssembler>();

            var knifeModel = ProceduralModels.Weapon("knife", WeaponCategory.Knife, Faction.USSR).transform;
            knifeModel.SetParent(camGo.transform, false);
            knifeModel.localPosition = new Vector3(0.16f, -0.16f, 0.36f);
            knifeModel.gameObject.SetActive(false);
            SetLayer(knifeModel, ViewModelLayer);

            var trowelVm = ProceduralModels.Trowel();
            trowelVm.transform.SetParent(camGo.transform, false);
            trowelVm.transform.localPosition = new Vector3(0.2f, -0.2f, 0.38f);
            trowelVm.SetActive(false);
            SetLayer(trowelVm.transform, ViewModelLayer);

            var ghost = ProceduralModels.Box(p.transform, "BrickGhost", Vector3.zero, Vector3.one, new Color(0.4f, 1f, 0.5f));
            ghost.SetActive(false);

            var audio = p.AddComponent<AudioSource>(); audio.spatialBlend = 0;
            var pc = p.AddComponent<PlayerController>();
            pc.cameraPivot = pivot; pc.worldCamera = cam; pc.viewModelCamera = vmCam;
            p.AddComponent<PlayerHealth>();

            var knife = p.AddComponent<KnifeController>();
            Set(knife, "knifeModel", knifeModel); Set(knife, "audioSource", audio);
            Set(knife, "spinSound", SoundSynth.KnifeSwish()); Set(knife, "stabSound", SoundSynth.KnifeSwish()); Set(knife, "stabHitSound", SoundSynth.Thud());

            var trowel = p.AddComponent<TrowelTool>();
            Set(trowel, "player", pc); Set(trowel, "trowelViewModel", trowelVm);
            Set(trowel, "ghost", ghost.transform); Set(trowel, "ghostRenderer", ghost.GetComponent<Renderer>());
            Set(trowel, "audioSource", audio); Set(trowel, "laySound", SoundSynth.BrickLay());

            var wc = p.AddComponent<WeaponController>();
            Set(wc, "player", pc); Set(wc, "viewModelRoot", viewRoot); Set(wc, "assembler", assembler);
            Set(wc, "audioSource", audio); Set(wc, "knife", knife); Set(wc, "trowel", trowel); Set(wc, "viewModelLayer", ViewModelLayer);

            var anim = p.AddComponent<ViewModelAnimator>();
            anim.weapon = wc; anim.player = pc; anim.assembler = assembler; anim.knife = knife; anim.audioSource = audio;

            var arms = camGo.AddComponent<ViewModelArms>();
            arms.assembler = assembler; arms.knifeModel = knifeModel; arms.trowelModel = trowelVm.transform;
            arms.faction = cfg.playerFaction; arms.theme = theme; arms.layer = ViewModelLayer;

            var camFx = camGo.AddComponent<CameraEffects>();
            camFx.player = pc; camFx.weapon = wc; camFx.health = p.GetComponent<PlayerHealth>();

            var steps = p.AddComponent<FootstepAudio>();
            steps.player = pc; steps.source = audio; steps.snow = theme == MapTheme.Winter;

            var gt = p.AddComponent<GrenadeThrower>();
            gt.player = pc; gt.grenadePrefab = GrenadeTemplate();

            var interact = p.AddComponent<PlayerInteraction>();
            interact.player = pc;

            // Тело от третьего лица: тень, видимые ноги, оружие в руках
            var body = p.AddComponent<PlayerBody>();
            body.player = pc; body.weapon = wc; body.health = p.GetComponent<PlayerHealth>();
            body.faction = cfg.playerFaction; body.theme = theme; body.soldierClass = cfg.playerClass;

            // Реверберация окружения (открытая местность / лес)
            var rz = camGo.AddComponent<AudioReverbZone>();
            rz.reverbPreset = theme == MapTheme.Winter ? AudioReverbPreset.Mountains : AudioReverbPreset.Forest;
            rz.minDistance = 0; rz.maxDistance = 5000;
            return pc;
        }

        static Grenade GrenadeTemplate()
        {
            var g = new GameObject("Grenade");
            g.transform.SetParent(ProceduralModels.Root, false);
            g.layer = 2;
            ProceduralModels.Part(g.transform, "Head", MeshUtil.Cylinder(8), new Vector3(0, 0.05f, 0), new Vector3(0.07f, 0.1f, 0.07f), new Color(0.3f, 0.33f, 0.25f));
            ProceduralModels.Part(g.transform, "Handle", MeshUtil.Cylinder(6), new Vector3(0, -0.1f, 0), new Vector3(0.03f, 0.25f, 0.03f), ProceduralModels.Wood);
            var col = g.AddComponent<CapsuleCollider>(); col.radius = 0.04f; col.height = 0.36f; col.center = new Vector3(0, -0.04f, 0);
            var rb = g.AddComponent<Rigidbody>(); rb.mass = 0.6f; rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            var gr = g.AddComponent<Grenade>();
            gr.explosionSound = SoundSynth.Explosion();
            return gr;
        }

        // ======================= Боты =======================

        static void CreateBotPrefabs(MapTheme theme)
        {
            var w = World.DefaultGameObjectInjectionWorld;
            if (w == null) { Debug.LogError("[Frontline] Нет ECS-мира по умолчанию"); return; }
            var em = w.EntityManager;
            var list = new System.Collections.Generic.List<BotPrefabVariant>();
            foreach (Faction f in new[] { Faction.Germany, Faction.USSR })
                foreach (SoldierClass c in new[] { SoldierClass.Assault, SoldierClass.Engineer, SoldierClass.Support, SoldierClass.Recon })
                    list.Add(new BotPrefabVariant { prefab = BotBody.CreatePrefab(em, f, theme, c), team = (byte)f, soldierClass = c });
            var holder = em.CreateEntity();
            em.AddComponentData(holder, new BotPrefabs { germany = list[0].prefab, ussr = list[4].prefab });
            var buf = em.AddBuffer<BotPrefabVariant>(holder);
            foreach (var v in list) buf.Add(v);
        }

        // ======================= Утилиты =======================

        Transform[] ToTransforms(Vector3[] pts, string name)
        {
            var arr = new Transform[pts.Length];
            for (int i = 0; i < pts.Length; i++)
            {
                var t = new GameObject($"{name}_{i}").transform;
                t.SetParent(_match.transform); t.position = pts[i];
                arr[i] = t;
            }
            return arr;
        }

        static int Layer(string name, int fallback) { int l = LayerMask.NameToLayer(name); return l >= 0 ? l : fallback; }

        static void Set(object target, string field, object value)
        {
            var f = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (f == null) { Debug.LogError($"[Frontline] Нет поля {target.GetType().Name}.{field}"); return; }
            f.SetValue(target, value);
        }
    }
}
