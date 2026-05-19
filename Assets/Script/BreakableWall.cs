using UnityEngine;

/// <summary>
/// モーニングスターで破壊できる壁・オブジェクト。
/// </summary>
[DisallowMultipleComponent]
public class BreakableWall : MonoBehaviour, IMorningStarHitReceiver
{
    [SerializeField, Min(1)] private int hitPoints = 1;
    [SerializeField] private bool destroyOnBreak = true;
    [SerializeField] private GameObject breakEffectPrefab;

    private int _remainingHp;

    private void Awake()
    {
        _remainingHp = hitPoints;
    }

    public void OnMorningStarHit(MorningStarHitContext context)
    {
        if (_remainingHp <= 0)
            return;

        _remainingHp = Mathf.Max(0, _remainingHp - context.Damage);

        if (_remainingHp > 0)
            return;

        if (breakEffectPrefab != null)
            Instantiate(breakEffectPrefab, context.ImpactPoint, Quaternion.identity);

        if (destroyOnBreak)
            Destroy(gameObject);
        else
            gameObject.SetActive(false);
    }
}
