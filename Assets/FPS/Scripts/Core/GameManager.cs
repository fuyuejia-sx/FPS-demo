using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FPS
{
    /// <summary>游戏状态。</summary>
    public enum GameState
    {
        Playing,
        GameOver,
        Victory
    }

    /// <summary>
    /// 游戏流程模块：判定失败（玩家死亡）和胜利（敌人全灭），结束后禁用玩家操作，并处理重新开始。
    /// 结束后按换弹键（键盘 R / 手柄右肩键）重新加载当前场景。
    /// 挂载对象：场景中一个独立的空物体（例如 GameManager），整个场景只需要一个。
    /// </summary>
    [DisallowMultipleComponent]
    public class GameManager : MonoBehaviour
    {
        [Header("引用")]
        [Tooltip("玩家的 Health。留空时自动从 Tag 为 Player 的物体上查找。")]
        [SerializeField] private Health playerHealth;

        [Tooltip("玩家的输入组件，用于读取“重新开始”按键。留空时自动查找。")]
        [SerializeField] private PlayerInputReader playerInput;

        [Tooltip("游戏结束时要禁用的组件，例如 PlayerMovement、PlayerLook、PlayerWeaponController。")]
        [SerializeField] private Behaviour[] disableOnGameEnd;

        [Header("规则")]
        [Tooltip("敌人全部死亡时是否判定胜利。场景开始时没有敌人则不会触发。")]
        [SerializeField] private bool winWhenAllEnemiesDead = true;

        [Tooltip("游戏结束后至少等待多少秒才允许重新开始，避免误触。")]
        [SerializeField, Min(0f)] private float restartDelay = 1f;

        private float _restartAllowedTime;

        /// <summary>当前游戏状态。</summary>
        public GameState State { get; private set; } = GameState.Playing;

        /// <summary>游戏状态变化时触发。HUD 监听这个事件显示结算界面。</summary>
        public event Action<GameState> StateChanged;

        private void Awake()
        {
            if (playerHealth == null || playerInput == null)
            {
                GameObject player = GameObject.FindGameObjectWithTag("Player");
                if (player != null)
                {
                    if (playerHealth == null) playerHealth = player.GetComponent<Health>();
                    if (playerInput == null) playerInput = player.GetComponent<PlayerInputReader>();
                }
            }

            if (playerHealth == null)
            {
                Debug.LogWarning($"{nameof(GameManager)}: 没有找到玩家的 Health，无法判定失败。", this);
            }
        }

        private void OnEnable()
        {
            if (playerHealth != null)
            {
                playerHealth.Died += OnPlayerDied;
            }

            EnemyAI.EnemyDied += OnEnemyDied;
        }

        private void OnDisable()
        {
            if (playerHealth != null)
            {
                playerHealth.Died -= OnPlayerDied;
            }

            EnemyAI.EnemyDied -= OnEnemyDied;
        }

        private void Update()
        {
            if (State == GameState.Playing || Time.unscaledTime < _restartAllowedTime)
            {
                return;
            }

            if (playerInput != null && playerInput.ReloadPressed)
            {
                Restart();
            }
        }

        /// <summary>重新加载当前场景。</summary>
        public void Restart()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.buildIndex < 0)
            {
                Debug.LogError(
                    $"{nameof(GameManager)}: 场景 {scene.name} 不在构建列表中，无法重新加载。" +
                    "请在 File > Build Profiles 的场景列表中添加当前场景。", this);
                return;
            }

            SceneManager.LoadScene(scene.buildIndex);
        }

        private void OnPlayerDied(DamageInfo damage)
        {
            EndGame(GameState.GameOver);
        }

        private void OnEnemyDied(int remaining)
        {
            if (winWhenAllEnemiesDead && remaining <= 0)
            {
                EndGame(GameState.Victory);
            }
        }

        private void EndGame(GameState result)
        {
            if (State != GameState.Playing)
            {
                return;
            }

            State = result;
            _restartAllowedTime = Time.unscaledTime + restartDelay;

            if (disableOnGameEnd != null)
            {
                foreach (Behaviour behaviour in disableOnGameEnd)
                {
                    if (behaviour != null)
                    {
                        behaviour.enabled = false;
                    }
                }
            }

            StateChanged?.Invoke(State);
        }
    }
}
