using System;
using UnityEngine;

namespace FPS
{
    /// <summary>
    /// 武器模块：射线检测（hitscan）武器。负责射速控制、命中检测和造成伤害。
    /// 本组件不读取输入，由外部（例如 PlayerWeaponController）调用 TryFire。
    /// 挂载对象：武器物体，一般是 PlayerCamera 的子物体（例如 Weapon）。
    /// </summary>
    public class Weapon : MonoBehaviour
    {
        [Header("引用")]
        [Tooltip("射线的起点和方向来源，一般是 PlayerCamera。留空时自动使用主相机。")]
        [SerializeField] private Transform aimOrigin;

        [Header("射击参数")]
        [Tooltip("每发子弹的伤害。")]
        [SerializeField, Min(0f)] private float damage = 25f;

        [Tooltip("射速：每秒可以发射多少发。")]
        [SerializeField, Min(0.1f)] private float fireRate = 8f;

        [Tooltip("最大射程（米）。")]
        [SerializeField, Min(0f)] private float range = 100f;

        [Tooltip("勾选为全自动（按住连发），不勾选为半自动（每次点击一发）。")]
        [SerializeField] private bool automatic = true;

        [Tooltip("子弹可以命中的层。默认命中所有层。")]
        [SerializeField] private LayerMask hitMask = ~0;

        [Header("调试")]
        [Tooltip("在 Console 输出命中信息，并在 Scene 窗口画出弹道线。")]
        [SerializeField] private bool debugShots = true;

        private float _nextFireTime;
        private GameObject _owner;
        private WeaponAmmo _ammo;

        /// <summary>是否为全自动武器。</summary>
        public bool IsAutomatic => automatic;

        /// <summary>每发子弹伤害。</summary>
        public float Damage => damage;

        /// <summary>射速（发/秒）。</summary>
        public float FireRate => fireRate;

        /// <summary>成功开火时触发。之后的枪口火焰、音效、后坐力监听这个事件。</summary>
        public event Action Fired;

        /// <summary>子弹命中任意物体时触发，参数为命中信息。之后的命中特效监听这个事件。</summary>
        public event Action<RaycastHit> HitSomething;

        /// <summary>本武器的弹药组件。没有挂 WeaponAmmo 时为 null，表示弹药无限。</summary>
        public WeaponAmmo Ammo => _ammo;

        private void Awake()
        {
            _owner = transform.root.gameObject;
            _ammo = GetComponent<WeaponAmmo>();

            if (aimOrigin == null && Camera.main != null)
            {
                aimOrigin = Camera.main.transform;
            }

            if (aimOrigin == null)
            {
                Debug.LogError($"{nameof(Weapon)}: 未设置 Aim Origin，且场景中没有 Tag 为 MainCamera 的相机。", this);
                enabled = false;
            }
        }

        /// <summary>
        /// 尝试开火。射速冷却未结束、没有子弹或正在换弹时返回 false。
        /// </summary>
        public bool TryFire()
        {
            if (!isActiveAndEnabled || Time.time < _nextFireTime)
            {
                return false;
            }

            // 有弹药组件时，没子弹或正在换弹就不能开火
            if (_ammo != null && !_ammo.TryConsume())
            {
                return false;
            }

            _nextFireTime = Time.time + 1f / fireRate;
            Fire();
            return true;
        }

        private void Fire()
        {
            Vector3 origin = aimOrigin.position;
            Vector3 direction = aimOrigin.forward;

            Fired?.Invoke();

            bool hasHit = Physics.Raycast(
                origin, direction, out RaycastHit hit, range, hitMask, QueryTriggerInteraction.Ignore);

            if (debugShots)
            {
                Vector3 end = hasHit ? hit.point : origin + direction * range;
                Debug.DrawLine(origin, end, hasHit ? Color.red : Color.yellow, 1f);
            }

            if (!hasHit)
            {
                return;
            }

            // 不伤害自己（射线通常不会命中起点所在的碰撞体，这里再保险一次）
            if (hit.collider.transform.root == transform.root)
            {
                return;
            }

            HitSomething?.Invoke(hit);

            IDamageable damageable = hit.collider.GetComponentInParent<IDamageable>();
            if (damageable != null)
            {
                damageable.TakeDamage(new DamageInfo(damage, hit.point, direction, _owner));
            }

            if (debugShots)
            {
                string result = damageable != null ? $"造成 {damage} 点伤害" : "不可受伤";
                Debug.Log($"{name} 命中 {hit.collider.name}（距离 {hit.distance:F1} 米），{result}", hit.collider);
            }
        }
    }
}
