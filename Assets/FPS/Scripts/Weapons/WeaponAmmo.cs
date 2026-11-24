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

        /// <summary>弹匣内的子弹数。</summary>
        public int InMagazine => _inMagazine;

        /// <summary>备弹数。</summary>
        public int Reserve => _reserve;

        public int MagazineSize => magazineSize;
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
            _inMagazine = magazineSize;
            _reserve = startingReserve;
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

        /// <summary>增加备弹，用于弹药拾取。</summary>
        public void AddReserve(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            _reserve += amount;
            AmmoChanged?.Invoke(_inMagazine, _reserve);
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
