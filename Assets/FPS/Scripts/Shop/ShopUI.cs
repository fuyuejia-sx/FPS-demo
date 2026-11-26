using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace FPS
{
    /// <summary>
    /// 商城界面模块：在 HUD Canvas 下构建带实时 3D 陈列柜的商城面板，
    /// 支持点击货架槽位查看装备、拖拽旋转 3D 模型、使用金币购买与一键装备。
    /// 界面文本使用英文以适配项目默认 TMP 字体。
    /// 挂载对象：HUD 的 Canvas 物体（与 HUDController 同物体）。
    /// </summary>
    [DisallowMultipleComponent]
    public class ShopUI : MonoBehaviour
    {
        [Header("引用")]
        [Tooltip("商城逻辑管理器。留空时自动查找。")]
        [SerializeField] private ShopManager shopManager;

        [Tooltip("HUD 控制器（用于在打开商城时隐藏准星）。留空时自动查找。")]
        [SerializeField] private HUDController hudController;

        private sealed class SlotCard
        {
            public Button Button;
            public Image Background;
            public Outline Border;
            public TMP_Text NameLabel;
            public TMP_Text StatusLabel;
            public int ItemIndex;
        }

        /// <summary>挂在 3D 陈列柜 RawImage 上的拖拽监听器，拖动时旋转当前展示的模型。</summary>
        private sealed class CabinetDragSurface : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
        {
            public ShopDisplayCabinet Cabinet;

            public void OnBeginDrag(PointerEventData eventData) => Cabinet?.SetDragging(true);
            public void OnDrag(PointerEventData eventData) => Cabinet?.RotateSelected(eventData.delta.x * 0.45f);
            public void OnEndDrag(PointerEventData eventData) => Cabinet?.SetDragging(false);
        }

        private ShopDisplayCabinet _cabinet;
        private GameObject _shopPanel;
        private TMP_Text _hudHintText;
        private TMP_Text _headerCoinsText;
        private TMP_Text _detailNameText;
        private TMP_Text _detailStatsText;
        private TMP_Text _detailDescText;
        private TMP_Text _feedbackText;
        private Button _actionButton;
        private Image _actionButtonImage;
        private TMP_Text _actionButtonLabel;

        // 购买后弹出的是否替换确认框
        private GameObject _replacePromptPanel;
        private TMP_Text _replacePromptTitle;
        private TMP_Text _replacePromptBody;
        private int _pendingReplaceIndex = -1;

        private readonly List<SlotCard> _cards = new List<SlotCard>();
        private TMP_FontAsset _sharedFont;
        private int _selectedIndex;

        private void Start()
        {
            if (shopManager == null)
            {
                shopManager = FindFirstObjectByType<ShopManager>();
            }

            if (hudController == null)
            {
                hudController = GetComponent<HUDController>() ?? FindFirstObjectByType<HUDController>();
            }

            if (shopManager == null)
            {
                Debug.LogWarning($"{nameof(ShopUI)}: 场景中没有找到 ShopManager，请先执行 FPS > 配置商城与陈列柜。", this);
                return;
            }

            EnsureUiInputSupport();
            EnsureBackpackUi();
            CacheSharedFont();

            _cabinet = gameObject.AddComponent<ShopDisplayCabinet>();
            _cabinet.Initialize(shopManager.Items);

            BuildHudHint();
            BuildShopPanel();

            shopManager.ShopToggled += OnShopToggled;
            shopManager.ItemPurchased += OnItemPurchased;

            if (shopManager.CoinManager != null)
            {
                shopManager.CoinManager.CoinsChanged += OnCoinsChanged;
            }

            if (shopManager.PlayerEquipment != null)
            {
                shopManager.PlayerEquipment.Equipped += OnEquippedChanged;
            }

            if (shopManager.WeaponInventory != null)
            {
                shopManager.WeaponInventory.InventoryChanged += OnInventoryChanged;
            }

            SelectSlot(0);
            SetPanelVisible(false);
        }

        private void OnDestroy()
        {
            if (shopManager != null)
            {
                shopManager.ShopToggled -= OnShopToggled;
                shopManager.ItemPurchased -= OnItemPurchased;

                if (shopManager.CoinManager != null)
                {
                    shopManager.CoinManager.CoinsChanged -= OnCoinsChanged;
                }

                if (shopManager.PlayerEquipment != null)
                {
                    shopManager.PlayerEquipment.Equipped -= OnEquippedChanged;
                }

                if (shopManager.WeaponInventory != null)
                {
                    shopManager.WeaponInventory.InventoryChanged -= OnInventoryChanged;
                }
            }
        }

        private void EnsureBackpackUi()
        {
            if (GetComponent<BackpackUI>() == null && FindFirstObjectByType<BackpackUI>() == null)
            {
                gameObject.AddComponent<BackpackUI>();
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

        private void CacheSharedFont()
        {
            TMP_Text existing = GetComponentInChildren<TMP_Text>(true);
            if (existing != null)
            {
                _sharedFont = existing.font;
            }
        }

        // =====================================================================
        // 构建界面
        // =====================================================================

        private void BuildHudHint()
        {
            _hudHintText = CreateText(
                "ShopHintText", transform,
                $"[{shopManager.ToggleKeyLabel}] SHOP", 20f,
                TextAlignmentOptions.TopRight, new Color(0.75f, 0.86f, 0.96f, 0.9f));

            SetRect(_hudHintText.rectTransform, Vector2.one, Vector2.one, Vector2.one,
                new Vector2(-40f, -132f), new Vector2(300f, 34f));
            _hudHintText.transform.SetAsFirstSibling();
        }

        private void BuildShopPanel()
        {
            // 全屏半透明深色遮罩
            Image backdrop = CreateImage("ShopPanel", transform, new Color(0.03f, 0.05f, 0.09f, 0.86f));
            Stretch(backdrop.rectTransform);
            backdrop.raycastTarget = true;
            _shopPanel = backdrop.gameObject;

            // 居中主窗口（1360 x 880）
            Image window = CreateImage("ShopWindow", backdrop.transform, new Color(0.07f, 0.10f, 0.16f, 0.96f));
            SetRect(window.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(1360f, 880f));
            AddBorder(window.gameObject, new Color(0.20f, 0.72f, 0.95f, 0.55f), 2f);

            // ---------- 顶栏 ----------
            Image header = CreateImage("Header", window.transform, new Color(0.05f, 0.08f, 0.13f, 1f));
            SetRect(header.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                Vector2.zero, new Vector2(0f, 74f));

            TMP_Text title = CreateText("Title", header.transform, "ARMORY SHOWCASE SHOP", 34f,
                TextAlignmentOptions.MidlineLeft, new Color(0.92f, 0.97f, 1f));
            SetRect(title.rectTransform, new Vector2(0f, 0f), new Vector2(0.55f, 1f), new Vector2(0f, 0.5f),
                new Vector2(28f, 0f), new Vector2(-28f, 0f));

            _headerCoinsText = CreateText("HeaderCoins", header.transform, "COINS: 100", 30f,
                TextAlignmentOptions.MidlineRight, new Color(1f, 0.85f, 0.22f));
            SetRect(_headerCoinsText.rectTransform, new Vector2(0.55f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f),
                new Vector2(-190f, 0f), new Vector2(-220f, 0f));

            Button closeBtn = CreateButton("CloseButton", header.transform, $"CLOSE [{shopManager.ToggleKeyLabel}]",
                new Color(0.22f, 0.27f, 0.36f), 20f, out _, out _);
            SetRect((RectTransform)closeBtn.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-22f, 0f), new Vector2(148f, 44f));
            closeBtn.onClick.AddListener(() => shopManager.CloseShop());

            // ---------- 中央 3D 陈列柜视口 ----------
            Image cabinetFrame = CreateImage("CabinetFrame", window.transform, new Color(0.03f, 0.05f, 0.08f, 1f));
            SetRect(cabinetFrame.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -90f), new Vector2(-56f, 495f));
            AddBorder(cabinetFrame.gameObject, new Color(0.22f, 0.78f, 1f, 0.45f), 2f);

            var rawObj = new GameObject("CabinetViewport", typeof(RectTransform), typeof(RawImage));
            rawObj.layer = gameObject.layer;
            rawObj.transform.SetParent(cabinetFrame.transform, false);
            var raw = rawObj.GetComponent<RawImage>();
            raw.texture = _cabinet.OutputTexture;
            raw.raycastTarget = true;
            Stretch(raw.rectTransform);

            var drag = rawObj.AddComponent<CabinetDragSurface>();
            drag.Cabinet = _cabinet;

            TMP_Text dragHint = CreateText("DragHint", cabinetFrame.transform, "DRAG TO ROTATE 3D MODEL", 16f,
                TextAlignmentOptions.BottomRight, new Color(0.75f, 0.90f, 1f, 0.65f));
            SetRect(dragHint.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-14f, 10f), new Vector2(300f, 26f));

            // ---------- 底部：左侧陈列槽卡片 + 右侧详情与购买区 ----------
            var bottomArea = CreateUiObject("BottomArea", window.transform);
            SetRect(bottomArea, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 22f), new Vector2(-56f, 255f));

            // 左侧：陈列柜隔间卡片列表
            var shelfRow = CreateUiObject("ShelfCards", bottomArea);
            SetRect(shelfRow, new Vector2(0f, 0f), new Vector2(0.58f, 1f), new Vector2(0f, 0.5f),
                Vector2.zero, Vector2.zero);

            var layout = shelfRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 14f;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            _cards.Clear();
            int slotCount = _cabinet.BayCount;
            int itemCount = shopManager.Items.Count;

            for (int i = 0; i < slotCount; i++)
            {
                bool hasItem = i < itemCount && shopManager.Items[i] != null;
                CreateShelfCard(shelfRow, i, hasItem ? shopManager.Items[i] : null);
            }

            // 右侧：商品详情与购买按钮面板
            Image detailPanel = CreateImage("DetailPanel", bottomArea, new Color(0.10f, 0.14f, 0.21f, 0.96f));
            SetRect(detailPanel.rectTransform, new Vector2(0.60f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f),
                Vector2.zero, Vector2.zero);
            AddBorder(detailPanel.gameObject, new Color(0.28f, 0.40f, 0.56f, 0.5f), 1.5f);

            _detailNameText = CreateText("DetailName", detailPanel.transform, "PULSE BLASTER", 28f,
                TextAlignmentOptions.TopLeft, Color.white);
            SetRect(_detailNameText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -16f), new Vector2(-40f, 38f));

            _detailStatsText = CreateText("DetailStats", detailPanel.transform, string.Empty, 17f,
                TextAlignmentOptions.TopLeft, new Color(0.35f, 0.88f, 1f));
            SetRect(_detailStatsText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -56f), new Vector2(-40f, 28f));

            _detailDescText = CreateText("DetailDesc", detailPanel.transform, string.Empty, 17f,
                TextAlignmentOptions.TopLeft, new Color(0.78f, 0.84f, 0.92f));
            _detailDescText.textWrappingMode = TextWrappingModes.Normal;
            SetRect(_detailDescText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -88f), new Vector2(-40f, 66f));

            _feedbackText = CreateText("FeedbackText", detailPanel.transform, string.Empty, 18f,
                TextAlignmentOptions.BottomLeft, new Color(1f, 0.85f, 0.25f));
            SetRect(_feedbackText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 74f), new Vector2(-40f, 28f));

            _actionButton = CreateButton("ActionButton", detailPanel.transform, "BUY - 80 COINS",
                new Color(0.16f, 0.62f, 0.34f), 24f, out _actionButtonImage, out _actionButtonLabel);
            SetRect((RectTransform)_actionButton.transform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 16f), new Vector2(-40f, 52f));
            _actionButton.onClick.AddListener(OnActionButtonClicked);

            BuildReplacePrompt(window.transform);
        }

        private void BuildReplacePrompt(Transform parent)
        {
            Image overlay = CreateImage("ReplacePromptOverlay", parent, new Color(0.02f, 0.04f, 0.07f, 0.80f));
            Stretch(overlay.rectTransform);
            overlay.raycastTarget = true;
            _replacePromptPanel = overlay.gameObject;

            Image dialog = CreateImage("ReplaceDialog", overlay.transform, new Color(0.09f, 0.13f, 0.21f, 1f));
            SetRect(dialog.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(640f, 290f));
            AddBorder(dialog.gameObject, new Color(0.25f, 0.88f, 1f, 0.85f), 2f);

            _replacePromptTitle = CreateText("PromptTitle", dialog.transform, "EQUIP NEW WEAPON?", 30f,
                TextAlignmentOptions.Center, new Color(1f, 0.86f, 0.25f));
            SetRect(_replacePromptTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -24f), new Vector2(-48f, 42f));

            _replacePromptBody = CreateText("PromptBody", dialog.transform, string.Empty, 20f,
                TextAlignmentOptions.Center, new Color(0.86f, 0.92f, 0.98f));
            _replacePromptBody.textWrappingMode = TextWrappingModes.Normal;
            SetRect(_replacePromptBody.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 8f), new Vector2(-56f, 108f));

            Button replaceBtn = CreateButton("ReplaceYesButton", dialog.transform, "YES - REPLACE",
                new Color(0.16f, 0.64f, 0.36f), 22f, out _, out _);
            SetRect((RectTransform)replaceBtn.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(1f, 0f),
                new Vector2(-16f, 26f), new Vector2(240f, 54f));
            replaceBtn.onClick.AddListener(OnConfirmReplace);

            Button keepBtn = CreateButton("ReplaceNoButton", dialog.transform, "NO - KEEP CURRENT",
                new Color(0.24f, 0.30f, 0.40f), 22f, out _, out _);
            SetRect((RectTransform)keepBtn.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 0f),
                new Vector2(16f, 26f), new Vector2(240f, 54f));
            keepBtn.onClick.AddListener(OnKeepCurrent);

            _replacePromptPanel.SetActive(false);
        }

        private void CreateShelfCard(Transform parent, int index, ShopItemData item)
        {
            Image bg = CreateImage($"SlotCard_{index}", parent, new Color(0.10f, 0.14f, 0.21f, 0.95f));
            bg.raycastTarget = true;
            Outline border = AddBorder(bg.gameObject, new Color(0.24f, 0.34f, 0.48f, 0.55f), 2f);

            Button button = bg.gameObject.AddComponent<Button>();
            button.targetGraphic = bg;

            TMP_Text bayTag = CreateText("BayTag", bg.transform, $"SLOT 0{index + 1}", 16f,
                TextAlignmentOptions.TopLeft, new Color(0.55f, 0.70f, 0.85f));
            SetRect(bayTag.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -14f), new Vector2(-28f, 24f));

            TMP_Text nameLabel = CreateText("ItemName", bg.transform,
                item != null ? item.displayName : "EMPTY SLOT", 22f,
                TextAlignmentOptions.MidlineLeft,
                item != null ? Color.white : new Color(0.45f, 0.52f, 0.62f));
            nameLabel.textWrappingMode = TextWrappingModes.Normal;
            SetRect(nameLabel.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 8f), new Vector2(-28f, 70f));

            TMP_Text statusLabel = CreateText("ItemStatus", bg.transform,
                item != null ? $"{item.price} COINS" : "COMING SOON", 20f,
                TextAlignmentOptions.BottomLeft,
                item != null ? new Color(1f, 0.85f, 0.22f) : new Color(0.42f, 0.48f, 0.58f));
            SetRect(statusLabel.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 16f), new Vector2(-28f, 32f));

            int capturedIndex = index;
            button.onClick.AddListener(() => SelectSlot(capturedIndex));

            _cards.Add(new SlotCard
            {
                Button = button,
                Background = bg,
                Border = border,
                NameLabel = nameLabel,
                StatusLabel = statusLabel,
                ItemIndex = index
            });
        }

        // =====================================================================
        // 交互与状态刷新
        // =====================================================================

        private void SelectSlot(int index)
        {
            _selectedIndex = Mathf.Clamp(index, 0, Mathf.Max(0, _cards.Count - 1));
            _cabinet?.SelectItem(_selectedIndex);

            if (_feedbackText != null)
            {
                _feedbackText.text = string.Empty;
            }

            RefreshAll();
        }

        private void OnActionButtonClicked()
        {
            if (_selectedIndex < 0 || _selectedIndex >= shopManager.Items.Count)
            {
                return;
            }

            if (shopManager.IsOwned(_selectedIndex))
            {
                if (shopManager.TryEquip(_selectedIndex))
                {
                    SetFeedback("SWAPPED WEAPON! PREVIOUS WEAPON STORED IN BACKPACK.", new Color(0.35f, 0.88f, 1f));
                }

                RefreshAll();
                return;
            }

            bool ok = shopManager.TryBuy(_selectedIndex, out string message);
            SetFeedback(message, ok ? new Color(0.35f, 0.95f, 0.52f) : new Color(1f, 0.42f, 0.42f));
            RefreshAll();

            if (ok)
            {
                ShowReplacePrompt(_selectedIndex, shopManager.Items[_selectedIndex]);
            }
        }

        private void ShowReplacePrompt(int index, ShopItemData item)
        {
            if (_replacePromptPanel == null || item == null)
            {
                return;
            }

            _pendingReplaceIndex = index;
            string currentName = shopManager.WeaponInventory?.EquippedWeapon?.DisplayName ?? "CURRENT WEAPON";
            string backpackKey = shopManager.WeaponInventory != null ? shopManager.WeaponInventory.ToggleKeyLabel : "I";

            _replacePromptTitle.text = $"EQUIP {item.displayName}?";
            _replacePromptBody.text =
                $"Replace {currentName} with {item.displayName}?\n" +
                $"The unused weapon will be stored in Backpack [{backpackKey}].";
            _replacePromptPanel.SetActive(true);
        }

        private void OnConfirmReplace()
        {
            if (_pendingReplaceIndex >= 0 && shopManager.TryEquip(_pendingReplaceIndex))
            {
                ShopItemData item = shopManager.Items[_pendingReplaceIndex];
                string backpackKey = shopManager.WeaponInventory != null ? shopManager.WeaponInventory.ToggleKeyLabel : "I";
                SetFeedback($"EQUIPPED {item.displayName}! OLD WEAPON MOVED TO BACKPACK [{backpackKey}].",
                    new Color(0.35f, 0.95f, 0.52f));
            }

            HideReplacePrompt();
            RefreshAll();
        }

        private void OnKeepCurrent()
        {
            if (_pendingReplaceIndex >= 0 && _pendingReplaceIndex < shopManager.Items.Count)
            {
                ShopItemData item = shopManager.Items[_pendingReplaceIndex];
                string backpackKey = shopManager.WeaponInventory != null ? shopManager.WeaponInventory.ToggleKeyLabel : "I";
                SetFeedback($"KEPT CURRENT WEAPON. {item.displayName} STORED IN BACKPACK [{backpackKey}].",
                    new Color(0.35f, 0.88f, 1f));
            }

            HideReplacePrompt();
            RefreshAll();
        }

        private void HideReplacePrompt()
        {
            _pendingReplaceIndex = -1;
            if (_replacePromptPanel != null)
            {
                _replacePromptPanel.SetActive(false);
            }
        }

        private void OnShopToggled(bool open)
        {
            if (!open)
            {
                HideReplacePrompt();
            }

            SetPanelVisible(open);
            if (open)
            {
                if (_feedbackText != null)
                {
                    _feedbackText.text = string.Empty;
                }

                RefreshAll();
            }
        }

        private void OnItemPurchased(int index, ShopItemData item)
        {
            RefreshAll();
        }

        private void OnCoinsChanged(int total, int delta)
        {
            if (_shopPanel != null && _shopPanel.activeSelf)
            {
                RefreshAll();
            }
        }

        private void OnEquippedChanged(GameObject equipped)
        {
            if (_shopPanel != null && _shopPanel.activeSelf)
            {
                RefreshAll();
            }
        }

        private void OnInventoryChanged()
        {
            if (_shopPanel != null && _shopPanel.activeSelf)
            {
                RefreshAll();
            }
        }

        private void SetPanelVisible(bool visible)
        {
            if (_shopPanel != null)
            {
                _shopPanel.SetActive(visible);
            }

            _cabinet?.SetActive(visible);

            if (hudController != null)
            {
                hudController.SetCrosshairVisible(!visible);
            }
        }

        private void SetFeedback(string message, Color color)
        {
            if (_feedbackText == null)
            {
                return;
            }

            _feedbackText.text = message;
            _feedbackText.color = color;
        }

        private void RefreshAll()
        {
            if (shopManager == null)
            {
                return;
            }

            int coins = shopManager.CoinManager != null ? shopManager.CoinManager.CurrentCoins : 0;
            if (_headerCoinsText != null)
            {
                _headerCoinsText.text = $"COINS: {coins}";
            }

            for (int i = 0; i < _cards.Count; i++)
            {
                SlotCard card = _cards[i];
                bool selected = i == _selectedIndex;
                bool hasItem = i < shopManager.Items.Count && shopManager.Items[i] != null;

                card.Background.color = selected
                    ? new Color(0.14f, 0.22f, 0.34f, 1f)
                    : new Color(0.09f, 0.13f, 0.20f, 0.95f);
                card.Border.effectColor = selected
                    ? new Color(0.25f, 0.88f, 1f, 0.95f)
                    : new Color(0.24f, 0.34f, 0.48f, 0.45f);

                if (!hasItem)
                {
                    card.StatusLabel.text = "COMING SOON";
                    card.StatusLabel.color = new Color(0.45f, 0.50f, 0.58f);
                    continue;
                }

                ShopItemData item = shopManager.Items[i];
                if (shopManager.IsEquipped(i))
                {
                    card.StatusLabel.text = "EQUIPPED";
                    card.StatusLabel.color = new Color(0.32f, 0.92f, 1f);
                }
                else if (shopManager.IsOwned(i))
                {
                    card.StatusLabel.text = "IN BACKPACK";
                    card.StatusLabel.color = new Color(0.35f, 0.95f, 0.52f);
                }
                else
                {
                    card.StatusLabel.text = $"{item.price} COINS";
                    card.StatusLabel.color = new Color(1f, 0.85f, 0.22f);
                }
            }

            RefreshDetailPanel();
            _cabinet?.RefreshBayHighlights(shopManager);
        }

        private void RefreshDetailPanel()
        {
            bool hasItem = _selectedIndex >= 0
                && _selectedIndex < shopManager.Items.Count
                && shopManager.Items[_selectedIndex] != null;

            if (!hasItem)
            {
                _detailNameText.text = "EMPTY DISPLAY SLOT";
                _detailStatsText.text = "MORE GEAR COMING SOON";
                _detailDescText.text = "Add new items in ShopManager Inspector to display them on this pedestal.";
                _actionButton.interactable = false;
                _actionButtonImage.color = new Color(0.20f, 0.24f, 0.30f);
                _actionButtonLabel.text = "UNAVAILABLE";
                return;
            }

            ShopItemData item = shopManager.Items[_selectedIndex];
            _detailNameText.text = item.displayName;
            _detailStatsText.text = item.BuildStatsSummary();
            _detailDescText.text = item.description;

            if (shopManager.IsEquipped(_selectedIndex))
            {
                _actionButton.interactable = false;
                _actionButtonImage.color = new Color(0.18f, 0.42f, 0.58f);
                _actionButtonLabel.text = "EQUIPPED";
            }
            else if (shopManager.IsOwned(_selectedIndex))
            {
                _actionButton.interactable = true;
                _actionButtonImage.color = new Color(0.18f, 0.52f, 0.78f);
                _actionButtonLabel.text = "EQUIP FROM BACKPACK";
            }
            else if (shopManager.CanAfford(_selectedIndex))
            {
                _actionButton.interactable = true;
                _actionButtonImage.color = new Color(0.16f, 0.64f, 0.34f);
                _actionButtonLabel.text = $"BUY - {item.price} COINS";
            }
            else
            {
                _actionButton.interactable = true; // 允许点击弹出“金币不足”提示
                _actionButtonImage.color = new Color(0.45f, 0.24f, 0.24f);
                _actionButtonLabel.text = $"NEED {item.price} COINS";
            }
        }

        // =====================================================================
        // UI 工具方法
        // =====================================================================

        private RectTransform CreateUiObject(string objectName, Transform parent)
        {
            var go = new GameObject(objectName, typeof(RectTransform));
            go.layer = gameObject.layer;
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private Image CreateImage(string objectName, Transform parent, Color color)
        {
            Image image = CreateUiObject(objectName, parent).gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private TMP_Text CreateText(
            string objectName, Transform parent, string content, float fontSize,
            TextAlignmentOptions alignment, Color color)
        {
            TextMeshProUGUI label = CreateUiObject(objectName, parent).gameObject.AddComponent<TextMeshProUGUI>();
            if (_sharedFont != null)
            {
                label.font = _sharedFont;
            }

            label.text = content;
            label.fontSize = fontSize;
            label.alignment = alignment;
            label.color = color;
            label.raycastTarget = false;
            return label;
        }

        private Button CreateButton(
            string objectName, Transform parent, string text, Color bgColor, float fontSize,
            out Image bgImage, out TMP_Text label)
        {
            bgImage = CreateImage(objectName, parent, bgColor);
            bgImage.raycastTarget = true;

            Button button = bgImage.gameObject.AddComponent<Button>();
            button.targetGraphic = bgImage;

            label = CreateText("Label", bgImage.transform, text, fontSize, TextAlignmentOptions.Center, Color.white);
            Stretch(label.rectTransform);
            return button;
        }

        private static Outline AddBorder(GameObject target, Color color, float thickness)
        {
            Outline outline = target.AddComponent<Outline>();
            outline.effectColor = color;
            outline.effectDistance = new Vector2(thickness, -thickness);
            return outline;
        }

        private static void SetRect(
            RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 anchoredPosition, Vector2 size)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
