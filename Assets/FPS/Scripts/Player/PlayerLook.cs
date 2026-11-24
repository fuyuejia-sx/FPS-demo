using UnityEngine;

namespace FPS
{
    /// <summary>
    /// 第一人称视角模块：水平转动旋转玩家本体，上下俯仰只旋转相机，并负责锁定鼠标光标。
    /// 挂载对象：玩家根物体（Player），与 PlayerInputReader 在同一物体上。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerInputReader))]
    [DefaultExecutionOrder(-10)] // 先于移动模块执行，保证本帧移动使用最新朝向
    public class PlayerLook : MonoBehaviour
    {
        [Header("引用")]
        [Tooltip("负责上下俯仰的物体，一般是 Player 的子物体 PlayerCamera。")]
        [SerializeField] private Transform cameraPivot;

        [Header("灵敏度")]
        [Tooltip("鼠标灵敏度：鼠标每移动 1 像素转动的角度（度）。")]
        [SerializeField, Min(0f)] private float mouseSensitivity = 0.1f;

        [Tooltip("手柄灵敏度：摇杆推满时每秒转动的角度（度/秒）。")]
        [SerializeField, Min(0f)] private float gamepadSensitivity = 180f;

        [Tooltip("是否反转上下视角。")]
        [SerializeField] private bool invertY;

        [Header("俯仰限制")]
        [Tooltip("向上看的最大角度（度）。")]
        [SerializeField, Range(0f, 89f)] private float maxLookUp = 85f;

        [Tooltip("向下看的最大角度（度）。")]
        [SerializeField, Range(0f, 89f)] private float maxLookDown = 85f;

        [Header("光标")]
        [Tooltip("启用时锁定并隐藏鼠标光标。光标未锁定时不响应视角输入。")]
        [SerializeField] private bool lockCursor = true;

        private PlayerInputReader _input;

        // 俯仰角：正值向下看，负值向上看（与 Unity 绕 X 轴旋转的方向一致）
        private float _pitch;

        // 游戏逻辑是否希望光标处于锁定状态（与编辑器实际状态可能不一致）
        private bool _cursorLockRequested;

        private void Awake()
        {
            _input = GetComponent<PlayerInputReader>();

            if (cameraPivot == null)
            {
                Debug.LogError($"{nameof(PlayerLook)}: 未设置 Camera Pivot，请在 Inspector 中拖入 PlayerCamera。", this);
                enabled = false;
                return;
            }

            // 以相机当前的俯仰角作为初始值，避免开始时视角跳变
            _pitch = Mathf.DeltaAngle(0f, cameraPivot.localEulerAngles.x);
        }

        private void OnEnable()
        {
            if (lockCursor)
            {
                SetCursorLocked(true);
            }
        }

        private void OnDisable()
        {
            if (lockCursor)
            {
                SetCursorLocked(false);
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            // 窗口重新获得焦点时恢复锁定（切到别的程序再切回来的情况）
            if (hasFocus && _cursorLockRequested)
            {
                ApplyCursorLock(true);
            }
        }

        private void Update()
        {
            // 光标被释放（例如在编辑器里按了 Esc）时不转动视角
            if (lockCursor && Cursor.lockState != CursorLockMode.Locked)
            {
                // 编辑器在启动 Play 或按 Esc 后会丢弃锁定状态，需要在点击画面时重新申请
                if (_cursorLockRequested && _input.AttackPressed)
                {
                    ApplyCursorLock(true);
                }

                return;
            }

            Vector2 look = _input.Look;
            if (look == Vector2.zero)
            {
                return;
            }

            Vector2 delta = _input.LookIsPointerDelta
                ? look * mouseSensitivity
                : look * (gamepadSensitivity * Time.deltaTime);

            // 水平：旋转玩家本体
            transform.Rotate(0f, delta.x, 0f, Space.Self);

            // 垂直：只旋转相机，并限制角度
            float pitchDelta = invertY ? delta.y : -delta.y;
            _pitch = Mathf.Clamp(_pitch + pitchDelta, -maxLookUp, maxLookDown);
            cameraPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        /// <summary>锁定或释放鼠标光标。之后的菜单、暂停等模块可以调用。</summary>
        public void SetCursorLocked(bool locked)
        {
            _cursorLockRequested = locked;
            ApplyCursorLock(locked);
        }

        private static void ApplyCursorLock(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
