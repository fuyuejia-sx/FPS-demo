using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

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

        [Tooltip("左下角拾取弹药提示文字。留空时运行时自动创建。")]
        [SerializeField] private TMP_Text ammoPickupText;

        [Tooltip("左下角拾取弹药提示的显示总时长（秒）。")]
        [SerializeField, Min(0.2f)] private float ammoPickupDisplayDuration = 2f;

        [Tooltip("准星物体，游戏结束时隐藏。")]
        [SerializeField] private GameObject crosshair;

        [Tooltip("结算面板，游戏进行中隐藏。")]
        [SerializeField] private GameObject messagePanel;

        [Tooltip("结算面板上的文字。")]
        [SerializeField] private TMP_Text messageText;

        private WeaponAmmo _ammo;
        private float _coinGainTimer;
        private Color _coinGainBaseColor = new Color(1f, 0.88f, 0.25f);
        private float _ammoPickupTimer;
        private Color _ammoPickupColor = new Color(0.35f, 0.95f, 0.55f);
        private GameObject _pausePanel;

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

            EnsureAmmoPickupText();
            AmmoPickup.AmmoPickedUp += OnAmmoPickedUp;
            AmmoPickup.AmmoAlreadyFull += OnAmmoAlreadyFull;

            if (gameManager == null)
            {
                gameManager = FindFirstObjectByType<GameManager>();
            }

            EnsureUiInputSupport();
            BuildPauseMenuAndHint();
            EnsureSettlementButtons();

            if (gameManager != null)
            {
                gameManager.StateChanged += OnGameStateChanged;
                gameManager.PauseChanged += OnPauseChanged;
            }

            if (messagePanel != null)
            {
                messagePanel.SetActive(false);
            }
        }

        private void Update()
        {
            if (_coinGainTimer > 0f && coinGainText != null)
            {
                _coinGainTimer -= Time.deltaTime;
                if (_coinGainTimer <= 0f)
                {
                    _coinGainTimer = 0f;
                    coinGainText.text = string.Empty;
                    SetCoinGainAlpha(0f);
                }
                else
                {
                    float fadeWindow = Mathf.Min(0.8f, coinGainDisplayDuration * 0.4f);
                    float alpha = _coinGainTimer < fadeWindow ? Mathf.Clamp01(_coinGainTimer / fadeWindow) : 1f;
                    SetCoinGainAlpha(alpha);
                }
            }

            if (_ammoPickupTimer > 0f && ammoPickupText != null)
            {
                _ammoPickupTimer -= Time.deltaTime;
                if (_ammoPickupTimer <= 0f)
                {
                    _ammoPickupTimer = 0f;
                    ammoPickupText.text = string.Empty;
                    SetAmmoPickupAlpha(0f);
                }
                else
                {
                    float fadeWindow = Mathf.Min(0.8f, ammoPickupDisplayDuration * 0.4f);
                    float alpha = _ammoPickupTimer < fadeWindow ? Mathf.Clamp01(_ammoPickupTimer / fadeWindow) : 1f;
                    SetAmmoPickupAlpha(alpha);
                }
            }
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
            AmmoPickup.AmmoPickedUp -= OnAmmoPickedUp;
            AmmoPickup.AmmoAlreadyFull -= OnAmmoAlreadyFull;

            if (coinManager != null)
            {
                coinManager.CoinsChanged -= OnCoinsChanged;
            }

            if (gameManager != null)
            {
                gameManager.StateChanged -= OnGameStateChanged;
                gameManager.PauseChanged -= OnPauseChanged;
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

        private void OnAmmoPickedUp(int added, int reserve, int maxReserve)
        {
            if (ammoPickupText == null)
            {
                return;
            }

            string suffix = reserve >= maxReserve ? " (MAX)" : string.Empty;
            _ammoPickupColor = new Color(0.35f, 0.95f, 0.55f);
            ammoPickupText.text = $"+{added} AMMO{suffix}";
            _ammoPickupTimer = ammoPickupDisplayDuration;
            SetAmmoPickupAlpha(1f);
        }

        private void OnAmmoAlreadyFull(int reserve, int maxReserve)
        {
            if (ammoPickupText == null)
            {
                return;
            }

            _ammoPickupColor = new Color(1f, 0.70f, 0.30f);
            ammoPickupText.text = $"AMMO FULL ({reserve} / {maxReserve})";
            _ammoPickupTimer = Mathf.Min(1.4f, ammoPickupDisplayDuration);
            SetAmmoPickupAlpha(1f);
        }

        private void SetAmmoPickupAlpha(float alpha)
        {
            Color color = _ammoPickupColor;
            color.a = alpha;
            ammoPickupText.color = color;
        }

        private void EnsureAmmoPickupText()
        {
            if (ammoPickupText == null)
            {
                ammoPickupText = CreateRuntimeText(
                    "AmmoPickupText",
                    Vector2.zero, Vector2.zero, Vector2.zero,
                    new Vector2(40f, 150f), new Vector2(420f, 36f),
                    24f, TextAlignmentOptions.BottomLeft, _ammoPickupColor);
            }

            ammoPickupText.text = string.Empty;
            SetAmmoPickupAlpha(0f);
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

        private void OnPauseChanged(bool paused)
        {
            if (_pausePanel != null)
            {
                _pausePanel.SetActive(paused);
                if (paused)
                {
                    _pausePanel.transform.SetAsLastSibling();
                }
            }

            if (crosshair != null && (gameManager == null || gameManager.State == GameState.Playing))
            {
                crosshair.SetActive(!paused);
            }
        }

        private void EnsureUiInputSupport()
        {
            if (GetComponent<GraphicRaycaster>() == null)
            {
                gameObject.AddComponent<GraphicRaycaster>();
            }

            if (FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                es.transform.SetParent(null);
            }
        }

        /// <summary>构建 ESC 暂停菜单（继续、重新开始、退出游戏）及右上角快捷键提示。</summary>
        private void BuildPauseMenuAndHint()
        {
            // 右上角快捷键提示（排在 [B] SHOP 和 [I] BACKPACK 下方）
            TMP_Text pauseHint = CreateRuntimeText(
                "PauseHintText",
                Vector2.one, Vector2.one, Vector2.one,
                new Vector2(-40f, -200f), new Vector2(300f, 34f),
                20f, TextAlignmentOptions.TopRight, new Color(0.75f, 0.86f, 0.96f, 0.9f));
            pauseHint.text = "[ESC] PAUSE";

            // 全屏暗色遮罩
            var backdropObj = new GameObject("PausePanel", typeof(RectTransform), typeof(Image));
            backdropObj.layer = gameObject.layer;
            backdropObj.transform.SetParent(transform, false);
            var backdropRect = (RectTransform)backdropObj.transform;
            backdropRect.anchorMin = Vector2.zero;
            backdropRect.anchorMax = Vector2.one;
            backdropRect.offsetMin = Vector2.zero;
            backdropRect.offsetMax = Vector2.zero;
            Image backdrop = backdropObj.GetComponent<Image>();
            backdrop.color = new Color(0.02f, 0.04f, 0.08f, 0.78f);
            backdrop.raycastTarget = true;

            // 居中暂停窗口
            var cardObj = new GameObject("PauseWindow", typeof(RectTransform), typeof(Image), typeof(Outline));
            cardObj.layer = gameObject.layer;
            cardObj.transform.SetParent(backdropObj.transform, false);
            var cardRect = (RectTransform)cardObj.transform;
            cardRect.anchorMin = new Vector2(0.5f, 0.5f);
            cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.pivot = new Vector2(0.5f, 0.5f);
            cardRect.anchoredPosition = Vector2.zero;
            cardRect.sizeDelta = new Vector2(460f, 390f);
            cardObj.GetComponent<Image>().color = new Color(0.07f, 0.10f, 0.16f, 0.97f);
            Outline border = cardObj.GetComponent<Outline>();
            border.effectColor = new Color(0.25f, 0.80f, 1f, 0.65f);
            border.effectDistance = new Vector2(2f, -2f);

            TMP_Text title = CreatePanelText(cardObj.transform, "PauseTitle", "PAUSED", 44f, Color.white);
            RectTransform titleRect = title.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -28f);
            titleRect.sizeDelta = new Vector2(-40f, 56f);

            CreatePanelButton(cardObj.transform, "ResumeButton", "RESUME [ESC]",
                new Vector2(0f, 34f), new Vector2(320f, 58f), new Color(0.16f, 0.62f, 0.36f),
                () => gameManager?.Resume());

            CreatePanelButton(cardObj.transform, "RestartButton", "RESTART",
                new Vector2(0f, -42f), new Vector2(320f, 58f), new Color(0.20f, 0.45f, 0.68f),
                () => gameManager?.Restart());

            CreatePanelButton(cardObj.transform, "QuitButton", "QUIT GAME",
                new Vector2(0f, -118f), new Vector2(320f, 58f), new Color(0.62f, 0.22f, 0.22f),
                () => gameManager?.QuitGame());

            backdropObj.SetActive(false);
            _pausePanel = backdropObj;
        }

        /// <summary>在胜利/失败结算界面添加可点击的重新开始与退出游戏按钮。</summary>
        private void EnsureSettlementButtons()
        {
            if (messagePanel == null || messagePanel.transform.Find("SettlementButtons") != null)
            {
                return;
            }

            Image panelImage = messagePanel.GetComponent<Image>();
            if (panelImage != null)
            {
                panelImage.raycastTarget = true;
            }

            var container = new GameObject("SettlementButtons", typeof(RectTransform));
            container.layer = gameObject.layer;
            container.transform.SetParent(messagePanel.transform, false);
            var rect = (RectTransform)container.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, -170f);
            rect.sizeDelta = new Vector2(560f, 68f);

            CreatePanelButton(container.transform, "SettlementRestartButton", "RESTART [R]",
                new Vector2(-140f, 0f), new Vector2(240f, 60f), new Color(0.18f, 0.55f, 0.78f),
                () => gameManager?.Restart());

            CreatePanelButton(container.transform, "SettlementQuitButton", "QUIT GAME",
                new Vector2(140f, 0f), new Vector2(240f, 60f), new Color(0.62f, 0.22f, 0.22f),
                () => gameManager?.QuitGame());
        }

        private TMP_Text CreatePanelText(Transform parent, string objectName, string content, float fontSize, Color color)
        {
            var go = new GameObject(objectName, typeof(RectTransform));
            go.layer = gameObject.layer;
            go.transform.SetParent(parent, false);
            TextMeshProUGUI label = go.AddComponent<TextMeshProUGUI>();
            if (enemyCountText != null && enemyCountText.font != null)
            {
                label.font = enemyCountText.font;
            }

            label.text = content;
            label.fontSize = fontSize;
            label.alignment = TextAlignmentOptions.Center;
            label.color = color;
            label.raycastTarget = false;
            return label;
        }

        private void CreatePanelButton(
            Transform parent, string objectName, string labelText,
            Vector2 anchoredPosition, Vector2 size, Color bgColor,
            UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Button));
            go.layer = gameObject.layer;
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            Image bg = go.GetComponent<Image>();
            bg.color = bgColor;
            bg.raycastTarget = true;

            Button button = go.GetComponent<Button>();
            button.targetGraphic = bg;
            button.onClick.AddListener(onClick);

            TMP_Text label = CreatePanelText(go.transform, "Label", labelText, 24f, Color.white);
            RectTransform labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
        }

        private void OnGameStateChanged(GameState state)
        {
            if (state == GameState.Playing)
            {
                return;
            }

            if (_pausePanel != null)
            {
                _pausePanel.SetActive(false);
            }

            if (crosshair != null)
            {
                crosshair.SetActive(false);
            }

            if (messageText != null)
            {
                string title = state == GameState.Victory ? "VICTORY" : "GAME OVER";
                messageText.text = $"{title}\n<size=45%>Press R to restart</size>";
            }

            if (messagePanel != null)
            {
                messagePanel.SetActive(true);
                messagePanel.transform.SetAsLastSibling();
            }
        }
    }
}
