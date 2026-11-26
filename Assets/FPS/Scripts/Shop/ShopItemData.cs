using System;
using UnityEngine;

namespace FPS
{
    /// <summary>
    /// 商城商品数据：描述陈列柜中的一件可购买装备及其对应的玩家装备物体与展示模型。
    /// 界面文本默认使用英文，以兼容项目自带的 TextMesh Pro 默认字体。
    /// </summary>
    [Serializable]
    public class ShopItemData
    {
        [Tooltip("商品唯一 ID，用于区分是否已购买。")]
        public string id = "pulse_blaster";

        [Tooltip("商城中显示的商品名称。")]
        public string displayName = "PULSE BLASTER";

        [Tooltip("商品说明文字。")]
        [TextArea(2, 3)]
        public string description = "High-impact sci-fi energy rifle. Press 1 / 2 to switch weapons.";

        [Tooltip("购买所需的金币价格。")]
        [Min(0)] public int price = 80;

        [Header("装备与陈列模型")]
        [Tooltip("玩家身上对应的装备物体（例如 PlayerCamera/Blaster）。购买前会被锁定，购买后解锁并自动装备。")]
        public GameObject equipmentObject;

        [Tooltip("陈列柜中展示的 3D 模型预制体。留空时自动克隆 equipmentObject 下的模型。")]
        public GameObject displayModelPrefab;

        [Tooltip("陈列柜展示模型使用的材质（可选）。留空时保留模型自带材质。")]
        public Material displayMaterial;

        [Tooltip("陈列柜中展示模型的初始旋转偏移（欧拉角）。")]
        public Vector3 displayRotationOffset = new Vector3(0f, 30f, 0f);

        [Tooltip("陈列柜中展示模型的额外缩放倍率。")]
        [Min(0.1f)] public float displayScale = 1f;

        /// <summary>自动读取对应武器/弹药参数，生成商城详情页的属性摘要。</summary>
        public string BuildStatsSummary()
        {
            if (equipmentObject == null)
            {
                return string.Empty;
            }

            Weapon weapon = equipmentObject.GetComponent<Weapon>();
            if (weapon == null)
            {
                return string.Empty;
            }

            WeaponAmmo ammo = equipmentObject.GetComponent<WeaponAmmo>();
            string ammoPart = ammo != null
                ? $"  |  MAG {ammo.MagazineSize} / {(ammo.HasInfiniteReserve ? "INF" : ammo.Reserve.ToString())}"
                : string.Empty;
            string mode = weapon.IsAutomatic ? "AUTO" : "SEMI";
            return $"DMG {weapon.Damage:0}  |  RATE {weapon.FireRate:0.0}/s  |  {mode}{ammoPart}";
        }
    }
}
