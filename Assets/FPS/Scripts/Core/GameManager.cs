using System;
using UnityEngine;
using UnityEngine.InputSystem;
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
    /// 游戏流程模块：判定失败（玩家死亡）和胜利（敌人全灭），管理 ESC 暂停菜单、
    /// 鼠标光标解锁、重新开始和退出游戏（Application.Quit）。
    /// 结束后按换弹键（键盘 R / 手柄右肩键）或点击界面按钮可重新加载当前场景。
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

        [Tooltip("游戏结束或暂停时要禁用的组件，例如 PlayerMovement、PlayerLook、PlayerWeaponController、PlayerEquipment。")]
        [SerializeField] private Behaviour[] disableOnGameEnd;

        [Header("规则")]
        [Tooltip("敌人全部死亡时是否判定胜利。场景开始时没有敌人则不会触发。")]
        [SerializeField] private bool winWhenAllEnemiesDead = true;

        [Tooltip("游戏结束后至少等待多少秒才允许重新开始，避免误触。")]
        [SerializeField, Min(0f)] private float restartDelay = 1f;

        private static int _escapeConsumedFrame = -1;

        private float _restartAllowedTime;
        private bool[] _pauseSavedEnabledStates;

        /// <summary>当前游戏状态。</summary>
        public GameState State { get; private set; } = GameState.Playing;

        /// <summary>当前是否处于 ESC 暂停状态。</summary>
        public bool IsPaused { get; private set; }

        /// <summary>游戏状态变化时触发。HUD 监听这个事件显示结算界面。</summary>
        public event Action<GameState> StateChanged;

        /// <summary>暂停状态变化时触发（true = 进入暂停，false = 恢复游戏）。</summary>
        public event Action<bool> PauseChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _escapeConsumedFrame = -1;
        }

        /// <summary>
        /// 供商城、背包等二级界面在响应 Esc 关闭时调用，避免同一帧顺带触发暂停菜单。
        /// </summary>
        public static void ConsumeEscapeThisFrame()
        {
            _escapeConsumedFrame = Time.frameCount;
        }

        private void Awake()
        {
            Time.timeScale = 1f;

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

        private void OnDestroy()
        {
            if (IsPaused)
            {
                Time.timeScale = 1f;
                IsPaused = false;
            }
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

        private void LateUpdate()
        {
            if (State != GameState.Playing)
            {
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame)
            {
                return;
            }

            if (_escapeConsumedFrame == Time.frameCount)
            {
                return;
            }

            TogglePause();
        }

        /// <summary>切换 ESC 暂停状态。</summary>
        public void TogglePause()
        {
            SetPaused(!IsPaused);
        }

        /// <summary>从暂停菜单恢复游戏。</summary>
        public void Resume()
        {
            SetPaused(false);
        }

        /// <summary>进入或退出暂停状态：同步控制 Time.timeScale、玩家输入禁用以及鼠标锁定/解锁。</summary>
        public void SetPaused(bool paused)
        {
            if (State != GameState.Playing || IsPaused == paused)
            {
                return;
            }

            IsPaused = paused;
            Time.timeScale = paused ? 0f : 1f;

            if (paused)
            {
                if (disableOnGameEnd != null)
                {
                    _pauseSavedEnabledStates = new bool[disableOnGameEnd.Length];
                    for (int i = 0; i < disableOnGameEnd.Length; i++)
                    {
                        Behaviour behaviour = disableOnGameEnd[i];
                        if (behaviour != null)
                        {
                            _pauseSavedEnabledStates[i] = behaviour.enabled;
                            behaviour.enabled = false;
                        }
                    }
                }

                UnlockCursor();
            }
            else
            {
                if (disableOnGameEnd != null && _pauseSavedEnabledStates != null)
                {
                    int count = Mathf.Min(disableOnGameEnd.Length, _pauseSavedEnabledStates.Length);
                    for (int i = 0; i < count; i++)
                    {
                        if (disableOnGameEnd[i] != null)
                        {
                            disableOnGameEnd[i].enabled = _pauseSavedEnabledStates[i];
                        }
                    }
                }
            }

            PauseChanged?.Invoke(IsPaused);
        }

        /// <summary>重新加载当前场景。</summary>
        public void Restart()
        {
            Time.timeScale = 1f;
            IsPaused = false;

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

        /// <summary>退出游戏。打包运行后调用 Application.Quit()；在编辑器里停止播放模式便于直接测试。</summary>
        public void QuitGame()
        {
            Time.timeScale = 1f;
            IsPaused = false;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
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

            if (IsPaused)
            {
                IsPaused = false;
                Time.timeScale = 1f;
                PauseChanged?.Invoke(false);
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

            // 结算界面强制解锁并显示鼠标，确保玩家可以直接点击重新开始或退出按钮
            UnlockCursor();

            StateChanged?.Invoke(State);
        }

        private static void UnlockCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
