using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FPS
{
    /// <summary>
    /// 背包与武器库模块：管理玩家已拥有的所有主武器、当前装备的主武器以及背包中未使用的武器。
    /// 玩家快捷栏始终保留「当前主武器 + 盾牌」；其余已拥有但未使用的武器自动收纳在背包里，
    /// 按 I 键可随时打开背包切换武器（切换后原武器自动放回背包并保留剩余弹药）。
    /// 挂载对象：场景中的常驻管理物体（例如 GameManager）。
    /// </summary>
    [DisallowMultipleComponent]
    public class WeaponInventory : MonoBehaviour
    {
        /// <summary>背包/武器库中的一件武器记录。</summary>
        public sealed class WeaponEntry
        {
            public GameObject WeaponObject;
            public string DisplayName;
            public string Description;

            public string BuildStatsSummary()
            {
                if (WeaponObject == null)
                {
                    return string.Empty;
                }

                Weapon weapon = WeaponObject.GetComponent<Weapon>();
                if (weapon == null)
                {
                    return string.Empty;
                }

                WeaponAmmo ammo = WeaponObject.GetComponent<WeaponAmmo>();
                string ammoPart = ammo != null
                    ? $"  |  AMMO {ammo.InMagazine} / {(ammo.HasInfiniteReserve ? "INF" : ammo.Reserve.ToString())}"
                    : string.Empty;
                string mode = weapon.IsAutomatic ? "AUTO" : "SEMI";
                return $"DMG {weapon.Damage:0}  |  RATE {weapon.FireRate:0.0}/s  |  {mode}{ammoPart}";
            }
        }

        [Header("引用")]
        [Tooltip("玩家装备切换组件。留空时自动查找。")]
        [SerializeField] private PlayerEquipment playerEquipment;

        [Tooltip("玩家武器控制器，用于识别初始主武器。留空时自动查找。")]
        [SerializeField] private PlayerWeaponController weaponController;

        [Tooltip("游戏流程管理器（游戏结束后禁止打开背包）。留空时自动查找。")]
        [SerializeField] private GameManager gameManager;

        [Tooltip("商城管理器（用于互斥切换：打开背包时自动关闭商城）。留空时自动查找。")]
        [SerializeField] private ShopManager shopManager;

        [Tooltip("打开背包时临时禁用的玩家控制组件。留空时自动收集。")]
        [SerializeField] private Behaviour[] disableWhileOpen;

        [Header("初始武器信息")]
        [Tooltip("玩家开局自带主武器在背包和替换提示里显示的名称。")]
        [SerializeField] private string starterWeaponName = "ASSAULT RIFLE";

        [Tooltip("玩家开局自带主武器的简介。")]
        [SerializeField] private string starterWeaponDescription = "Standard balanced automatic rifle.";

        [Header("操作")]
        [Tooltip("打开或关闭背包的快捷键，默认 I 键。打开后也可以按 Esc 关闭。")]
        [SerializeField] private Key toggleKey = Key.I;

        private readonly List<WeaponEntry> _ownedWeapons = new List<WeaponEntry>();
        private WeaponEntry _equippedWeapon;
        private bool[] _previousEnabledStates;
        private bool _initialized;

        /// <summary>背包界面当前是否处于打开状态。</summary>
        public bool IsBackpackOpen { get; private set; }

        /// <summary>当前装备在快捷栏上的主武器。</summary>
        public WeaponEntry EquippedWeapon => _equippedWeapon;

        /// <summary>玩家已拥有的全部主武器（包含当前装备的和背包里的）。</summary>
        public IReadOnlyList<WeaponEntry> OwnedWeapons => _ownedWeapons;

        /// <summary>背包开关快捷键名称（用于 HUD 提示）。</summary>
        public string ToggleKeyLabel => toggleKey.ToString().ToUpperInvariant();

        /// <summary>背包打开或关闭时触发。</summary>
        public event Action<bool> BackpackToggled;

        /// <summary>武器库内容或当前装备武器发生变化时触发。</summary>
        public event Action InventoryChanged;

        private void Awake()
        {
            EnsureInitialized();
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
            if (IsBackpackOpen)
            {
                Time.timeScale = 1f;
                IsBackpackOpen = false;
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
                if (IsBackpackOpen)
                {
                    CloseBackpack();
                }

                return;
            }

            if (gameManager != null && gameManager.IsPaused)
            {
                return;
            }

            if (keyboard[toggleKey].wasPressedThisFrame)
            {
                ToggleBackpack();
            }
            else if (IsBackpackOpen && keyboard.escapeKey.wasPressedThisFrame)
            {
                CloseBackpack();
                GameManager.ConsumeEscapeThisFrame();
            }
        }

        /// <summary>确保初始武器与组件引用已就绪（供 ShopManager 在 Awake 中提前调用）。</summary>
        public void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            ResolveReferences();
            RegisterStarterWeapon();
        }

        /// <summary>获取当前收纳在背包中（未被装备）的所有武器。</summary>
        public List<WeaponEntry> GetBackpackWeapons()
        {
            EnsureInitialized();
            var result = new List<WeaponEntry>();
            for (int i = 0; i < _ownedWeapons.Count; i++)
            {
                WeaponEntry entry = _ownedWeapons[i];
                if (entry != null && entry != _equippedWeapon)
                {
                    result.Add(entry);
                }
            }

            return result;
        }

        /// <summary>玩家是否已拥有某把武器。</summary>
        public bool HasWeapon(GameObject weaponObject)
        {
            EnsureInitialized();
            return FindEntry(weaponObject) != null;
        }

        /// <summary>某把武器当前是否正是主武器槽里装备的武器。</summary>
        public bool IsEquippedWeapon(GameObject weaponObject)
        {
            EnsureInitialized();
            return weaponObject != null && _equippedWeapon != null && _equippedWeapon.WeaponObject == weaponObject;
        }

        /// <summary>
        /// 登记一件新获得的武器（例如从商城购买）。
        /// equipNow 为 true 时立刻替换当前主武器并把旧武器放入背包；为 false 时直接存入背包。
        /// </summary>
        public void AddOwnedWeapon(GameObject weaponObject, string displayName, string description, bool equipNow)
        {
            EnsureInitialized();
            if (weaponObject == null)
            {
                return;
            }

            WeaponEntry entry = FindEntry(weaponObject);
            if (entry == null)
            {
                entry = new WeaponEntry
                {
                    WeaponObject = weaponObject,
                    DisplayName = string.IsNullOrEmpty(displayName) ? weaponObject.name.ToUpperInvariant() : displayName,
                    Description = description ?? string.Empty
                };
                _ownedWeapons.Add(entry);
            }

            if (equipNow)
            {
                EquipWeapon(weaponObject);
            }
            else
            {
                // 保持当前武器不动，未使用的武器自动移出快捷槽并放入背包
                if (playerEquipment != null && !IsEquippedWeapon(weaponObject))
                {
                    playerEquipment.RemoveSlot(weaponObject);
                }

                weaponObject.SetActive(false);
                InventoryChanged?.Invoke();
            }
        }

        /// <summary>
        /// 将背包中的武器替换到主武器槽并立即手持；原先装备的主武器自动放回背包并保留弹药。
        /// </summary>
        public bool EquipWeapon(GameObject weaponObject)
        {
            EnsureInitialized();
            WeaponEntry target = FindEntry(weaponObject);
            if (target == null || target.WeaponObject == null)
            {
                return false;
            }

            GameObject previous = _equippedWeapon != null ? _equippedWeapon.WeaponObject : null;
            _equippedWeapon = target;

            if (playerEquipment != null)
            {
                playerEquipment.ReplaceSlot(previous, target.WeaponObject, equipImmediately: true);
            }
            else
            {
                if (previous != null && previous != target.WeaponObject)
                {
                    previous.SetActive(false);
                }

                target.WeaponObject.SetActive(true);
            }

            InventoryChanged?.Invoke();
            return true;
        }

        /// <summary>切换背包开关状态。</summary>
        public void ToggleBackpack()
        {
            if (IsBackpackOpen)
            {
                CloseBackpack();
            }
            else
            {
                OpenBackpack();
            }
        }

        /// <summary>打开背包：如果商城开着则先关闭商城，然后暂停游戏并释放光标。</summary>
        public void OpenBackpack()
        {
            EnsureInitialized();
            if (IsBackpackOpen)
            {
                return;
            }

            if (gameManager != null && gameManager.State != GameState.Playing)
            {
                return;
            }

            ResolveReferences();
            if (shopManager != null && shopManager.IsOpen)
            {
                shopManager.CloseShop();
            }

            IsBackpackOpen = true;
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

            BackpackToggled?.Invoke(true);
        }

        /// <summary>关闭背包：恢复时间、玩家控制和鼠标锁定。</summary>
        public void CloseBackpack()
        {
            if (!IsBackpackOpen)
            {
                return;
            }

            IsBackpackOpen = false;
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

            BackpackToggled?.Invoke(false);
        }

        private WeaponEntry FindEntry(GameObject weaponObject)
        {
            if (weaponObject == null)
            {
                return null;
            }

            for (int i = 0; i < _ownedWeapons.Count; i++)
            {
                if (_ownedWeapons[i].WeaponObject == weaponObject)
                {
                    return _ownedWeapons[i];
                }
            }

            return null;
        }

        private void RegisterStarterWeapon()
        {
            Weapon starter = null;
            if (weaponController != null && weaponController.CurrentWeapon != null)
            {
                starter = weaponController.CurrentWeapon;
            }
            else if (playerEquipment != null)
            {
                foreach (Weapon candidate in playerEquipment.GetComponentsInChildren<Weapon>(true))
                {
                    if (!candidate.name.Equals("Blaster", StringComparison.OrdinalIgnoreCase))
                    {
                        starter = candidate;
                        break;
                    }
                }
            }

            if (starter == null)
            {
                return;
            }

            var entry = new WeaponEntry
            {
                WeaponObject = starter.gameObject,
                DisplayName = starterWeaponName,
                Description = starterWeaponDescription
            };

            _ownedWeapons.Add(entry);
            _equippedWeapon = entry;
        }

        private void ResolveReferences()
        {
            if (playerEquipment == null)
            {
                playerEquipment = FindFirstObjectByType<PlayerEquipment>();
            }

            if (weaponController == null)
            {
                weaponController = playerEquipment != null
                    ? playerEquipment.GetComponent<PlayerWeaponController>()
                    : FindFirstObjectByType<PlayerWeaponController>();
            }

            if (gameManager == null)
            {
                gameManager = GetComponent<GameManager>() ?? FindFirstObjectByType<GameManager>();
            }

            if (shopManager == null)
            {
                shopManager = GetComponent<ShopManager>() ?? FindFirstObjectByType<ShopManager>();
            }

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

        private void OnGameStateChanged(GameState state)
        {
            if (state != GameState.Playing && IsBackpackOpen)
            {
                CloseBackpack();
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
