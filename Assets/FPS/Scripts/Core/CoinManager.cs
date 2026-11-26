using System;
using UnityEngine;

namespace FPS
{
    /// <summary>
    /// 金币奖励模块：管理玩家当前金币总数，监听敌人击杀事件并发放对应金币奖励。
    /// 无骨骼敌人与带骨骼敌人可分别配置奖励数值；界面展示由 HUDController 监听本模块事件完成。
    /// 挂载对象：场景中常驻的管理物体（例如 GameManager），整个场景只需要一个。
    /// </summary>
    [DisallowMultipleComponent]
    public class CoinManager : MonoBehaviour
    {
        [Header("初始配置")]
        [Tooltip("开局时的初始金币数量。")]
        [SerializeField, Min(0)] private int initialCoins = 100;

        [Header("击杀奖励")]
        [Tooltip("击杀无骨骼敌人奖励的金币数。")]
        [SerializeField, Min(0)] private int unriggedEnemyReward = 10;

        [Tooltip("击杀带骨骼敌人奖励的金币数。")]
        [SerializeField, Min(0)] private int riggedEnemyReward = 20;

        [Header("运行时状态（只用于观察，不要手动修改）")]
        [SerializeField] private int currentCoins;

        /// <summary>当前金币总数。</summary>
        public int CurrentCoins => currentCoins;

        /// <summary>金币数量变化时触发，参数为（当前金币总数，本次变化量）。</summary>
        public event Action<int, int> CoinsChanged;

        private void Awake()
        {
            currentCoins = initialCoins;
        }

        private void OnEnable()
        {
            EnemyAI.EnemyKilled += OnEnemyKilled;
        }

        private void OnDisable()
        {
            EnemyAI.EnemyKilled -= OnEnemyKilled;
        }

        /// <summary>增加金币并通知监听者。</summary>
        public void AddCoins(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            currentCoins += amount;
            CoinsChanged?.Invoke(currentCoins, amount);
        }

        /// <summary>扣除金币；余额不足时返回 false。</summary>
        public bool SpendCoins(int amount)
        {
            if (amount <= 0 || currentCoins < amount)
            {
                return false;
            }

            currentCoins -= amount;
            CoinsChanged?.Invoke(currentCoins, -amount);
            return true;
        }

        private void OnEnemyKilled(EnemyAI enemy)
        {
            if (enemy == null)
            {
                return;
            }

            int reward = enemy.IsRigged ? riggedEnemyReward : unriggedEnemyReward;
            AddCoins(reward);
        }

        // ---- 以下为测试入口：运行时点组件右上角的三个点即可调用 ----

        [ContextMenu("测试：模拟击杀无骨骼敌人 (+10)")]
        private void DebugRewardUnrigged()
        {
            AddCoins(unriggedEnemyReward);
            Debug.Log($"[CoinManager] +{unriggedEnemyReward} 金币，当前总数 {currentCoins}", this);
        }

        [ContextMenu("测试：模拟击杀带骨骼敌人 (+20)")]
        private void DebugRewardRigged()
        {
            AddCoins(riggedEnemyReward);
            Debug.Log($"[CoinManager] +{riggedEnemyReward} 金币，当前总数 {currentCoins}", this);
        }
    }
}
