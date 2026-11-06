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
    [SerializeField, Min(0f), Tooltip("Attraction Pointを中心とした吸引範囲。")]
    private float attractionRadius = 6f;
    [SerializeField, Min(0f), Tooltip("既存マグネットと同じ吸引力。0なら吸引しません。")]
    private float pullStrength = 15f;
    [SerializeField, Min(0f), Tooltip("ギミックのマグネットと同じ鉄球の最高速度。")]
    private float maxPullSpeed = 6f;
    [SerializeField, Min(0f), Tooltip("この距離まで鉄球が近づくと、磁石と同じ方法で吸着します。")]
    private float attachDistance = 0.8f;
    [SerializeField, Min(0f)] private float chargeDuration = 0.7f;
    [SerializeField, Min(0f)] private float attractionDuration = 1.5f;
    [SerializeField, Min(0f)] private float attackCooldown = 2f;
    [Header("References")]
    [SerializeField] private Transform attractionPoint;
    [SerializeField] private Transform visualRoot;
    [SerializeField] private Animator visualAnimator;
    [SerializeField] private SpriteRenderer visualRenderer;
    [SerializeField] private Texture2D attackTexture;
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
    private MagnetPoint magnet;
    private MaterialPropertyBlock colorProperties;
    private Texture lastVisualTexture;
    private static readonly int Walk = Animator.StringToHash("Walk");
    private static readonly int Charge = Animator.StringToHash("Charge");
    private static readonly int PaletteGain = Shader.PropertyToID("_PaletteGain");
    private static readonly Vector4 AttackPaletteGain = new Vector4(1.28f, 1.50f, 1.75f, 1f);

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        bodyCollider = GetComponent<BoxCollider2D>();
        health = GetComponent<EnemyHealth>();
        terrainFilter = new ContactFilter2D { useTriggers = false };
        terrainFilter.SetLayerMask(terrainLayers);
        if (attractionPoint != null)
        {
            attractionOffset = attractionPoint.localPosition;
            magnet = attractionPoint.GetComponent<MagnetPoint>();
        }
        if (visualRenderer == null && visualRoot != null) visualRenderer = visualRoot.GetComponent<SpriteRenderer>();
        colorProperties = new MaterialPropertyBlock();
        health.HitConfirmed += OnHitConfirmed;
        FindPlayer();
    }

    private void OnEnable()
    {
        direction = startFacingRight ? 1 : -1;
        SetState(ActionState.Patrol);
        Face();
    }

    private void OnDisable()
    {
        StopMagnet();
        lastVisualTexture = null;
        if (visualAnimator != null) visualAnimator.speed = 0f;
    }
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

    private void LateUpdate()
    {
        if (visualRenderer == null || visualRenderer.sprite == null) return;
        Texture current = visualRenderer.sprite.texture;
        if (current == lastVisualTexture) return;
        lastVisualTexture = current;
        visualRenderer.GetPropertyBlock(colorProperties);
        colorProperties.SetVector(PaletteGain, current == attackTexture ? AttackPaletteGain : Vector4.one);
        visualRenderer.SetPropertyBlock(colorProperties);
    }

    private bool TargetInRange()
    {
        float rangeSquared = detectionRange * detectionRange;
        Vector2 origin = body.position;
        return ((Vector2)player.position - origin).sqrMagnitude <= rangeSquared
            || (BallAvailable() && (launcher.BallBody.position - origin).sqrMagnitude <= rangeSquared);
    }

    private void PullBall()
    {
        if (BallAvailable() && magnet != null)
            magnet.AttractControlledBall(launcher.BallBody, launcher, attractionRadius,
                pullStrength, maxPullSpeed, attachDistance);
    }

    private void StopMagnet()
    {
        if (magnet != null)
            magnet.StopControlledAttraction(launcher, launcher != null ? launcher.BallBody : null);
    }

    private void SetState(ActionState next)
    {
        if (state == ActionState.Attract && next != ActionState.Attract) StopMagnet();
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
