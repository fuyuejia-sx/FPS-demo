using System;
using UnityEngine;
using UnityEngine.Events;

namespace FPS
{
    /// <summary>
    /// 生命值组件：管理当前生命值，处理受伤、治疗和死亡，并通过事件通知其他模块。
    /// 本组件只负责数值和事件，不关心死亡后具体发生什么（由监听者决定）。
    /// 挂载对象：任何可以受伤的物体的根物体，例如 Player、敌人、靶子。
    /// </summary>
    [DisallowMultipleComponent]
    public class Health : MonoBehaviour, IDamageable
    {
        [Header("生命值")]
        [Tooltip("最大生命值，开始时生命值等于该值。")]
        [SerializeField, Min(1f)] private float maxHealth = 100f;

        [Tooltip("勾选后不会受到伤害。")]
        [SerializeField] private bool invulnerable;

        [Header("死亡处理")]
        [Tooltip("死亡后是否销毁该物体。靶子、敌人可勾选；玩家不要勾选。")]
        [SerializeField] private bool destroyOnDeath;

        [Tooltip("死亡后延迟多少秒销毁，留给死亡特效或动画播放。")]
        [SerializeField, Min(0f)] private float destroyDelay;

        [Header("事件（可在 Inspector 中绑定）")]
        [Tooltip("受到伤害时触发。")]
        [SerializeField] private UnityEvent onDamaged;

        [Tooltip("死亡时触发，只触发一次。")]
        [SerializeField] private UnityEvent onDied;

        [Header("运行时状态（只用于观察，不要手动修改）")]
        [SerializeField] private float currentHealth;

        /// <summary>生命值变化时触发，参数为（当前生命值，最大生命值）。HUD 血条监听这个事件。</summary>
        public event Action<float, float> HealthChanged;

        /// <summary>受到伤害时触发，参数为本次伤害信息。</summary>
        public event Action<DamageInfo> Damaged;

        /// <summary>死亡时触发，参数为造成致命一击的伤害信息。</summary>
        public event Action<DamageInfo> Died;

        public float CurrentHealth => currentHealth;
        public float MaxHealth => maxHealth;
        public bool IsAlive => currentHealth > 0f;

        private IDamageAbsorber[] _absorbers;

        private void Awake()
        {
            currentHealth = maxHealth;
            _absorbers = GetComponents<IDamageAbsorber>();
        }

        public void TakeDamage(DamageInfo damage)
        {
            if (!IsAlive || invulnerable || damage.Amount <= 0f)
            {
                return;
            }

            // 先让盾牌等吸收者拦截，剩下的伤害才扣生命值
            if (_absorbers != null)
            {
                foreach (IDamageAbsorber absorber in _absorbers)
                {
                    float remaining = absorber.AbsorbDamage(damage);
                    if (remaining <= 0f)
                    {
                        return;
                    }

                    if (remaining < damage.Amount)
                    {
                        damage = damage.WithAmount(remaining);
                    }
                }
            }

            currentHealth = Mathf.Max(0f, currentHealth - damage.Amount);

            HealthChanged?.Invoke(currentHealth, maxHealth);
            Damaged?.Invoke(damage);
            onDamaged?.Invoke();

            if (!IsAlive)
            {
                Die(damage);
            }
        }

        /// <summary>回复生命值，不会超过最大值。已死亡时无效。</summary>
        public void Heal(float amount)
        {
            if (!IsAlive || amount <= 0f)
            {
                return;
            }

            currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
            HealthChanged?.Invoke(currentHealth, maxHealth);
        }

        /// <summary>恢复满血并复活，用于重生或重新开始。</summary>
        public void ResetHealth()
        {
            currentHealth = maxHealth;
            HealthChanged?.Invoke(currentHealth, maxHealth);
        }

        private void Die(DamageInfo damage)
        {
            Died?.Invoke(damage);
            onDied?.Invoke();

            if (destroyOnDeath)
            {
                Destroy(gameObject, destroyDelay);
            }
        }

        // ---- 以下为测试入口：运行时点组件右上角的三个点即可调用 ----

        [ContextMenu("测试：受到 25 点伤害")]
        private void DebugTakeDamage()
        {
            TakeDamage(new DamageInfo(25f));
            Debug.Log($"{name} 受到 25 点伤害，当前生命值 {currentHealth}/{maxHealth}", this);
        }

        [ContextMenu("测试：回复 25 点生命")]
        private void DebugHeal()
        {
            Heal(25f);
            Debug.Log($"{name} 回复生命，当前生命值 {currentHealth}/{maxHealth}", this);
        }

        [ContextMenu("测试：直接击杀")]
        private void DebugKill()
        {
            TakeDamage(new DamageInfo(currentHealth));
            Debug.Log($"{name} 被击杀，IsAlive = {IsAlive}", this);
        }
    }
}
