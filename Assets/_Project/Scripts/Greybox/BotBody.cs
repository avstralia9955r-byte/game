using Frontline.Bots;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

namespace Frontline
{
    public enum LimbKind : byte { Torso, Head, Helmet, Thigh, Shin, UpperArm, Forearm, Gun, Pack }

    /// <summary>Часть тела бота (дочерняя сущность). side: -1 левая, +1 правая, 0 — центральная.</summary>
    public struct BotLimb : IComponentData
    {
        public LimbKind kind;
        public float side;
    }

    /// <summary>Вариант префаба бота по фракции и классу (у каждого класса своё оружие и снаряжение).</summary>
    public struct BotPrefabVariant : IBufferElementData
    {
        public Entity prefab;
        public byte team;
        public SoldierClass soldierClass;
    }

    /// <summary>
    /// Физика «тряпичной куклы» убитого бота (в духе Ravenfield): тело получает импульс от пули или взрыва,
    /// летит по баллистике, опрокидывается вокруг стоп как перевёрнутый маятник, бьётся о землю с отскоком
    /// и затихает. Конечности при этом безвольно болтаются (SoldierPose, deadT).
    /// </summary>
    public struct BotRagdoll : IComponentData
    {
        public float3 pos, vel, axis;
        public float angle, angVel, groundY;
        public byte active;
    }

    /// <summary>
    /// Составной солдат из 13 частей в стиле Ravenfield: рубленые сужающиеся бруски с плоскими гранями
    /// (широкая грудь — узкий пояс, бёдра шире голеней), кубические кисти, массивные сапоги, крупная голова в каске;
    /// матовые цвета формы, чтобы стороны читались издалека. ECS-иерархия: Parent + LinkedEntityGroup.
    /// Оружие класса — та же детальная модель, что у игрока, запечённая в один меш. ~1.1k треугольников.
    /// </summary>
    public static class BotBody
    {
        public const float HipY = 0.92f, Thigh = 0.44f, Shin = 0.48f, Upper = 0.3f, Fore = 0.31f;

        /// <summary>Цвета формы по стороне и карте — общие для ботов и тела игрока.</summary>
        public static void Palette(Faction f, MapTheme theme, out Color uniform, out Color trousers, out Color helmet, out Color gear, out Color skin)
        {
            bool de = f == Faction.Germany;
            if (theme == MapTheme.Winter)
            {
                uniform = de ? new Color(0.85f, 0.87f, 0.9f) : new Color(0.9f, 0.89f, 0.84f);
                trousers = de ? new Color(0.76f, 0.78f, 0.81f) : new Color(0.82f, 0.8f, 0.74f);
                helmet = de ? new Color(0.88f, 0.9f, 0.92f) : new Color(0.9f, 0.9f, 0.86f);
            }
            else
            {
                uniform = de ? new Color(0.34f, 0.4f, 0.42f) : new Color(0.58f, 0.52f, 0.32f);    // фельдграу / хаки
                trousers = de ? new Color(0.27f, 0.31f, 0.33f) : new Color(0.46f, 0.41f, 0.26f);
                helmet = de ? new Color(0.27f, 0.31f, 0.3f) : new Color(0.3f, 0.38f, 0.24f);
            }
            gear = de ? new Color(0.15f, 0.13f, 0.1f) : new Color(0.42f, 0.35f, 0.22f);
            skin = new Color(0.87f, 0.67f, 0.52f);
        }

        public static Entity CreatePrefab(EntityManager em, Faction f, MapTheme theme, SoldierClass cls)
        {
            Palette(f, theme, out var uniform, out var trousers, out var helmet, out var gear, out var skin);
            const float matte = 0.08f;   // Ravenfield — матовые плоские цвета без бликов
            var mats = new[]
            {
                ProceduralModels.Mat(uniform, matte), ProceduralModels.Mat(trousers, matte), ProceduralModels.Mat(skin, matte),
                ProceduralModels.Mat(helmet, 0.22f), ProceduralModels.Mat(ProceduralModels.Wood, 0.3f), ProceduralModels.Mat(gear, matte)
            };
            var meshes = new[]
            {
                TorsoMesh(cls), LimbMesh("thigh", 0.17f, Thigh, 0), LimbMesh("shin", 0.13f, Shin, 1), LimbMesh("upper", 0.115f, Upper, 0),
                LimbMesh("fore", 0.095f, Fore, 2), HeadMesh(), HelmetMesh(f), GunMesh(f, cls), PackMesh(cls)
            };
            // Детальная модель оружия класса (та же, что у игрока), одним мешем с палитрой
            int gunMat = 4, gunMesh = 7;
            if (GunBaker.TryGet(f, cls, false, out var bakedMesh, out var bakedMat))
            {
                System.Array.Resize(ref mats, mats.Length + 1); mats[^1] = bakedMat; gunMat = mats.Length - 1;
                System.Array.Resize(ref meshes, meshes.Length + 1); meshes[^1] = bakedMesh; gunMesh = meshes.Length - 1;
            }
            var rma = new RenderMeshArray(mats, meshes);
            // На телефоне боты тени не отбрасывают и не принимают: 128 солдат × 13 частей — главный расход теней
            bool mobile = Application.isMobilePlatform;
            var desc = new RenderMeshDescription(mobile ? ShadowCastingMode.Off : ShadowCastingMode.On, !mobile);

            var root = em.CreateEntity();
            em.AddComponentData(root, LocalTransform.Identity);
            em.AddComponentData(root, new LocalToWorld { Value = float4x4.identity });
            AddBotComponents(em, root, f);
            em.AddBuffer<LinkedEntityGroup>(root).Add(root);

            void Limb(LimbKind k, float side, int mat, int mesh)
            {
                var e = em.CreateEntity();
                em.AddComponentData(e, LocalTransform.Identity);
                em.AddComponentData(e, new Parent { Value = root });
                em.AddComponentData(e, new LocalToWorld { Value = float4x4.identity });
                em.AddComponentData(e, new BotLimb { kind = k, side = side });
                RenderMeshUtility.AddComponents(e, em, desc, rma, MaterialMeshInfo.FromRenderMeshArrayIndices(mat, mesh));
                em.AddComponent<Prefab>(e);
                em.GetBuffer<LinkedEntityGroup>(root).Add(e);
            }

            Limb(LimbKind.Torso, 0, 0, 0);
            Limb(LimbKind.Head, 0, 2, 5);
            Limb(LimbKind.Helmet, 0, 3, 6);
            Limb(LimbKind.Thigh, -1, 1, 1); Limb(LimbKind.Thigh, 1, 1, 1);
            Limb(LimbKind.Shin, -1, 1, 2); Limb(LimbKind.Shin, 1, 1, 2);
            Limb(LimbKind.UpperArm, -1, 0, 3); Limb(LimbKind.UpperArm, 1, 0, 3);
            Limb(LimbKind.Forearm, -1, 0, 4); Limb(LimbKind.Forearm, 1, 0, 4);
            Limb(LimbKind.Gun, 0, gunMat, gunMesh);
            Limb(LimbKind.Pack, 0, 5, 8);

            em.AddComponent<Prefab>(root);
            return root;
        }

        public static void AddBotComponents(EntityManager em, Entity e, Faction f)
        {
            em.AddComponentData(e, new BotIdentity { team = (byte)f });
            em.AddComponentData(e, new BotHealth { value = 50, max = 50 });   // вдвое меньше прежнего
            em.AddComponentData(e, new BotDead());
            em.SetComponentEnabled<BotDead>(e, false);
            em.AddComponentData(e, new BotMotion());
            em.AddComponentData(e, new BotBrain());
            em.AddComponentData(e, new BotPerception { targetActorId = -1, candidateActorId = -1 });
            em.AddComponentData(e, new BotWeapon());
            em.AddComponentData(e, new BotSkill());
            em.AddBuffer<BotPathPoint>(e);
            em.AddComponentData(e, new BotPathCursor());
            em.AddComponentData(e, new BotPathRequest());
            em.AddComponentData(e, new BotRagdoll());
            em.AddComponentData(e, new BotInVehicle());
            em.SetComponentEnabled<BotInVehicle>(e, false);
            em.AddComponentData(e, new BotLodLevel());
        }

        // ---------- Меши ----------

        static readonly System.Collections.Generic.Dictionary<string, Mesh> _meshes = new();

        static Mesh Combine(string name, params (Mesh mesh, Matrix4x4 m)[] parts)
        {
            if (_meshes.TryGetValue(name, out var cached) && cached) return cached;
            var ci = new CombineInstance[parts.Length];
            for (int i = 0; i < parts.Length; i++) ci[i] = new CombineInstance { mesh = parts[i].mesh, transform = parts[i].m };
            var mesh = new Mesh { name = name };
            mesh.CombineMeshes(ci, true, true);
            mesh.RecalculateBounds();
            return _meshes[name] = mesh;
        }

        static Matrix4x4 TRS(Vector3 p, Vector3 e, Vector3 s) => Matrix4x4.TRS(p, Quaternion.Euler(e), s);
        static Mesh Bv => MeshUtil.Bevel(0.12f);
        static Mesh Soft => MeshUtil.Bevel(0.26f);

        /// <summary>
        /// Торс (пивот — таз): клин груди (широкие плечи → узкий пояс), таз, ремень с подсумками, шея;
        /// у пулемётчика — лента через плечо, у снайпера — плащ-накидка.
        /// </summary>
        public static Mesh TorsoMesh(SoldierClass cls)
        {
            var parts = new System.Collections.Generic.List<(Mesh, Matrix4x4)>
            {
                (MeshUtil.Taper(1.08f, 1.02f), TRS(new(0, 0.02f, 0), default, new(0.33f, 0.2f, 0.22f))),            // таз
                (MeshUtil.Taper(1.34f, 1.12f, 0.03f), TRS(new(0, 0.34f, 0), default, new(0.33f, 0.44f, 0.22f))),    // клин груди
                (Soft, TRS(new(0, 0.555f, 0.01f), default, new(0.47f, 0.08f, 0.25f))),                             // линия плеч
                (MeshUtil.Cylinder(6), TRS(new(0, 0.62f, 0.01f), default, new(0.12f, 0.09f, 0.12f))),              // шея
                (Bv, TRS(new(0, 0.12f, 0), default, new(0.36f, 0.05f, 0.24f))),                                    // ремень
                (Bv, TRS(new(-0.11f, 0.14f, 0.125f), default, new(0.09f, 0.08f, 0.05f))),                          // подсумки
                (Bv, TRS(new(0.11f, 0.14f, 0.125f), default, new(0.09f, 0.08f, 0.05f))),
                (Bv, TRS(new(0.2f, 0.06f, -0.05f), new(0, 0, -6), new(0.06f, 0.16f, 0.09f))),                      // фляга
            };
            if (cls == SoldierClass.Support)
                for (int i = 0; i < 6; i++) parts.Add((MeshUtil.Cylinder(5), TRS(new(-0.15f + i * 0.06f, 0.5f - i * 0.07f, 0.125f), new(0, 0, 35), new(0.02f, 0.07f, 0.02f))));
            if (cls == SoldierClass.Recon)
                parts.Add((MeshUtil.Taper(1.3f, 1.1f), TRS(new(0, 0.33f, -0.01f), default, new(0.37f, 0.5f, 0.26f))));   // плащ-накидка
            return Combine("RfTorso_" + cls, parts.ToArray());
        }

        /// <summary>
        /// Сегмент конечности с пивотом в верхнем суставе, сужается книзу. kind: 0 — простой (бедро, плечо),
        /// 1 — голень с массивным сапогом, 2 — предплечье с кубической кистью.
        /// </summary>
        public static Mesh LimbMesh(string name, float thick, float len, int kind) => kind switch
        {
            1 => Combine("Rf_" + name,
                    (MeshUtil.Taper(1.22f, 1.22f), TRS(new(0, -len * 0.36f, 0), default, new(thick * 0.88f, len * 0.72f, thick * 0.88f))),
                    (MeshUtil.Taper(1.05f, 1.05f), TRS(new(0, -len * 0.8f, 0.005f), default, new(thick * 1.12f, len * 0.26f, thick * 1.18f))),   // голенище
                    (Bv, TRS(new(0, -len * 0.96f, 0.05f), default, new(thick * 1.1f, len * 0.11f, thick * 2.1f)))),                              // подошва и носок
            2 => Combine("Rf_" + name,
                    (MeshUtil.Taper(1.25f, 1.25f), TRS(new(0, -len * 0.38f, 0), default, new(thick * 0.85f, len * 0.76f, thick * 0.85f))),
                    (Bv, TRS(new(0, -len * 0.77f, 0), default, new(thick * 1.12f, len * 0.07f, thick * 1.12f))),                                 // манжета
                    (Soft, TRS(new(0, -len * 0.9f, 0.005f), default, new(thick * 0.92f, thick * 1.15f, thick * 1.05f)))),                        // кисть-кубик
            _ => Combine("Rf_" + name,
                    (MeshUtil.Taper(1.3f, 1.3f), TRS(new(0, -len * 0.5f, 0), default, new(thick * 0.82f, len, thick * 0.82f))),
                    (MeshUtil.Ico(), TRS(new(0, -len, 0), default, new(thick * 0.8f, thick * 0.8f, thick * 0.8f)))),                             // сустав (колено/локоть)
        };

        public static Mesh HeadMesh() => Combine("RfHead",
            (Soft, TRS(Vector3.zero, default, new(0.21f, 0.24f, 0.22f))),
            (Bv, TRS(new(0, -0.075f, 0.02f), default, new(0.17f, 0.08f, 0.17f))),        // челюсть
            (Bv, TRS(new(0, -0.01f, 0.112f), default, new(0.04f, 0.055f, 0.04f))));    // нос — задаёт направление лица

        /// <summary>Каска: Stahlhelm — купол с развёрнутым назатыльником и козырьком; СШ-40 — высокий купол с узким бортиком.</summary>
        public static Mesh HelmetMesh(Faction f) => f == Faction.Germany
            ? Combine("RfStahlhelm", (MeshUtil.Ico(), TRS(new(0, 0.035f, 0), default, new(0.29f, 0.22f, 0.31f))),
                                     (MeshUtil.Frustum(8, 0.85f), TRS(new(0, -0.045f, -0.02f), default, new(0.37f, 0.08f, 0.38f))),
                                     (Bv, TRS(new(0, -0.03f, 0.14f), new(18, 0, 0), new(0.21f, 0.016f, 0.08f))))
            : Combine("RfSSh40", (MeshUtil.Ico(), TRS(new(0, 0.05f, 0), default, new(0.28f, 0.26f, 0.3f))),
                                 (MeshUtil.Frustum(8, 0.92f), TRS(new(0, -0.04f, 0), default, new(0.32f, 0.045f, 0.33f))));
        /// <summary>Оружие бота — силуэт по классу и фракции (пивот — у рукоятки, ствол вдоль +Z).</summary>
        public static Mesh GunMesh(Faction f, SoldierClass cls)
        {
            var c6 = MeshUtil.Cylinder(6);
            bool de = f == Faction.Germany;
            switch (cls)
            {
                case SoldierClass.Engineer:
                    return de
                        ? Combine("BotGun_mp40", (Bv, TRS(new(0, 0, 0.1f), default, new(0.045f, 0.055f, 0.36f))), (c6, TRS(new(0, 0, 0.36f), new(90, 0, 0), new(0.022f, 0.18f, 0.022f))),
                                                 (Bv, TRS(new(0, -0.14f, 0.17f), default, new(0.025f, 0.22f, 0.035f))), (Bv, TRS(new(0, -0.07f, -0.04f), new(-12, 0, 0), new(0.035f, 0.1f, 0.04f))))
                        : Combine("BotGun_ppsh", (Bv, TRS(new(0, -0.02f, -0.12f), new(-6, 0, 0), new(0.045f, 0.09f, 0.4f))), (Bv, TRS(new(0, 0.01f, 0.26f), default, new(0.045f, 0.048f, 0.3f))),
                                                 (MeshUtil.Cylinder(10), TRS(new(0, -0.08f, 0.1f), new(0, 0, 90), new(0.16f, 0.05f, 0.16f))));
                case SoldierClass.Support:
                    return de
                        ? Combine("BotGun_mg42", (Bv, TRS(new(0, 0, 0.05f), default, new(0.06f, 0.08f, 0.38f))), (Bv, TRS(new(0, 0.01f, 0.45f), default, new(0.05f, 0.056f, 0.44f))),
                                                 (Bv, TRS(new(0, -0.01f, -0.28f), new(-4, 0, 0), new(0.038f, 0.11f, 0.24f))), (c6, TRS(new(-0.06f, -0.16f, 0.52f), new(12, 0, -22), new(0.014f, 0.3f, 0.014f))),
                                                 (c6, TRS(new(0.06f, -0.16f, 0.52f), new(12, 0, 22), new(0.014f, 0.3f, 0.014f))), (MeshUtil.Cylinder(10), TRS(new(-0.085f, -0.01f, 0.04f), new(90, 0, 0), new(0.125f, 0.09f, 0.125f))))
                        : Combine("BotGun_dp28", (Bv, TRS(new(0, 0, 0.05f), default, new(0.05f, 0.06f, 0.32f))), (c6, TRS(new(0, 0.005f, 0.42f), new(90, 0, 0), new(0.052f, 0.44f, 0.052f))),
                                                 (Bv, TRS(new(0, -0.04f, -0.3f), new(-8, 0, 0), new(0.042f, 0.12f, 0.26f))), (MeshUtil.Cylinder(12), TRS(new(0, 0.06f, 0.06f), default, new(0.27f, 0.024f, 0.27f))),
                                                 (c6, TRS(new(-0.06f, -0.15f, 0.5f), new(12, 0, -22), new(0.014f, 0.3f, 0.014f))), (c6, TRS(new(0.06f, -0.15f, 0.5f), new(12, 0, 22), new(0.014f, 0.3f, 0.014f))));
                case SoldierClass.Recon:
                    return Combine("BotGun_sniper_" + f, (Bv, TRS(new(0, -0.03f, 0.05f), default, new(0.048f, 0.065f, 0.75f))), (Bv, TRS(new(0, -0.07f, -0.4f), new(-8, 0, 0), new(0.045f, 0.13f, 0.24f))),
                                                         (c6, TRS(new(0, 0.01f, 0.62f), new(90, 0, 0), new(0.022f, 0.4f, 0.022f))), (c6, TRS(new(0, 0.055f, 0.05f), new(90, 0, 0), new(0.034f, 0.24f, 0.034f))));
                default:
                    return de
                        ? Combine("BotGun_stg", (Bv, TRS(new(0, 0.01f, 0.06f), default, new(0.045f, 0.07f, 0.36f))), (Bv, TRS(new(0, 0, -0.24f), default, new(0.04f, 0.09f, 0.24f))),
                                                (c6, TRS(new(0, 0.02f, 0.42f), new(90, 0, 0), new(0.022f, 0.36f, 0.022f))), (Bv, TRS(new(0, -0.13f, 0.14f), new(-22, 0, 0), new(0.03f, 0.2f, 0.05f))),
                                                (Bv, TRS(new(0, -0.08f, -0.05f), new(-14, 0, 0), new(0.035f, 0.1f, 0.045f))))
                        : Combine("BotGun_svt", (Bv, TRS(new(0, -0.03f, 0.08f), default, new(0.048f, 0.065f, 0.7f))), (Bv, TRS(new(0, -0.07f, -0.36f), new(-8, 0, 0), new(0.045f, 0.13f, 0.22f))),
                                                (c6, TRS(new(0, 0.012f, 0.6f), new(90, 0, 0), new(0.022f, 0.44f, 0.022f))), (Bv, TRS(new(0, -0.1f, 0.06f), default, new(0.032f, 0.09f, 0.07f))));
            }
        }

        public static Mesh PackMesh(SoldierClass cls) => cls switch
        {
            SoldierClass.Engineer => Combine("BotPack_eng", (Bv, TRS(Vector3.zero, default, new(0.32f, 0.36f, 0.15f))),
                                                            (MeshUtil.Cylinder(6), TRS(new(0.12f, 0.12f, -0.09f), new(0, 0, 10), new(0.03f, 0.5f, 0.03f))),   // черенок лопаты
                                                            (Bv, TRS(new(0.14f, -0.16f, -0.09f), default, new(0.12f, 0.16f, 0.02f)))),
            SoldierClass.Support => Combine("BotPack_sup", (Bv, TRS(Vector3.zero, default, new(0.3f, 0.3f, 0.13f))),
                                                           (Bv, TRS(new(0, -0.22f, 0.02f), default, new(0.2f, 0.12f, 0.1f)))),                                // коробка с лентой
            SoldierClass.Recon => Combine("BotPack_rec", (Bv, TRS(new(0, -0.05f, 0), default, new(0.24f, 0.2f, 0.1f)))),
            _ => Combine("BotPack_std", (Bv, TRS(Vector3.zero, default, new(0.3f, 0.32f, 0.13f))),
                                        (MeshUtil.Cylinder(8), TRS(new(0, 0.2f, 0), new(0, 0, 90), new(0.1f, 0.32f, 0.1f)))),                                 // скатка
        };
    }

    /// <summary>
    /// Процедурная анимация солдата прямой кинематикой: шаг с подъёмом колена, маятник рук, наклон корпуса на бегу,
    /// присед на согнутых ногах, прицельная и походная стойки, перезарядка (оружие опущено), отдача при выстреле,
    /// вздрагивание от попадания, расслабленная поза при смерти. Работает до TransformSystemGroup.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    public partial struct BotLimbAnimationSystem : ISystem
    {
        ComponentLookup<BotMotion> _motion;
        ComponentLookup<BotPerception> _perc;
        ComponentLookup<BotIdentity> _id;
        ComponentLookup<BotHealth> _hp;
        ComponentLookup<BotWeapon> _weapon;

        public void OnCreate(ref SystemState state)
        {
            _motion = state.GetComponentLookup<BotMotion>(true);
            _perc = state.GetComponentLookup<BotPerception>(true);
            _id = state.GetComponentLookup<BotIdentity>(true);
            _hp = state.GetComponentLookup<BotHealth>(true);
            _weapon = state.GetComponentLookup<BotWeapon>(true);
        }

        int _frame;

        public void OnUpdate(ref SystemState state)
        {
            _frame++;
            var cam = UnityEngine.Camera.main;
            float3 viewer = cam ? (float3)cam.transform.position : float3.zero;
            state.Dependency = new GaitJob { dt = SystemAPI.Time.DeltaTime }.ScheduleParallel(state.Dependency);
            _motion.Update(ref state); _perc.Update(ref state); _id.Update(ref state); _hp.Update(ref state); _weapon.Update(ref state);
            state.Dependency = new LimbJob
            {
                motion = _motion, perc = _perc, ids = _id, hps = _hp, weapons = _weapon, time = (float)SystemAPI.Time.ElapsedTime,
                frame = _frame, viewer = viewer, hasViewer = cam != null, lod = BotLod.Scale
            }.ScheduleParallel(state.Dependency);
        }

        /// <summary>Накопление фазы шага у каждого живого бота.</summary>
        [BurstCompile]
        [WithNone(typeof(BotDead))]
        partial struct GaitJob : IJobEntity
        {
            public float dt;
            void Execute(ref BotMotion m)
            {
                m.gait += SoldierPose.Cadence(math.length(m.velocity.xz)) * dt;
                if (m.gait > 1000f) m.gait -= 2f * math.PI * 159f;   // держим число маленьким (точность float)
            }
        }

        [BurstCompile]
        partial struct LimbJob : IJobEntity
        {
            [ReadOnly] public ComponentLookup<BotMotion> motion;
            [ReadOnly] public ComponentLookup<BotPerception> perc;
            [ReadOnly] public ComponentLookup<BotIdentity> ids;
            [ReadOnly] public ComponentLookup<BotHealth> hps;
            [ReadOnly] public ComponentLookup<BotWeapon> weapons;
            public float time;
            public int frame;
            public float3 viewer;
            public bool hasViewer;
            public float lod;

            void Execute(ref LocalTransform lt, in BotLimb limb, in Parent parent)
            {
                var root = parent.Value;
                if (!motion.HasComponent(root)) return;
                var m = motion[root]; var p = perc[root]; var hp = hps[root]; var w = weapons[root];
                // Дальние боты: поза конечностей обновляется реже (150+ м — раз в 3 кадра, 300+ м — раз в 6);
                // на таком расстоянии разницы не видно, а 512 ботов × 13 частей — заметная работа
                bool simple = false;
                if (hasViewer)
                {
                    float d2 = math.distancesq(m.lastPos, viewer);
                    float f2 = lod * lod;
                    // Дальше — реже и проще: 90+ м — раз в 2 кадра, 150+ — раз в 3, 300+ — раз в 6
                    int every = d2 > 300f * 300f * f2 ? 6 : d2 > 150f * 150f * f2 ? 3 : d2 > 90f * 90f * f2 ? 2 : 1;
                    if (every > 1 && (frame + (int)(ids[root].seed % 97u)) % every != 0) return;
                    simple = d2 > 150f * 150f * f2;
                }
                bool dead = hp.value <= 0;
                float sinceShot = time - (w.nextFire - w.fireInterval);
                bool reloading = !dead && w.reloadEnd > time;
                // Скорость в осях тела (x — вправо, y — вперёд)
                float cy = math.cos(m.yaw), sy = math.sin(m.yaw);
                float2 lv = new float2(m.velocity.x * cy - m.velocity.z * sy, m.velocity.x * sy + m.velocity.z * cy);
                float3 toT = p.targetPos - m.lastPos;
                var input = new PoseInput
                {
                    localVel = lv, speed = math.length(m.velocity.xz), crouch = m.crouched ? 1 : 0,
                    sprint = math.saturate((math.length(m.velocity.xz) - 4.2f) / 1.2f),
                    aim = (!dead && p.targetVisible && !reloading) ? 1 : 0,
                    aimPitch = math.clamp(math.atan2(toT.y, math.max(1f, math.length(toT.xz))), -0.6f, 0.6f),
                    recoil = dead ? 0 : math.saturate(1f - sinceShot / 0.09f) * (sinceShot >= 0 ? 1 : 0),
                    reload = reloading ? 1 : 0,
                    flinch = dead ? 0 : math.saturate(1f - (time - hp.lastHitTime) / 0.25f),
                    deadK = dead ? math.saturate((time - hp.lastHitTime) / 0.5f) : 0,
                    deadT = dead ? time - hp.lastHitTime : 0,
                    gait = m.gait,
                    time = time, seed = ids[root].seed, lookIdle = 1
                };
                if (simple)
                {
                    // Простая анимация вдали: без отдачи, вздрагиваний, поворота головы и прицеливания по высоте
                    input.recoil = 0; input.flinch = 0; input.lookIdle = 0; input.aimPitch = 0;
                }
                SoldierPose.Pose(limb.kind, limb.side, input, out var pos, out var rot);
                lt.Position = pos; lt.Rotation = rot; lt.Scale = 1f;
            }
        }
    }

    /// <summary>
    /// «Тряпичная кукла» убитого бота (как в Ravenfield): импульс от пули/взрыва, баллистический полёт,
    /// опрокидывание вокруг стоп под действием тяжести, удар о землю с отскоком и трением. Интегрируется мелкими
    /// шагами, поэтому устойчиво и при низком FPS. Боты с VAT-анимацией используют клип смерти и сюда не попадают.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    public partial struct BotDeathFallSystem : ISystem
    {
        NativeArray<float> _noGround;

        public void OnCreate(ref SystemState state) => _noGround = new NativeArray<float>(4, Allocator.Persistent);
        public void OnDestroy(ref SystemState state) { if (_noGround.IsCreated) _noGround.Dispose(); }

        public void OnUpdate(ref SystemState state)
        {
            bool ground = WorldStores.Ready && WorldStores.Height.IsCreated && WorldStores.Height.Length >= 4;
            new FallJob
            {
                dt = math.min(SystemAPI.Time.DeltaTime, 0.1f),
                hasGround = ground,
                height = ground ? WorldStores.Height : _noGround,
                gridSize = ground ? WorldStores.GridSize : new int2(2, 2),
                origin = ground ? WorldStores.GridOrigin : float2.zero,
                cell = ground ? WorldStores.CellSize : 1f
            }.ScheduleParallel();
        }

        [BurstCompile]
        [WithAll(typeof(BotDead))]
        [WithNone(typeof(VatClip))]
        partial struct FallJob : IJobEntity
        {
            public float dt, cell;
            public bool hasGround;
            [ReadOnly] public NativeArray<float> height;
            public int2 gridSize;
            public float2 origin;

            void Execute(ref LocalTransform lt, ref BotRagdoll rd, in BotMotion m)
            {
                if (rd.active == 0)
                {
                    // Смерть без импульса (например, от урона без направления) — просто оседает вперёд/назад
                    float sgn = m.pitch >= 0 ? 1f : -1f;
                    float3 fwd = new float3(math.sin(m.yaw), 0, math.cos(m.yaw)) * sgn;
                    rd = new BotRagdoll { active = 1, pos = lt.Position, groundY = lt.Position.y, angle = 0.02f, angVel = 0.8f,
                                          axis = math.normalizesafe(math.cross(new float3(0, 1, 0), fwd), new float3(1, 0, 0)) };
                }
                const float maxAngle = 1.5f;              // лежит на земле (≈ 86°)
                int steps = math.max(1, (int)math.ceil(dt * 120f));
                float h = dt / steps;
                for (int i = 0; i < steps; i++)
                {
                    bool air = rd.pos.y > rd.groundY + 0.002f || rd.vel.y > 0.01f;
                    rd.vel.y -= 9.81f * h;
                    rd.pos += rd.vel * h;
                    // Земля под телом — по сетке высот (тело катится по склону и не висит над оврагом)
                    if (hasGround) rd.groundY = FlowField.GroundHeight(height, gridSize, origin, cell, rd.pos);
                    if (rd.pos.y <= rd.groundY)
                    {
                        rd.pos.y = rd.groundY;
                        if (rd.vel.y < -2.5f) { rd.vel.y *= -0.28f; rd.vel.xz *= 0.5f; rd.angVel *= 0.6f; }   // удар и отскок
                        else { rd.vel.y = 0; rd.vel.xz *= math.max(0f, 1f - 7f * h); }                      // трение о землю
                    }
                    // Опрокидывание: на земле — перевёрнутый маятник вокруг стоп, в полёте — свободное вращение
                    if (!air) rd.angVel += 10.3f * math.sin(math.max(rd.angle, 0.02f)) * h;
                    rd.angVel *= 1f - 0.4f * h;
                    rd.angle += rd.angVel * h;
                    if (rd.angle >= maxAngle)
                    {
                        rd.angle = maxAngle;
                        if (!air) rd.angVel = rd.angVel > 1.4f ? -rd.angVel * 0.2f : 0f;   // тело шлёпается и замирает
                        else rd.angVel = math.min(rd.angVel, 0f);
                    }
                    if (rd.angle < 0.02f) { rd.angle = 0.02f; rd.angVel = math.max(rd.angVel, 0.3f); }
                }
                // Лежащее тело приподнято на толщину корпуса, чтобы не уходило в землю
                lt.Position = rd.pos + new float3(0, 0.13f * math.sin(rd.angle), 0);
                lt.Rotation = math.mul(quaternion.AxisAngle(rd.axis, rd.angle), quaternion.RotateY(m.yaw));
            }
        }
    }
}
