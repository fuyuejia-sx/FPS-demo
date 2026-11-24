using UnityEngine;

namespace FPS
{
    /// <summary>
    /// 玩家移动模块：基于 CharacterController 实现行走、冲刺、跳跃和重力。
    /// 移动方向相对于玩家自身朝向（transform.forward / right），朝向由视角模块负责旋转。
    /// 挂载对象：玩家根物体（Player），与 CharacterController、PlayerInputReader 在同一物体上。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(PlayerInputReader))]
    public class PlayerMovement : MonoBehaviour
    {
        [Header("速度")]
        [Tooltip("行走速度（米/秒）")]
        [SerializeField, Min(0f)] private float walkSpeed = 5f;

        [Tooltip("冲刺速度（米/秒）。只有向前移动时才能冲刺。")]
        [SerializeField, Min(0f)] private float sprintSpeed = 8f;

        [Tooltip("地面加速度（米/秒²）。越大起步和停下越干脆。")]
        [SerializeField, Min(0f)] private float groundAcceleration = 50f;

        [Tooltip("空中加速度（米/秒²）。越小空中越难改变方向。")]
        [SerializeField, Min(0f)] private float airAcceleration = 15f;

        [Header("跳跃与重力")]
        [Tooltip("跳跃高度（米）")]
        [SerializeField, Min(0f)] private float jumpHeight = 1.2f;

        [Tooltip("重力加速度（米/秒²），应为负值。比真实的 -9.81 大一些手感更利落。")]
        [SerializeField] private float gravity = -20f;

        [Tooltip("站在地面时保持的向下速度，用于贴住地面和斜坡，应为较小的负值。")]
        [SerializeField] private float groundedStickVelocity = -2f;

        private CharacterController _controller;
        private PlayerInputReader _input;

        private Vector3 _horizontalVelocity;
        private float _verticalVelocity;

        /// <summary>是否站在地面上。</summary>
        public bool IsGrounded => _controller.isGrounded;

        /// <summary>当前是否处于冲刺状态。</summary>
        public bool IsSprinting { get; private set; }

        /// <summary>当前速度（世界坐标，米/秒）。</summary>
        public Vector3 Velocity => _horizontalVelocity + Vector3.up * _verticalVelocity;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _input = GetComponent<PlayerInputReader>();
        }

        private void Update()
        {
            bool grounded = _controller.isGrounded;

            UpdateHorizontalVelocity(grounded);
            UpdateVerticalVelocity(grounded);

            CollisionFlags flags = _controller.Move(Velocity * Time.deltaTime);

            // 头顶撞到天花板时停止上升，避免贴着天花板悬停
            if ((flags & CollisionFlags.Above) != 0 && _verticalVelocity > 0f)
            {
                _verticalVelocity = 0f;
            }
        }

        private void UpdateHorizontalVelocity(bool grounded)
        {
            Vector2 move = Vector2.ClampMagnitude(_input.Move, 1f);

            // 只有在地面、按住冲刺键且有向前的输入时才算冲刺
            IsSprinting = grounded && _input.SprintHeld && move.y > 0.1f;

            float targetSpeed = IsSprinting ? sprintSpeed : walkSpeed;
            Vector3 targetVelocity = (transform.right * move.x + transform.forward * move.y) * targetSpeed;

            float acceleration = grounded ? groundAcceleration : airAcceleration;
            _horizontalVelocity = Vector3.MoveTowards(
                _horizontalVelocity, targetVelocity, acceleration * Time.deltaTime);
        }

        private void UpdateVerticalVelocity(bool grounded)
        {
            if (grounded)
            {
                if (_verticalVelocity < 0f)
                {
                    _verticalVelocity = groundedStickVelocity;
                }

                if (_input.JumpPressed)
                {
                    // v = sqrt(2 * |g| * h)：达到指定跳跃高度所需的初速度
                    _verticalVelocity = Mathf.Sqrt(2f * jumpHeight * Mathf.Abs(gravity));
                }
            }

            _verticalVelocity += gravity * Time.deltaTime;
        }
    }
}
