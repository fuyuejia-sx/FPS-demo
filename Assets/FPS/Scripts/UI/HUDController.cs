using TMPro;
using UnityEngine;

namespace FPS
{
    /// <summary>
    /// HUD 模块：显示准星、玩家血量、弹药、剩余敌人数量，以及游戏结束时的结算信息。
    /// 只监听其他模块的事件来刷新界面，不包含任何游戏逻辑。
    /// 界面文字使用英文，因为项目自带的 TextMesh Pro 默认字体不包含中文字符。
    /// 挂载对象：HUD 的 Canvas 物体。
    /// </summary>
    [DisallowMultipleComponent]
    public class HUDController : MonoBehaviour
    {
        [Header("数据来源")]
        [Tooltip("玩家的 Health。")]
        [SerializeField] private Health playerHealth;

        [Tooltip("玩家的武器控制器，用于获取当前武器的弹药。")]
        [SerializeField] private PlayerWeaponController weaponController;

        [Tooltip("游戏流程管理器，用于显示结算界面。可留空。")]
        [SerializeField] private GameManager gameManager;

        [Tooltip("玩家的盾牌。可留空。")]
        [SerializeField] private PlayerShield playerShield;

        [Tooltip("金币管理器，用于显示金币总数和击杀奖励。留空时自动查找或创建。")]
        [SerializeField] private CoinManager coinManager;

        [Header("界面元素")]
        [Tooltip("血条的填充部分。通过改变它的右侧锚点来表示血量比例。")]
        [SerializeField] private RectTransform healthFill;

        [Tooltip("血量数字。")]
        [SerializeField] private TMP_Text healthText;

        [Tooltip("盾值条的填充部分。通过改变它的右侧锚点来表示盾值比例。")]
        [SerializeField] private RectTransform shieldFill;

        [Tooltip("盾值数字。")]
        [SerializeField] private TMP_Text shieldText;

        [Tooltip("盾值条整体的 CanvasGroup。持盾时完全显示，收起时变暗。")]
        [SerializeField] private CanvasGroup shieldGroup;

        [Tooltip("收起盾牌时盾值条的透明度。")]
        [SerializeField, Range(0f, 1f)] private float shieldLoweredAlpha = 0.35f;

        [Tooltip("弹药文字。")]
        [SerializeField] private TMP_Text ammoText;

        [Tooltip("剩余敌人数量文字。")]
        [SerializeField] private TMP_Text enemyCountText;

        [Tooltip("右上角金币总数文字。")]
        [SerializeField] private TMP_Text coinCountText;

        [Tooltip("左下角击杀获得金币提示文字。")]
        [SerializeField] private TMP_Text coinGainText;

        [Tooltip("左下角金币增加提示的显示总时长（秒）。")]
        [SerializeField, Min(0.2f)] private float coinGainDisplayDuration = 2f;

        [Tooltip("准星物体，游戏结束时隐藏。")]
        [SerializeField] private GameObject crosshair;

        [Tooltip("结算面板，游戏进行中隐藏。")]
        [SerializeField] private GameObject messagePanel;

        [Tooltip("结算面板上的文字。")]
        [SerializeField] private TMP_Text messageText;

        private WeaponAmmo _ammo;
        private float _coinGainTimer;
        private Color _coinGainBaseColor = new Color(1f, 0.88f, 0.25f);

        private void Start()
        {
            if (playerHealth != null)
            {
                playerHealth.HealthChanged += OnHealthChanged;
                OnHealthChanged(playerHealth.CurrentHealth, playerHealth.MaxHealth);
            }

            if (playerShield != null)
            {
                playerShield.ShieldChanged += OnShieldChanged;
                playerShield.RaisedChanged += OnShieldRaisedChanged;
                OnShieldChanged(playerShield.CurrentShield, playerShield.MaxShield);
                OnShieldRaisedChanged(playerShield.IsRaised);
            }

            if (weaponController != null)
            {
                weaponController.CurrentWeaponChanged += OnCurrentWeaponChanged;
                BindWeaponAmmo(weaponController.CurrentWeapon);
            }
            else
            {
                RefreshAmmo();
            }

            EnemyAI.EnemyDied += OnEnemyDied;
            OnEnemyDied(EnemyAI.AliveCount);

            EnsureCoinSystem();
            if (coinManager != null)
            {
                coinManager.CoinsChanged += OnCoinsChanged;
                RefreshCoins(coinManager.CurrentCoins);
            }

            if (coinGainText != null)
            {
                _coinGainBaseColor = coinGainText.color;
                coinGainText.text = string.Empty;
                SetCoinGainAlpha(0f);
            }

            if (gameManager != null)
            {
                gameManager.StateChanged += OnGameStateChanged;
            }

            if (messagePanel != null)
            {
                messagePanel.SetActive(false);
            }
        }

        private void Update()
        {
            if (_coinGainTimer <= 0f || coinGainText == null)
            {
                return;
            }

            _coinGainTimer -= Time.deltaTime;
            if (_coinGainTimer <= 0f)
            {
                _coinGainTimer = 0f;
                coinGainText.text = string.Empty;
                SetCoinGainAlpha(0f);
                return;
            }

            float fadeWindow = Mathf.Min(0.8f, coinGainDisplayDuration * 0.4f);
            float alpha = _coinGainTimer < fadeWindow ? Mathf.Clamp01(_coinGainTimer / fadeWindow) : 1f;
            SetCoinGainAlpha(alpha);
        }

        private void OnDestroy()
        {
            if (playerHealth != null)
            {
                playerHealth.HealthChanged -= OnHealthChanged;
            }

            if (playerShield != null)
            {
                playerShield.ShieldChanged -= OnShieldChanged;
                playerShield.RaisedChanged -= OnShieldRaisedChanged;
            }

            if (weaponController != null)
            {
                weaponController.CurrentWeaponChanged -= OnCurrentWeaponChanged;
            }

            UnbindWeaponAmmo();

            EnemyAI.EnemyDied -= OnEnemyDied;

            if (coinManager != null)
            {
                coinManager.CoinsChanged -= OnCoinsChanged;
            }

            if (gameManager != null)
            {
                gameManager.StateChanged -= OnGameStateChanged;
            }
        }

        private void OnHealthChanged(float current, float max)
        {
            float ratio = max > 0f ? Mathf.Clamp01(current / max) : 0f;

            if (healthFill != null)
            {
                Vector2 anchorMax = healthFill.anchorMax;
                anchorMax.x = ratio;
                healthFill.anchorMax = anchorMax;
            }

            if (healthText != null)
            {
                healthText.text = Mathf.CeilToInt(current).ToString();
            }
        }

        private void OnShieldChanged(float current, float max)
        {
            if (shieldFill != null)
            {
                Vector2 anchorMax = shieldFill.anchorMax;
                anchorMax.x = max > 0f ? Mathf.Clamp01(current / max) : 0f;
                shieldFill.anchorMax = anchorMax;
            }

            if (shieldText != null)
            {
                shieldText.text = Mathf.CeilToInt(current).ToString();
            }
        }

        private void OnShieldRaisedChanged(bool raised)
        {
            if (shieldGroup != null)
            {
                shieldGroup.alpha = raised ? 1f : shieldLoweredAlpha;
            }
        }

        private void OnCurrentWeaponChanged(Weapon weapon)
        {
            BindWeaponAmmo(weapon);
        }

        private void BindWeaponAmmo(Weapon weapon)
        {
            UnbindWeaponAmmo();
            _ammo = weapon != null ? weapon.Ammo : null;
            if (_ammo != null)
            {
                _ammo.AmmoChanged += OnAmmoChanged;
                _ammo.ReloadStarted += RefreshAmmo;
                _ammo.ReloadFinished += RefreshAmmo;
            }

            RefreshAmmo();
        }

        private void UnbindWeaponAmmo()
        {
            if (_ammo != null)
            {
                _ammo.AmmoChanged -= OnAmmoChanged;
                _ammo.ReloadStarted -= RefreshAmmo;
                _ammo.ReloadFinished -= RefreshAmmo;
                _ammo = null;
            }
        }

        /// <summary>供商城等界面临时隐藏或恢复准星。</summary>
        public void SetCrosshairVisible(bool visible)
        {
            if (crosshair != null)
            {
                crosshair.SetActive(visible);
            }
        }

        private void OnAmmoChanged(int inMagazine, int reserve)
        {
            RefreshAmmo();
        }

        private void RefreshAmmo()
        {
            if (ammoText == null)
            {
                return;
            }

            if (_ammo == null)
            {
                ammoText.text = "-- / --";
            }
            else if (_ammo.IsReloading)
            {
                ammoText.text = "RELOADING";
            }
            else
            {
                string reserve = _ammo.HasInfiniteReserve ? "--" : _ammo.Reserve.ToString();
                ammoText.text = $"{_ammo.InMagazine} / {reserve}";
            }
        }

        private void OnEnemyDied(int remaining)
        {
            if (enemyCountText != null)
            {
                enemyCountText.text = $"ENEMIES: {remaining}";
            }
        }

        private void OnCoinsChanged(int total, int delta)
        {
            RefreshCoins(total);

            if (delta > 0 && coinGainText != null)
            {
                coinGainText.text = $"+{delta} COINS";
                _coinGainTimer = coinGainDisplayDuration;
                SetCoinGainAlpha(1f);
            }
        }

        private void RefreshCoins(int total)
        {
            if (coinCountText != null)
            {
                coinCountText.text = $"COINS: {total}";
            }
        }

        private void SetCoinGainAlpha(float alpha)
        {
            Color color = _coinGainBaseColor;
            color.a = alpha;
            coinGainText.color = color;
        }

        /// <summary>
        /// 场景里未提前运行“一键配置场景”时，运行时自动补齐 CoinManager 和金币 UI 文字，
        /// 保证直接点 Play 也能正常显示右上角金币总数与左下角击杀奖励。
        /// </summary>
        private void EnsureCoinSystem()
        {
            if (coinManager == null)
            {
                coinManager = FindFirstObjectByType<CoinManager>();
                if (coinManager == null)
                {
                    GameObject host = gameManager != null ? gameManager.gameObject : gameObject;
                    coinManager = host.AddComponent<CoinManager>();
                }
            }

            if (coinCountText == null)
            {
                coinCountText = CreateRuntimeText(
                    "CoinCountText",
                    Vector2.one, Vector2.one, Vector2.one,
                    new Vector2(-40f, -80f), new Vector2(500f, 50f),
                    32f, TextAlignmentOptions.TopRight, new Color(1f, 0.85f, 0.2f));
            }

            if (coinGainText == null)
            {
                coinGainText = CreateRuntimeText(
                    "CoinGainText",
                    Vector2.zero, Vector2.zero, Vector2.zero,
                    new Vector2(40f, 108f), new Vector2(360f, 40f),
                    28f, TextAlignmentOptions.BottomLeft, new Color(1f, 0.88f, 0.25f));
            }
        }

        private TMP_Text CreateRuntimeText(
            string objectName,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 anchoredPosition, Vector2 size,
            float fontSize, TextAlignmentOptions alignment, Color color)
        {
            var go = new GameObject(objectName, typeof(RectTransform));
            go.layer = gameObject.layer;
            go.transform.SetParent(transform, false);
            go.transform.SetAsFirstSibling();

            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            TextMeshProUGUI label = go.AddComponent<TextMeshProUGUI>();
            if (enemyCountText != null && enemyCountText.font != null)
            {
                label.font = enemyCountText.font;
            }

            label.fontSize = fontSize;
            label.alignment = alignment;
            label.color = color;
            label.raycastTarget = false;
            return label;
        }

        private void OnGameStateChanged(GameState state)
        {
            if (state == GameState.Playing)
            {
                return;
            }

            if (crosshair != null)
            {
                crosshair.SetActive(false);
            }

            if (messageText != null)
            {
                string title = state == GameState.Victory ? "VICTORY" : "GAME OVER";
                messageText.text = $"{title}\n<size=50%>Press R to restart</size>";
            }

            if (messagePanel != null)
            {
                messagePanel.SetActive(true);
            }
        }
    }
}
