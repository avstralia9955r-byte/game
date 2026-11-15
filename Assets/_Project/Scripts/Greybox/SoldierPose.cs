using Unity.Mathematics;

namespace Frontline
{
    /// <summary>Входные данные позы солдата (одинаковы для ECS-ботов и тела игрока).</summary>
    public struct PoseInput
    {
        public float2 localVel;     // скорость в осях тела: x — вправо, y — вперёд (м/с)
        public float speed, crouch, sprint, aim, aimPitch, recoil, reload, flinch, deadK, deadT, time, airborne, lookIdle;
        public float gait;          // фаза шага, накопленная по времени (рад): d(gait)/dt = SoldierPose.Cadence(speed)
        public uint seed;
    }

    /// <summary>
    /// Процедурная поза солдата прямой кинематикой (Burst-совместимо, без статических полей). Манера движений —
    /// как в Ravenfield: размашистый бег с сильным наклоном корпуса и высоким коленом, пружинящая походка,
    /// широкая боевая стойка при стрельбе, после гибели конечности безвольно болтаются (затухающие колебания),
    /// а само тело валит физика BotDeathFallSystem.
    /// • Ноги идут по направлению движения, а корпус смотрит на цель: при стрейфе бёдра разворачиваются вбок,
    ///   при отходе назад шаг «обращается»; колено поднимается в фазе переноса, стопа ставится с перекатом.
    /// • Бег: длинный шаг, наклон корпуса, руки работают у груди, оружие в «положении для бега».
    /// • Присед: колено правой ноги на земле, левая согнута; при движении — «гусиный шаг».
    /// • Прицел: приклад в плече, левая рука под цевьём, наклон головы к прицелу, отдача корпусом.
    /// • Перезарядка: оружие опущено и развёрнуто, левая рука ходит к магазину.
    /// • Попадание — вздрагивание; смерть — руки и голова безвольно опадают; прыжок — колени подтянуты.
    /// </summary>
    public static class SoldierPose
    {
        public const float HipY = 0.92f, Thigh = 0.44f, Shin = 0.48f, Upper = 0.3f, Fore = 0.31f;

        /// <summary>
        /// Частота шага (рад/с фазы; полный цикл = два шага) под скорость, как у живого человека:
        /// ходьба 2 м/с ≈ 2,2 шага/с, бег 4,5 м/с ≈ 2,8 шага/с, спринт 6 м/с ≈ 3,2 шага/с.
        /// </summary>
        public static float Cadence(float speed) => speed > 0.15f ? speed / StepLength(speed) * math.PI : 0f;

        /// <summary>Длина одного шага, м: ~0,7 м шагом, ~1,9 м в спринте.</summary>
        public static float StepLength(float speed) => math.lerp(0.7f, 1.9f, math.saturate((speed - 1.2f) / 4.6f));

        public static void Pose(LimbKind kind, float s, in PoseInput p, out float3 pos, out quaternion rot)
        {
            float speed = p.speed;
            float walk = math.saturate(speed / 4.5f);
            float sprint = p.sprint;
            float crouch = p.crouch;
            float aim = p.aim * (1 - sprint);
            bool dead = p.deadK > 0;
            if (dead) { walk = 0; crouch = 0; sprint = 0; }

            // Направление шага относительно корпуса
            float2 lv = p.localVel;
            bool back = lv.y < -0.25f * speed && speed > 0.3f;
            float legYaw = speed > 0.3f ? math.atan2(back ? -lv.x : lv.x, back ? -lv.y : lv.y) : 0;
            legYaw = math.clamp(legYaw, -1.15f, 1.15f);
            float dirSign = back ? -1f : 1f;

            // Фаза шага — накопленная (p.gait), а не time × cadence: при time в сотни секунд малейшее изменение
            // скорости сдвигало фазу на десятки радиан, и ноги «молотили» с бешеной частотой.
            float phase = p.gait + (p.seed % 628) * 0.01f;
            // Размах бедра — по длине шага (широкий на бегу, короткий шагом)
            float stride = dead ? 0f : math.clamp(StepLength(speed) * 0.42f, 0.28f, 0.85f) * math.saturate(speed / 1.2f);
            float stance = aim * (1 - walk);                        // боевая стойка на месте: ноги шире, колени согнуты

            // Корпус: пружинящий шаг, на бегу — сильный наклон вперёд
            float bob = math.abs(math.cos(phase)) * (0.045f + 0.045f * sprint) * walk;
            float3 hipC = new float3(0, HipY - 0.38f * crouch + bob - 0.12f * p.airborne - 0.05f * stance, 0);
            float breathe = 0.02f * math.sin(p.time * 1.4f + p.seed) * (1 - walk);
            float torsoPitch = 0.15f * walk + 0.4f * sprint + 0.3f * crouch + 0.08f * aim - 0.28f * p.flinch - 0.07f * p.recoil + breathe;
            float torsoRoll = -lv.x * 0.035f * (1 - sprint);
            float torsoTwist = -legYaw * 0.35f * walk + math.sin(phase) * 0.08f * walk * (1 + sprint);
            quaternion rotT = math.mul(quaternion.RotateY(torsoTwist * 0.4f), math.mul(quaternion.RotateX(torsoPitch), quaternion.RotateZ(torsoRoll)));
            quaternion legFrame = quaternion.RotateY(legYaw);

            switch (kind)
            {
                case LimbKind.Torso:
                    pos = hipC; rot = rotT; return;

                case LimbKind.Pack:
                    pos = hipC + math.mul(rotT, new float3(0, 0.32f, -0.19f)); rot = rotT; return;

                case LimbKind.Head:
                case LimbKind.Helmet:
                {
                    float idleLook = p.lookIdle * math.sin(p.time * 0.35f + p.seed * 0.7f) * 0.4f * (1 - walk) * (1 - aim);
                    float pitch = aim > 0 ? -p.aimPitch * 0.8f + 0.12f : math.sin(p.time * 0.6f + p.seed) * 0.08f;
                    if (dead) pitch += Flail(p, 12f, 1.3f) * 0.7f;    // голова мотается при падении
                    quaternion hr = math.mul(rotT, math.mul(quaternion.RotateY(idleLook + aim * 0.12f), math.mul(quaternion.RotateX(pitch + p.deadK * 0.6f), quaternion.RotateZ(-aim * 0.12f))));
                    float3 neck = hipC + math.mul(rotT, new float3(0, 0.66f, 0.01f));
                    pos = neck + math.mul(hr, new float3(0, kind == LimbKind.Head ? 0.07f : 0.13f, 0));
                    rot = hr; return;
                }

                case LimbKind.Thigh:
                case LimbKind.Shin:
                {
                    float ph = phase + (s > 0 ? math.PI : 0);
                    float swing = math.sin(ph) * stride * dirSign;
                    float lift = math.max(0, math.cos(ph)) * (1.0f + 0.85f * sprint) * walk;         // высокое колено в фазе переноса
                    // Присед: правое колено на земле, левая нога — упор
                    float kneelThigh = s > 0 ? 0.25f : 1.35f, kneelKnee = s > 0 ? 1.95f : 1.55f;
                    float fwd = math.lerp(swing, kneelThigh + swing * 0.5f, crouch) + 0.9f * p.airborne + p.deadK * (s > 0 ? 0.3f : -0.1f)
                              + stance * (s > 0 ? -0.18f : 0.22f);
                    float knee = math.lerp(lift + 0.12f, kneelKnee + lift * 0.4f, crouch) + 1.1f * p.airborne + p.deadK * 0.5f + stance * 0.28f;
                    float spread = s * (0.1f + 0.05f * stance);
                    if (dead)
                    {
                        fwd += Flail(p, 9f + s, 2.0f + s * 0.4f) * 0.9f;
                        knee += math.abs(Flail(p, 7.5f, 0.7f + s)) * 1.1f;
                        spread += s * 0.08f * p.deadK;
                    }
                    float3 hip = hipC + math.mul(legFrame, new float3(spread, 0, 0));
                    quaternion tr = math.mul(legFrame, quaternion.RotateX(-fwd));
                    if (kind == LimbKind.Thigh) { pos = hip; rot = tr; }
                    else { pos = hip + math.mul(tr, new float3(0, -Thigh, 0)); rot = math.mul(legFrame, quaternion.RotateX(-fwd + knee)); }
                    return;
                }

                case LimbKind.UpperArm:
                case LimbKind.Forearm:
                {
                    bool right = s > 0;
                    // При вскидке левое плечо выходит вперёд (корпус доворачивается к прикладу)
                    float3 shoulder = hipC + math.mul(rotT, new float3(s * 0.2f, 0.53f, right ? -0.02f * aim : 0.09f * aim));
                    if (dead) { DeadArm(kind, s, p, shoulder, rotT, out pos, out rot); return; }
                    // Оружие в руках: кисти ставятся на рукоять и цевьё, плечо и предплечье решаются IK
                    GunPose(p, hipC, rotT, phase, sprint, aim, false, out float3 gp, out quaternion gr);
                    float3 hand = right
                        ? gp + math.mul(gr, new float3(0.0f, -0.07f, -0.07f))
                        : gp + math.mul(gr, new float3(-0.01f, -0.035f, 0.2f));
                    if (!right && p.reload > 0)   // левая рука ходит к магазину
                        hand = math.lerp(hand, gp + math.mul(gr, new float3(-0.03f, -0.16f, 0.08f)), math.saturate(p.reload) * (0.5f + 0.5f * math.sin(p.time * 6.5f)));
                    float3 pole = math.mul(rotT, new float3(s * 0.9f, -1f, right ? -0.5f : -0.2f));
                    TwoBone(shoulder, hand, pole, Upper, Fore, out float3 elbow);
                    if (kind == LimbKind.UpperArm) { pos = shoulder; rot = BoneRot(elbow - shoulder, pole); }
                    else { pos = elbow; rot = BoneRot(hand - elbow, pole); }
                    return;
                }

                default: // Gun
                    GunPose(p, hipC, rotT, phase, sprint, aim, dead, out pos, out rot);
                    return;
            }
        }

        /// <summary>Положение и ориентация оружия в осях тела (начало — у рукояти, +Z — ствол).</summary>
        static void GunPose(in PoseInput p, float3 hipC, quaternion rotT, float phase, float sprint, float aim, bool dead, out float3 pos, out quaternion rot)
        {
            float3 aimPos = new float3(0.08f, 0.5f, 0.34f - 0.045f * p.recoil);
            float3 carryPos = new float3(0.07f, 0.3f, 0.3f);
            float3 runPos = new float3(0.0f, 0.42f, 0.26f) + new float3(math.sin(phase) * 0.035f, math.abs(math.cos(phase)) * 0.03f, 0f);   // оружие «качается» в такт бегу
            quaternion aimRot = quaternion.RotateX(-p.aimPitch - 0.07f * p.recoil);
            quaternion carryRot = math.mul(quaternion.RotateY(-0.55f), quaternion.RotateX(0.5f + p.reload * 0.4f));
            quaternion runRot = math.mul(quaternion.RotateY(-1.1f), math.mul(quaternion.RotateZ(0.5f), quaternion.RotateX(-0.6f + math.sin(phase * 2) * 0.05f)));
            float3 lp = math.lerp(math.lerp(carryPos, runPos, sprint), aimPos, aim);
            quaternion lr = math.slerp(math.slerp(carryRot, runRot, sprint), aimRot, aim);
            pos = hipC + math.mul(rotT, lp);
            rot = math.mul(rotT, lr);
            if (dead) { pos = math.lerp(pos, new float3(0.4f, 0.9f, 0.3f), p.deadK); rot = math.slerp(rot, quaternion.Euler(0.3f, 1.2f, 1.4f), p.deadK); }
        }

        /// <summary>Двухзвенная IK: локоть в плоскости «плечо — кисть — подсказка».</summary>
        static void TwoBone(float3 a, float3 target, float3 pole, float l1, float l2, out float3 elbow)
        {
            float3 d = target - a;
            float dist = math.length(d);
            float3 dir = dist > 1e-4f ? d / dist : new float3(0, -1, 0);
            float reach = math.clamp(dist, math.abs(l1 - l2) + 0.01f, l1 + l2 - 0.001f);
            float cosA = math.clamp((l1 * l1 + reach * reach - l2 * l2) / (2 * l1 * reach), -1f, 1f);
            float sinA = math.sqrt(1 - cosA * cosA);
            float3 perp = pole - dir * math.dot(pole, dir);
            perp = math.lengthsq(perp) > 1e-6f ? math.normalize(perp) : new float3(0, -1, 0);
            elbow = a + (dir * cosA + perp * sinA) * l1;
        }

        /// <summary>Поворот кости, у которой меш направлен вдоль локальной −Y.</summary>
        static quaternion BoneRot(float3 bone, float3 pole)
        {
            float3 u = math.normalizesafe(bone, new float3(0, -1, 0));
            float3 f = math.cross(math.normalizesafe(math.cross(u, pole), new float3(1, 0, 0)), u);
            f = math.normalizesafe(f, new float3(0, 0, 1));
            return quaternion.LookRotationSafe(f, -u);
        }

        /// <summary>Затухающее «болтание» конечности после гибели (как у тряпичной куклы Ravenfield).</summary>
        static float Flail(in PoseInput p, float freq, float phase)
        {
            float t = math.max(0f, p.deadT);
            float seedPh = (p.seed % 97) * 0.13f;
            return math.exp(-t * 2.4f) * math.sin(t * freq + phase + seedPh);
        }

        /// <summary>Безвольные руки при гибели: взлетают от толчка, разбрасываются в стороны и опадают.</summary>
        static void DeadArm(LimbKind kind, float s, in PoseInput p, float3 shoulder, quaternion rotT, out float3 pos, out quaternion rot)
        {
            quaternion ur = math.mul(rotT, quaternion.RotateX(-0.3f));
            ur = math.slerp(ur, math.mul(rotT, quaternion.RotateZ(s * 1.25f)), p.deadK);
            // Взмах: вверх-вперёд и в сторону, затухающе
            ur = math.mul(ur, math.mul(quaternion.RotateX(-Flail(p, 10f + s, s) * 1.5f), quaternion.RotateZ(s * Flail(p, 8f, 1.1f + s) * 0.9f)));
            if (kind == LimbKind.UpperArm) { pos = shoulder; rot = ur; }
            else
            {
                pos = shoulder + math.mul(ur, new float3(0, -Upper, 0));
                rot = math.mul(ur, quaternion.RotateX(-0.6f * (1 - p.deadK * 0.75f) - math.abs(Flail(p, 11f, 0.4f + s)) * 1.2f));
            }
        }
    }
}
