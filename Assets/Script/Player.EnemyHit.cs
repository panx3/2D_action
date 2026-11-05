using UnityEngine;

public partial class Player
{
    [Header("Enemy hit reaction")]
    [SerializeField, Min(0f), Tooltip("敵から被弾した後に操作を受け付けない時間（秒）。")]
    private float enemyHitControlLockDuration = 0.6f;

    private float enemyHitStartedAt;
    private float enemyHitUntil;
    private bool enemyHitGrounded;
    private PlayerEnemyHitSparks enemyHitSparks;

    public bool IsEnemyHitStunned => isActiveAndEnabled && Time.time < enemyHitUntil;
    public float EnemyHitElapsed => Mathf.Max(0f, Time.time - enemyHitStartedAt);
    public bool EnemyHitGrounded => enemyHitGrounded;

    /// <summary>確定した敵被弾だけに適用。物理ImpulseはPlayerHealthの既存経路が所有する。</summary>
    public void BeginEnemyHitReaction(Vector2 impulse)
    {
        if (!isActiveAndEnabled || !InputEnabled || _startIntroControlled || _goalInputBlocked || _gimmickInputBlocked
            || IsElectricStunned || IsItemDrinking || (_playerHealth != null && (_playerHealth.IsDead || _playerHealth.IsRespawning))) return;

        enemyHitStartedAt = Time.time;
        enemyHitUntil = Time.time + Mathf.Max(0f, enemyHitControlLockDuration);
        enemyHitGrounded = IsGrounded;
        _moveInput = _actionMoveInput = Vector2.zero;
        _jumpHeld = false;
        _jumpBufferTimer = _coyoteTimer = 0f;
        _trampolineJumpUntil = _lastJumpPressedAt = float.NegativeInfinity;
        StopFootstepAudio();
        if (_morningStarLauncher != null) _morningStarLauncher.BeginEnemyHitReaction();
        if (enemyHitSparks == null) enemyHitSparks = gameObject.AddComponent<PlayerEnemyHitSparks>();
        enemyHitSparks.Play(impulse);
    }

    public void CancelEnemyHitReaction()
    {
        enemyHitUntil = 0f;
        if (enemyHitSparks != null) enemyHitSparks.Clear();
    }
}

/// <summary>小さな衝撃線。描画のみで、PlayerのTransformやColliderには触れない。</summary>
internal sealed class PlayerEnemyHitSparks : MonoBehaviour
{
    private readonly SpriteRenderer[] sparks = new SpriteRenderer[4];
    private Sprite sprite;
    private SpriteRenderer body;
    private float startedAt = float.NegativeInfinity;
    private float side = 1f;
    private const float Duration = 0.16f;

    private void Awake()
    {
        body = GetComponent<SpriteRenderer>();
        if (body == null) body = GetComponentInChildren<SpriteRenderer>();
        sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(.5f, .5f), 32f);
        for (int i = 0; i < sparks.Length; i++)
        {
            var child = new GameObject("Enemy hit spark");
            child.transform.SetParent(transform, false);
            child.layer = gameObject.layer;
            sparks[i] = child.AddComponent<SpriteRenderer>();
            sparks[i].sprite = sprite;
            sparks[i].enabled = false;
        }
    }

    public void Play(Vector2 impulse)
    {
        startedAt = Time.time;
        side = impulse.x < 0f ? -1f : 1f;
    }

    public void Clear()
    {
        startedAt = float.NegativeInfinity;
        foreach (var spark in sparks) if (spark != null) spark.enabled = false;
    }

    private void LateUpdate()
    {
        float t = (Time.time - startedAt) / Duration;
        if (t >= 1f) { Clear(); return; }
        for (int i = 0; i < sparks.Length; i++)
        {
            var spark = sparks[i];
            float angle = (120f + i * 40f) * Mathf.Deg2Rad;
            Vector3 direction = new Vector3(Mathf.Cos(angle) * side, Mathf.Sin(angle), 0f);
            spark.transform.localPosition = new Vector3(-side * .3f, .32f, 0f) + direction * (.24f + .16f * t);
            spark.transform.localRotation = Quaternion.Euler(0f, 0f, angle * Mathf.Rad2Deg + 90f);
            spark.transform.localScale = new Vector3(1.5f, 3f, 1f);
            spark.color = new Color(1f, .82f, .88f, 1f - t);
            if (body != null) { spark.sortingLayerID = body.sortingLayerID; spark.sortingOrder = body.sortingOrder + 3; }
            spark.enabled = true;
        }
    }

    private void OnDisable() => Clear();
    private void OnDestroy() { if (sprite != null) Destroy(sprite); }
}
