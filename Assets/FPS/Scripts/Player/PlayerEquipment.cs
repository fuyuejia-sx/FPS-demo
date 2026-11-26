using System;
using System.Collections.Generic;
using UnityEngine;

namespace FPS
{
    /// <summary>
    /// 装备切换模块：管理玩家手上的多个装备（例如枪、盾），同一时间只显示并启用其中一个。
    /// 按“上一把 / 下一把”输入循环切换（键盘 1 / 2，手柄十字键左 / 右）。
    /// 未选中的装备会被整体隐藏（SetActive(false)），因此枪被收起时自然无法开火和换弹。
    /// 其他模块可以把某个装备标记为不可用（例如盾值耗尽的盾），不可用的装备会被自动收起且无法切换到。
    /// 挂载对象：玩家根物体（Player），与 PlayerInputReader 在同一物体上。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerInputReader))]
    public class PlayerEquipment : MonoBehaviour
    {
        [Tooltip("装备槽，按切换顺序排列。每一项是一个装备物体，例如 Weapon、Shield。")]
        [SerializeField] private GameObject[] slots;

        [Tooltip("开始时手持第几个装备，从 0 开始计数。")]
        [SerializeField, Min(0)] private int startIndex;

        private readonly HashSet<GameObject> _unavailable = new HashSet<GameObject>();

        private PlayerInputReader _input;
        private int _currentIndex = -1;
        private bool _started;

        /// <summary>当前手持装备的序号，没有手持任何装备时为 -1。</summary>
        public int CurrentIndex => _currentIndex;

        /// <summary>当前手持的装备物体，没有时为 null。</summary>
        public GameObject Current =>
            slots != null && _currentIndex >= 0 && _currentIndex < slots.Length ? slots[_currentIndex] : null;

        /// <summary>手持的装备发生变化后触发，参数为新手持的装备物体（可能为 null）。</summary>
        public event Action<GameObject> Equipped;

        private void Awake()
        {
            _input = GetComponent<PlayerInputReader>();
        }

        private void Start()
        {
            // 放在 Start 而不是 Awake：保证所有装备物体自己的 Awake 都已经执行过，再隐藏未选中的
            _started = true;

            if (slots == null || slots.Length == 0)
            {
                Debug.LogWarning($"{nameof(PlayerEquipment)}: 没有配置任何装备槽。", this);
                return;
            }

            int first = Mathf.Clamp(startIndex, 0, slots.Length - 1);
            SetCurrent(IsUsable(first) ? first : FindUsable(first, 1));
        }

        private void Update()
        {
            if (slots == null || slots.Length < 2)
            {
                return;
            }

            int direction = _input.NextPressed ? 1 : _input.PreviousPressed ? -1 : 0;
            if (direction == 0)
            {
                return;
            }

            int target = FindUsable(_currentIndex, direction);
            if (target >= 0)
            {
                SetCurrent(target);
            }
        }

        /// <summary>切换到指定序号的装备。该装备不可用时返回 false。</summary>
        public bool Equip(int index)
        {
            if (!IsUsable(index))
            {
                return false;
            }

            SetCurrent(index);
            return true;
        }

        /// <summary>切换到指定装备物体。不在槽位中或不可用时返回 false。</summary>
        public bool Equip(GameObject slot)
        {
            int index = IndexOf(slot);
            return index >= 0 && Equip(index);
        }

        /// <summary>查找装备物体在槽位数组中的序号，不存在时返回 -1。</summary>
        public int IndexOf(GameObject slot)
        {
            if (slots == null || slot == null)
            {
                return -1;
            }

            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] == slot)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>运行时向装备槽末尾追加一个新装备（如果尚未在槽位中）。</summary>
        public void AddSlot(GameObject slot, bool available = true)
        {
            if (slot == null)
            {
                return;
            }

            if (IndexOf(slot) < 0)
            {
                int oldLength = slots != null ? slots.Length : 0;
                var expanded = new GameObject[oldLength + 1];
                for (int i = 0; i < oldLength; i++)
                {
                    expanded[i] = slots[i];
                }

                expanded[oldLength] = slot;
                slots = expanded;
            }

            if (!available)
            {
                _unavailable.Add(slot);
                slot.SetActive(false);
            }
            else
            {
                _unavailable.Remove(slot);
                if (_started && Current != slot)
                {
                    slot.SetActive(false);
                }
            }
        }

        /// <summary>从快捷装备槽中移除某个装备物体（例如放入背包收纳时）。</summary>
        public void RemoveSlot(GameObject slot)
        {
            if (slot == null || slots == null)
            {
                return;
            }

            int removeIndex = IndexOf(slot);
            if (removeIndex < 0)
            {
                return;
            }

            bool wasCurrent = _started && _currentIndex == removeIndex;
            _unavailable.Remove(slot);
            slot.SetActive(false);

            var reduced = new GameObject[slots.Length - 1];
            int dst = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                if (i != removeIndex)
                {
                    reduced[dst++] = slots[i];
                }
            }

            slots = reduced;

            if (wasCurrent)
            {
                int next = FindUsable(-1, 1);
                SetCurrent(next);
            }
            else if (_currentIndex > removeIndex)
            {
                _currentIndex--;
            }
        }

        /// <summary>
        /// 用 newSlot 替换快捷装备槽里的 oldSlot（用于商城购买替换或背包切换主武器）。
        /// 被替换下来的 oldSlot 会自动隐藏；如果原先正手持 oldSlot 或 equipImmediately 为 true，则立刻切到 newSlot。
        /// </summary>
        public bool ReplaceSlot(GameObject oldSlot, GameObject newSlot, bool equipImmediately = false)
        {
            if (newSlot == null)
            {
                return false;
            }

            if (oldSlot == newSlot)
            {
                _unavailable.Remove(newSlot);
                if (IndexOf(newSlot) < 0)
                {
                    AddSlot(newSlot, true);
                }

                if (equipImmediately && _started)
                {
                    Equip(newSlot);
                }

                return true;
            }

            // 如果 newSlot 之前已经占着另一个槽位，先移除重复项避免槽位里出现两把武器
            int existingNew = IndexOf(newSlot);
            if (existingNew >= 0)
            {
                RemoveSlot(newSlot);
            }

            int index = IndexOf(oldSlot);
            if (index < 0)
            {
                AddSlot(newSlot, true);
                if (equipImmediately && _started)
                {
                    Equip(newSlot);
                }

                return true;
            }

            bool wasHoldingOld = _started && _currentIndex == index;
            if (oldSlot != null)
            {
                _unavailable.Remove(oldSlot);
                oldSlot.SetActive(false);
            }

            _unavailable.Remove(newSlot);
            slots[index] = newSlot;

            if (_started && (wasHoldingOld || equipImmediately))
            {
                SetCurrent(index);
            }
            else
            {
                newSlot.SetActive(false);
            }

            return true;
        }

        /// <summary>
        /// 设置某个装备是否可用。不可用的装备无法被切换到；
        /// 如果它正拿在手上，会立刻被收起并换成下一个可用的装备。
        /// </summary>
        public void SetSlotAvailable(GameObject slot, bool available)
        {
            if (slot == null)
            {
                return;
            }

            if (available)
            {
                _unavailable.Remove(slot);
                return;
            }

            _unavailable.Add(slot);

            if (_started && Current == slot)
            {
                // 找不到其他可用装备时结果是 -1，表示空手
                SetCurrent(FindUsable(_currentIndex, 1));
            }
        }

        /// <summary>某个装备当前是否可用。</summary>
        public bool IsSlotAvailable(GameObject slot)
        {
            return slot != null && !_unavailable.Contains(slot);
        }

        private bool IsUsable(int index)
        {
            return slots != null && index >= 0 && index < slots.Length && IsSlotAvailable(slots[index]);
        }

        /// <summary>从 from 出发按 direction 方向找下一个可用的装备，不包括 from 自己。找不到返回 -1。</summary>
        private int FindUsable(int from, int direction)
        {
            int count = slots.Length;
            for (int step = 1; step < count + 1; step++)
            {
                int index = ((from + direction * step) % count + count) % count;
                if (index == from)
                {
                    break;
                }

                if (IsUsable(index))
                {
                    return index;
                }
            }

            return -1;
        }

        private void SetCurrent(int index)
        {
            _currentIndex = index;

            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] != null)
                {
                    slots[i].SetActive(i == index);
                }
            }

            Equipped?.Invoke(Current);
        }
    }
}
