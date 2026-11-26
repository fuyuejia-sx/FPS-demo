using System;
using UnityEngine;

namespace FPS
{
    /// <summary>
    /// 玩家武器控制模块：读取开火和换弹输入，指挥当前武器开火、换弹。
    /// 全自动武器按住连发，半自动武器每次按下发射一发；
    /// 当玩家通过 PlayerEquipment 切换不同武器时，自动跟随当前手持的武器并通知 HUD 刷新弹药。
    /// 挂载对象：玩家根物体（Player），与 PlayerInputReader 在同一物体上。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerInputReader))]
    public class PlayerWeaponController : MonoBehaviour
    {
        [Tooltip("当前使用的武器。留空时自动在子物体中查找第一个 Weapon。")]
        [SerializeField] private Weapon currentWeapon;

        private PlayerInputReader _input;
        private PlayerEquipment _equipment;

        /// <summary>当前使用的武器。</summary>
        public Weapon CurrentWeapon => currentWeapon;

        /// <summary>当前武器发生变化时触发（例如切换到商城购买的新武器）。</summary>
        public event Action<Weapon> CurrentWeaponChanged;

        private void Awake()
        {
            _input = GetComponent<PlayerInputReader>();
            _equipment = GetComponent<PlayerEquipment>();

            if (currentWeapon == null)
            {
                currentWeapon = GetComponentInChildren<Weapon>(true);
            }

            if (currentWeapon == null)
            {
                Debug.LogWarning($"{nameof(PlayerWeaponController)}: 没有找到武器，玩家暂时无法开火。", this);
            }
        }

        private void OnEnable()
        {
            if (_equipment != null)
            {
                _equipment.Equipped += OnEquipmentChanged;
                if (_equipment.Current != null)
                {
                    OnEquipmentChanged(_equipment.Current);
                }
            }
        }

        private void OnDisable()
        {
            if (_equipment != null)
            {
                _equipment.Equipped -= OnEquipmentChanged;
            }
        }

        private void Update()
        {
            if (currentWeapon == null)
            {
                return;
            }

            if (_input.ReloadPressed && currentWeapon.Ammo != null)
            {
                currentWeapon.Ammo.TryStartReload();
            }

            bool wantsToFire = currentWeapon.IsAutomatic ? _input.AttackHeld : _input.AttackPressed;
            if (wantsToFire)
            {
                currentWeapon.TryFire();
            }
        }

        private void OnEquipmentChanged(GameObject equipped)
        {
            if (equipped == null)
            {
                return;
            }

            Weapon weapon = equipped.GetComponent<Weapon>();
            if (weapon != null && weapon != currentWeapon)
            {
                currentWeapon = weapon;
                CurrentWeaponChanged?.Invoke(currentWeapon);
            }
        }
    }
}
