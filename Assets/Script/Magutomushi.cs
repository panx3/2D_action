using UnityEngine;

/// <summary>地上を巡回し、一定時間だけ鉄球へ磁力を作用させる。</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D), typeof(EnemyHealth))]
public sealed class Magutomushi : MonoBehaviour
{
    private enum ActionState { Patrol, Charge, Attract, Cooldown }
    [Header("Patrol")]
    [SerializeField, Min(0f)] private float moveSpeed = 1.2f;
    [SerializeField] private bool startFacingRight;
    [SerializeField] private LayerMask terrainLayers = (1 << 0) | (1 << 6);
    [SerializeField, Min(0.01f)] private float groundDetectionDepth = 0.15f;
    [Header("Magnet Attack")]
    [SerializeField, Min(0f)] private float detectionRange = 5f;
    [SerializeField, Min(0f)] private float attractionRadius = 6f;
    [SerializeField, Min(0f), Tooltip("鉄球に加える加速度。質量の影響を受けません。")]
    private float pullStrength = 15f;
    [SerializeField, Min(0f), Tooltip("吸引方向に磁力が加速する上限。他の方向の速度は制限しません。")]
    private float maxPullSpeed = 6f;
    [SerializeField, Min(0f)] private float chargeDuration = 0.7f;
    [SerializeField, Min(0f)] private float attractionDuration = 1.5f;
    [SerializeField, Min(0f)] private float attackCooldown = 2f;
    [Header("References")]
    [SerializeField] private Transform attractionPoint;
    [SerializeField] private Transform visualRoot;
    [SerializeField] private Animator visualAnimator;
    [SerializeField, Min(0.01f)] private float chargeAnimationLength = 0.4f;

    private readonly RaycastHit2D[] hits = new RaycastHit2D[24];
    private Rigidbody2D body;
    private BoxCollider2D bodyCollider;
    private EnemyHealth health;
    private Transform player;
    private PlayerHealth playerHealth;
    private MorningStarLauncher launcher;
    private ContactFilter2D terrainFilter;
    private ActionState state;
    private float stateElapsed;
    private int direction;
    private Vector3 attractionOffset;
    private static readonly int Walk = Animator.StringToHash("Walk");
    private static readonly int Charge = Animator.StringToHash("Charge");

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        bodyCollider = GetComponent<BoxCollider2D>();
        health = GetComponent<EnemyHealth>();
        terrainFilter = new ContactFilter2D { useTriggers = false };
        terrainFilter.SetLayerMask(terrainLayers);
        if (attractionPoint != null) attractionOffset = attractionPoint.localPosition;
        health.HitConfirmed += OnHitConfirmed;
        FindPlayer();
    }

    private void OnEnable()
    {
        direction = startFacingRight ? 1 : -1;
        SetState(ActionState.Patrol);
        Face();
    }

    private void OnDisable() { if (visualAnimator != null) visualAnimator.speed = 0f; }
    private void OnDestroy() { if (health != null) health.HitConfirmed -= OnHitConfirmed; }

    private void FindPlayer()
    {
        GameObject target = GameObject.FindGameObjectWithTag("Player");
        if (target == null) return;
        player = target.transform;
        playerHealth = target.GetComponent<PlayerHealth>();
        launcher = target.GetComponent<MorningStarLauncher>();
    }

    private bool TargetAvailable => player != null && player.gameObject.activeInHierarchy
        && (playerHealth == null || (!playerHealth.IsDead && !playerHealth.IsRespawning));

    private void FixedUpdate()
    {
        if (health.IsDeathHandled || health.CurrentHp <= 0) return;
        if (health.IsHitStunned) return; // EnemyHealth owns knockback during hit stun.
        if (player == null) FindPlayer();
        stateElapsed += Time.fixedDeltaTime;
        float speed = 0f;
        switch (state)
        {
            case ActionState.Patrol:
                if (TargetAvailable && TargetInRange())
                {
                    Vector2 target = player.position;
                    if (BallAvailable() && (launcher.BallBody.position - body.position).sqrMagnitude
                        < (target - body.position).sqrMagnitude) target = launcher.BallBody.position;
                    direction = target.x >= body.position.x ? 1 : -1;
                    Face();
                    SetState(ActionState.Charge);
                }
                else if (IsGrounded())
                {
                    if (!CanWalk(direction) && CanWalk(-direction)) { direction = -direction; Face(); }
                    if (CanWalk(direction)) speed = direction * moveSpeed;
                }
                break;
            case ActionState.Charge:
                if (!TargetAvailable) SetState(ActionState.Cooldown);
                else if (stateElapsed >= chargeDuration) SetState(ActionState.Attract);
                break;
            case ActionState.Attract:
                if (!TargetAvailable || stateElapsed >= attractionDuration) SetState(ActionState.Cooldown);
                else PullBall();
                break;
            case ActionState.Cooldown:
                if (stateElapsed >= attackCooldown) SetState(ActionState.Patrol);
                break;
        }
        body.linearVelocity = new Vector2(speed, body.linearVelocity.y);
        if (state == ActionState.Patrol && visualAnimator != null)
            visualAnimator.speed = Mathf.Abs(speed) > 0.001f ? 1f : 0f;
    }

    private bool BallAvailable() => launcher != null && launcher.isActiveAndEnabled && launcher.BallBody != null
        && launcher.BallBody.gameObject.activeInHierarchy;

    private bool TargetInRange()
    {
        float rangeSquared = detectionRange * detectionRange;
        Vector2 origin = body.position;
        return ((Vector2)player.position - origin).sqrMagnitude <= rangeSquared
            || (BallAvailable() && (launcher.BallBody.position - origin).sqrMagnitude <= rangeSquared);
    }

    private void PullBall()
    {
        if (!BallAvailable() || attractionPoint == null) return;
        var ball = launcher.BallBody;
        var ballState = launcher.State;
        // Never compete with scripted recall, charging, hook/swing, intro, or respawn ownership.
        if (!ball.simulated || ball.bodyType != RigidbodyType2D.Dynamic
            || (ballState != MorningStarLauncher.MorningStarState.Dragging
                && ballState != MorningStarLauncher.MorningStarState.Dropping
                && ballState != MorningStarLauncher.MorningStarState.Thrown)) return;
        Vector2 delta = (Vector2)attractionPoint.position - ball.position;
        float distance = delta.magnitude;
        if (distance <= 0.001f || distance > attractionRadius) return;
        Vector2 toward = delta / distance;
        float along = Vector2.Dot(ball.linearVelocity, toward);
        float acceleration = Mathf.Min(pullStrength, Mathf.Max(0f, (maxPullSpeed - along) / Time.fixedDeltaTime));
        ball.AddForce(toward * (acceleration * ball.mass), ForceMode2D.Force);
    }

    private void SetState(ActionState next)
    {
        state = next;
        stateElapsed = 0f;
        if (visualAnimator == null) return;
        if (next == ActionState.Charge)
        {
            visualAnimator.Play(Charge, 0, 0f);
            visualAnimator.speed = chargeDuration > 0f ? chargeAnimationLength / chargeDuration : 1f;
        }
        else if (next == ActionState.Attract)
        {
            visualAnimator.Play(Charge, 0, 0.999f);
            visualAnimator.speed = 0f;
        }
        else
        {
            visualAnimator.Play(Walk, 0, 0f);
            visualAnimator.speed = 0f;
        }
    }

    private void OnHitConfirmed(MorningStarHitContext context, bool lethal)
    {
        if (!isActiveAndEnabled) return;
        if (state == ActionState.Patrol) body.linearVelocity = new Vector2(0f, body.linearVelocity.y);
        SetState(ActionState.Cooldown);
    }

    private void Face()
    {
        if (visualRoot != null) visualRoot.localScale = new Vector3(direction > 0 ? -1f : 1f, 1f, 1f);
        if (attractionPoint != null)
            attractionPoint.localPosition = new Vector3(Mathf.Abs(attractionOffset.x) * direction,
                attractionOffset.y, attractionOffset.z);
    }

    private bool IsGrounded()
    {
        if (body.linearVelocity.y > 0.1f) return false;
        int count = bodyCollider.Cast(Vector2.down, terrainFilter, hits, 0.05f);
        for (int i = 0; i < count; i++)
            if (IsTerrain(hits[i]) && hits[i].normal.y > 0.5f) return true;
        return false;
    }

    private bool CanWalk(int facing)
    {
        float step = moveSpeed * Time.fixedDeltaTime + 0.02f;
        Vector2 forward = Vector2.right * facing;
        int count = bodyCollider.Cast(forward, terrainFilter, hits, Mathf.Max(0.08f, step));
        for (int i = 0; i < count; i++)
            if (IsTerrain(hits[i]) && Vector2.Dot(hits[i].normal, forward) < -0.5f) return false;
        Bounds b = bodyCollider.bounds;
        Vector2 origin = new Vector2(b.center.x + facing * (b.extents.x + Mathf.Max(0.08f, step)), b.min.y + 0.04f);
        count = gameObject.scene.GetPhysicsScene2D().Raycast(origin, Vector2.down, 0.04f + groundDetectionDepth, terrainFilter, hits);
        for (int i = 0; i < count; i++)
            if (IsTerrain(hits[i]) && hits[i].normal.y > 0.5f) return true;
        return false;
    }

    private bool IsTerrain(RaycastHit2D hit) => hit.collider != null && hit.collider.attachedRigidbody != body
        && !hit.collider.transform.IsChildOf(transform);
}
