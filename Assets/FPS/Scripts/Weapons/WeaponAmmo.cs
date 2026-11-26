using System;
using UnityEngine;

namespace FPS
{
    /// <summary>
    /// 弹药模块：管理弹匣、备弹和换弹计时。
    /// 与 Weapon 挂在同一物体上时，Weapon 每次开火会自动消耗一发；不挂则武器弹药无限。
    /// 挂载对象：武器物体（与 Weapon 同一物体）。
    /// </summary>
    [DisallowMultipleComponent]
    public class WeaponAmmo : MonoBehaviour
    {
        [Header("弹药")]
        [Tooltip("弹匣容量。")]
        [SerializeField, Min(1)] private int magazineSize = 30;

        [Tooltip("开始时携带的备弹数量（不含弹匣内的子弹）。")]
        [SerializeField, Min(0)] private int startingReserve = 90;

        [Tooltip("最大备弹上限（不含弹匣内的子弹）。0 表示默认等于初始备弹量，拾取弹药不会超过该上限。")]
        [SerializeField, Min(0)] private int maxReserve;

        [Tooltip("勾选后备弹无限，换弹不消耗备弹。")]
        [SerializeField] private bool infiniteReserve;

        [Header("换弹")]
        [Tooltip("换弹所需时间（秒）。")]
        [SerializeField, Min(0f)] private float reloadTime = 1.5f;

        [Tooltip("弹匣打空后是否自动换弹。")]
        [SerializeField] private bool autoReloadWhenEmpty = true;

        private int _inMagazine;
        private int _reserve;
        private float _reloadEndTime;
        private bool _initialized;

        /// <summary>弹匣内的子弹数（未激活过的武器返回初始弹匣容量）。</summary>
        public int InMagazine => _initialized ? _inMagazine : magazineSize;

        /// <summary>备弹数（未激活过的武器返回初始备弹量）。</summary>
        public int Reserve => _initialized ? _reserve : startingReserve;

        public int MagazineSize => magazineSize;

        /// <summary>最大备弹上限。未单独配置（0）时默认等于初始备弹量。</summary>
        public int MaxReserve => maxReserve > 0 ? maxReserve : startingReserve;

        /// <summary>当前总弹药数（弹匣内 + 备弹）。</summary>
        public int TotalAmmo => InMagazine + Reserve;

        /// <summary>总弹药上限（弹匣容量 + 最大备弹上限）。</summary>
        public int MaxTotalAmmo => magazineSize + MaxReserve;

        /// <summary>备弹是否已达上限（无限备弹视为已满）。</summary>
        public bool IsReserveFull => infiniteReserve || Reserve >= MaxReserve;

        /// <summary>总弹药是否已全部装满（弹匣满且备弹满）。</summary>
        public bool IsAmmoFull => InMagazine >= magazineSize && IsReserveFull;

        public bool HasInfiniteReserve => infiniteReserve;

        /// <summary>是否正在换弹。</summary>
        public bool IsReloading { get; private set; }

        /// <summary>弹药数量变化时触发，参数为（弹匣内数量，备弹数量）。</summary>
        public event Action<int, int> AmmoChanged;

        /// <summary>开始换弹时触发。</summary>
        public event Action ReloadStarted;

        /// <summary>换弹完成时触发。</summary>
        public event Action ReloadFinished;

        private void Awake()
        {
            if (!_initialized)
            {
                _initialized = true;
                _inMagazine = magazineSize;
                _reserve = startingReserve;
            }
        }

        private void Update()
        {
            if (IsReloading && Time.time >= _reloadEndTime)
            {
                FinishReload();
            }
        }

        private void OnDisable()
        {
            // 武器被收起或禁用时取消未完成的换弹，并通知界面刷新（否则会一直显示换弹中）
            if (IsReloading)
            {
                IsReloading = false;
                AmmoChanged?.Invoke(_inMagazine, _reserve);
            }
        }

        /// <summary>
        /// 尝试消耗一发子弹。换弹中或弹匣为空时返回 false。
        /// </summary>
        public bool TryConsume()
        {
            if (IsReloading)
            {
                return false;
            }

            if (_inMagazine <= 0)
            {
                if (autoReloadWhenEmpty)
                {
                    TryStartReload();
                }

                return false;
            }

            _inMagazine--;
            AmmoChanged?.Invoke(_inMagazine, _reserve);

            if (_inMagazine == 0 && autoReloadWhenEmpty)
            {
                TryStartReload();
            }

            return true;
        }

        /// <summary>
        /// 尝试开始换弹。正在换弹、弹匣已满或没有备弹时返回 false。
        /// </summary>
        public bool TryStartReload()
        {
            bool hasReserve = infiniteReserve || _reserve > 0;
            if (IsReloading || _inMagazine >= magazineSize || !hasReserve || !isActiveAndEnabled)
            {
                return false;
            }

            IsReloading = true;
            _reloadEndTime = Time.time + reloadTime;
            ReloadStarted?.Invoke();
            return true;
        }

        /// <summary>
        /// 弹药拾取补给：优先补足备弹至 MaxReserve，若备弹已满且弹匣未满（例如 29/90），
        /// 则将剩余补给量补满当前弹匣（不超过 MagazineSize）。返回实际增加的子弹总数。
        /// </summary>
        public int AddAmmo(int amount)
        {
            if (amount <= 0)
            {
                return 0;
            }

            if (!_initialized)
            {
                _initialized = true;
                _inMagazine = magazineSize;
                _reserve = startingReserve;
            }

            int remaining = amount;
            int addedReserve = 0;

            if (!infiniteReserve)
            {
                int reserveRoom = Mathf.Max(0, MaxReserve - _reserve);
                addedReserve = Mathf.Min(remaining, reserveRoom);
                _reserve += addedReserve;
                remaining -= addedReserve;
            }

            int addedMagazine = 0;
            if (remaining > 0 && _inMagazine < magazineSize)
            {
                int magRoom = magazineSize - _inMagazine;
                addedMagazine = Mathf.Min(remaining, magRoom);
                _inMagazine += addedMagazine;

                if (IsReloading && _inMagazine >= magazineSize)
                {
                    IsReloading = false;
                    ReloadFinished?.Invoke();
                }
            }

            int totalAdded = addedReserve + addedMagazine;
            if (totalAdded > 0)
            {
                AmmoChanged?.Invoke(_inMagazine, _reserve);
            }

            return totalAdded;
        }

        /// <summary>
        /// 仅增加备弹。不会超过 MaxReserve 上限；返回实际增加的子弹数（已满时返回 0）。
        /// </summary>
        public int AddReserve(int amount)
        {
            if (amount <= 0 || infiniteReserve)
            {
                return 0;
            }

            if (!_initialized)
            {
                _initialized = true;
                _inMagazine = magazineSize;
                _reserve = startingReserve;
            }

            int cap = MaxReserve;
            if (_reserve >= cap)
            {
                return 0;
            }

            int added = Mathf.Min(amount, cap - _reserve);
            _reserve += added;
            AmmoChanged?.Invoke(_inMagazine, _reserve);
            return added;
        }

        private void FinishReload()
        {
            IsReloading = false;

            int needed = magazineSize - _inMagazine;
            int loaded = infiniteReserve ? needed : Mathf.Min(needed, _reserve);

            _inMagazine += loaded;
            if (!infiniteReserve)
            {
                _reserve -= loaded;
            }

            AmmoChanged?.Invoke(_inMagazine, _reserve);
            ReloadFinished?.Invoke();
        }
    }
}
