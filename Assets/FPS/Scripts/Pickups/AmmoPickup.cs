using System;
using UnityEngine;

namespace FPS
{
    /// <summary>
    /// 弹药补给拾取模块：玩家靠近或走过弹药箱时为当前武器补充备弹。
    /// 备弹数量严格受 WeaponAmmo.MaxReserve 限制，已满时不会再往上增加、也不会白白消耗补给箱。
    /// 支持一次性拾取（respawnTime = 0）或定时刷新（respawnTime > 0）。
    /// 挂载对象：带触发器碰撞体（isTrigger = true）的弹药箱物体。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public class AmmoPickup : MonoBehaviour
    {
        [Header("补给配置")]
        [Tooltip("每次拾取最多补充多少发备弹（不会超过当前武器的最大备弹上限）。")]
        [SerializeField, Min(1)] private int ammoAmount = 30;

        [Tooltip("拾取后重新刷新的冷却时间（秒）。0 表示一次性补给箱，拾取后直接销毁。")]
        [SerializeField, Min(0f)] private float respawnTime = 15f;

        [Header("外观动效")]
        [Tooltip("负责悬浮与旋转的视觉子物体。留空时使用自身第一个子物体。")]
        [SerializeField] private Transform visualRoot;

        [Tooltip("原地旋转速度（度/秒）。")]
        [SerializeField] private float rotateSpeed = 45f;

        [Tooltip("上下悬浮幅度（米）。")]
        [SerializeField, Min(0f)] private float bobAmplitude = 0.08f;

        [Tooltip("上下悬浮频率。")]
        [SerializeField, Min(0f)] private float bobFrequency = 2.2f;

        [Header("音效（可选）")]
        [Tooltip("成功拾取弹药时播放的音效。可留空。")]
        [SerializeField] private AudioClip pickupClip;

        /// <summary>
        /// 成功拾取弹药时触发，参数为（实际增加的弹药数，当前备弹，最大备弹上限）。
        /// HUDController 监听该事件显示拾取提示。
        /// </summary>
        public static event Action<int, int, int> AmmoPickedUp;

        /// <summary>玩家接触弹药箱但弹药已满时触发，参数为（当前备弹，最大备弹上限）。</summary>
        public static event Action<int, int> AmmoAlreadyFull;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            AmmoPickedUp = null;
            AmmoAlreadyFull = null;
        }

        private const float FullNoticeCooldown = 1.5f;

        private Collider _trigger;
        private Renderer[] _renderers;
        private Light[] _lights;

        private Vector3 _visualRestPosition;
        private bool _available = true;
        private float _respawnAt;
        private float _nextFullNoticeTime;

        private void Awake()
        {
            _trigger = GetComponent<Collider>();
            _trigger.isTrigger = true;

            if (visualRoot == null && transform.childCount > 0)
            {
                visualRoot = transform.GetChild(0);
            }

            if (visualRoot != null)
            {
                _visualRestPosition = visualRoot.localPosition;
            }

            _renderers = GetComponentsInChildren<Renderer>(true);
            _lights = GetComponentsInChildren<Light>(true);
        }

        private void Update()
        {
            if (!_available)
            {
                if (respawnTime > 0f && Time.time >= _respawnAt)
                {
                    SetAvailable(true);
                }

                return;
            }

            if (visualRoot != null)
            {
                visualRoot.Rotate(0f, rotateSpeed * Time.deltaTime, 0f, Space.Self);
                float offsetY = Mathf.Sin(Time.time * bobFrequency) * bobAmplitude;
                visualRoot.localPosition = _visualRestPosition + Vector3.up * offsetY;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            TrySupply(other, isInitialEnter: true);
        }

        private void OnTriggerStay(Collider other)
        {
            TrySupply(other, isInitialEnter: false);
        }

        private void TrySupply(Collider other, bool isInitialEnter)
        {
            if (!_available || other == null)
            {
                return;
            }

            PlayerWeaponController weaponController = other.GetComponentInParent<PlayerWeaponController>();
            if (weaponController == null)
            {
                return;
            }

            Weapon weapon = weaponController.CurrentWeapon;
            WeaponAmmo ammo = weapon != null ? weapon.Ammo : null;
            if (ammo == null)
            {
                return;
            }

            // 总弹药已达上限（弹匣和备弹都满）：不继续增加，也不消耗补给箱
            if (ammo.IsAmmoFull)
            {
                if (isInitialEnter || Time.time >= _nextFullNoticeTime)
                {
                    _nextFullNoticeTime = Time.time + FullNoticeCooldown;
                    AmmoAlreadyFull?.Invoke(ammo.TotalAmmo, ammo.MaxTotalAmmo);
                }

                return;
            }

            int added = ammo.AddAmmo(ammoAmount);
            if (added <= 0)
            {
                return;
            }

            if (pickupClip != null)
            {
                AudioSource.PlayClipAtPoint(pickupClip, transform.position);
            }

            AmmoPickedUp?.Invoke(added, ammo.TotalAmmo, ammo.MaxTotalAmmo);

            if (respawnTime > 0f)
            {
                SetAvailable(false);
                _respawnAt = Time.time + respawnTime;
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void SetAvailable(bool available)
        {
            _available = available;

            if (_trigger != null)
            {
                _trigger.enabled = available;
            }

            if (_renderers != null)
            {
                for (int i = 0; i < _renderers.Length; i++)
                {
                    if (_renderers[i] != null)
                    {
                        _renderers[i].enabled = available;
                    }
                }
            }

            if (_lights != null)
            {
                for (int i = 0; i < _lights.Length; i++)
                {
                    if (_lights[i] != null)
                    {
                        _lights[i].enabled = available;
                    }
                }
            }
        }
    }
}
