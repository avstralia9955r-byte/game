using System.Collections.Generic;
using Frontline.Bots;
using TMPro;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;

namespace Frontline
{
    /// <summary>
    /// HUD: здоровье, патроны (магазин/запас), миникарта, счёт, таймер, индикатор захвата, направление урона,
    /// подсказки, прогресс кирпича, хитмаркер, лента убийств. Обновление текста — только при изменении значений
    /// (без аллокаций строк каждый кадр).
    /// </summary>
    public class HUDController : MonoBehaviour
    {
        [Header("Здоровье / патроны")]
        public Image healthFill; public TMP_Text healthText;
        public TMP_Text ammoText, reserveText, weaponNameText;
        [Header("Счёт / время")]
        public TMP_Text scoreGermany, scoreUSSR, timerText, modeText;
        [Header("Захват")]
        public GameObject captureRoot; public Image captureFill; public TMP_Text captureLetter;
        public Color colGermany = new(0.6f, 0.62f, 0.68f), colUSSR = new(0.8f, 0.2f, 0.15f), colNeutral = Color.white;
        [Header("Урон")]
        public RectTransform damageIndicatorPrefab; public RectTransform damageRoot; public float damageIndicatorLife = 1.6f;
        public Image hitMarker; public Image lowHealthVignette;
        [Header("Строительство")]
        public GameObject buildRoot; public Image buildFill; public TMP_Text buildHint;
        [Header("Миникарта")]
        public RectTransform minimapRect; public RawImage minimapImage; public Image playerArrow;
        public Image dotPrefab; public TMP_Text pointLabelPrefab; public float minimapZoom = 1f;
        [Header("Прочее")]
        public TMP_Text hintText, killFeedText, respawnText;
        public GameObject crosshair; public GameObject scopeOverlay;
        public GameObject matchEndRoot; public TMP_Text matchEndText;

        PlayerController _player; PlayerHealth _hp; WeaponController _weapon; TrowelTool _trowel;
        GameModeManager _gm;
        int _lastAmmo = -1, _lastReserve = -1, _lastHp = -1, _lastSec = -1, _lastScoreG = -1, _lastScoreS = -1;
        readonly List<(RectTransform rt, Vector3 dir, float t)> _dmg = new();
        readonly List<Image> _dots = new();
        readonly List<TMP_Text> _pointLabels = new();
        readonly Queue<string> _feed = new();
        float _hitTimer;

        void Start()
        {
            _gm = GameModeManager.I;
            _gm.KillFeed += OnKill;
            _gm.MatchEnded += OnMatchEnd;
            if (modeText) modeText.text = _gm.Mode.Describe();
            if (minimapImage && _gm.Config.map && _gm.Config.map.minimapImage) minimapImage.texture = _gm.Config.map.minimapImage.texture;
            if (matchEndRoot) matchEndRoot.SetActive(false);
        }

        void Bind()
        {
            _player = _gm.Player;
            if (!_player) return;
            _hp = _player.GetComponent<PlayerHealth>();
            _weapon = _player.GetComponentInChildren<WeaponController>();
            _trowel = _player.GetComponentInChildren<TrowelTool>(true);
            _hp.Damaged += OnDamaged;
            _weapon.HitConfirmed += _ => _hitTimer = 0.15f;
        }

        void Update()
        {
            if (!_player) { Bind(); if (!_player) return; }

            // Здоровье
            int hp = Mathf.CeilToInt(_hp.Health);
            if (hp != _lastHp)
            {
                _lastHp = hp;
                healthFill.fillAmount = _hp.Health / _hp.maxHealth;
                healthText.SetText("{0}", (float)hp);
            }
            if (lowHealthVignette) lowHealthVignette.color = new Color(0.6f, 0, 0, Mathf.Clamp01(1f - _hp.Health / 40f) * 0.55f);

            // Патроны
            if (_weapon.Current)
            {
                if (_weapon.Ammo != _lastAmmo || _weapon.Reserve != _lastReserve)
                {
                    _lastAmmo = _weapon.Ammo; _lastReserve = _weapon.Reserve;
                    ammoText.SetText("{0}", (float)_weapon.Ammo); reserveText.SetText("/ {0}", (float)_weapon.Reserve);
                    weaponNameText.text = _weapon.Current.displayName;
                }
            }
            else if (_lastAmmo != -2) { _lastAmmo = -2; ammoText.text = "—"; reserveText.text = ""; weaponNameText.text = "Шпатель"; }

            // Прицел/оптика
            bool scoped = _weapon.Stats.scopeOverlay && _weapon.Ads01 > 0.9f;
            if (scopeOverlay) scopeOverlay.SetActive(scoped);
            if (crosshair) crosshair.SetActive(_weapon.Ads01 < 0.5f);

            // Счёт / таймер
            var mode = _gm.Mode;
            if (mode.Score[0] != _lastScoreG) { _lastScoreG = mode.Score[0]; scoreGermany.SetText("{0}", (float)mode.Score[0]); }
            if (mode.Score[1] != _lastScoreS) { _lastScoreS = mode.Score[1]; scoreUSSR.SetText("{0}", (float)mode.Score[1]); }
            int sec = Mathf.CeilToInt(mode.TimeLeft);
            if (sec != _lastSec) { _lastSec = sec; timerText.SetText("{0:00}:{1:00}", (float)(sec / 60), (float)(sec % 60)); }

            UpdateCapture();
            UpdateBuild();
            UpdateDamageIndicators();
            UpdateMinimap();

            _hitTimer -= Time.deltaTime;
            if (hitMarker) hitMarker.enabled = _hitTimer > 0;

            if (respawnText)
            {
                bool dead = !_hp.Alive;
                respawnText.gameObject.SetActive(dead);
                if (dead) respawnText.SetText("Возрождение через {0}", (float)Mathf.CeilToInt(_gm.PlayerRespawnAt - Time.time));
            }
        }

        void UpdateCapture()
        {
            if (!captureRoot || !WorldStores.CapturePoints.IsCreated || _gm.Config.mode != GameModeType.TerritoryControl) { if (captureRoot) captureRoot.SetActive(false); return; }
            int inside = -1;
            var pts = WorldStores.CapturePoints;
            for (int i = 0; i < pts.Length; i++)
                if (math.distancesq(pts[i].position, (float3)_player.transform.position) < pts[i].radius * pts[i].radius) { inside = i; break; }
            captureRoot.SetActive(inside >= 0);
            if (inside < 0) return;
            var d = pts[inside];
            captureLetter.text = ((char)('A' + inside)).ToString();
            captureFill.fillAmount = Mathf.Abs(d.progress);
            captureFill.color = d.progress > 0 ? colUSSR : d.progress < 0 ? colGermany : colNeutral;
            if (hintText) hintText.text = d.contested != 0 ? "Точка оспаривается!" : "";
        }

        void UpdateBuild()
        {
            bool on = _trowel && _trowel.Active;
            if (buildRoot) buildRoot.SetActive(on);
            if (!on) return;
            buildFill.fillAmount = _trowel.Progress01;
            buildHint.text = _trowel.BlockReason ?? "Укладка кирпича…";
        }

        void OnDamaged(Vector3 fromDir, float amount)
        {
            if (!damageIndicatorPrefab) return;
            var rt = Instantiate(damageIndicatorPrefab, damageRoot);
            _dmg.Add((rt, fromDir, damageIndicatorLife));
        }

        void UpdateDamageIndicators()
        {
            var fwd = _player.transform.forward; fwd.y = 0;
            for (int i = _dmg.Count - 1; i >= 0; i--)
            {
                var (rt, dir, t) = _dmg[i];
                t -= Time.deltaTime;
                if (t <= 0) { Destroy(rt.gameObject); _dmg.RemoveAt(i); continue; }
                Vector3 d = dir; d.y = 0;
                float ang = Vector3.SignedAngle(fwd, d, Vector3.up);
                rt.localRotation = Quaternion.Euler(0, 0, -ang);
                var g = rt.GetComponent<CanvasGroup>(); if (g) g.alpha = t / damageIndicatorLife;
                _dmg[i] = (rt, dir, t);
            }
        }

        void UpdateMinimap()
        {
            if (!minimapRect || !_gm.Config.map) return;
            var map = _gm.Config.map;
            Vector2 size = minimapRect.rect.size;
            Vector2 ToMap(float3 p)
            {
                Vector2 t = new((p.x - map.minimapWorldMin.x) / (map.minimapWorldMax.x - map.minimapWorldMin.x),
                                (p.z - map.minimapWorldMin.y) / (map.minimapWorldMax.y - map.minimapWorldMin.y));
                return (t - new Vector2(0.5f, 0.5f)) * size * minimapZoom;
            }

            var actors = CombatWorld.Actors;
            byte myTeam = _player.Team;
            int used = 0;
            // Союзники видны всегда; враги — только когда стреляют без глушителя рядом (как «засвет» на миникарте).
            for (int i = 1; i < actors.Length; i++)
            {
                var a = actors[i];
                if (!a.alive || a.team != myTeam) continue;
                var dot = GetDot(used++);
                dot.rectTransform.anchoredPosition = ToMap(a.position);
                dot.color = myTeam == 0 ? colGermany : colUSSR;
            }
            foreach (var n in CombatWorld.Noise)
            {
                if (n.team == myTeam || n.kind != 0) continue;
                var dot = GetDot(used++);
                dot.rectTransform.anchoredPosition = ToMap(n.position);
                dot.color = Color.red;
            }
            for (int i = used; i < _dots.Count; i++) _dots[i].enabled = false;

            if (playerArrow)
            {
                playerArrow.rectTransform.anchoredPosition = ToMap(_player.transform.position);
                playerArrow.rectTransform.localRotation = Quaternion.Euler(0, 0, -_player.transform.eulerAngles.y);
            }

            var pts = WorldStores.CapturePoints;
            if (!pts.IsCreated || !pointLabelPrefab) return;
            while (_pointLabels.Count < pts.Length) _pointLabels.Add(Instantiate(pointLabelPrefab, minimapRect));
            for (int i = 0; i < pts.Length; i++)
            {
                var l = _pointLabels[i];
                l.text = ((char)('A' + i)).ToString();
                l.rectTransform.anchoredPosition = ToMap(pts[i].position);
                l.color = pts[i].owner == 0 ? colGermany : pts[i].owner == 1 ? colUSSR : colNeutral;
            }
        }

        Image GetDot(int i)
        {
            while (_dots.Count <= i) _dots.Add(Instantiate(dotPrefab, minimapRect));
            _dots[i].enabled = true;
            return _dots[i];
        }

        void OnKill(KillEvent k)
        {
            string killer = k.killerActorId == 0 ? "<b>Вы</b>" : $"Бот {k.killerActorId}";
            string victim = k.victimActorId == 0 ? "<b>Вы</b>" : $"Бот {k.victimActorId}";
            _feed.Enqueue($"{killer} → {victim}{(k.headshot ? " ◎" : "")}");
            while (_feed.Count > 5) _feed.Dequeue();
            if (killFeedText) killFeedText.text = string.Join("\n", _feed);
        }

        void OnMatchEnd(GameMode m)
        {
            if (!matchEndRoot) return;
            matchEndRoot.SetActive(true);
            string res = m.Winner == null ? "Ничья" : m.Winner == _gm.Config.playerFaction ? "Победа!" : "Поражение";
            matchEndText.text = $"{res}\nГермания {m.Score[0]} : {m.Score[1]} СССР\nУбийств: {_gm.PlayerKills}  Смертей: {_gm.PlayerDeaths}\nДеньги: {SaveSystem.Progress.currency}";
        }
    }
}
