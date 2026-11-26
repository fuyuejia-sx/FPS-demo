using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace FPS
{
    /// <summary>
    /// 背包界面模块：按 I 键弹出背包窗口，顶部显示当前装备的主武器，下方列出背包中所有未使用的武器，
    /// 点击任意武器旁的 EQUIP 按钮即可将其切换为当前主武器，原武器自动放回背包。
    /// 界面文本使用英文以适配项目默认 TMP 字体。
    /// 挂载对象：HUD 的 Canvas 物体（与 HUDController、ShopUI 同物体）。
    /// </summary>
    [DisallowMultipleComponent]
    public class BackpackUI : MonoBehaviour
    {
        [Header("引用")]
        [Tooltip("武器库与背包管理器。留空时自动查找。")]
        [SerializeField] private WeaponInventory inventory;

        [Tooltip("HUD 控制器（用于在打开背包时隐藏准星）。留空时自动查找。")]
        [SerializeField] private HUDController hudController;

        private GameObject _backpackPanel;
        private TMP_Text _hudHintText;
        private TMP_Text _equippedNameText;
        private TMP_Text _equippedStatsText;
        private TMP_Text _equippedDescText;
        private TMP_Text _feedbackText;
        private TMP_Text _emptyBackpackText;
        private Transform _listContainer;

        private readonly List<GameObject> _rowObjects = new List<GameObject>();
        private TMP_FontAsset _sharedFont;

        private void Start()
        {
            if (inventory == null)
            {
                inventory = FindFirstObjectByType<WeaponInventory>();
            }

            if (hudController == null)
            {
                hudController = GetComponent<HUDController>() ?? FindFirstObjectByType<HUDController>();
            }

            if (inventory == null)
            {
                Debug.LogWarning($"{nameof(BackpackUI)}: 场景中没有找到 WeaponInventory。", this);
                return;
            }

            EnsureUiInputSupport();
            CacheSharedFont();

            BuildHudHint();
            BuildBackpackPanel();

            inventory.BackpackToggled += OnBackpackToggled;
            inventory.InventoryChanged += OnInventoryChanged;

            SetPanelVisible(false);
        }

        private void OnDestroy()
        {
            if (inventory != null)
            {
                inventory.BackpackToggled -= OnBackpackToggled;
                inventory.InventoryChanged -= OnInventoryChanged;
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
                "BackpackHintText", transform,
                $"[{inventory.ToggleKeyLabel}] BACKPACK", 20f,
                TextAlignmentOptions.TopRight, new Color(0.75f, 0.86f, 0.96f, 0.9f));

            SetRect(_hudHintText.rectTransform, Vector2.one, Vector2.one, Vector2.one,
                new Vector2(-40f, -166f), new Vector2(300f, 34f));
            _hudHintText.transform.SetAsFirstSibling();
        }

        private void BuildBackpackPanel()
        {
            Image backdrop = CreateImage("BackpackPanel", transform, new Color(0.03f, 0.05f, 0.09f, 0.84f));
            Stretch(backdrop.rectTransform);
            backdrop.raycastTarget = true;
            _backpackPanel = backdrop.gameObject;

            // 居中主面板（920 x 680）
            Image window = CreateImage("BackpackWindow", backdrop.transform, new Color(0.07f, 0.10f, 0.16f, 0.97f));
            SetRect(window.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(920f, 680f));
            AddBorder(window.gameObject, new Color(0.25f, 0.78f, 1f, 0.55f), 2f);

            // ---------- 顶栏 ----------
            Image header = CreateImage("Header", window.transform, new Color(0.05f, 0.08f, 0.13f, 1f));
            SetRect(header.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                Vector2.zero, new Vector2(0f, 70f));

            TMP_Text title = CreateText("Title", header.transform, "WEAPON BACKPACK", 32f,
                TextAlignmentOptions.MidlineLeft, new Color(0.92f, 0.97f, 1f));
            SetRect(title.rectTransform, new Vector2(0f, 0f), new Vector2(0.7f, 1f), new Vector2(0f, 0.5f),
                new Vector2(28f, 0f), new Vector2(-28f, 0f));

            Button closeBtn = CreateButton("CloseButton", header.transform, $"CLOSE [{inventory.ToggleKeyLabel}]",
                new Color(0.22f, 0.27f, 0.36f), 20f, out _, out _);
            SetRect((RectTransform)closeBtn.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-22f, 0f), new Vector2(150f, 42f));
            closeBtn.onClick.AddListener(() => inventory.CloseBackpack());

            // ---------- 当前装备区 ----------
            TMP_Text activeHeader = CreateText("ActiveSectionTitle", window.transform, "CURRENTLY EQUIPPED WEAPON", 18f,
                TextAlignmentOptions.MidlineLeft, new Color(0.35f, 0.88f, 1f));
            SetRect(activeHeader.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -86f), new Vector2(-56f, 26f));

            Image activeCard = CreateImage("EquippedCard", window.transform, new Color(0.11f, 0.17f, 0.27f, 1f));
            SetRect(activeCard.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -118f), new Vector2(-56f, 128f));
            AddBorder(activeCard.gameObject, new Color(0.25f, 0.88f, 1f, 0.8f), 2f);

            _equippedNameText = CreateText("EquippedName", activeCard.transform, "ASSAULT RIFLE", 28f,
                TextAlignmentOptions.TopLeft, Color.white);
            SetRect(_equippedNameText.rectTransform, new Vector2(0f, 1f), new Vector2(0.75f, 1f), new Vector2(0f, 1f),
                new Vector2(22f, -16f), new Vector2(-24f, 36f));

            _equippedStatsText = CreateText("EquippedStats", activeCard.transform, string.Empty, 18f,
                TextAlignmentOptions.TopLeft, new Color(1f, 0.85f, 0.25f));
            SetRect(_equippedStatsText.rectTransform, new Vector2(0f, 1f), new Vector2(0.75f, 1f), new Vector2(0f, 1f),
                new Vector2(22f, -56f), new Vector2(-24f, 26f));

            _equippedDescText = CreateText("EquippedDesc", activeCard.transform, string.Empty, 17f,
                TextAlignmentOptions.TopLeft, new Color(0.78f, 0.85f, 0.93f));
            SetRect(_equippedDescText.rectTransform, new Vector2(0f, 1f), new Vector2(0.75f, 1f), new Vector2(0f, 1f),
                new Vector2(22f, -86f), new Vector2(-24f, 32f));

            Image badge = CreateImage("EquippedBadge", activeCard.transform, new Color(0.15f, 0.45f, 0.62f, 1f));
            SetRect(badge.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-22f, 0f), new Vector2(150f, 44f));
            TMP_Text badgeText = CreateText("BadgeLabel", badge.transform, "EQUIPPED", 20f,
                TextAlignmentOptions.Center, Color.white);
            Stretch(badgeText.rectTransform);

            // ---------- 背包闲置武器列表 ----------
            TMP_Text storageHeader = CreateText("StorageSectionTitle", window.transform,
                "STORED IN BACKPACK (CLICK EQUIP TO SWAP)", 18f,
                TextAlignmentOptions.MidlineLeft, new Color(0.78f, 0.86f, 0.95f));
            SetRect(storageHeader.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -268f), new Vector2(-56f, 26f));

            Image listBg = CreateImage("StorageBox", window.transform, new Color(0.05f, 0.08f, 0.13f, 0.92f));
            SetRect(listBg.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -124f), new Vector2(-56f, -352f));
            AddBorder(listBg.gameObject, new Color(0.24f, 0.34f, 0.48f, 0.5f), 1.5f);

            var listRoot = CreateUiObject("ListContainer", listBg.transform);
            Stretch(listRoot);
            var vLayout = listRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            vLayout.padding = new RectOffset(16, 16, 16, 16);
            vLayout.spacing = 12f;
            vLayout.childForceExpandWidth = true;
            vLayout.childForceExpandHeight = false;
            vLayout.childControlWidth = true;
            vLayout.childControlHeight = false;
            _listContainer = listRoot;

            _emptyBackpackText = CreateText("EmptyText", listBg.transform,
                "BACKPACK IS EMPTY\nBuy new weapons in Shop [B] to store or swap them here.", 22f,
                TextAlignmentOptions.Center, new Color(0.55f, 0.64f, 0.74f));
            _emptyBackpackText.textWrappingMode = TextWrappingModes.Normal;
            Stretch(_emptyBackpackText.rectTransform);

            // ---------- 底部提示 ----------
            _feedbackText = CreateText("FeedbackText", window.transform, string.Empty, 19f,
                TextAlignmentOptions.MidlineLeft, new Color(0.35f, 0.95f, 0.52f));
            SetRect(_feedbackText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 16f), new Vector2(-56f, 30f));
        }

        // =====================================================================
        // 刷新与交互
        // =====================================================================

        private void OnBackpackToggled(bool open)
        {
            SetPanelVisible(open);
            if (open)
            {
                if (_feedbackText != null)
                {
                    _feedbackText.text = string.Empty;
                }

                Refresh();
            }
        }

        private void OnInventoryChanged()
        {
            if (_backpackPanel != null && _backpackPanel.activeSelf)
            {
                Refresh();
            }
        }

        private void SetPanelVisible(bool visible)
        {
            if (_backpackPanel != null)
            {
                _backpackPanel.SetActive(visible);
            }

            if (hudController != null)
            {
                hudController.SetCrosshairVisible(!visible);
            }
        }

        private void Refresh()
        {
            if (inventory == null)
            {
                return;
            }

            WeaponInventory.WeaponEntry equipped = inventory.EquippedWeapon;
            if (equipped != null)
            {
                _equippedNameText.text = equipped.DisplayName;
                _equippedStatsText.text = equipped.BuildStatsSummary();
                _equippedDescText.text = equipped.Description;
            }
            else
            {
                _equippedNameText.text = "NONE";
                _equippedStatsText.text = string.Empty;
                _equippedDescText.text = string.Empty;
            }

            for (int i = 0; i < _rowObjects.Count; i++)
            {
                if (_rowObjects[i] != null)
                {
                    Destroy(_rowObjects[i]);
                }
            }

            _rowObjects.Clear();

            List<WeaponInventory.WeaponEntry> stored = inventory.GetBackpackWeapons();
            _emptyBackpackText.gameObject.SetActive(stored.Count == 0);

            for (int i = 0; i < stored.Count; i++)
            {
                CreateStorageRow(stored[i]);
            }
        }

        private void CreateStorageRow(WeaponInventory.WeaponEntry entry)
        {
            Image row = CreateImage($"Stored_{entry.DisplayName}", _listContainer, new Color(0.10f, 0.14f, 0.21f, 1f));
            AddBorder(row.gameObject, new Color(0.28f, 0.40f, 0.56f, 0.6f), 1.5f);

            var layoutElem = row.gameObject.AddComponent<LayoutElement>();
            layoutElem.preferredHeight = 96f;
            layoutElem.minHeight = 96f;

            TMP_Text nameLabel = CreateText("Name", row.transform, entry.DisplayName, 24f,
                TextAlignmentOptions.TopLeft, Color.white);
            SetRect(nameLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0.72f, 1f), new Vector2(0f, 1f),
                new Vector2(20f, -14f), new Vector2(-24f, 32f));

            TMP_Text statsLabel = CreateText("Stats", row.transform, entry.BuildStatsSummary(), 17f,
                TextAlignmentOptions.BottomLeft, new Color(0.35f, 0.88f, 1f));
            SetRect(statsLabel.rectTransform, new Vector2(0f, 0f), new Vector2(0.72f, 0f), new Vector2(0f, 0f),
                new Vector2(20f, 16f), new Vector2(-24f, 26f));

            Button equipBtn = CreateButton("EquipButton", row.transform, "EQUIP",
                new Color(0.16f, 0.60f, 0.36f), 22f, out _, out _);
            SetRect((RectTransform)equipBtn.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-20f, 0f), new Vector2(150f, 48f));

            GameObject weaponObj = entry.WeaponObject;
            string weaponName = entry.DisplayName;
            equipBtn.onClick.AddListener(() =>
            {
                if (inventory.EquipWeapon(weaponObj))
                {
                    _feedbackText.text = $"SWAPPED TO {weaponName}! PREVIOUS WEAPON STORED IN BACKPACK.";
                }
            });

            _rowObjects.Add(row.gameObject);
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
