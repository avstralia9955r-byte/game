using UnityEngine;

namespace Frontline
{
    /// <summary>
    /// Деревянный забор (пролёт ~2,6 м): ломается от взрывов, танков, очередей. При поломке пролёт рассыпается
    /// на физические доски и столбы (Rigidbody), которые разлетаются от источника и через 20 с исчезают — как в Polyfield.
    /// </summary>
    public class Fence : MonoBehaviour
    {
        public float length = 2.6f, hp = 70f;
        public float yA, yB;            // высота земли у концов относительно центра пролёта
        public Color color;
        public bool picket;
        bool _broken;

        public void Damage(float dmg, Vector3 point, Vector3 dir)
        {
            if (_broken) return;
            hp -= dmg;
            if (hp <= 0) Break(point - dir * 0.5f, dir * 6f);
            else if (FxPool.I && Random.value < 0.4f) FxPool.I.BrickChips(point, -dir);
        }

        public void Break(Vector3 from, Vector3 impulse)
        {
            if (_broken) return;
            _broken = true;
            var col = GetComponent<Collider>(); if (col) col.enabled = false;
            var mr = GetComponent<MeshRenderer>(); if (mr) mr.enabled = false;
            var t = transform;
            var mat = ProceduralModels.Mat(color);
            float half = length * 0.5f;

            void Piece(Vector3 localCenter, Vector3 size, Quaternion localRot)
            {
                var go = new GameObject("Plank");
                go.transform.SetParent(t.parent, false);   // уничтожаются вместе с картой
                go.layer = 2;   // Ignore Raycast: не мешает пулям и лучам ботов
                go.transform.SetPositionAndRotation(t.TransformPoint(localCenter), t.rotation * localRot);
                go.transform.localScale = size;
                go.AddComponent<MeshFilter>().sharedMesh = ProceduralModels.CubeMesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                go.AddComponent<BoxCollider>();
                var rb = go.AddComponent<Rigidbody>();
                rb.mass = Mathf.Max(0.5f, size.x * size.y * size.z * 600f);
                Vector3 away = go.transform.position - from; away.y = Mathf.Abs(away.y) + 0.5f;
                rb.linearVelocity = away.normalized * Random.Range(2f, 5f) + impulse * Random.Range(0.4f, 0.9f) + Vector3.up * Random.Range(1f, 3f);
                rb.angularVelocity = Random.insideUnitSphere * 7f;
                Destroy(go, Random.Range(16f, 22f));
            }

            // Столбы
            Piece(new Vector3(0, yA + 0.5f, -half), new Vector3(0.12f, 1.1f, 0.12f), Quaternion.identity);
            Piece(new Vector3(0, yB + 0.5f, half), new Vector3(0.12f, 1.1f, 0.12f), Quaternion.identity);
            // Жерди / штакетник по частям
            float slope = Mathf.Atan2(yB - yA, length) * Mathf.Rad2Deg;
            for (int k = 0; k < 2; k++)
            {
                float h = k == 0 ? 0.35f : 0.8f;
                for (int s = 0; s < 2; s++)
                {
                    float z = (s - 0.5f) * half;
                    float y = Mathf.Lerp(yA, yB, (z + half) / length) + h;
                    Piece(new Vector3(0.04f, y, z), new Vector3(0.05f, 0.1f, half * 0.95f), Quaternion.Euler(-slope, 0, 0));
                }
            }
            if (picket)
                for (int i = 0; i < 5; i++)
                {
                    float z = -half + 0.3f + i * (length - 0.6f) / 4f;
                    float y = Mathf.Lerp(yA, yB, (z + half) / length) + 0.5f;
                    Piece(new Vector3(0.08f, y, z), new Vector3(0.025f, 0.95f, 0.1f), Quaternion.identity);
                }

            BotAudio.PlayAt(SoundSynth.WoodBreak(Random.Range(0, 3)), t.position + Vector3.up * 0.6f, 0.9f);
            if (FxPool.I) FxPool.I.BrickChips(t.position + Vector3.up * 0.6f, Vector3.up);
            Destroy(gameObject, 0.1f);
        }
    }
}
