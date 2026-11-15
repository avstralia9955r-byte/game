using UnityEngine;

namespace Frontline
{
    /// <summary>
    /// Шпатель (Штурмовик / Инженер). В режиме строительства (B или слот 4, на мобильных — кнопка «Строить»)
    /// игрок наводит прицел, и пока точка валидна, каждые 2 секунды автоматически кладётся 1 кирпич.
    /// </summary>
    public class TrowelTool : MonoBehaviour
    {
        [SerializeField] PlayerController player;
        [SerializeField] GameObject trowelViewModel;
        [SerializeField] Transform ghost;                 // полупрозрачный кирпич-призрак
        [SerializeField] Renderer ghostRenderer;
        [SerializeField] Color validColor = new(0.4f, 1f, 0.5f, 0.45f), invalidColor = new(1f, 0.3f, 0.3f, 0.45f);
        [SerializeField] Animator animator;               // "Trowel_Lay"
        [SerializeField] AudioSource audioSource;
        [SerializeField] AudioClip laySound;
        public float secondsPerBrick = 2f;
        public float buildDistance = 4f;

        public bool Active { get; private set; }
        public float Progress01 => _timer / secondsPerBrick;
        public string BlockReason { get; private set; }

        float _timer;
        // Процедурная анимация шпателя: зачерпнуть раствор → намазать → уложить кирпич левой рукой
        Vector3 _vmPos; Quaternion _vmRot; Transform _brickHand; Vector3 _brickHandPos; bool _vmCached; float _slap;
        MaterialPropertyBlock _mpb;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public void SetActive(bool on)
        {
            Active = on; _timer = 0;
            if (trowelViewModel && !_vmCached)
            {
                _vmCached = true;
                _vmPos = trowelViewModel.transform.localPosition; _vmRot = trowelViewModel.transform.localRotation;
                _brickHand = trowelViewModel.transform.Find("BrickHand");
                if (_brickHand) _brickHandPos = _brickHand.localPosition;
            }
            if (trowelViewModel) trowelViewModel.SetActive(on);
            if (ghost) ghost.gameObject.SetActive(on);
        }

        void LateUpdate()
        {
            if (!Active || !trowelViewModel || !_vmCached) return;
            float p = Progress01, t = Time.time;
            _slap = Mathf.MoveTowards(_slap, 0, Time.deltaTime * 3f);
            // 0–0.45: шпатель «черпает» (назад-вниз с поворотом); 0.45–1: размазывает (мелкие движения вперёд)
            float scoop = Mathf.Sin(Mathf.Clamp01(p / 0.45f) * Mathf.PI);
            float spread = Mathf.Clamp01((p - 0.45f) / 0.55f);
            Vector3 pos = _vmPos + new Vector3(0, -0.05f * scoop, -0.06f * scoop)
                        + new Vector3(Mathf.Sin(t * 9f) * 0.02f, 0.02f, 0.08f) * spread * (1 - _slap)
                        + new Vector3(0, -0.06f, 0.1f) * Mathf.Sin(_slap * Mathf.PI);
            Vector3 rot = new Vector3(25f * scoop - 10f * spread - 30f * Mathf.Sin(_slap * Mathf.PI), Mathf.Sin(t * 9f) * 8f * spread, -20f * scoop);
            float bob = player.IsGrounded ? Mathf.Sin(t * 9f) * 0.006f * player.Speed01 : 0;
            trowelViewModel.transform.localPosition = pos + new Vector3(0, bob, 0);
            trowelViewModel.transform.localRotation = _vmRot * Quaternion.Euler(rot);
            // Левая рука с кирпичом подаётся вперёд к моменту укладки и возвращается с «новым» кирпичом
            if (_brickHand) _brickHand.localPosition = _brickHandPos + new Vector3(0.08f, 0.04f, 0.16f) * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.7f, 1f, p)) + new Vector3(0, -0.25f, 0) * Mathf.Sin(_slap * Mathf.PI);
        }

        void Update()
        {
            if (!Active || !BrickSystem.I) return;

            var preview = BrickSystem.I.Evaluate(player.AimRay(), buildDistance, player.transform.eulerAngles.y, player.Team);
            BlockReason = preview.valid ? null : preview.reason;
            UpdateGhost(preview);

            // Во время бега и в прыжке не строим — иначе стены «рисуются» на ходу.
            bool canWork = preview.valid && player.IsGrounded && !player.IsSprinting;
            if (!canWork) { _timer = Mathf.Max(0, _timer - Time.deltaTime * 2f); return; }

            _timer += Time.deltaTime;
            if (_timer >= secondsPerBrick)
            {
                _timer = 0;
                if (BrickSystem.I.Place(preview, player.Team))
                {
                    if (animator) animator.SetTrigger("Trowel_Lay");
                    if (audioSource && laySound) audioSource.PlayOneShot(laySound);
                    SaveSystem.Progress.bricksPlaced++;
                    _slap = 1f;
                }
            }
        }

        void UpdateGhost(in BrickSystem.PlacementPreview p)
        {
            if (!ghost) return;
            bool show = p.valid || p.position != Vector3.zero;
            ghost.gameObject.SetActive(show);
            if (!show) return;
            ghost.SetPositionAndRotation(p.position, p.rotation);
            ghost.localScale = BrickSystem.I.brickSize;
            if (ghostRenderer)
            {
                _mpb ??= new MaterialPropertyBlock();
                _mpb.SetColor(BaseColorId, p.valid ? validColor : invalidColor);
                ghostRenderer.SetPropertyBlock(_mpb);
            }
        }
    }
}
