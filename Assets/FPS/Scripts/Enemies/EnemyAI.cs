using System;
using UnityEngine;
using UnityEngine.AI;

namespace FPS
{
    /// <summary>
    /// 敌人 AI 模块：基于 NavMeshAgent 的简单状态机，包含待机/巡逻、追击、近战攻击。
    /// 生命值由同物体上的 Health 管理；受到攻击时会主动追击。
    /// 挂载对象：敌人根物体，需要同时有 NavMeshAgent、Health 和一个碰撞体（用于被子弹命中）。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(Health))]
    public class EnemyAI : MonoBehaviour
    {
        private enum State
        {
            Idle,
            Patrol,
            Chase,
            Attack,
            Dead
        }

        [Header("目标")]
        [Tooltip("追击目标。留空时自动查找 Tag 为 Player 的物体。")]
        [SerializeField] private Transform target;

        [Header("感知")]
        [Tooltip("发现目标的距离（米）。")]
        [SerializeField, Min(0f)] private float detectionRange = 15f;

        [Tooltip("追击中目标超出该距离后放弃追击（米），应大于发现距离。")]
        [SerializeField, Min(0f)] private float loseRange = 25f;

        [Tooltip("是否需要视线不被遮挡才能发现和攻击目标。")]
        [SerializeField] private bool requireLineOfSight = true;

        [Tooltip("会遮挡视线的层。")]
        [SerializeField] private LayerMask sightMask = ~0;

        [Tooltip("受到攻击后强制追击的时间（秒），期间无视距离限制。")]
        [SerializeField, Min(0f)] private float aggroDuration = 5f;

        [Header("移动")]
        [Tooltip("移动速度（米/秒）。")]
        [SerializeField, Min(0f)] private float moveSpeed = 3.5f;

        [Tooltip("攻击时转向目标的速度（度/秒）。")]
        [SerializeField, Min(0f)] private float turnSpeed = 360f;

        [Header("攻击")]
        [Tooltip("攻击距离（米）。")]
        [SerializeField, Min(0f)] private float attackRange = 2f;

        [Tooltip("每次攻击的伤害。")]
        [SerializeField, Min(0f)] private float attackDamage = 10f;

        [Tooltip("两次攻击的间隔（秒）。")]
        [SerializeField, Min(0.05f)] private float attackCooldown = 1f;

        [Header("巡逻（可选）")]
        [Tooltip("巡逻点。留空则原地待机。")]
        [SerializeField] private Transform[] patrolPoints;

        [Tooltip("到达巡逻点后的停留时间（秒）。")]
        [SerializeField, Min(0f)] private float patrolWaitTime = 2f;

        [Header("动画（可选）")]
        [Tooltip("带骨骼模型上的 Animator。留空时自动在子物体里查找；没有 Animator 的敌人不播放动画。")]
        [SerializeField] private Animator animator;

        [Tooltip("走路动画按原速播放时对应的移动速度（米/秒），用来让脚步和实际移动速度一致。0 表示始终按原速播放。")]
        [SerializeField, Min(0f)] private float walkAnimationSpeed;

        [Tooltip("攻击动画开始后多久造成伤害（秒），对应挥击打中的那一刻。0 表示开始攻击时立即造成伤害。")]
        [SerializeField, Min(0f)] private float attackHitDelay;

        [Tooltip("攻击动画的时长（秒），播放期间敌人停在原地。0 表示不等待动画。")]
        [SerializeField, Min(0f)] private float attackDuration;

        private const float RepathInterval = 0.2f;

        // Animator 参数：Speed 是当前移动速度（米/秒），WalkCycle 是走路动画的播放倍速，
        // Attack、Die 是攻击和死亡的触发器（Controller 里没有这两个参数时不触发）
        private static readonly int SpeedParameter = Animator.StringToHash("Speed");
        private static readonly int WalkCycleParameter = Animator.StringToHash("WalkCycle");
        private static readonly int AttackParameter = Animator.StringToHash("Attack");
        private static readonly int DieParameter = Animator.StringToHash("Die");

        private NavMeshAgent _agent;
        private Health _health;
        private Health _targetHealth;
        private IDamageable _targetDamageable;

        private State _state = State.Idle;
        private float _nextAttackTime;
        private float _nextRepathTime;
        private float _aggroUntil;
        private float _patrolWaitUntil;
        private int _patrolIndex;
        private bool _counted;

        private bool _hitPending;      // 攻击动画已经开始，还没到造成伤害的时刻
        private float _hitTime;
        private float _attackBusyUntil; // 攻击动画播放结束的时间

        // ---- 存活敌人统计：供游戏流程和 HUD 使用 ----

        /// <summary>当前存活的敌人数量。</summary>
        public static int AliveCount { get; private set; }

        /// <summary>有敌人死亡时触发，参数为剩余存活数量。</summary>
        public static event Action<int> EnemyDied;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            AliveCount = 0;
            EnemyDied = null;
        }

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _health = GetComponent<Health>();

            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }
        }

        private void OnEnable()
        {
            _health.Damaged += OnDamaged;
            _health.Died += OnDied;

            if (_state != State.Dead && !_counted)
            {
                _counted = true;
                AliveCount++;
            }
        }

        private void OnDisable()
        {
            _health.Damaged -= OnDamaged;
            _health.Died -= OnDied;

            // 未死亡就被禁用或随场景卸载：只修正计数，不触发死亡事件
            if (_counted)
            {
                _counted = false;
                AliveCount--;
            }
        }

        private void Start()
        {
            if (target == null)
            {
                GameObject player = GameObject.FindGameObjectWithTag("Player");
                if (player != null)
                {
                    target = player.transform;
                }
            }

            if (target != null)
            {
                _targetHealth = target.GetComponentInParent<Health>();
                _targetDamageable = target.GetComponentInParent<IDamageable>();
            }
            else
            {
                Debug.LogWarning($"{nameof(EnemyAI)}: 没有找到目标，请确认玩家的 Tag 为 Player。", this);
            }

            _agent.speed = moveSpeed;
            _agent.stoppingDistance = attackRange * 0.8f;

            PlaceOnNavMesh();
            _state = HasPatrol ? State.Patrol : State.Idle;
        }

        private bool HasPatrol => patrolPoints != null && patrolPoints.Length > 0;

        /// <summary>
        /// NavMesh 是运行时生成的，所以 Agent 在场景里默认是禁用的，这里在 NavMesh 就绪后再启用。
        /// </summary>
        private void PlaceOnNavMesh()
        {
            if (_agent.enabled && _agent.isOnNavMesh)
            {
                return;
            }

            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 5f, NavMesh.AllAreas))
            {
                _agent.enabled = false;
                transform.position = hit.position + Vector3.up * _agent.baseOffset;
                _agent.enabled = true;
            }
            else
            {
                Debug.LogWarning(
                    $"{nameof(EnemyAI)}: {name} 附近 5 米内没有导航网格，无法移动。" +
                    "请确认场景中有 RuntimeNavMeshBaker，且敌人放在地面附近。", this);
            }
        }

        private void Update()
        {
            UpdateAnimation();

            if (_state == State.Dead || target == null || !_agent.enabled || !_agent.isOnNavMesh)
            {
                return;
            }

            // 目标已死亡：停下来
            if (_targetHealth != null && !_targetHealth.IsAlive)
            {
                _hitPending = false;

                if (_state == State.Chase || _state == State.Attack)
                {
                    StopMoving();
                    _state = State.Idle;
                }

                return;
            }

            float distance = Vector3.Distance(transform.position, target.position);

            // 攻击动画挥到命中的那一刻才结算伤害；目标已经躲开（超出攻击距离）就打空
            if (_hitPending && Time.time >= _hitTime)
            {
                _hitPending = false;
                if (distance <= attackRange * 1.2f)
                {
                    DealDamage();
                }
            }

            switch (_state)
            {
                case State.Idle:
                case State.Patrol:
                    if (distance <= detectionRange && HasLineOfSight())
                    {
                        _state = State.Chase;
                    }
                    else if (_state == State.Patrol)
                    {
                        UpdatePatrol();
                    }

                    break;

                case State.Chase:
                    UpdateChase(distance);
                    break;

                case State.Attack:
                    UpdateAttack(distance);
                    break;
            }
        }

        /// <summary>把当前移动速度告诉 Animator：移动时播放走路动画，停下时回到待机。</summary>
        private void UpdateAnimation()
        {
            if (animator == null || animator.runtimeAnimatorController == null)
            {
                return;
            }

            float speed = _agent.enabled && _agent.isOnNavMesh ? _agent.velocity.magnitude : 0f;
            animator.SetFloat(SpeedParameter, speed);
            animator.SetFloat(
                WalkCycleParameter,
                walkAnimationSpeed > 0f ? Mathf.Clamp(speed / walkAnimationSpeed, 0.5f, 3f) : 1f);
        }

        private void UpdatePatrol()
        {
            if (_agent.pathPending || Time.time < _patrolWaitUntil)
            {
                return;
            }

            if (!_agent.hasPath || _agent.remainingDistance <= _agent.stoppingDistance + 0.1f)
            {
                Transform point = patrolPoints[_patrolIndex];
                _patrolIndex = (_patrolIndex + 1) % patrolPoints.Length;
                _patrolWaitUntil = Time.time + patrolWaitTime;

                if (point != null)
                {
                    _agent.isStopped = false;
                    _agent.SetDestination(point.position);
                }
            }
        }

        private void UpdateChase(float distance)
        {
            bool aggro = Time.time < _aggroUntil;

            if (distance > loseRange && !aggro)
            {
                StopMoving();
                _state = HasPatrol ? State.Patrol : State.Idle;
                return;
            }

            if (distance <= attackRange && HasLineOfSight())
            {
                StopMoving();
                _state = State.Attack;
                return;
            }

            if (Time.time >= _nextRepathTime)
            {
                _nextRepathTime = Time.time + RepathInterval;
                _agent.isStopped = false;
                _agent.SetDestination(target.position);
            }
        }

        private void UpdateAttack(float distance)
        {
            // 攻击动画播放期间停在原地，播完才重新判断是否追击
            bool busy = Time.time < _attackBusyUntil;

            // 留一点余量，避免在攻击距离边缘来回切换状态
            if (!busy && (distance > attackRange * 1.2f || !HasLineOfSight()))
            {
                _state = State.Chase;
                return;
            }

            FaceTarget();

            if (!busy && Time.time >= _nextAttackTime)
            {
                _nextAttackTime = Time.time + Mathf.Max(attackCooldown, attackDuration);
                _attackBusyUntil = Time.time + attackDuration;

                if (HasParameter(AttackParameter))
                {
                    animator.SetTrigger(AttackParameter);
                }

                if (attackHitDelay > 0f)
                {
                    _hitPending = true;
                    _hitTime = Time.time + attackHitDelay;
                }
                else
                {
                    DealDamage();
                }
            }
        }

        private void DealDamage()
        {
            Vector3 direction = (target.position - transform.position).normalized;
            _targetDamageable?.TakeDamage(new DamageInfo(attackDamage, target.position, direction, gameObject));
        }

        /// <summary>Animator 的 Controller 里是否有这个参数。</summary>
        private bool HasParameter(int hash)
        {
            if (animator == null || animator.runtimeAnimatorController == null)
            {
                return false;
            }

            foreach (AnimatorControllerParameter parameter in animator.parameters)
            {
                if (parameter.nameHash == hash)
                {
                    return true;
                }
            }

            return false;
        }

        private void FaceTarget()
        {
            Vector3 toTarget = target.position - transform.position;
            toTarget.y = 0f;

            if (toTarget.sqrMagnitude < 0.0001f)
            {
                return;
            }

            Quaternion look = Quaternion.LookRotation(toTarget);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, look, turnSpeed * Time.deltaTime);
        }

        private bool HasLineOfSight()
        {
            if (!requireLineOfSight)
            {
                return true;
            }

            bool blocked = Physics.Linecast(
                transform.position, target.position, out RaycastHit hit, sightMask, QueryTriggerInteraction.Ignore);

            // 没有任何遮挡，或者第一个碰到的就是目标本身
            return !blocked || hit.transform.root == target.root;
        }

        private void StopMoving()
        {
            if (_agent.enabled && _agent.isOnNavMesh)
            {
                _agent.isStopped = true;
                _agent.ResetPath();
            }
        }

        private void OnDamaged(DamageInfo damage)
        {
            if (_state == State.Dead)
            {
                return;
            }

            // 被打了就追，哪怕目标在发现距离之外
            _aggroUntil = Time.time + aggroDuration;
            if (_state == State.Idle || _state == State.Patrol)
            {
                _state = State.Chase;
            }
        }

        private void OnDied(DamageInfo damage)
        {
            _state = State.Dead;
            _hitPending = false;

            if (_agent.enabled)
            {
                _agent.enabled = false;
            }

            // 播放死亡动画：尸体在 Health 的 Destroy Delay 之后才消失，期间不再挡子弹和玩家
            if (HasParameter(DieParameter))
            {
                if (HasParameter(AttackParameter))
                {
                    animator.ResetTrigger(AttackParameter);
                }

                animator.SetTrigger(DieParameter);

                foreach (Collider bodyCollider in GetComponents<Collider>())
                {
                    bodyCollider.enabled = false;
                }
            }

            if (_counted)
            {
                _counted = false;
                AliveCount--;
                EnemyDied?.Invoke(AliveCount);
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, detectionRange);
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, attackRange);
        }
    }
}
