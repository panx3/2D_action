using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// MorningStarの命中だけを受け付け、段階的に破壊されてから既存Goal処理へ接続する。
/// Player接触ではGoalにならない。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
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

    [Header("Hit Feedback")]
    [SerializeField] private Transform visual;
    [SerializeField] private AudioSource crystalAudioSource;
    [SerializeField] private AudioClip crackClip;
    [SerializeField] private AudioClip shatterClip;
    [SerializeField, Range(0f, 1f)] private float crackVolume = 0.75f;
    [SerializeField, Range(0f, 1f)] private float shatterVolume = 0.9f;
    [SerializeField, Min(0f)] private float hitShakeDuration = 0.12f;
    [SerializeField, Min(0f)] private float hitShakeAmount = 0.065f;
    [SerializeField, Min(0f)] private float finalShakeDuration = 0.14f;
    [SerializeField, Range(1f, 2f)] private float finalShakeMultiplier = 1.3f;

    [Header("Goal Presentation")]
    [SerializeField, Min(0f)] private float goalPresentationDelay = 0.18f;
    [SerializeField] private CameraShake2D cameraShake;
    [SerializeField, Min(0f)] private float breakShakeDuration = 0.12f;
    [SerializeField, Min(0f)] private float breakShakeStrength = 0.06f;
    [SerializeField] private GoalMenuController goalMenu;
    [SerializeField] private CrystalAcquiredUI crystalAcquiredUI;
    [SerializeField] private UnityEvent onGoalReached;

    [Header("Debug")]
    [SerializeField] private bool showDebugLog;

    private SpriteRenderer _spriteRenderer;
    private Collider2D _hitCollider;
    private int _hitCount;
    private float _nextHitTime;
    private bool _isBroken;
    private bool _isCleared;
    private bool _goalSequenceStarted;
    private Vector3 _visualInitialLocalPosition;
    private Coroutine _visualShakeRoutine;

    public int HitCount => _hitCount;
    public int RequiredHits => requiredHits;
    public bool IsBroken => _isBroken;
    public bool IsCleared => _isCleared;
    public int CrackSoundPlayCount { get; private set; }
    public int ShatterSoundPlayCount { get; private set; }

    private void Awake()
    {
        if (visual == null)
        {
            _spriteRenderer = GetComponentInChildren<SpriteRenderer>(true);
            visual = _spriteRenderer != null ? _spriteRenderer.transform : null;
        }
        else
        {
            _spriteRenderer = visual.GetComponent<SpriteRenderer>();
        }

        if (visual != null)
            _visualInitialLocalPosition = visual.localPosition;

        _hitCollider = GetComponent<Collider2D>();
        if (crystalAudioSource == null)
            crystalAudioSource = GetComponent<AudioSource>();
        if (crystalAudioSource != null)
        {
            crystalAudioSource.playOnAwake = false;
            crystalAudioSource.loop = false;
            crystalAudioSource.spatialBlend = 0f;
        }

        if (goalMenu == null)
            goalMenu = FindAnyObjectByType<GoalMenuController>(FindObjectsInactive.Include);
        if (crystalAcquiredUI == null)
            crystalAcquiredUI = FindAnyObjectByType<CrystalAcquiredUI>(FindObjectsInactive.Include);
        if (cameraShake == null)
            cameraShake = FindAnyObjectByType<CameraShake2D>(FindObjectsInactive.Exclude);

        requiredHits = Mathf.Max(1, requiredHits);
        ApplyCrystalStage(0);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (_isBroken || !TryGetMorningStarBody(collision.collider, out Rigidbody2D morningStarBody))
            return;

        // BreakableWall と同じ実衝突経路を使う。
        // Launcher の combat LayerMask に含まれない GoalPoint でも、
        // morningstar Tag と衝突転送Componentを持つ鉄球だけを受け付ける。
        float impactSpeed = Mathf.Max(
            collision.relativeVelocity.magnitude,
            morningStarBody.linearVelocity.magnitude);
        Vector2 impactDirection = GetCollisionDirection(collision, morningStarBody);
        Vector2 impactPoint = collision.contactCount > 0
            ? collision.GetContact(0).point
            : morningStarBody.position;

        OnMorningStarHit(new MorningStarHitContext(
            1,
            Vector2.zero,
            0f,
            impactPoint,
            impactDirection,
            impactSpeed,
            1f));
    }

    public void OnMorningStarHit(MorningStarHitContext context)
    {
        if (_isBroken || context.Damage <= 0 || Time.unscaledTime < _nextHitTime)
            return;

        _nextHitTime = Time.unscaledTime + hitCooldown;
        _hitCount = Mathf.Min(requiredHits, _hitCount + 1);
        bool finalHit = _hitCount >= requiredHits;

        PlayHitFeedback(finalHit);
        ApplyCrystalStage(_hitCount);
        SpawnFragments(context.ImpactPoint, context.ImpactDirection,
            finalHit ? fragmentsOnBreak : fragmentsPerHit);

        if (!finalHit)
            return;

        _isBroken = true;
        if (_hitCollider != null)
            _hitCollider.enabled = false;

        if (cameraShake != null)
            cameraShake.Shake(breakShakeDuration, breakShakeStrength);

        BeginGoalSequence();
    }

    private void OnDisable()
    {
        if (_visualShakeRoutine != null)
        {
            StopCoroutine(_visualShakeRoutine);
            _visualShakeRoutine = null;
        }

        ResetVisualPosition();
    }

    private void PlayHitFeedback(bool finalHit)
    {
        AudioClip clip = finalHit ? shatterClip : crackClip;
        float volume = finalHit ? shatterVolume : crackVolume;
        if (crystalAudioSource != null && clip != null)
        {
            // 前のOneShotも止め、最終HitでCrackとShatterが重ならないようにする。
            crystalAudioSource.Stop();
            crystalAudioSource.PlayOneShot(clip, volume);
            if (finalHit)
                ShatterSoundPlayCount++;
            else
                CrackSoundPlayCount++;
        }

        StartVisualShake(finalHit);
    }

    private void StartVisualShake(bool finalHit)
    {
        if (visual == null)
            return;

        if (_visualShakeRoutine != null)
            StopCoroutine(_visualShakeRoutine);

        ResetVisualPosition();
        float duration = finalHit ? finalShakeDuration : hitShakeDuration;
        float amount = finalHit ? hitShakeAmount * finalShakeMultiplier : hitShakeAmount;
        _visualShakeRoutine = StartCoroutine(ShakeVisualRoutine(duration, amount));
    }

    private IEnumerator ShakeVisualRoutine(float duration, float amount)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float fade = duration > 0f ? 1f - Mathf.Clamp01(elapsed / duration) : 0f;
            float x = Random.Range(-amount, amount) * fade;
            float y = Random.Range(-amount * 0.25f, amount * 0.25f) * fade;
            visual.localPosition = _visualInitialLocalPosition + new Vector3(x, y, 0f);
            yield return null;
        }

        ResetVisualPosition();
        _visualShakeRoutine = null;
    }

    private void ResetVisualPosition()
    {
        if (visual != null)
            visual.localPosition = _visualInitialLocalPosition;
    }

    private static bool TryGetMorningStarBody(Collider2D other, out Rigidbody2D morningStarBody)
    {
        morningStarBody = other != null ? other.attachedRigidbody : null;
        if (morningStarBody == null)
            return false;

        return morningStarBody.CompareTag("morningstar")
            && morningStarBody.GetComponent<MorningStarCollisionReporter>() != null;
    }

    private static Vector2 GetCollisionDirection(Collision2D collision, Rigidbody2D morningStarBody)
    {
        if (collision.contactCount > 0)
        {
            Vector2 direction = -collision.GetContact(0).normal;
            if (direction.sqrMagnitude > 1e-6f)
                return direction.normalized;
        }

        if (morningStarBody.linearVelocity.sqrMagnitude > 1e-6f)
            return morningStarBody.linearVelocity.normalized;

        return Vector2.right;
    }

    private void BeginGoalSequence()
    {
        if (_goalSequenceStarted)
            return;

        _goalSequenceStarted = true;
        // 最終破壊が成立し、獲得演出へ進むことが確定した時点で一度だけ切り替える。
        GameBgmController.Instance?.PlayGoal();
        if (crystalAcquiredUI != null && crystalAcquiredUI.Play(ReachGoal))
            return;

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
