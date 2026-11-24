using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D), typeof(EnemyHealth))]
public sealed class Funkorogashi : MonoBehaviour, IEnemyMorningStarDamageModifier
{
    private enum State { Approach, BombWindup, KickRecovery, Flee, Dead }
    private enum Pose { Walk = -1, Brace, Load, Kick, Recoil, Settle }

    private static readonly int WalkHash = Animator.StringToHash("Base Layer.Walk");
    private static readonly int ActionsHash = Animator.StringToHash("Base Layer.Actions");
    private const int ActionClipFrameCount = 8;
    private const float KickRecoveryTime = 0.24f;
    private const float GroundClearance = 0.015f;

    [Header("Movement")]
    [SerializeField, Min(0f)] private float moveSpeed = 1.2f;
    [FormerlySerializedAs("unarmedMoveSpeed")]
    [SerializeField, Min(0f)] private float fleeSpeed = 2.6f;
    [SerializeField] private bool startFacingRight;
    [SerializeField] private LayerMask terrainLayers = (1 << 0) | (1 << 6);
    [SerializeField, Min(0.01f), Tooltip("歩いて越えられる足元の段差の高さ。")]
    private float groundDetectionDepth = 0.15f;

    [Header("Bomb")]
    [SerializeField, Min(0f), Tooltip("プレイヤーを狙って爆弾を構える距離。")]
    private float attackRange = 10f;
    [SerializeField, Min(0f)] private float windupDuration = 0.6f;
    [SerializeField] private Vector2 bombHoldOffset = new Vector2(0.85f, 0.35f);
    [FormerlySerializedAs("bombLaunchSpeedX")]
    [SerializeField, Min(0.1f), Tooltip("狙いまでの飛行時間を決める基準速度。")]
    private float bombThrowSpeed = 8f;
    [SerializeField] private FunkorogashiBomb bombPrefab;
    [SerializeField] private AudioClip kickClip;

    [Header("Visual")]
    [SerializeField] private Transform visualRoot;
    [SerializeField] private Animator visualAnimator;
    [SerializeField, Min(0f)] private float walkAnimationSpeed = 1f;
    [SerializeField] private EnemySpriteEffect defeatMotionPrefab;

    private readonly RaycastHit2D[] hits = new RaycastHit2D[16];
    private Rigidbody2D body;
    private BoxCollider2D bodyCollider;
    private EnemyHealth health;
    private ContactFilter2D terrainFilter;
    private Transform player;
    private Collider2D playerBody;
    private PlayerHealth playerHealth;
    private FunkorogashiBomb heldBomb;
    private State state;
    private float stateElapsed;
    private int facing;
    private int moveDirection;
    private Vector2 throwTarget;
    private Vector3 visualBaseScale;
    private Vector3 visualBasePosition;
    private Quaternion visualBaseRotation;
    private SpriteRenderer visualRenderer;
    private Color visualBaseColor;
    private Pose displayedPose;
    private bool poseInitialized;

    private bool IsAlive => health.CurrentHp > 0 && !health.IsDeathHandled;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        bodyCollider = GetComponent<BoxCollider2D>();
        health = GetComponent<EnemyHealth>();
        terrainFilter = new ContactFilter2D { useTriggers = false };
        terrainFilter.SetLayerMask(terrainLayers);

        if (visualRoot != null)
        {
            visualBaseScale = visualRoot.localScale;
            visualBasePosition = visualRoot.localPosition;
            visualBaseRotation = visualRoot.localRotation;
            visualRenderer = visualRoot.GetComponent<SpriteRenderer>();
            if (visualRenderer != null) visualBaseColor = visualRenderer.color;
        }

        health.HitConfirmed += OnHitConfirmed;
        health.HealthReset += OnHealthReset;
    }

    private void OnEnable()
    {
        facing = startFacingRight ? 1 : -1;
        moveDirection = facing;
        poseInitialized = false;
        SetState(heldBomb != null ? State.Approach : State.Flee);
        Face();
        SetVisual(0f);
    }

    private void Start()
    {
        // 同じ個体では最初に持つ一個だけ。状態を戻しても補充しない。
        if (bombPrefab == null || !IsAlive) return;
        heldBomb = Instantiate(bombPrefab, transform.position, Quaternion.identity);
        heldBomb.transform.SetParent(transform, true);
        moveDirection = -facing;
        SetState(State.Approach);
        Face();
    }

    private void OnDisable()
    {
        if (heldBomb != null)
        {
            FunkorogashiBomb bomb = heldBomb;
            heldBomb = null;
            if (!IsAlive && bomb.IsHeld)
                bomb.Launch(body.linearVelocity, bodyCollider);
            else
                Destroy(bomb.gameObject);
        }

        if (visualAnimator != null) visualAnimator.speed = 0f;
    }

    private void OnDestroy()
    {
        if (health == null) return;
        health.HitConfirmed -= OnHitConfirmed;
        health.HealthReset -= OnHealthReset;
    }

    private void OnHealthReset()
    {
        SetState(heldBomb != null ? State.Approach : State.Flee);
        if (heldBomb != null) heldBomb.Hold();
        SetVisual(0f);
    }

    private void FindPlayer()
    {
        GameObject target = GameObject.FindGameObjectWithTag(PlayerColliderUtility.PlayerTag);
        if (target == null) return;
        player = target.transform;
        playerHealth = target.GetComponent<PlayerHealth>();
        playerBody = null;
        foreach (Collider2D candidate in target.GetComponentsInChildren<Collider2D>())
        {
            if (!candidate.isActiveAndEnabled || candidate.isTrigger || !PlayerColliderUtility.IsPlayerBody(candidate))
                continue;

            playerBody = candidate;
            break;
        }
    }

    private void FixedUpdate()
    {
        if (!IsAlive) return;
        if (health.IsHitStunned)
        {
            SetVisual(0f);
            return;
        }

        if (player == null || !player.gameObject.activeInHierarchy) FindPlayer();
        bool targetAvailable = player != null && player.gameObject.activeInHierarchy &&
            (playerHealth == null || (!playerHealth.IsDead && !playerHealth.IsRespawning));
        Vector2 targetPosition = targetAvailable ? GetTargetPoint() : Vector2.zero;
        bool grounded = IsGrounded();
        stateElapsed += Time.fixedDeltaTime;

        float speed = 0f;
        switch (state)
        {
            case State.Approach:
                if (heldBomb == null) SetState(State.Flee);
                else if (grounded) speed = UpdateApproach(targetAvailable, targetPosition);
                break;
            case State.BombWindup:
                UpdateWindup(targetAvailable && grounded, targetPosition);
                break;
            case State.KickRecovery:
                if (stateElapsed >= KickRecoveryTime) SetState(State.Flee);
                break;
            case State.Flee:
                if (grounded) speed = UpdateFlee(targetAvailable, targetPosition);
                break;
        }

        if (!IsAlive) return;
        SetHorizontalSpeed(speed);
        if (heldBomb != null) heldBomb.Roll(speed, Time.fixedDeltaTime);
        SetVisual(speed);
    }

    private Vector2 GetTargetPoint()
    {
        if (playerBody == null || !playerBody.isActiveAndEnabled) return player.position;
        Bounds bounds = playerBody.bounds;
        return bounds.center + Vector3.up * (bounds.extents.y * 0.35f);
    }

    private float UpdateApproach(bool targetAvailable, Vector2 target)
    {
        if (targetAvailable)
        {
            Vector2 difference = target - (Vector2)bodyCollider.bounds.center;
            moveDirection = DirectionFrom(difference.x, moveDirection);
            if (IsInThrowRange(target) && HasClearPath(target))
            {
                facing = -moveDirection;
                Face();
                throwTarget = target;
                SetState(State.BombWindup);
                return 0f;
            }

            if (Mathf.Abs(difference.x) < 0.12f) return 0f;
        }

        return WalkAlongGround(moveSpeed, !targetAvailable, pushingBomb: true);
    }

    private void UpdateWindup(bool canAttack, Vector2 target)
    {
        if (heldBomb == null)
        {
            SetState(State.Flee);
            return;
        }

        if (!canAttack)
        {
            SetState(State.Approach);
            return;
        }

        // 構えの前半は左右も追う。後半は狙いを見せ、ジャンプや切り返しで避けられる間を作る。
        if (stateElapsed < windupDuration * 0.6f)
        {
            if (!IsInThrowRange(target))
            {
                SetState(State.Approach);
                return;
            }

            throwTarget = target;
            moveDirection = DirectionFrom(target.x - body.position.x, moveDirection);
            facing = -moveDirection;
            Face();
        }

        heldBomb.SettleForThrow(Time.fixedDeltaTime);
        if (stateElapsed < windupDuration) return;
        if (!HasClearPath(throwTarget))
        {
            SetState(State.Approach);
            return;
        }

        FunkorogashiBomb bomb = heldBomb;
        if (bomb.TryDetonateHeldStrike()) return;
        heldBomb = null;
        bomb.LaunchAt(throwTarget, bombThrowSpeed, bodyCollider);
        moveDirection = facing;
        SetState(State.KickRecovery);
        OneShotAudioUtility.Play2D(OneShotAudioUtility.FindWorldImpactSource(), kickClip, 0.34f, bomb.transform.position);
    }

    private float UpdateFlee(bool targetAvailable, Vector2 target)
    {
        if (targetAvailable)
            moveDirection = DirectionFrom(body.position.x - target.x, moveDirection);

        return WalkAlongGround(fleeSpeed, !targetAvailable, pushingBomb: false);
    }

    private float WalkAlongGround(float speed, bool mayTurn, bool pushingBomb)
    {
        bool canWalk = TryGetWalkRise(moveDirection, speed, out float rise);
        if (!canWalk && mayTurn && TryGetWalkRise(-moveDirection, speed, out rise))
        {
            moveDirection = -moveDirection;
            canWalk = true;
        }

        facing = pushingBomb ? -moveDirection : moveDirection;
        Face();
        if (!canWalk) return 0f;
        if (rise > 0f) body.position += Vector2.up * rise;
        return moveDirection * speed;
    }

    private static int DirectionFrom(float offset, int fallback) =>
        Mathf.Abs(offset) < 0.12f ? fallback : offset > 0f ? 1 : -1;

    private bool IsInThrowRange(Vector2 target) =>
        (target - (Vector2)bodyCollider.bounds.center).sqrMagnitude <= attackRange * attackRange;

    private void SetHorizontalSpeed(float speed) => body.linearVelocity = new Vector2(speed, body.linearVelocity.y);

    private void SetState(State next)
    {
        state = next;
        stateElapsed = 0f;
    }

    private void Face()
    {
        if (visualRoot != null)
        {
            Vector3 scale = visualBaseScale;
            scale.x *= facing > 0 ? -1f : 1f;
            visualRoot.localScale = scale;
        }

        if (heldBomb != null)
            heldBomb.transform.localPosition = new Vector3(-facing * Mathf.Abs(bombHoldOffset.x), bombHoldOffset.y, 0f);
    }

    private void SetVisual(float speed)
    {
        Pose pose = Pose.Walk;
        if (state == State.BombWindup)
        {
            pose = stateElapsed < windupDuration * 0.4f ? Pose.Brace : Pose.Load;
        }
        else if (state == State.KickRecovery)
        {
            if (stateElapsed < 0.08f) pose = Pose.Kick;
            else if (stateElapsed < 0.16f) pose = Pose.Recoil;
            else pose = Pose.Settle;
        }

        bool moving = Mathf.Abs(speed) > 0.001f;
        bool fleeing = state == State.Flee && moving;
        if (visualRoot != null)
        {
            float shift = pose == Pose.Kick ? 0.13f : pose == Pose.Recoil ? 0.06f : 0f;
            visualRoot.localPosition = visualBasePosition + Vector3.right * (shift * facing);
            visualRoot.localRotation = fleeing
                ? visualBaseRotation * Quaternion.Euler(0f, 0f, -4f * facing)
                : visualBaseRotation;
        }

        if (visualRenderer != null)
        {
            float warning = state == State.BombWindup ? 0.29f * Mathf.InverseLerp(0f, windupDuration, stateElapsed) : 0f;
            visualRenderer.color = Color.Lerp(visualBaseColor, new Color(1f, 0.86f, 0.61f, visualBaseColor.a), warning);
        }

        if (visualAnimator == null || !visualAnimator.isActiveAndEnabled || visualAnimator.runtimeAnimatorController == null)
            return;

        bool walking = pose == Pose.Walk;
        if (!poseInitialized || displayedPose != pose)
        {
            if (walking && visualAnimator.HasState(0, WalkHash)) visualAnimator.Play(WalkHash, 0, 0f);
            else if (!walking && visualAnimator.HasState(0, ActionsHash))
                visualAnimator.Play(ActionsHash, 0, ((int)pose + 0.5f) / ActionClipFrameCount);
            displayedPose = pose;
            poseInitialized = true;
        }

        visualAnimator.speed = walking && moving ? walkAnimationSpeed * (fleeing ? 1.65f : 1f) : 0f;
    }

    private void OnHitConfirmed(MorningStarHitContext context, bool lethal)
    {
        if (!isActiveAndEnabled) return;
        if (lethal) PlayDefeatMotion();
        SetState(lethal ? State.Dead : heldBomb != null ? State.Approach : State.Flee);
        SetHorizontalSpeed(0f);
        SetVisual(0f);
    }

    private void PlayDefeatMotion()
    {
        if (defeatMotionPrefab == null || visualRoot == null) return;
        EnemySpriteEffect effect = Instantiate(defeatMotionPrefab, visualRoot.position, visualRoot.rotation);
        SceneManager.MoveGameObjectToScene(effect.gameObject, gameObject.scene);
        Vector3 scale = visualRoot.lossyScale;
        effect.transform.localScale = new Vector3(Mathf.Abs(scale.x) * (facing > 0 ? -1f : 1f), Mathf.Abs(scale.y), 1f);
    }

    public int ModifyMorningStarDamage(MorningStarHitContext context, int damage, int currentHp)
    {
        if (heldBomb == null || !heldBomb.IsHeld) return damage;
        FunkorogashiBomb bomb = heldBomb;
        heldBomb = null;
        bomb.Explode(health); // 本体の撃破は、この鉄球ヒットで確定する。
        return currentHp;
    }

    internal void OnHeldBombLost(FunkorogashiBomb bomb)
    {
        if (heldBomb != bomb) return;
        heldBomb = null;
        if (!isActiveAndEnabled || !IsAlive) return;
        moveDirection = facing;
        SetState(State.Flee);
        SetVisual(0f);
    }

    private bool HasClearPath(Vector2 target)
    {
        Vector2 origin = bodyCollider.bounds.center;
        Vector2 delta = target - origin;
        if (delta.sqrMagnitude < 0.0001f) return true;
        float distance = delta.magnitude;
        int count = gameObject.scene.GetPhysicsScene2D().Raycast(origin, delta / distance, distance, terrainFilter, hits);
        for (int i = 0; i < count; i++)
            if (IsTerrain(hits[i])) return false;

        return true;
    }

    private bool IsGrounded()
    {
        if (body.linearVelocity.y > 0.1f) return false;
        int count = bodyCollider.Cast(Vector2.down, terrainFilter, hits, 0.05f);
        for (int i = 0; i < count; i++)
            if (IsTerrain(hits[i]) && hits[i].normal.y > 0.5f) return true;

        return false;
    }

    private bool TryGetWalkRise(int direction, float speed, out float rise)
    {
        rise = 0f;
        if (speed <= 0f) return false;
        float step = speed * Time.fixedDeltaTime + GroundClearance;
        Bounds bounds = bodyCollider.bounds;
        PhysicsScene2D physics = gameObject.scene.GetPhysicsScene2D();
        Vector2 origin = new Vector2(bounds.center.x + direction * (bounds.extents.x + step),
            bounds.min.y + groundDetectionDepth + GroundClearance);
        int count = physics.Raycast(origin, Vector2.down, 2f * groundDetectionDepth + GroundClearance, terrainFilter, hits);
        RaycastHit2D support = default;
        for (int i = 0; i < count; i++)
        {
            if (!IsTerrain(hits[i]) || hits[i].distance <= 0f || hits[i].normal.y <= 0.5f) continue;
            support = hits[i];
            break;
        }
        if (support.collider == null) return false;

        Vector2 forward = Vector2.right * direction;
        count = bodyCollider.Cast(forward, terrainFilter, hits, step);
        for (int i = 0; i < count; i++)
        {
            if (!IsTerrain(hits[i]) || Vector2.Dot(hits[i].normal, forward) >= -0.5f) continue;

            // 床の継ぎ目の縦面を検出したときだけ、上がった位置に胴体が収まるか確かめる。
            float height = support.point.y - bounds.min.y;
            if (height <= 0f || height > groundDetectionDepth) return false;
            float lift = height + GroundClearance;
            int blocked = physics.BoxCast((Vector2)bounds.center + Vector2.up * lift, bounds.size, 0f,
                forward, step, terrainFilter, hits);
            for (int j = 0; j < blocked; j++)
                if (IsTerrain(hits[j])) return false;

            rise = lift;
            break;
        }

        return true;
    }

    private bool IsTerrain(RaycastHit2D hit) =>
        hit.collider != null && hit.collider.attachedRigidbody != body && !hit.collider.transform.IsChildOf(transform);
}
