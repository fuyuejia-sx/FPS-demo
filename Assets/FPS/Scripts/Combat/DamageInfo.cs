using UnityEngine;

namespace FPS
{
    /// <summary>
    /// 一次伤害的完整信息。由攻击方（武器、敌人、陷阱等）创建，传给受击方。
    /// 这是纯数据结构，不需要挂载到任何物体上。
    /// </summary>
    public readonly struct DamageInfo
    {
        /// <summary>伤害数值。</summary>
        public readonly float Amount;

        /// <summary>命中位置（世界坐标），用于播放命中特效。</summary>
        public readonly Vector3 Point;

        /// <summary>伤害方向（世界坐标，从攻击方指向受击方），用于击退、受击方向提示等。</summary>
        public readonly Vector3 Direction;

        /// <summary>伤害来源物体，可以为空（例如环境伤害）。</summary>
        public readonly GameObject Source;

        public DamageInfo(float amount, Vector3 point, Vector3 direction, GameObject source)
        {
            Amount = amount;
            Point = point;
            Direction = direction;
            Source = source;
        }

        /// <summary>返回一份只改了伤害数值的副本，用于伤害被盾牌等部分吸收之后继续传递。</summary>
        public DamageInfo WithAmount(float amount)
        {
            return new DamageInfo(amount, Point, Direction, Source);
        }

        /// <summary>只关心数值时使用的简化构造。</summary>
        public DamageInfo(float amount, GameObject source = null)
            : this(amount, Vector3.zero, Vector3.zero, source)
        {
        }
    }
}
