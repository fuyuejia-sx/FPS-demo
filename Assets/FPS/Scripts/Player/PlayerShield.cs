using System;
using UnityEngine;

namespace FPS
{
    /// <summary>
    /// 盾牌模块：持盾时在生命值外面套一层盾值，受到的伤害优先扣盾值，扣完后剩余部分才扣生命值。
    /// 收起盾牌（切换到其他装备）时不提供保护，但盾值会保留，不会因为来回切换而重置。
    /// 盾值耗尽时盾牌立刻消失并自动换回其他装备；盾值为 0 期间无法切换到盾。
    /// 挂载对象：玩家根物体（Player），与 Health、PlayerEquipment 在同一物体上。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Health))]
    [RequireComponent(typeof(PlayerEquipment))]
    public class PlayerShield : MonoBehaviour, IDamageAbsorber
    {
        [Header("引用")]
        [Tooltip("代表盾牌的装备物体。当 PlayerEquipment 手持的是它时，盾牌生效。")]
        [SerializeField] private GameObject shieldObject;

        [Header("盾值")]
        [Tooltip("盾值上限。")]
        [SerializeField, Min(0f)] private float maxShield = 30f;

        [Header("恢复（可选）")]
        [Tooltip("盾值每秒恢复量。0 表示不恢复，打空后盾牌永久失效。大于 0 时，盾值恢复后可以重新持盾。")]
        [SerializeField, Min(0f)] private float regenPerSecond;

        [Tooltip("盾牌最后一次吸收伤害后，要等多少秒才开始恢复。")]
        [SerializeField, Min(0f)] private float regenDelay = 3f;

        private PlayerEquipment _equipment;
        private float _current;
        private float _lastAbsorbTime = float.NegativeInfinity;

        /// <summary>当前盾值。</summary>
        public float CurrentShield => _current;

        /// <summary>盾值上限。</summary>
        public float MaxShield => maxShield;

        /// <summary>是否正在持盾。只有持盾时才吸收伤害。</summary>
        public bool IsRaised { get; private set; }

        /// <summary>盾值变化时触发，参数为（当前盾值，盾值上限）。</summary>
        public event Action<float, float> ShieldChanged;

        /// <summary>举起或收起盾牌时触发，参数为是否正在持盾。</summary>
        public event Action<bool> RaisedChanged;

        private void Awake()
        {
            _equipment = GetComponent<PlayerEquipment>();
            _current = maxShield;
        }

        private void OnEnable()
        {
            _equipment.Equipped += OnEquipped;
            UpdateAvailability();
            OnEquipped(_equipment.Current);
        }

        private void OnDisable()
        {
            _equipment.Equipped -= OnEquipped;
            SetRaised(false);
        }

        private void Update()
        {
            if (regenPerSecond <= 0f || _current >= maxShield || Time.time < _lastAbsorbTime + regenDelay)
            {
                return;
            }

            _current = Mathf.Min(maxShield, _current + regenPerSecond * Time.deltaTime);
            ShieldChanged?.Invoke(_current, maxShield);
            UpdateAvailability();
        }

        public float AbsorbDamage(DamageInfo damage)
        {
            if (!IsRaised || _current <= 0f)
            {
                return damage.Amount;
            }

            float absorbed = Mathf.Min(_current, damage.Amount);
            _current -= absorbed;
            _lastAbsorbTime = Time.time;
            ShieldChanged?.Invoke(_current, maxShield);

            // 盾值耗尽：盾牌立刻消失并自动换回其他装备，之后无法再切换到盾
            UpdateAvailability();

            return damage.Amount - absorbed;
        }

        /// <summary>把盾值补满，用于拾取道具或重生。</summary>
        public void Refill()
        {
            _current = maxShield;
            ShieldChanged?.Invoke(_current, maxShield);
            UpdateAvailability();
        }

        /// <summary>只有盾值大于 0 时，盾牌才是可以拿在手上的装备。</summary>
        private void UpdateAvailability()
        {
            if (shieldObject != null)
            {
                _equipment.SetSlotAvailable(shieldObject, _current > 0f);
            }
        }

        private void OnEquipped(GameObject equipped)
        {
            SetRaised(shieldObject != null && equipped == shieldObject);
        }

        private void SetRaised(bool raised)
        {
            if (IsRaised == raised)
            {
                return;
            }

            IsRaised = raised;
            RaisedChanged?.Invoke(raised);
        }
    }
}
