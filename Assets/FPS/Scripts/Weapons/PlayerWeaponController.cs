using UnityEngine;

namespace FPS
{
    /// <summary>
    /// 玩家武器控制模块：读取开火和换弹输入，指挥当前武器开火、换弹。
    /// 全自动武器按住连发，半自动武器每次按下发射一发。
    /// 挂载对象：玩家根物体（Player），与 PlayerInputReader 在同一物体上。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerInputReader))]
    public class PlayerWeaponController : MonoBehaviour
    {
        [Tooltip("当前使用的武器。留空时自动在子物体中查找第一个 Weapon。")]
        [SerializeField] private Weapon currentWeapon;

        private PlayerInputReader _input;

        /// <summary>当前使用的武器。</summary>
        public Weapon CurrentWeapon => currentWeapon;

        private void Awake()
        {
            _input = GetComponent<PlayerInputReader>();

            if (currentWeapon == null)
            {
                currentWeapon = GetComponentInChildren<Weapon>();
            }

            if (currentWeapon == null)
            {
                Debug.LogWarning($"{nameof(PlayerWeaponController)}: 没有找到武器，玩家暂时无法开火。", this);
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
    }
}
