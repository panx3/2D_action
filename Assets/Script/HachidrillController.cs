using UnityEngine;

/// <summary>空中巡回と表示のみを制御する。HP・被弾Impulse・撃破はEnemyHealthが所有する。</summary>
[DisallowMultipleComponent, SelectionBase]
[RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D), typeof(EnemyHealth))]
public sealed class HachidrillController : MonoBehaviour
{
    public enum InitialDirection { Left = -1, Right = 1 }

    [Header("Flight patrol")]
    [SerializeField, Min(0f)] private float moveSpeed = 1.2f;
    [SerializeField, Min(0f), Tooltip("初期配置位置から左右への距離（ワールド単位）。")]
    private float patrolHalfRange = 3f;
    [SerializeField] private InitialDirection startDirection = InitialDirection.Left;
    [SerializeField] private LayerMask terrainLayers = (1 << 0) | (1 << 6);
    [Header("Presentation (no collider movement)")]
    [SerializeField] private Transform visualRoot;
    [SerializeField] private SpriteRenderer visualRenderer;
    [SerializeField] private Animator visualAnimator;
    [SerializeField, Min(0f), Tooltip("VisualRootのローカル単位。初期値は原画の約1～2ピクセル。")]
    private float hoverAmplitude = 0.03f;
    [SerializeField, Min(0f), Tooltip("1秒あたりの上下往復回数（Hz）。")]
    private float hoverFrequency = 0.4f;
    [SerializeField, Min(0f)] private float animationFPS = 6f;
    [Header("Body contact")]
    [SerializeField] private bool enableContactDamage = true;
    [SerializeField, Min(0)] private int contactDamage = 1;
    [SerializeField, Min(0f)] private float contactKnockback = 5f;
    [SerializeField, Min(0.02f)] private float contactCooldown = 0.7f;

    private const float Skin = 0.02f;
    private readonly RaycastHit2D[] hits = new RaycastHit2D[24];
    private Rigidbody2D body;
    private BoxCollider2D bodyCollider;
    private EnemyHealth health;
    private Vector2 patrolCenter;
    private Vector3 visualRest;
    private int direction;
    private float hoverTime, nextContact;

    // 将来の攻撃制御は巡回を一時停止できる。HPや接触判定には介入しない。
    public bool PatrolPaused { get; set; }
    public Vector2 PatrolCenter => patrolCenter;
    public int Facing => direction;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        bodyCollider = GetComponent<BoxCollider2D>();
        health = GetComponent<EnemyHealth>();
        patrolCenter = body.position;
        visualRest = visualRoot != null ? visualRoot.localPosition : Vector3.zero;
        health.HitConfirmed += OnHit;
        health.HealthReset += ResetPatrol;
    }

    private void OnEnable() => ResetPatrol();

    private void ResetPatrol()
    {
        direction = startDirection == InitialDirection.Right ? 1 : -1;
        hoverTime = nextContact = 0f;
        PatrolPaused = false;
    }

    private void OnDisable()
    {
        if (body != null) body.linearVelocity = Vector2.zero;
        if (visualAnimator != null) visualAnimator.speed = 0f;
        if (visualRoot != null) visualRoot.localPosition = visualRest;
    }

    private void OnDestroy()
    {
        if (health == null) return;
        health.HitConfirmed -= OnHit;
        health.HealthReset -= ResetPatrol;
    }

    private void OnHit(MorningStarHitContext context, bool lethal)
    {
        // EnemyHealthがImpulseを加える前に巡回速度を解除する。
        body.linearVelocity = Vector2.zero;
    }

    private void FixedUpdate()
    {
        if (health.CurrentHp <= 0 || health.IsDeathHandled) return;
        // 被弾硬直中はEnemyHealthだけがノックバック速度を扱う。
        if (health.IsHitStunned) return;
        if (PatrolPaused || patrolHalfRange <= 0f || moveSpeed <= 0f)
        {
            body.linearVelocity = Vector2.zero;
            return;
        }

        float left = patrolCenter.x - patrolHalfRange;
        float right = patrolCenter.x + patrolHalfRange;
        if (body.position.x >= right - 0.0001f) direction = -1;
        else if (body.position.x <= left + 0.0001f) direction = 1;

        // 地形は既存敵と同じLayer。1ステップ先まで掃引し、高速設定でも壁を跨がない。
        var filter = new ContactFilter2D { useTriggers = false };
        filter.SetLayerMask(terrainLayers);
        float step = moveSpeed * Time.fixedDeltaTime;
        if (FreeDistance(direction, step, filter) <= 0.0001f)
            direction = -direction;
        float rangeRemaining = direction > 0 ? right - body.position.x : body.position.x - left;
        float distance = Mathf.Min(step, Mathf.Max(0f, rangeRemaining), FreeDistance(direction, step, filter));
        body.linearVelocity = new Vector2(direction * distance / Time.fixedDeltaTime, 0f);
    }

    private float FreeDistance(int facing, float step, ContactFilter2D filter)
    {
        Vector2 forward = Vector2.right * facing;
        int count = bodyCollider.Cast(forward, filter, hits, step + Skin);
        float free = step;
        for (int i = 0; i < count; i++)
        {
            var hit = hits[i];
            if (hit.collider == null || hit.collider.attachedRigidbody == body
                || hit.collider.transform.IsChildOf(transform) || Vector2.Dot(hit.normal, forward) > -0.5f) continue;
            free = Mathf.Min(free, Mathf.Max(0f, hit.distance - Skin));
        }
        return free;
    }

    private void LateUpdate()
    {
        if (health.CurrentHp <= 0) return;
        if (visualRenderer != null) visualRenderer.flipX = direction > 0;
        if (visualAnimator != null) visualAnimator.speed = animationFPS / 6f;
        hoverTime += Time.deltaTime;
        if (visualRoot == null) return;
        float offset = Mathf.Sin(hoverTime * hoverFrequency * Mathf.PI * 2f) * hoverAmplitude;
        if (visualRenderer != null && visualRenderer.sprite != null)
        {
            // 原画ピクセルの整数倍に揃える。AnimatorはSpriteだけを差し替える。
            float pixel = Mathf.Abs(visualRenderer.transform.localScale.y) / visualRenderer.sprite.pixelsPerUnit;
            if (pixel > 0f) offset = Mathf.Round(offset / pixel) * pixel;
        }
        visualRoot.localPosition = visualRest + Vector3.up * offset;
    }

    private void OnCollisionEnter2D(Collision2D collision) => TryContactDamage(collision.collider);
    private void OnCollisionStay2D(Collision2D collision) => TryContactDamage(collision.collider);

    private void TryContactDamage(Collider2D other)
    {
        if (!isActiveAndEnabled || !enableContactDamage || contactDamage <= 0 || health.CurrentHp <= 0
            || health.IsDeathHandled || health.IsHitStunned || Time.time < nextContact
            || !PlayerColliderUtility.IsPlayerBody(other)) return;
        var player = other.GetComponentInParent<PlayerHealth>();
        if (player == null || player.IsDead || player.IsRespawning || player.IsInvincible) return;
        float away = other.bounds.center.x >= bodyCollider.bounds.center.x ? 1f : -1f;
        int before = player.CurrentHp;
        player.TakeDamage(contactDamage, new Vector2(away, 0.2f).normalized * contactKnockback);
        if (player.CurrentHp < before) nextContact = Time.time + Mathf.Max(0.02f, contactCooldown);
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 center = Application.isPlaying ? (Vector3)patrolCenter : transform.position;
        float half = Mathf.Max(0f, patrolHalfRange);
        Vector3 left = center + Vector3.left * half, right = center + Vector3.right * half;
        Gizmos.color = new Color(0.3f, 0.85f, 1f);
        Gizmos.DrawLine(left, right);
        Gizmos.DrawLine(left + Vector3.down * 0.2f, left + Vector3.up * 0.2f);
        Gizmos.DrawLine(right + Vector3.down * 0.2f, right + Vector3.up * 0.2f);
        Gizmos.DrawWireSphere(center, 0.06f);
    }
}
