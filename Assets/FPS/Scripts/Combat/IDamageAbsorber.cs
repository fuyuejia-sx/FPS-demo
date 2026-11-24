namespace FPS
{
    /// <summary>
    /// 伤害吸收者接口：在伤害扣到生命值之前先拦截一部分，例如盾牌、护甲。
    /// Health 会在受伤时依次询问同一物体上的所有吸收者。
    /// </summary>
    public interface IDamageAbsorber
    {
        /// <summary>
        /// 吸收一部分伤害，返回没有被吸收、需要继续向下传递的伤害值。
        /// 不吸收时原样返回 damage.Amount。
        /// </summary>
        float AbsorbDamage(DamageInfo damage);
    }
}
