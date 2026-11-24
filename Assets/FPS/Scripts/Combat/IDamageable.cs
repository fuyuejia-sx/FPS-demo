namespace FPS
{
    /// <summary>
    /// 可受伤对象的统一接口。攻击方只依赖这个接口，不需要知道对方是玩家、敌人还是靶子。
    /// 用法：hit.collider.GetComponentInParent&lt;IDamageable&gt;()?.TakeDamage(info);
    /// </summary>
    public interface IDamageable
    {
        /// <summary>是否还活着。</summary>
        bool IsAlive { get; }

        /// <summary>承受一次伤害。</summary>
        void TakeDamage(DamageInfo damage);
    }
}
