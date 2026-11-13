using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D), typeof(EnemyHealth))]
public sealed class Funkorogashi : MonoBehaviour
{
    private enum State { Patrol, Windup, Cooldown, Dead }
    [Header("Movement")]
    [SerializeField, Min(0f)] private float moveSpeed = 1.2f;
    [SerializeField, HideInInspector] private float detectionRange = 6f; // Preserve existing serialized values; pursuit is unlimited.
    [SerializeField, Min(0f)] private float attackRange = 4f;
    [SerializeField] private bool startFacingRight;
    [SerializeField] private LayerMask terrainLayers = (1 << 0) | (1 << 6);
    [SerializeField, Min(0.01f)] private float groundDetectionDepth = 0.15f;
    [Header("Attack")]
    [SerializeField, Min(0f)] private float windupDuration = 0.6f;
    [SerializeField, Min(0f)] private float attackCooldown = 2f;
    [SerializeField, Min(0f), Tooltip("前の爆弾が爆発してから次を用意するまでの秒数。")] private float bombRespawnDelay = 1.5f;
    [SerializeField] private Vector2 bombHoldOffset = new Vector2(0.85f, 0.35f);
    [SerializeField, Min(0f)] private float bombLaunchSpeedX = 7f;
    [SerializeField, Min(0f)] private float bombLaunchSpeedY = 4f;
    [SerializeField, Min(0f)] private float bombRotationSpeed = 240f;
    [Header("Visual and Prefab")]
    [SerializeField] private FunkorogashiBomb bombPrefab;
    [SerializeField] private Transform visualRoot;
    [SerializeField] private Animator visualAnimator;
    [SerializeField, Min(0f)] private float walkAnimationSpeed = 1f;

    public float BombRotationSpeed => bombRotationSpeed;

    private readonly RaycastHit2D[] hits = new RaycastHit2D[16];
    private Rigidbody2D body;
    private BoxCollider2D bodyCollider;
    private EnemyHealth health;
    private ContactFilter2D terrainFilter;
    private Transform player;
    private PlayerHealth playerHealth;
    private FunkorogashiBomb heldBomb;
    private FunkorogashiBomb outstandingBomb;
    private float nextBombReadyTime = float.NegativeInfinity;
    private State state;
    private float stateElapsed;
    private int facing;
    private int moveDirection;
    private int throwDirection;
    private Vector3 visualBasePosition;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        bodyCollider = GetComponent<BoxCollider2D>();
        health = GetComponent<EnemyHealth>();
        terrainFilter = new ContactFilter2D { useTriggers = false };
        terrainFilter.SetLayerMask(terrainLayers);
        if (visualRoot != null) visualBasePosition = visualRoot.localPosition;
        health.HitConfirmed += OnHitConfirmed;
        health.HealthReset += OnHealthReset;
    }

    private void OnEnable()
    {
        facing = startFacingRight ? 1 : -1;
        moveDirection = -facing;
        state = heldBomb == null && (outstandingBomb != null || Time.time < nextBombReadyTime)
            ? State.Cooldown : State.Patrol;
        stateElapsed = 0f;
        Face();
    }

    private void Start() { PrepareBomb(); }
    private void OnDestroy()
    {
        if (health != null) { health.HitConfirmed -= OnHitConfirmed; health.HealthReset -= OnHealthReset; }
        if (outstandingBomb != null) outstandingBomb.Exploded -= OnBombExploded;
    }
    private void OnDisable()
    {
        if (heldBomb != null)
        {
            var bomb = heldBomb;
            heldBomb = null;
            if (health != null && health.CurrentHp <= 0 && bomb.IsHeld)
                bomb.Launch(body != null ? body.linearVelocity : Vector2.zero, bombRotationSpeed, bodyCollider);
            else
            {
                if (outstandingBomb == bomb)
                {
                    bomb.Exploded -= OnBombExploded;
                    outstandingBomb = null;
                }
                Destroy(bomb.gameObject);
            }
        }
        if (visualAnimator != null) visualAnimator.speed = 0f;
    }

    private void OnHealthReset()
    {
        state = heldBomb == null && (outstandingBomb != null || Time.time < nextBombReadyTime)
            ? State.Cooldown : State.Patrol;
        stateElapsed = 0f;
        if (heldBomb == null) PrepareBomb();
    }

    private void FindPlayer()
    {
        var target = GameObject.FindGameObjectWithTag("Player");
        if (target == null) return;
        player = target.transform;
        playerHealth = target.GetComponent<PlayerHealth>();
    }

    private void FixedUpdate()
    {
        if (health.IsDeathHandled || health.CurrentHp <= 0) return;
        if (health.IsHitStunned) { SetVisual(0f); return; }
        if (player == null) FindPlayer();
        stateElapsed += Time.fixedDeltaTime;
        bool targetAvailable = player != null && player.gameObject.activeInHierarchy &&
            (playerHealth == null || (!playerHealth.IsDead && !playerHealth.IsRespawning));
        float distance = targetAvailable ? Vector2.Distance(body.position, player.position) : float.PositiveInfinity;
        float speed = 0f;
        switch (state)
        {
            case State.Patrol:
                if (heldBomb != null && targetAvailable && distance <= attackRange && IsGrounded())
                {
                    throwDirection = player.position.x >= body.position.x ? 1 : -1;
                    facing = -throwDirection; // The rear legs and held bomb face the target.
                    Face();
                    SetState(State.Windup);
                }
                else if (IsGrounded())
                {
                    if (targetAvailable && distance > attackRange)
                    {
                        int toward = player.position.x >= body.position.x ? 1 : -1;
                        moveDirection = toward;
                    }
                    else if (!CanWalk(moveDirection) && CanWalk(-moveDirection)) moveDirection = -moveDirection;
                    facing = -moveDirection; // Walk backwards, pushing the bomb with the rear legs.
                    Face();
                    if (CanWalk(moveDirection)) speed = moveDirection * moveSpeed;
                }
                break;
            case State.Windup:
                if (!targetAvailable) SetState(State.Patrol);
                else if (stateElapsed >= windupDuration)
                {
                    if (heldBomb != null)
                    {
                        heldBomb.Launch(new Vector2(throwDirection * bombLaunchSpeedX, bombLaunchSpeedY), bombRotationSpeed, bodyCollider);
                        heldBomb = null;
                    }
                    SetState(State.Cooldown);
                }
                break;
            case State.Cooldown:
                if (heldBomb == null && outstandingBomb == null && Time.time >= nextBombReadyTime) PrepareBomb();
                if (heldBomb != null && stateElapsed >= attackCooldown) SetState(State.Patrol);
                break;
        }
        body.linearVelocity = new Vector2(speed, body.linearVelocity.y);
        if (heldBomb != null) heldBomb.Roll(speed, bombRotationSpeed, Time.fixedDeltaTime);
        SetVisual(speed);
    }

    private void PrepareBomb()
    {
        if (heldBomb != null || outstandingBomb != null || Time.time < nextBombReadyTime || bombPrefab == null || health.CurrentHp <= 0) return;
        heldBomb = Instantiate(bombPrefab, transform.position, Quaternion.identity);
        outstandingBomb = heldBomb;
        outstandingBomb.Exploded += OnBombExploded;
        heldBomb.transform.SetParent(transform, true);
        heldBomb.Hold();
        PositionBomb();
    }

    private void Face()
    {
        if (visualRoot != null) visualRoot.localScale = new Vector3(facing > 0 ? -1f : 1f, 1f, 1f);
        PositionBomb();
    }

    private void PositionBomb()
    {
        if (heldBomb != null)
            heldBomb.transform.localPosition = new Vector3(-facing * Mathf.Abs(bombHoldOffset.x), bombHoldOffset.y, 0f);
    }

    private void SetVisual(float speed)
    {
        if (visualAnimator != null) visualAnimator.speed = state == State.Patrol && Mathf.Abs(speed) > 0.001f ? walkAnimationSpeed : 0f;
        if (visualRoot != null)
        {
            float windup = state == State.Windup && windupDuration > 0f ? Mathf.Clamp01(stateElapsed / windupDuration) : 0f;
            visualRoot.localPosition = visualBasePosition + Vector3.right * facing * (0.09f * windup);
            visualRoot.localRotation = Quaternion.Euler(0f, 0f, -facing * 8f * windup);
        }
    }

    private void SetState(State next) { state = next; stateElapsed = 0f; }
    private void OnHitConfirmed(MorningStarHitContext context, bool lethal)
    {
        SetState(lethal ? State.Dead : State.Cooldown);
        SetVisual(0f);
    }

    private void OnBombExploded(FunkorogashiBomb bomb)
    {
        if (outstandingBomb != bomb) return;
        bomb.Exploded -= OnBombExploded;
        outstandingBomb = null;
        nextBombReadyTime = Time.time + Mathf.Max(0f, bombRespawnDelay);
        if (heldBomb == null && state != State.Cooldown && health != null && health.CurrentHp > 0 && !health.IsDeathHandled)
            SetState(State.Cooldown);
    }

    public void OnHeldBombReleased(FunkorogashiBomb bomb)
    {
        if (heldBomb != bomb) return;
        heldBomb = null;
        if (health.CurrentHp > 0 && !health.IsDeathHandled) SetState(State.Cooldown);
    }

    private bool IsGrounded()
    {
        if (body.linearVelocity.y > 0.1f) return false;
        int count = bodyCollider.Cast(Vector2.down, terrainFilter, hits, 0.05f);
        for (int i = 0; i < count; i++) if (IsTerrain(hits[i]) && hits[i].normal.y > 0.5f) return true;
        return false;
    }

    private bool CanWalk(int direction)
    {
        float step = Mathf.Max(0.08f, moveSpeed * Time.fixedDeltaTime + 0.02f);
        Vector2 forward = Vector2.right * direction;
        int count = bodyCollider.Cast(forward, terrainFilter, hits, step);
        for (int i = 0; i < count; i++) if (IsTerrain(hits[i]) && Vector2.Dot(hits[i].normal, forward) < -0.5f) return false;
        Bounds b = bodyCollider.bounds;
        Vector2 origin = new Vector2(b.center.x + direction * (b.extents.x + step), b.min.y + 0.04f);
        count = gameObject.scene.GetPhysicsScene2D().Raycast(origin, Vector2.down, 0.04f + groundDetectionDepth, terrainFilter, hits);
        for (int i = 0; i < count; i++) if (IsTerrain(hits[i]) && hits[i].normal.y > 0.5f) return true;
        return false;
    }
    private bool IsTerrain(RaycastHit2D hit) => hit.collider != null && hit.collider.attachedRigidbody != body && !hit.collider.transform.IsChildOf(transform);
}
