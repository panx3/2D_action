using UnityEngine;

/// <summary>
/// モーニングスター命中時に対象へ渡す情報。
/// </summary>
public readonly struct MorningStarHitContext
{
    public int Damage { get; }
    public Vector2 KnockbackImpulse { get; }
    public float HitStopSeconds { get; }
    public Vector2 ImpactPoint { get; }
    public Vector2 ImpactDirection { get; }
    public float ImpactSpeed { get; }
    public float ChargeMultiplier { get; }

    public MorningStarHitContext(
        int damage,
        Vector2 knockbackImpulse,
        float hitStopSeconds,
        Vector2 impactPoint,
        Vector2 impactDirection,
        float impactSpeed,
        float chargeMultiplier)
    {
        Damage = damage;
        KnockbackImpulse = knockbackImpulse;
        HitStopSeconds = hitStopSeconds;
        ImpactPoint = impactPoint;
        ImpactDirection = impactDirection;
        ImpactSpeed = impactSpeed;
        ChargeMultiplier = chargeMultiplier;
    }
}

/// <summary>
/// モーニングスターの攻撃を受け取るコンポーネント用インターフェース。
/// </summary>
public interface IMorningStarHitReceiver
{
    void OnMorningStarHit(MorningStarHitContext context);
}
