using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// MorningStarの命中だけを受け付け、段階的に破壊されてから既存Goal処理へ接続する。
/// Player接触ではGoalにならない。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer), typeof(Collider2D))]
public sealed class GoalPoint : MonoBehaviour, IMorningStarHitReceiver
{
    [Header("Crystal Damage")]
    [SerializeField, Min(1)] private int requiredHits = 3;
    [SerializeField, Min(0f)] private float hitCooldown = 0.15f;
    [SerializeField] private Sprite[] crystalStages;

    [Header("Fragments")]
    [SerializeField] private CrystalFragment fragmentPrefab;
    [SerializeField, Min(0)] private int fragmentsPerHit = 3;
    [SerializeField, Min(0)] private int fragmentsOnBreak = 12;
    [SerializeField, Min(0f)] private float minFragmentForce = 1.5f;
    [SerializeField, Min(0f)] private float maxFragmentForce = 4f;
    [SerializeField] private Vector2 fragmentSpawnSpread = new Vector2(0.25f, 0.45f);
    [SerializeField] private Vector2 fragmentLifetimeRange = new Vector2(1f, 2f);

    [Header("Goal Presentation")]
    [SerializeField, Min(0f)] private float goalPresentationDelay = 0.18f;
    [SerializeField] private CameraShake2D cameraShake;
    [SerializeField, Min(0f)] private float breakShakeDuration = 0.12f;
    [SerializeField, Min(0f)] private float breakShakeStrength = 0.06f;
    [SerializeField] private GoalMenuController goalMenu;
    [SerializeField] private UnityEvent onGoalReached;

    [Header("Debug")]
    [SerializeField] private bool showDebugLog;

    private SpriteRenderer _spriteRenderer;
    private Collider2D _hitCollider;
    private int _hitCount;
    private float _nextHitTime;
    private bool _isBroken;
    private bool _isCleared;

    public int HitCount => _hitCount;
    public int RequiredHits => requiredHits;
    public bool IsBroken => _isBroken;
    public bool IsCleared => _isCleared;

    private void Awake()
    {
        _spriteRenderer = GetComponent<SpriteRenderer>();
        _hitCollider = GetComponent<Collider2D>();

        if (goalMenu == null)
            goalMenu = FindAnyObjectByType<GoalMenuController>(FindObjectsInactive.Include);
        if (cameraShake == null)
            cameraShake = FindAnyObjectByType<CameraShake2D>(FindObjectsInactive.Exclude);

        requiredHits = Mathf.Max(1, requiredHits);
        ApplyCrystalStage(0);
    }

    public void OnMorningStarHit(MorningStarHitContext context)
    {
        if (_isBroken || context.Damage <= 0 || Time.unscaledTime < _nextHitTime)
            return;

        _nextHitTime = Time.unscaledTime + hitCooldown;
        _hitCount = Mathf.Min(requiredHits, _hitCount + 1);
        bool finalHit = _hitCount >= requiredHits;

        ApplyCrystalStage(_hitCount);
        SpawnFragments(context.ImpactPoint, context.ImpactDirection,
            finalHit ? fragmentsOnBreak : fragmentsPerHit);

        if (showDebugLog)
            Debug.Log($"[GoalPoint] Crystal hit {_hitCount}/{requiredHits}", this);

        if (!finalHit)
            return;

        _isBroken = true;
        if (_hitCollider != null)
            _hitCollider.enabled = false;

        if (cameraShake != null)
            cameraShake.Shake(breakShakeDuration, breakShakeStrength);

        StartCoroutine(CompleteGoalRoutine());
    }

    private IEnumerator CompleteGoalRoutine()
    {
        if (goalPresentationDelay > 0f)
            yield return new WaitForSecondsRealtime(goalPresentationDelay);

        ReachGoal();
    }

    private void ReachGoal()
    {
        if (_isCleared)
            return;

        _isCleared = true;
        if (showDebugLog)
            Debug.Log("[GoalPoint] Crystal broken. Stage Clear!", this);

        if (goalMenu != null)
            goalMenu.ShowGoal();
        else
            Debug.LogWarning("[GoalPoint] GoalMenuControllerがSceneにありません。", this);

        onGoalReached?.Invoke();
    }

    private void ApplyCrystalStage(int hitCount)
    {
        if (_spriteRenderer == null || crystalStages == null || crystalStages.Length == 0)
            return;

        int stageIndex = requiredHits > 0
            ? Mathf.RoundToInt((crystalStages.Length - 1) * Mathf.Clamp01(hitCount / (float)requiredHits))
            : 0;
        stageIndex = Mathf.Clamp(stageIndex, 0, crystalStages.Length - 1);
        if (crystalStages[stageIndex] != null)
            _spriteRenderer.sprite = crystalStages[stageIndex];
        _spriteRenderer.color = Color.white;
    }

    private void SpawnFragments(Vector2 impactPoint, Vector2 impactDirection, int count)
    {
        if (fragmentPrefab == null || count <= 0)
            return;

        Vector2 origin = impactPoint;
        if (!float.IsFinite(origin.x) || !float.IsFinite(origin.y))
            origin = transform.position;

        Vector2 directionalBias = impactDirection.sqrMagnitude > 0.0001f
            ? impactDirection.normalized * 0.35f
            : Vector2.up * 0.35f;

        for (int i = 0; i < count; i++)
        {
            Vector2 spread = new Vector2(
                Random.Range(-fragmentSpawnSpread.x, fragmentSpawnSpread.x),
                Random.Range(-fragmentSpawnSpread.y, fragmentSpawnSpread.y));
            CrystalFragment fragment = Instantiate(fragmentPrefab, origin + spread, Quaternion.identity);
            fragment.Initialize(Random.Range(fragmentLifetimeRange.x, fragmentLifetimeRange.y));

            Rigidbody2D body = fragment.GetComponent<Rigidbody2D>();
            if (body == null)
                continue;

            Vector2 randomDirection = Random.insideUnitCircle.normalized + directionalBias;
            if (randomDirection.y < 0.1f)
                randomDirection.y = Mathf.Abs(randomDirection.y) + 0.25f;
            body.AddForce(randomDirection.normalized * Random.Range(minFragmentForce, maxFragmentForce),
                ForceMode2D.Impulse);
            body.angularVelocity = Random.Range(-420f, 420f);
        }
    }
}
