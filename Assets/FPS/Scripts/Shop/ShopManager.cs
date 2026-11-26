using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FPS
{
    /// <summary>
    /// 商城核心管理模块：负责商城开关（默认按 B 键）、暂停/恢复战斗控制、锁定未购买装备，
    /// 以及扣除金币并解锁装备的购买逻辑。界面和 3D 陈列柜由 ShopUI 与 ShopDisplayCabinet 负责。
    /// 挂载对象：场景中的常驻管理物体（例如 GameManager）。
    /// </summary>
    [DisallowMultipleComponent]
    public class ShopManager : MonoBehaviour
    {
        [Header("引用")]
        [Tooltip("金币管理器。留空时自动查找。")]
        [SerializeField] private CoinManager coinManager;

        [Tooltip("玩家装备切换组件。留空时自动查找。")]
        [SerializeField] private PlayerEquipment playerEquipment;

        [Tooltip("武器背包管理器。留空时自动查找或创建。")]
        [SerializeField] private WeaponInventory weaponInventory;

        [Tooltip("游戏流程管理器（游戏结束后禁止打开商城）。留空时自动查找。")]
        [SerializeField] private GameManager gameManager;

        [Tooltip("打开商城时临时禁用的玩家控制组件（移动、视角、射击、切枪）。留空时自动收集。")]
        [SerializeField] private Behaviour[] disableWhileOpen;

        [Header("操作")]
        [Tooltip("打开或关闭商城的快捷键，默认 B 键。打开后也可以按 Esc 关闭。")]
        [SerializeField] private Key toggleKey = Key.B;

        [Header("商品列表")]
        [SerializeField] private List<ShopItemData> items = new List<ShopItemData>();

        private readonly HashSet<string> _ownedIds = new HashSet<string>();
        private bool[] _previousEnabledStates;

        /// <summary>商城当前是否处于打开状态。</summary>
        public bool IsOpen { get; private set; }

        /// <summary>商城中的商品列表。</summary>
        public IReadOnlyList<ShopItemData> Items => items;

        /// <summary>关联的金币管理器。</summary>
        public CoinManager CoinManager => coinManager;

        /// <summary>关联的玩家装备切换组件。</summary>
        public PlayerEquipment PlayerEquipment => playerEquipment;

        /// <summary>关联的武器背包管理器。</summary>
        public WeaponInventory WeaponInventory => weaponInventory;

        /// <summary>开关快捷键名称（用于 HUD 提示）。</summary>
        public string ToggleKeyLabel => toggleKey.ToString().ToUpperInvariant();

        /// <summary>商城打开或关闭时触发。</summary>
        public event Action<bool> ShopToggled;

        /// <summary>购买商品成功时触发，参数为（商品索引，商品数据）。</summary>
        public event Action<int, ShopItemData> ItemPurchased;

        private void Awake()
        {
            ResolveReferences();
            LockUnpurchasedItems();
        }

        private void OnEnable()
        {
            if (gameManager != null)
            {
                gameManager.StateChanged += OnGameStateChanged;
            }
        }

        private void OnDisable()
        {
            if (gameManager != null)
            {
                gameManager.StateChanged -= OnGameStateChanged;
            }
        }

        private void OnDestroy()
        {
            if (IsOpen)
            {
                Time.timeScale = 1f;
                IsOpen = false;
            }
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (gameManager != null && gameManager.State != GameState.Playing)
            {
                if (IsOpen)
                {
                    CloseShop();
                }

                return;
            }

            if (gameManager != null && gameManager.IsPaused)
            {
                return;
            }

            if (keyboard[toggleKey].wasPressedThisFrame)
            {
                ToggleShop();
            }
            else if (IsOpen && keyboard.escapeKey.wasPressedThisFrame)
            {
                CloseShop();
                GameManager.ConsumeEscapeThisFrame();
            }
        }

        /// <summary>切换商城开关状态。</summary>
        public void ToggleShop()
        {
            if (IsOpen)
            {
                CloseShop();
            }
            else
            {
                OpenShop();
            }
        }

        /// <summary>打开商城：暂停时间、禁用玩家操作并释放鼠标光标。</summary>
        public void OpenShop()
        {
            if (IsOpen)
            {
                return;
            }

            if (gameManager != null && gameManager.State != GameState.Playing)
            {
                return;
            }

            ResolveReferences();
            if (weaponInventory != null && weaponInventory.IsBackpackOpen)
            {
                weaponInventory.CloseBackpack();
            }

            IsOpen = true;
            Time.timeScale = 0f;

            if (disableWhileOpen != null)
            {
                _previousEnabledStates = new bool[disableWhileOpen.Length];
                for (int i = 0; i < disableWhileOpen.Length; i++)
                {
                    Behaviour behaviour = disableWhileOpen[i];
                    if (behaviour != null)
                    {
                        _previousEnabledStates[i] = behaviour.enabled;
                        behaviour.enabled = false;
                    }
                }
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            ShopToggled?.Invoke(true);
        }

        /// <summary>关闭商城：恢复时间、恢复玩家操作并重新锁定光标。</summary>
        public void CloseShop()
        {
            if (!IsOpen)
            {
                return;
            }

            IsOpen = false;
            Time.timeScale = 1f;

            if (disableWhileOpen != null && _previousEnabledStates != null)
            {
                int count = Mathf.Min(disableWhileOpen.Length, _previousEnabledStates.Length);
                for (int i = 0; i < count; i++)
                {
                    if (disableWhileOpen[i] != null)
                    {
                        disableWhileOpen[i].enabled = _previousEnabledStates[i];
                    }
                }
            }

            ShopToggled?.Invoke(false);
        }

        /// <summary>指定商品是否已经购买。</summary>
        public bool IsOwned(int index)
        {
            ShopItemData item = GetItem(index);
            return item != null && _ownedIds.Contains(GetItemKey(item, index));
        }

        /// <summary>指定商品对应的装备当前是否已装备在主武器槽中。</summary>
        public bool IsEquipped(int index)
        {
            ShopItemData item = GetItem(index);
            if (item == null || item.equipmentObject == null)
            {
                return false;
            }

            if (weaponInventory != null)
            {
                return weaponInventory.IsEquippedWeapon(item.equipmentObject);
            }

            return playerEquipment != null && playerEquipment.Current == item.equipmentObject;
        }

        /// <summary>当前金币是否足够购买指定商品。</summary>
        public bool CanAfford(int index)
        {
            ShopItemData item = GetItem(index);
            return item != null && coinManager != null && coinManager.CurrentCoins >= item.price;
        }

        /// <summary>
        /// 尝试购买指定商品。成功时扣金币并将新武器自动存入背包（不会强行替换当前武器，由弹窗让玩家选择是否替换）。
        /// </summary>
        public bool TryBuy(int index, out string feedback)
        {
            ShopItemData item = GetItem(index);
            if (item == null)
            {
                feedback = "ITEM UNAVAILABLE";
                return false;
            }

            string key = GetItemKey(item, index);
            if (_ownedIds.Contains(key))
            {
                feedback = "ALREADY OWNED";
                return false;
            }

            if (coinManager == null)
            {
                coinManager = FindFirstObjectByType<CoinManager>();
            }

            if (coinManager == null || !coinManager.SpendCoins(item.price))
            {
                feedback = $"NOT ENOUGH COINS (NEED {item.price})";
                return false;
            }

            _ownedIds.Add(key);

            if (item.equipmentObject != null)
            {
                if (weaponInventory != null)
                {
                    weaponInventory.AddOwnedWeapon(
                        item.equipmentObject, item.displayName, item.description, equipNow: false);
                }
                else if (playerEquipment != null)
                {
                    playerEquipment.AddSlot(item.equipmentObject, true);
                }
            }

            feedback = $"PURCHASED {item.displayName}!";
            ItemPurchased?.Invoke(index, item);
            return true;
        }

        /// <summary>将一件已经购买的武器替换到当前主武器槽，原先的主武器自动放入背包。</summary>
        public bool TryEquip(int index)
        {
            ShopItemData item = GetItem(index);
            if (item == null || !IsOwned(index) || item.equipmentObject == null)
            {
                return false;
            }

            if (weaponInventory != null)
            {
                return weaponInventory.EquipWeapon(item.equipmentObject);
            }

            return playerEquipment != null && playerEquipment.Equip(item.equipmentObject);
        }

        private ShopItemData GetItem(int index)
        {
            return items != null && index >= 0 && index < items.Count ? items[index] : null;
        }

        private static string GetItemKey(ShopItemData item, int index)
        {
            return !string.IsNullOrEmpty(item.id) ? item.id : $"item_{index}";
        }

        private void ResolveReferences()
        {
            if (coinManager == null)
            {
                coinManager = GetComponent<CoinManager>() ?? FindFirstObjectByType<CoinManager>();
            }

            if (gameManager == null)
            {
                gameManager = GetComponent<GameManager>() ?? FindFirstObjectByType<GameManager>();
            }

            if (playerEquipment == null)
            {
                playerEquipment = FindFirstObjectByType<PlayerEquipment>();
            }

            if (weaponInventory == null)
            {
                weaponInventory = GetComponent<WeaponInventory>() ?? FindFirstObjectByType<WeaponInventory>();
                if (weaponInventory == null)
                {
                    weaponInventory = gameObject.AddComponent<WeaponInventory>();
                }
            }

            weaponInventory.EnsureInitialized();

            if ((disableWhileOpen == null || disableWhileOpen.Length == 0) && playerEquipment != null)
            {
                GameObject player = playerEquipment.gameObject;
                var list = new List<Behaviour>();
                AddIfPresent<PlayerMovement>(player, list);
                AddIfPresent<PlayerLook>(player, list);
                AddIfPresent<PlayerWeaponController>(player, list);
                AddIfPresent<PlayerEquipment>(player, list);
                disableWhileOpen = list.ToArray();
            }
        }

        private void LockUnpurchasedItems()
        {
            if (items == null)
            {
                return;
            }

            for (int i = 0; i < items.Count; i++)
            {
                ShopItemData item = items[i];
                if (item == null || item.equipmentObject == null || IsOwned(i))
                {
                    continue;
                }

                // 未购买的商城武器不占快捷栏，避免影响玩家当前主武器与盾牌的切换
                if (playerEquipment != null)
                {
                    playerEquipment.RemoveSlot(item.equipmentObject);
                }

                item.equipmentObject.SetActive(false);
            }
        }

        private void OnGameStateChanged(GameState state)
        {
            if (state != GameState.Playing && IsOpen)
            {
                CloseShop();
            }
        }

        private static void AddIfPresent<T>(GameObject target, List<Behaviour> list) where T : Behaviour
        {
            T component = target.GetComponent<T>();
            if (component != null)
            {
                list.Add(component);
            }
        }
    }
}
