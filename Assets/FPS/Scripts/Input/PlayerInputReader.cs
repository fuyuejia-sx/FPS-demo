using UnityEngine;
using UnityEngine.InputSystem;

namespace FPS
{
    /// <summary>
    /// 输入模块：封装自动生成的 InputSystem_Actions 类，向其他模块提供玩家输入。
    /// 其他脚本（移动、视角、武器等）只读取本组件的属性，不直接接触 Input System。
    /// 挂载对象：玩家根物体（Player）。
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerInputReader : MonoBehaviour
    {
        [Header("调试")]
        [Tooltip("运行时在屏幕左上角显示当前输入值，用于验证输入模块是否工作。正式游戏中关闭。")]
        [SerializeField] private bool showDebugOverlay;

        private InputSystem_Actions _actions;
        private InputSystem_Actions.PlayerActions _player;

        // ---- 持续型输入 ----

        /// <summary>移动输入。x 为左右，y 为前后，范围 -1 到 1。</summary>
        public Vector2 Move => _player.Move.ReadValue<Vector2>();

        /// <summary>视角输入。鼠标为本帧位移（像素），手柄为摇杆值（-1 到 1）。</summary>
        public Vector2 Look => _player.Look.ReadValue<Vector2>();

        /// <summary>
        /// 当前视角输入是否来自鼠标等指针设备。
        /// 为 true 时 Look 是本帧位移，不应再乘 deltaTime；为 false 时（手柄摇杆）需要乘 deltaTime。
        /// </summary>
        public bool LookIsPointerDelta => _player.Look.activeControl?.device is Pointer;

        /// <summary>冲刺键是否按住。</summary>
        public bool SprintHeld => _player.Sprint.IsPressed();

        /// <summary>下蹲键是否按住。</summary>
        public bool CrouchHeld => _player.Crouch.IsPressed();

        /// <summary>开火键是否按住（全自动武器使用）。</summary>
        public bool AttackHeld => _player.Attack.IsPressed();

        // ---- 触发型输入（仅在触发的那一帧为 true）----

        /// <summary>本帧是否按下跳跃键。</summary>
        public bool JumpPressed => _player.Jump.WasPressedThisFrame();

        /// <summary>本帧是否按下开火键（半自动武器使用）。</summary>
        public bool AttackPressed => _player.Attack.WasPressedThisFrame();

        /// <summary>本帧是否按下换弹键。</summary>
        public bool ReloadPressed => _player.Reload.WasPressedThisFrame();

        /// <summary>本帧是否按下下蹲键。</summary>
        public bool CrouchPressed => _player.Crouch.WasPressedThisFrame();

        /// <summary>本帧是否完成交互。该动作配置了 Hold 交互，需要按住一小段时间才触发。</summary>
        public bool InteractPerformed => _player.Interact.WasPerformedThisFrame();

        /// <summary>本帧是否按下“下一把武器”。</summary>
        public bool NextPressed => _player.Next.WasPressedThisFrame();

        /// <summary>本帧是否按下“上一把武器”。</summary>
        public bool PreviousPressed => _player.Previous.WasPressedThisFrame();

        private void OnEnable()
        {
            _actions ??= new InputSystem_Actions();
            _player = _actions.Player;
            _player.Enable();
        }

        private void OnDisable()
        {
            _actions?.Player.Disable();
        }

        private void OnDestroy()
        {
            _actions?.Dispose();
            _actions = null;
        }

        private void OnGUI()
        {
            if (!showDebugOverlay || _actions == null)
            {
                return;
            }

            GUILayout.BeginArea(new Rect(10f, 10f, 320f, 260f), GUI.skin.box);
            GUILayout.Label("PlayerInputReader");
            GUILayout.Label($"Move: {Move}");
            GUILayout.Label($"Look: {Look}");
            GUILayout.Label($"Sprint: {SprintHeld}    Crouch: {CrouchHeld}");
            GUILayout.Label($"Attack: {AttackHeld}    Jump: {_player.Jump.IsPressed()}");
            GUILayout.Label($"Interact: {_player.Interact.IsPressed()}    Reload: {_player.Reload.IsPressed()}");
            GUILayout.Label($"Previous: {_player.Previous.IsPressed()}    Next: {_player.Next.IsPressed()}");
            GUILayout.EndArea();
        }
    }
}
