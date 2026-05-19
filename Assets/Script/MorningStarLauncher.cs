using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

/// <summary>
/// モーニングスター：状態管理・発射・回収・壁刺し・スイング引っ張り・鎖解除。
/// Joint2D は使わず ChainConstraint2D + 独自張力。
/// </summary>
public class MorningStarLauncher : MonoBehaviour
{
    public enum MorningStarState
    {
        Dragging,
        SpinCharging,
        RecallBeforeThrow,
        Thrown,
        Dropping,
        Returning,
        Hooked
    }

    [Header("参照")]
    [SerializeField] private Rigidbody2D morningStarRb;
    [SerializeField] private LineRenderer lineRenderer;
    [SerializeField] private ChainLineController chainLineController;
    [SerializeField] private LineRenderer aimGuideLineRenderer;
    [SerializeField] private Transform aimMark;
    [SerializeField] private Transform handAnchor;
    [SerializeField] private Transform throwSocket;
    [SerializeField] private Rigidbody2D playerRigidbody2D;
    [SerializeField] private bool usePlayerOnSameObject = true;
    [SerializeField] private Player player;
    [SerializeField] private ChainConstraint2D chainConstraint;
    [FormerlySerializedAs("mainCamera")]
    [SerializeField] private Camera aimCamera;
    [SerializeField] private bool restrictPointerToGameView = true;
    [SerializeField] private float screenZ = 10f;

    [Header("レイヤー")]
    [SerializeField] private LayerMask hookableLayers;
    [SerializeField] private LayerMask enemyLayers;
    [SerializeField] private LayerMask breakableLayers;

    [Header("Combat（命中ダメージ・ノックバック・ヒットストップ）")]
    [SerializeField] private float minCombatHitSpeed = 3f;
    [SerializeField] private int baseDamage = 1;
    [SerializeField] private float damagePerSpeed = 0.2f;
    [SerializeField] private int minDamage = 1;
    [SerializeField] private int maxDamage = 24;
    [SerializeField] private float baseKnockback = 2f;
    [SerializeField] private float knockbackPerSpeed = 0.45f;
    [SerializeField] private float maxKnockback = 14f;
    [SerializeField] private float hitStopPerDamage = 0.006f;
    [SerializeField] private float minCombatHitStop = 0.02f;
    [SerializeField] private float maxCombatHitStop = 0.1f;
    [SerializeField] private float hitCooldownPerTarget = 0.2f;
    [SerializeField, Range(0f, 1f)] private float postHitSpeedRetention = 0.7f;

    [Header("紐の長さ（Dragging / Thrown 時の ChainConstraint2D）")]
    [SerializeField] private float maxRopeLength = 4.5f;

    [Header("鉄球物理")]
    [SerializeField] private float ballMass = 0.35f;
    [SerializeField] private float maxBallLinearSpeed = 20f;
    [SerializeField] private float throwSpeed = 18f;

    [Header("クリック照準・発射")]
    [SerializeField] private float minAimDistance = 0.2f;
    [SerializeField] private float launchStartOffset = 0.25f;
    [SerializeField] private float aimLaunchCooldown = 0f;
    [SerializeField] private float fireInputBufferTime = 0.15f;

    [Header("RecallBeforeThrow（発射前の見える引き寄せ）")]
    [SerializeField] private bool useVisibleRecallBeforeThrow = true;
    [SerializeField] private float visibleRecallTime = 0.12f;
    [SerializeField] private float recallEasePower = 2f;
    [SerializeField] private float recallHoldTime = 0.04f;
    [SerializeField] private float recallStartDelay = 0f;
    [SerializeField] private bool disableChainConstraintDuringRecall = true;
    [SerializeField] private float chargedRecallTimeMultiplier = 0.6f;

    [Header("Thrown → Dropping")]
    [SerializeField] private float maxThrownTime = 0.45f;
    [SerializeField] private float maxThrowDistance = 4.8f;
    [SerializeField] private float dropTransitionSpeed = 2f;
    [SerializeField] private float dropToDraggingTime = 0.35f;
    [SerializeField] private bool enableChainConstraintWhileDropping = true;

    [Header("Returning（右クリック/Bのみ）")]
    [SerializeField] private float returnSpeed = 21f;
    [SerializeField] private float returnFinishDistance = 0.25f;
    [SerializeField] private bool disableChainConstraintDuringReturn = true;

    [Header("Hooked / Swing")]
    [SerializeField] private float hookMinSpeed = 2f;
    [SerializeField] private float pullForce = 32f;
    [SerializeField] private float radialVelocityDamping = 0.9f;
    [SerializeField, Range(0f, 1f)] private float tangentVelocityKeepRate = 0.95f;
    [SerializeField] private float swingInputForce = 10f;
    [SerializeField] private float maxSwingSpeed = 16f;
    [SerializeField] private float releaseBoost = 3f;
    [SerializeField] private bool disableChainConstraintWhileHooked = true;
    [SerializeField] private bool leftClickReThrowFromHook = true;
    [SerializeField] private float rehookLockoutTime = 0.15f;
    [SerializeField] private float hookStickMinTime = 0.1f;

    [Header("Hooked フィードバック")]
    [SerializeField] private float hookedChainWidthMultiplier = 1.25f;
    [SerializeField] private Color normalChainColor = Color.white;
    [SerializeField] private Color hookedChainColor = new Color(1f, 0.9f, 0.5f, 1f);
    [SerializeField] private float hookHitStopTime = 0.04f;
    [SerializeField] private GameObject hookSparkPrefab;

    [Header("SpinCharge")]
    [SerializeField] private float holdThreshold = 0.18f;
    [SerializeField] private float maxChargeTime = 1.2f;
    [SerializeField] private float spinRadius = 1.5f;
    [SerializeField] private float spinAngularSpeed = 720f;
    [SerializeField] private float minChargedThrowMultiplier = 1.2f;
    [SerializeField] private float maxChargedThrowMultiplier = 2.2f;
    [SerializeField] private Collider2D spinGuardCollider;

    [Header("発射時プレイヤー")]
    [SerializeField] private float launchPlayerPullImpulse = 0f;

    [Header("照準ガイド")]
    [SerializeField] private bool showAimGuide = true;
    [SerializeField] private float aimGuideMaxLength = 8f;

    [Header("Animator（任意）")]
    [SerializeField] private Animator animator;
    [SerializeField] private string backwardAimParam = "BackwardAim";
    [SerializeField] private string launchChargeParam = "LaunchCharge";
    [SerializeField] private string launchFireTrigger = "LaunchFire";
    [SerializeField] private string launchRecoilTrigger = "LaunchRecoil";
    [SerializeField] private float launchRecoilDelay = 0.08f;

    private Rigidbody2D _playerRb;
    private MorningStarState _state = MorningStarState.Dragging;
    private float _nextLaunchTime;
    private Vector2 _pendingLaunchDir;
    private Vector2 _recallStartPosition;
    private Vector2 _recallTargetPosition;
    private float _recallTimer;
    private float _recallHoldTimer;
    private float _recallDelayTimer;
    private float _activeRecallDuration;
    private float _pendingThrowSpeedMultiplier = 1f;
    private float _thrownElapsed;
    private float _dropElapsed;
    private Vector2 _hookPoint;
    private bool _isHooked;
    private float _rehookLockoutTimer;
    private float _fireBufferTimer;
    private Vector2 _bufferedAimDirection;
    private float _recoilTriggerTime = -1f;
    private float _hookedAtTime = -1f;
    private float _fireHoldTime;
    private float _chargeTime;
    private float _spinAngle;
    private Vector2 _pendingAimDirection;
    private float _lastCharge01;
    private float _lastSpeedMultiplier = 1f;
    private Coroutine _hitStopRoutine;
    private float _savedTimeScale = 1f;
    private readonly Dictionary<EntityId, float> _lastCombatHitTimeByColliderId = new Dictionary<EntityId, float>();
    private Color _defaultLineColor = Color.white;
    private float _defaultLineWidth;
    private bool _lineVisualDefaultsCached;

    private int _hashBackwardAim;
    private int _hashLaunchCharge;
    private int _hashLaunchFire;
    private int _hashLaunchRecoil;

    public MorningStarState State => _state;
    public float MaxRopeLength => GetEffectiveRopeLength();
    public float LastCharge01 => _lastCharge01;
    public float LastSpeedMultiplier => _lastSpeedMultiplier;

    public bool IsRopeLineVisible =>
        _state == MorningStarState.Dragging
        || _state == MorningStarState.SpinCharging
        || _state == MorningStarState.RecallBeforeThrow
        || _state == MorningStarState.Thrown
        || _state == MorningStarState.Dropping
        || _state == MorningStarState.Returning
        || _state == MorningStarState.Hooked;

    public bool IsHookedState => _state == MorningStarState.Hooked;

    private void Awake()
    {
        if (usePlayerOnSameObject && playerRigidbody2D == null)
            playerRigidbody2D = GetComponent<Rigidbody2D>();
        if (player == null && playerRigidbody2D != null)
            player = playerRigidbody2D.GetComponent<Player>();
        if (animator == null)
            animator = GetComponent<Animator>();
        if (handAnchor == null)
            handAnchor = transform;

        _hashBackwardAim = Animator.StringToHash(backwardAimParam);
        _hashLaunchCharge = Animator.StringToHash(launchChargeParam);
        _hashLaunchFire = Animator.StringToHash(launchFireTrigger);
        _hashLaunchRecoil = Animator.StringToHash(launchRecoilTrigger);
    }

    private void OnValidate()
    {
        if (hookableLayers.value == 0)
            hookableLayers = LayerMask.GetMask("Walls", "Default");
        if (enemyLayers.value == 0)
            enemyLayers = LayerMask.GetMask("Enemy");
        SyncRopeLengthToConstraint();
    }

    private void Start()
    {
        _playerRb = playerRigidbody2D;

        if (morningStarRb == null)
        {
            GameObject head = GameObject.FindGameObjectWithTag("morningstar");
            if (head != null)
                morningStarRb = head.GetComponent<Rigidbody2D>();
        }

        if (morningStarRb != null)
        {
            morningStarRb.mass = ballMass;
            IgnorePlayerBallCollision();
            EnsureCollisionReporter();
        }

        SyncRopeLengthToConstraint();

        if (chainLineController != null)
        {
            chainLineController.SetLauncher(this);
            chainLineController.ConfigureHookVisual(normalChainColor, hookedChainColor, hookedChainWidthMultiplier);
        }

        SetSpinGuardActive(false);
        ApplyHookedChainVisual(false);

        if (lineRenderer != null)
        {
            lineRenderer.positionCount = 2;
            lineRenderer.enabled = true;
        }

        if (aimGuideLineRenderer != null)
        {
            aimGuideLineRenderer.positionCount = 2;
            aimGuideLineRenderer.enabled = false;
        }

        EnterDraggingState(true);
    }

    private void Update()
    {
        if (morningStarRb == null)
            return;

        _rehookLockoutTimer = Mathf.Max(0f, _rehookLockoutTimer - Time.deltaTime);

        ProcessRecoilTrigger();
        UpdateFallbackLineRenderer();

        // 1–2. Hooked：解除 → 左クリック再射出
        if (_state == MorningStarState.Hooked)
        {
            if (WasReleasePressedThisFrame())
            {
                BeginRelease();
                return;
            }

            if (leftClickReThrowFromHook && WasFirePressedThisFrame())
            {
                Vector2 aimDir = CalculateAimDirectionFromHook();
                if (aimDir.sqrMagnitude > 0.001f)
                    BeginReThrowFromHook(aimDir);
            }

            return;
        }

        if (_state == MorningStarState.SpinCharging)
        {
            ProcessSpinChargingInput();
            return;
        }

        if (WasReleasePressedThisFrame()
            && (_state == MorningStarState.Thrown || _state == MorningStarState.Dropping))
        {
            BeginReturn();
            return;
        }

        ProcessFireAndSpinInput();

        if (_fireBufferTimer > 0f)
            _fireBufferTimer -= Time.deltaTime;

        if (_state == MorningStarState.Dragging || _state == MorningStarState.Dropping)
            TryConsumeBufferedFire();
    }

    private void FixedUpdate()
    {
        if (_playerRb == null || morningStarRb == null)
            return;
        if (Time.timeScale < 0.01f)
            return;

        float dt = Time.fixedDeltaTime;
        Vector2 hand = GetHandWorld();

        switch (_state)
        {
            case MorningStarState.Dragging:
                UpdateChainConstraintForState();
                break;

            case MorningStarState.SpinCharging:
                SetChainConstraintActive(false);
                UpdateSpinChargingFixed(dt);
                break;

            case MorningStarState.RecallBeforeThrow:
                SetChainConstraintActive(!disableChainConstraintDuringRecall);
                UpdateRecallBeforeThrow(dt);
                break;

            case MorningStarState.Thrown:
                SetChainConstraintActive(true);
                UpdateThrown(dt, hand);
                break;

            case MorningStarState.Dropping:
                SetChainConstraintActive(enableChainConstraintWhileDropping);
                UpdateDropping(dt);
                break;

            case MorningStarState.Returning:
                SetChainConstraintActive(!disableChainConstraintDuringReturn);
                UpdateReturning(dt);
                break;

            case MorningStarState.Hooked:
                SetChainConstraintActive(false);
                UpdateHookedFixed(dt);
                break;
        }
    }

    private void UpdateChainConstraintForState()
    {
        SetChainConstraintActive(true);
    }

    private void EnterDraggingState(bool snapBallToSocket)
    {
        _state = MorningStarState.Dragging;
        _isHooked = false;
        _hookPoint = Vector2.zero;
        _recallTimer = 0f;
        _recallHoldTimer = 0f;
        _recallDelayTimer = 0f;
        _pendingThrowSpeedMultiplier = 1f;
        _thrownElapsed = 0f;
        _dropElapsed = 0f;
        _fireHoldTime = 0f;
        _chargeTime = 0f;
        SetChainConstraintActive(true);
        SetSpinGuardActive(false);
        ApplyHookedChainVisual(false);
        SetAnimatorBool(_hashLaunchCharge, false);

        if (snapBallToSocket && morningStarRb != null)
            SnapBallToSocket(zeroVelocity: true);

        TryConsumeBufferedFire();
    }

    private static bool WasFirePressedThisFrame()
    {
        Mouse mouse = Mouse.current;
        return mouse != null && mouse.leftButton.wasPressedThisFrame;
    }

    private static bool WasReleasePressedThisFrame()
    {
        Mouse mouse = Mouse.current;
        if (mouse != null && mouse.rightButton.wasPressedThisFrame)
            return true;

        Keyboard kb = Keyboard.current;
        if (kb != null && kb.bKey.wasPressedThisFrame)
            return true;

        Gamepad pad = Gamepad.current;
        if (pad != null && pad.buttonEast.wasPressedThisFrame)
            return true;

        return false;
    }

    private void ProcessFireAndSpinInput()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null)
            return;

        if (_state == MorningStarState.Dragging || _state == MorningStarState.Dropping)
        {
            if (mouse.leftButton.wasPressedThisFrame)
            {
                Vector2 aim = CalculateAimDirection();
                if (aim.sqrMagnitude > 0.001f)
                {
                    _pendingAimDirection = aim;
                    _fireHoldTime = 0f;
                }
            }

            if (mouse.leftButton.isPressed)
            {
                if (_fireHoldTime >= 0f)
                    _fireHoldTime += Time.deltaTime;

                Vector2 aim = CalculateAimDirection();
                if (aim.sqrMagnitude > 0.001f)
                    _pendingAimDirection = aim;

                if (_fireHoldTime >= holdThreshold)
                    BeginSpinCharging();
            }

            if (mouse.leftButton.wasReleasedThisFrame
                && (_state == MorningStarState.Dragging || _state == MorningStarState.Dropping))
            {
                if (_fireHoldTime > 0f && _fireHoldTime < holdThreshold
                    && _pendingAimDirection.sqrMagnitude > 0.001f)
                {
                    _bufferedAimDirection = _pendingAimDirection;
                    TryConsumeBufferedFire();
                }

                _fireHoldTime = 0f;
            }

            return;
        }

        if (mouse.leftButton.wasPressedThisFrame)
        {
            Vector2 aimDir = CalculateAimDirection();
            if (aimDir.sqrMagnitude <= 0.001f)
                return;

            _bufferedAimDirection = aimDir;
            _fireBufferTimer = fireInputBufferTime;
        }
    }

    private void ProcessSpinChargingInput()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null)
            return;

        Vector2 aim = CalculateAimDirection();
        if (aim.sqrMagnitude > 0.001f)
            _pendingAimDirection = aim;

        if (mouse.leftButton.wasReleasedThisFrame)
            ExecuteChargedThrow();
    }

    private void BeginSpinCharging()
    {
        if (_state != MorningStarState.Dragging)
            return;
        if (Time.time < _nextLaunchTime)
            return;

        _state = MorningStarState.SpinCharging;
        _chargeTime = 0f;
        _spinAngle = 0f;
        _fireHoldTime = -1f;
        SetChainConstraintActive(false);
        SetSpinGuardActive(true);
        SetAnimatorBool(_hashLaunchCharge, true);

        if (morningStarRb != null)
        {
            morningStarRb.linearVelocity = Vector2.zero;
            morningStarRb.angularVelocity = 0f;
        }
    }

    private void UpdateSpinChargingFixed(float dt)
    {
        if (morningStarRb == null)
            return;

        _chargeTime = Mathf.Min(_chargeTime + dt, maxChargeTime);
        _spinAngle += spinAngularSpeed * dt * Mathf.Deg2Rad;

        Vector2 center = GetHandWorld();
        Vector2 offset = new Vector2(Mathf.Cos(_spinAngle), Mathf.Sin(_spinAngle)) * spinRadius;
        morningStarRb.MovePosition(center + offset);
        morningStarRb.linearVelocity = Vector2.zero;
        morningStarRb.angularVelocity = 0f;
    }

    private void ExecuteChargedThrow()
    {
        if (_state != MorningStarState.SpinCharging || morningStarRb == null)
            return;

        float charge01 = maxChargeTime > 0f ? Mathf.Clamp01(_chargeTime / maxChargeTime) : 1f;
        _lastCharge01 = charge01;
        _lastSpeedMultiplier = Mathf.Lerp(minChargedThrowMultiplier, maxChargedThrowMultiplier, charge01);

        Vector2 aimDir = _pendingAimDirection.sqrMagnitude > 0.001f
            ? _pendingAimDirection.normalized
            : Vector2.right;

        SetSpinGuardActive(false);
        BeginRecallBeforeThrow(aimDir, chargedRecallTimeMultiplier, _lastSpeedMultiplier);
    }

    private void SetSpinGuardActive(bool active)
    {
        if (spinGuardCollider != null)
            spinGuardCollider.enabled = active;
    }

    private Vector2 CalculateAimDirection()
    {
        return CalculateAimDirectionFrom(GetThrowOriginPosition());
    }

    private Vector2 CalculateAimDirectionFromHook()
    {
        Vector2 origin = _hookPoint.sqrMagnitude > 1e-8f
            ? _hookPoint
            : (Vector2)morningStarRb.position;
        return CalculateAimDirectionFrom(origin);
    }

    private Vector2 CalculateAimDirectionFrom(Vector2 origin)
    {
        if (handAnchor == null)
            return Vector2.zero;

        if (!TryGetPointerScreen(out Vector2 screenPos))
            return Vector2.zero;

        Vector2 mouseWorld = WorldFromScreen(screenPos);
        Vector2 dir = mouseWorld - origin;

        if (dir.sqrMagnitude < minAimDistance * minAimDistance)
            return Vector2.zero;

        return dir.normalized;
    }

    private Vector2 GetThrowOriginPosition()
    {
        Transform origin = GetThrowSocket();
        return origin != null ? (Vector2)origin.position : GetHandWorld();
    }

    private void TryConsumeBufferedFire()
    {
        if (_state != MorningStarState.Dragging && _state != MorningStarState.Dropping)
            return;
        if (_fireBufferTimer <= 0f)
            return;
        if (Time.time < _nextLaunchTime)
            return;

        BeginRecallBeforeThrow(_bufferedAimDirection);
        _fireBufferTimer = 0f;
    }

    private void BeginRelease()
    {
        if (_state != MorningStarState.Hooked || !_isHooked)
            return;

        ApplyReleaseBoostToPlayer();

        _isHooked = false;
        _hookPoint = Vector2.zero;

        if (morningStarRb != null)
        {
            morningStarRb.linearVelocity = Vector2.zero;
            morningStarRb.angularVelocity = 0f;
            morningStarRb.WakeUp();
        }

        _rehookLockoutTimer = rehookLockoutTime;
        ApplyHookedChainVisual(false);
        BeginReturn();
    }

    private void ApplyReleaseBoostToPlayer()
    {
        if (_playerRb == null || releaseBoost <= 0f)
            return;

        Vector2 v = _playerRb.linearVelocity;
        Vector2 boostDir;
        if (v.sqrMagnitude > 0.25f)
            boostDir = v.normalized;
        else
        {
            boostDir = (Vector2)_playerRb.position - _hookPoint;
            if (boostDir.sqrMagnitude < 1e-6f)
                boostDir = Vector2.right;
            else
                boostDir.Normalize();
        }

        if (player != null)
            player.ApplyExternalImpulse(boostDir * releaseBoost, ForceMode2D.Impulse);
        else
            _playerRb.AddForce(boostDir * releaseBoost, ForceMode2D.Impulse);
    }

    public void OnMorningStarCollision(Collision2D collision)
    {
        if (morningStarRb == null || collision.collider == null)
            return;

        if (TryProcessCombatHit(collision))
            return;

        if (_state != MorningStarState.Thrown)
            return;
        if (_rehookLockoutTimer > 0f)
            return;

        if (!IsHookableCollision(collision))
            return;

        float speed = morningStarRb.linearVelocity.magnitude;
        if (speed < hookMinSpeed)
            return;

        Vector2 hookPoint = collision.GetContact(0).point;
        if (hookPoint.sqrMagnitude < 1e-8f)
            hookPoint = morningStarRb.position;

        BeginHook(hookPoint);
    }

    private bool CanStateDealCombatDamage()
    {
        return _state == MorningStarState.Thrown || _state == MorningStarState.Dropping;
    }

    private bool TryProcessCombatHit(Collision2D collision)
    {
        if (!CanStateDealCombatDamage())
            return false;

        Collider2D other = collision.collider;
        if (other.CompareTag("Player"))
            return false;

        IMorningStarHitReceiver receiver = other.GetComponentInParent<IMorningStarHitReceiver>();
        if (receiver == null)
            return false;

        if (!AllowsCombatHitOnLayer(other.gameObject.layer))
            return false;

        float impactSpeed = morningStarRb.linearVelocity.magnitude;
        if (impactSpeed < minCombatHitSpeed)
            return false;

        EntityId colliderId = other.GetEntityId();
        if (_lastCombatHitTimeByColliderId.TryGetValue(colliderId, out float lastHitTime)
            && Time.time - lastHitTime < hitCooldownPerTarget)
            return false;

        MorningStarHitContext context = BuildHitContext(collision, impactSpeed);
        receiver.OnMorningStarHit(context);

        _lastCombatHitTimeByColliderId[colliderId] = Time.time;
        ApplyPostHitBallResponse(context);
        PlayCombatHitStop(context.HitStopSeconds);

        return true;
    }

    private bool AllowsCombatHitOnLayer(int layer)
    {
        if (enemyLayers.value == 0 && breakableLayers.value == 0)
            return true;

        int layerBit = 1 << layer;
        if (enemyLayers.value != 0 && (enemyLayers.value & layerBit) != 0)
            return true;
        return breakableLayers.value != 0 && (breakableLayers.value & layerBit) != 0;
    }

    private MorningStarHitContext BuildHitContext(Collision2D collision, float impactSpeed)
    {
        Vector2 impactDir = morningStarRb.linearVelocity.sqrMagnitude > 1e-6f
            ? morningStarRb.linearVelocity.normalized
            : Vector2.right;

        if (collision.contactCount > 0)
        {
            Vector2 normal = collision.GetContact(0).normal;
            if (normal.sqrMagnitude > 1e-6f)
                impactDir = -normal.normalized;
        }

        float chargeMult = Mathf.Max(1f, _lastSpeedMultiplier);
        float scaledSpeed = impactSpeed * chargeMult;

        int damage = Mathf.RoundToInt(baseDamage + scaledSpeed * damagePerSpeed);
        damage = Mathf.Clamp(damage, minDamage, maxDamage);

        float knockbackMag = Mathf.Clamp(baseKnockback + scaledSpeed * knockbackPerSpeed, 0f, maxKnockback);
        Vector2 knockback = impactDir * knockbackMag;

        float hitStop = Mathf.Clamp(damage * hitStopPerDamage, minCombatHitStop, maxCombatHitStop);
        hitStop *= Mathf.Lerp(0.85f, 1.15f, Mathf.InverseLerp(minDamage, maxDamage, damage));

        Vector2 impactPoint = collision.contactCount > 0
            ? collision.GetContact(0).point
            : morningStarRb.position;

        return new MorningStarHitContext(
            damage,
            knockback,
            hitStop,
            impactPoint,
            impactDir,
            impactSpeed,
            chargeMult);
    }

    private void ApplyPostHitBallResponse(MorningStarHitContext context)
    {
        if (postHitSpeedRetention >= 1f || morningStarRb == null)
            return;

        morningStarRb.linearVelocity *= postHitSpeedRetention;
    }

    private void PlayCombatHitStop(float duration)
    {
        if (duration <= 0f)
            return;

        if (_hitStopRoutine != null)
            StopCoroutine(_hitStopRoutine);

        _hitStopRoutine = StartCoroutine(HitStopRoutine(duration));
    }

    private bool IsHookableCollision(Collision2D collision)
    {
        if (collision.collider == null)
            return false;
        if (collision.collider.CompareTag("Player"))
            return false;
        if (((1 << collision.gameObject.layer) & hookableLayers) == 0)
            return false;
        return true;
    }

    private void BeginHook(Vector2 hookPoint)
    {
        _state = MorningStarState.Hooked;
        _hookPoint = hookPoint;
        _isHooked = true;
        _hookedAtTime = Time.time;
        _thrownElapsed = 0f;
        _fireBufferTimer = 0f;

        morningStarRb.position = _hookPoint;
        morningStarRb.linearVelocity = Vector2.zero;
        morningStarRb.angularVelocity = 0f;
        morningStarRb.WakeUp();

        if (disableChainConstraintWhileHooked)
            SetChainConstraintActive(false);

        _rehookLockoutTimer = Mathf.Max(rehookLockoutTime, hookStickMinTime);
        SetAnimatorBool(_hashLaunchCharge, false);
        SetSpinGuardActive(false);
        ApplyHookedChainVisual(true);
        PlayHookFeedback();
        Debug.Log("MorningStar Hooked");
    }

    private void PlayHookFeedback()
    {
        PlayCombatHitStop(hookHitStopTime);

        if (hookSparkPrefab != null)
            Instantiate(hookSparkPrefab, _hookPoint, Quaternion.identity);
    }

    private IEnumerator HitStopRoutine(float duration)
    {
        _savedTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        yield return new WaitForSecondsRealtime(duration);
        Time.timeScale = _savedTimeScale;
        _hitStopRoutine = null;
    }

    private void ApplyHookedChainVisual(bool hooked)
    {
        if (chainLineController != null)
        {
            chainLineController.SetHookedVisual(hooked);
            return;
        }

        if (lineRenderer == null)
            return;

        if (!_lineVisualDefaultsCached)
        {
            _defaultLineColor = lineRenderer.startColor;
            _defaultLineWidth = lineRenderer.startWidth;
            _lineVisualDefaultsCached = true;
        }

        Color c = hooked ? hookedChainColor : normalChainColor;
        lineRenderer.startColor = c;
        lineRenderer.endColor = c;
        float w = hooked ? _defaultLineWidth * hookedChainWidthMultiplier : _defaultLineWidth;
        lineRenderer.startWidth = w;
        lineRenderer.endWidth = w;
    }

    private void BeginReThrowFromHook(Vector2 aimDir)
    {
        if (_state != MorningStarState.Hooked || !_isHooked)
            return;
        if (aimDir.sqrMagnitude < 1e-6f)
            return;
        if (Time.time < _nextLaunchTime)
            return;
        if (Time.time - _hookedAtTime < hookStickMinTime)
            return;

        Vector2 d = aimDir.normalized;
        Vector2 hand = GetHandWorld();
        Vector2 worldTarget = (Vector2)morningStarRb.position + d * Mathf.Max(minAimDistance, maxThrowDistance);
        UpdateAimFacing(worldTarget);
        ShowClickAimVisuals(worldTarget, hand);

        _isHooked = false;
        _hookPoint = Vector2.zero;
        _rehookLockoutTimer = rehookLockoutTime;
        ApplyHookedChainVisual(false);

        morningStarRb.position = morningStarRb.position;
        morningStarRb.linearVelocity = Vector2.zero;
        morningStarRb.angularVelocity = 0f;

        float speed = throwSpeed;
        if (maxBallLinearSpeed > 0f)
            speed = Mathf.Min(speed, maxBallLinearSpeed);
        morningStarRb.linearVelocity = d * speed;
        morningStarRb.WakeUp();

        _thrownElapsed = 0f;
        _lastSpeedMultiplier = 1f;
        _state = MorningStarState.Thrown;
        SetChainConstraintActive(true);
        SetAnimatorBool(_hashLaunchCharge, false);
        SetAnimatorTrigger(_hashLaunchFire);
        _recoilTriggerTime = Time.time + launchRecoilDelay;

        if (launchPlayerPullImpulse > 0f && _playerRb != null)
        {
            _playerRb.AddForce(d * launchPlayerPullImpulse, ForceMode2D.Impulse);
            if (player != null && !player.IsGrounded)
                player.PlayAirLaunchBlink();
        }

        if (aimLaunchCooldown > 0f)
            _nextLaunchTime = Time.time + aimLaunchCooldown;

        Debug.Log("MorningStar ReThrown From Hook");
    }

    private void UpdateHookedFixed(float dt)
    {
        if (morningStarRb == null)
        {
            EnterDraggingState(false);
            return;
        }

        if (_state != MorningStarState.Hooked || !_isHooked)
            return;

        morningStarRb.position = _hookPoint;
        morningStarRb.linearVelocity = Vector2.zero;
        morningStarRb.angularVelocity = 0f;

        ApplyHookPullToPlayer();
    }

    private void ApplyHookPullToPlayer()
    {
        float chainLen = GetEffectiveRopeLength();
        Vector2 playerPos = _playerRb.position;
        Vector2 toHook = _hookPoint - playerPos;
        float distance = toHook.magnitude;
        if (distance < 1e-4f)
            return;

        Vector2 radialIn = toHook / distance;
        Vector2 tangent = new Vector2(-radialIn.y, radialIn.x);

        if (distance > chainLen)
        {
            float overshoot = distance - chainLen;
            _playerRb.AddForce(radialIn * pullForce * (1f + overshoot * 0.35f), ForceMode2D.Force);
        }

        Vector2 v = _playerRb.linearVelocity;
        float outwardSpeed = Vector2.Dot(v, -radialIn);
        if (outwardSpeed > 0f)
            v -= (-radialIn) * outwardSpeed * radialVelocityDamping;

        float radialAlong = Vector2.Dot(v, radialIn);
        float tangentAlong = Vector2.Dot(v, tangent);
        v = radialIn * radialAlong + tangent * (tangentAlong * tangentVelocityKeepRate);

        float moveX = player != null ? player.MoveInputX : 0f;
        if (Mathf.Abs(moveX) > 0.01f)
            _playerRb.AddForce(tangent * (moveX * swingInputForce), ForceMode2D.Force);

        if (maxSwingSpeed > 0f && v.magnitude > maxSwingSpeed)
            v = v.normalized * maxSwingSpeed;

        _playerRb.linearVelocity = v;
    }

    private void BeginRecallBeforeThrow(Vector2 launchDir, float recallTimeMultiplier = 1f, float throwSpeedMultiplier = 1f)
    {
        if (launchDir.sqrMagnitude < 1e-6f || morningStarRb == null)
            return;

        Vector2 hand = GetHandWorld();
        Vector2 d = launchDir.normalized;
        Vector2 worldTarget = hand + d * Mathf.Max(minAimDistance, maxThrowDistance);

        UpdateAimFacing(worldTarget);
        ShowClickAimVisuals(worldTarget, hand);

        _pendingLaunchDir = d;
        _pendingThrowSpeedMultiplier = throwSpeedMultiplier;

        if (!useVisibleRecallBeforeThrow)
        {
            FirePendingThrow();
            return;
        }

        _recallStartPosition = morningStarRb.position;
        _recallTargetPosition = GetThrowOriginPosition();
        _recallTimer = 0f;
        _recallHoldTimer = 0f;
        _recallDelayTimer = recallStartDelay;
        _activeRecallDuration = Mathf.Max(0.01f, visibleRecallTime * Mathf.Max(0.05f, recallTimeMultiplier));

        _state = MorningStarState.RecallBeforeThrow;

        if (disableChainConstraintDuringRecall)
            SetChainConstraintActive(false);

        morningStarRb.linearVelocity = Vector2.zero;
        morningStarRb.angularVelocity = 0f;
        morningStarRb.WakeUp();
        SetAnimatorBool(_hashLaunchCharge, true);
    }

    private void UpdateRecallBeforeThrow(float dt)
    {
        if (morningStarRb == null)
        {
            EnterDraggingState(false);
            return;
        }

        morningStarRb.linearVelocity = Vector2.zero;
        morningStarRb.angularVelocity = 0f;

        if (_recallDelayTimer > 0f)
        {
            _recallDelayTimer -= dt;
            return;
        }

        _recallTimer += dt;
        float t = Mathf.Clamp01(_recallTimer / _activeRecallDuration);
        float eased = 1f - Mathf.Pow(1f - t, recallEasePower);
        Vector2 pos = Vector2.Lerp(_recallStartPosition, _recallTargetPosition, eased);
        morningStarRb.position = pos;

        if (t < 1f)
            return;

        morningStarRb.position = _recallTargetPosition;
        _recallHoldTimer += dt;

        if (_recallHoldTimer >= recallHoldTime)
            FirePendingThrow();
    }

    private void FirePendingThrow()
    {
        if (morningStarRb == null)
            return;

        Vector2 origin = GetThrowOriginPosition();
        Vector2 d = _pendingLaunchDir.sqrMagnitude > 1e-12f ? _pendingLaunchDir.normalized : Vector2.right;

        morningStarRb.position = origin;
        morningStarRb.linearVelocity = Vector2.zero;
        morningStarRb.angularVelocity = 0f;

        if (chainConstraint != null)
            chainConstraint.enabled = true;

        float speed = throwSpeed * _pendingThrowSpeedMultiplier;
        if (maxBallLinearSpeed > 0f)
            speed = Mathf.Min(speed, maxBallLinearSpeed);

        float offset = Mathf.Max(0f, launchStartOffset);
        morningStarRb.position = origin + d * offset;
        morningStarRb.linearVelocity = d * speed;
        morningStarRb.WakeUp();

        if (launchPlayerPullImpulse > 0f && _playerRb != null)
        {
            _playerRb.AddForce(d * launchPlayerPullImpulse, ForceMode2D.Impulse);
            if (player != null && !player.IsGrounded)
                player.PlayAirLaunchBlink();
        }

        _thrownElapsed = 0f;
        _lastSpeedMultiplier = _pendingThrowSpeedMultiplier;
        _state = MorningStarState.Thrown;
        SetAnimatorBool(_hashLaunchCharge, false);
        SetAnimatorTrigger(_hashLaunchFire);
        _recoilTriggerTime = Time.time + launchRecoilDelay;

        if (aimLaunchCooldown > 0f)
            _nextLaunchTime = Time.time + aimLaunchCooldown;
    }

    private void UpdateThrown(float dt, Vector2 hand)
    {
        if (_state != MorningStarState.Thrown)
            return;

        _thrownElapsed += dt;

        if (_thrownElapsed >= maxThrownTime)
        {
            BeginDropAfterThrow();
            return;
        }

        float dist = Vector2.Distance(hand, morningStarRb.position);
        if (dist >= maxThrowDistance && !IsMorningStarTouchingHookable())
        {
            BeginDropAfterThrow();
            return;
        }

        if (_thrownElapsed > 0.1f && morningStarRb.linearVelocity.magnitude <= dropTransitionSpeed)
            BeginDropAfterThrow();
    }

    private void BeginDropAfterThrow()
    {
        if (_state != MorningStarState.Thrown)
            return;

        _state = MorningStarState.Dropping;
        _dropElapsed = 0f;

        if (chainConstraint != null)
            chainConstraint.enabled = enableChainConstraintWhileDropping;

        morningStarRb.WakeUp();
    }

    private void UpdateDropping(float dt)
    {
        if (_state != MorningStarState.Dropping)
            return;

        _dropElapsed += dt;

        if (_dropElapsed >= dropToDraggingTime)
            EnterDraggingState(false);
    }

    private bool IsMorningStarTouchingHookable()
    {
        if (morningStarRb == null)
            return false;

        Collider2D ballCol = morningStarRb.GetComponent<Collider2D>();
        if (ballCol == null)
            return false;

        ContactFilter2D filter = new ContactFilter2D();
        filter.useLayerMask = true;
        filter.layerMask = hookableLayers;
        filter.useTriggers = false;

        return ballCol.IsTouching(filter);
    }

    private void BeginReturn()
    {
        if (_state == MorningStarState.Returning)
            return;
        if (_state == MorningStarState.Hooked && _isHooked)
            return;

        bool canManualReturn = _state == MorningStarState.Thrown
            || _state == MorningStarState.Dropping
            || (_state == MorningStarState.Hooked && !_isHooked);
        if (!canManualReturn)
            return;

        _isHooked = false;
        _hookPoint = Vector2.zero;
        _state = MorningStarState.Returning;
        _rehookLockoutTimer = Mathf.Max(_rehookLockoutTimer, rehookLockoutTime);

        if (disableChainConstraintDuringReturn)
            SetChainConstraintActive(false);
        else
            SetChainConstraintActive(true);

        SetAnimatorBool(_hashLaunchCharge, false);
        ApplyHookedChainVisual(false);

        if (morningStarRb != null)
            morningStarRb.WakeUp();
    }

    private void UpdateReturning(float dt)
    {
        if (morningStarRb == null)
        {
            EnterDraggingState(false);
            return;
        }

        Vector2 target = GetThrowSocketWorld();
        Vector2 current = morningStarRb.position;
        Vector2 toTarget = target - current;
        float dist = toTarget.magnitude;

        if (dist <= returnFinishDistance)
        {
            FinishReturn();
            return;
        }

        float speed = Mathf.Min(returnSpeed, maxBallLinearSpeed > 0f ? maxBallLinearSpeed : returnSpeed);
        morningStarRb.linearVelocity = toTarget.normalized * speed;
        morningStarRb.angularVelocity = 0f;
    }

    private void FinishReturn()
    {
        if (morningStarRb == null)
        {
            EnterDraggingState(false);
            return;
        }

        morningStarRb.position = GetThrowSocketWorld();
        morningStarRb.linearVelocity = Vector2.zero;
        morningStarRb.angularVelocity = 0f;
        morningStarRb.WakeUp();
        EnterDraggingState(false);
    }

    private void SnapBallToSocket(bool zeroVelocity = true)
    {
        if (morningStarRb == null)
            return;

        morningStarRb.position = GetThrowSocketWorld();
        if (zeroVelocity)
        {
            morningStarRb.linearVelocity = Vector2.zero;
            morningStarRb.angularVelocity = 0f;
        }
        morningStarRb.WakeUp();
    }

    private void EnsureCollisionReporter()
    {
        MorningStarCollisionReporter reporter = morningStarRb.GetComponent<MorningStarCollisionReporter>();
        if (reporter == null)
            reporter = morningStarRb.gameObject.AddComponent<MorningStarCollisionReporter>();
        reporter.Initialize(this);
    }

    private Transform GetThrowSocket() => throwSocket != null ? throwSocket : handAnchor;

    private Vector2 GetThrowSocketWorld()
    {
        Transform socket = GetThrowSocket();
        return socket != null ? (Vector2)socket.position : GetHandWorld();
    }

    private void SetChainConstraintActive(bool active)
    {
        if (chainConstraint != null)
            chainConstraint.enabled = active;
    }

    private void SyncRopeLengthToConstraint()
    {
        if (chainConstraint != null)
        {
            chainConstraint.SetMaxRopeLength(maxRopeLength);
            chainConstraint.MaxBallSpeed = maxBallLinearSpeed;
        }
    }

    private float GetEffectiveRopeLength()
    {
        if (chainConstraint != null)
            return chainConstraint.MaxRopeLength;
        return maxRopeLength;
    }

    private Vector2 GetHandWorld()
    {
        return handAnchor != null ? (Vector2)handAnchor.position : (Vector2)transform.position;
    }

    private void UpdateFallbackLineRenderer()
    {
        if (chainLineController != null || lineRenderer == null || morningStarRb == null)
            return;

        lineRenderer.enabled = IsRopeLineVisible;
        if (!lineRenderer.enabled)
            return;

        lineRenderer.positionCount = 2;
        Vector3 start = GetHandWorld();
        Vector3 end = ClampToRopeLength(start, morningStarRb.position);
        lineRenderer.SetPosition(0, start);
        lineRenderer.SetPosition(1, end);
    }

    private Vector3 ClampToRopeLength(Vector3 start, Vector3 end)
    {
        float maxLen = GetEffectiveRopeLength();
        Vector3 off = end - start;
        if (off.sqrMagnitude <= maxLen * maxLen)
            return end;
        return start + off.normalized * maxLen;
    }

    private void ShowClickAimVisuals(Vector2 aimWorld, Vector2 hand)
    {
        if (aimMark != null)
        {
            aimMark.gameObject.SetActive(true);
            aimMark.position = aimWorld;
        }

        if (aimGuideLineRenderer == null || !showAimGuide)
            return;

        Vector2 toAim = aimWorld - hand;
        float len = Mathf.Min(toAim.magnitude, aimGuideMaxLength > 0f ? aimGuideMaxLength : GetEffectiveRopeLength());
        if (toAim.sqrMagnitude <= 1e-8f)
        {
            aimGuideLineRenderer.enabled = false;
            return;
        }

        aimGuideLineRenderer.enabled = true;
        aimGuideLineRenderer.positionCount = 2;
        aimGuideLineRenderer.SetPosition(0, hand);
        aimGuideLineRenderer.SetPosition(1, hand + toAim.normalized * len);
    }

    private void UpdateAimFacing(Vector2 aimWorld)
    {
        if (player == null)
            return;

        float moveX = player.MoveInputX;
        float aimDirX = aimWorld.x - player.transform.position.x;
        bool backward = Mathf.Abs(moveX) > 0.01f
            && Mathf.Abs(aimDirX) > 0.01f
            && Mathf.Sign(moveX) != Mathf.Sign(aimDirX);

        player.SetAimFacing(aimWorld.x, backward);
        SetAnimatorBool(_hashBackwardAim, backward);
    }

    private void IgnorePlayerBallCollision()
    {
        if (_playerRb == null || morningStarRb == null)
            return;

        Collider2D[] playerCols = _playerRb.GetComponents<Collider2D>();
        Collider2D[] ballCols = morningStarRb.GetComponents<Collider2D>();
        foreach (Collider2D pc in playerCols)
        {
            if (pc == null) continue;
            foreach (Collider2D bc in ballCols)
            {
                if (bc != null)
                    Physics2D.IgnoreCollision(pc, bc, true);
            }
        }
    }

    private bool TryGetPointerScreen(out Vector2 screenPos)
    {
        screenPos = default;
        Mouse mouse = Mouse.current;
        if (mouse == null)
            return false;

        screenPos = mouse.position.ReadValue();
        if (!restrictPointerToGameView)
            return true;

        Camera cam = GetAimCamera();
        return cam == null || cam.pixelRect.Contains(screenPos);
    }

    private Camera GetAimCamera() => aimCamera != null ? aimCamera : Camera.main;

    private Vector2 WorldFromScreen(Vector2 screen)
    {
        Camera cam = GetAimCamera();
        if (cam == null)
            return GetHandWorld();

        Vector3 world = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, screenZ));
        return new Vector2(world.x, world.y);
    }

    public void ApplyRecallThenLaunch(Vector2 worldDirection)
    {
        if (worldDirection.sqrMagnitude < 1e-12f)
            return;

        _bufferedAimDirection = worldDirection.normalized;
        _fireBufferTimer = fireInputBufferTime;

        if (_state == MorningStarState.Dragging || _state == MorningStarState.Dropping)
            TryConsumeBufferedFire();
    }

    public void RequestReturn()
    {
        if (_state == MorningStarState.Thrown || _state == MorningStarState.Dropping)
            BeginReturn();
        else if (_state == MorningStarState.Hooked)
            BeginRelease();
    }

    private void ProcessRecoilTrigger()
    {
        if (_recoilTriggerTime < 0f || Time.time < _recoilTriggerTime)
            return;
        SetAnimatorTrigger(_hashLaunchRecoil);
        _recoilTriggerTime = -1f;
    }

    private void SetAnimatorBool(int hash, bool value)
    {
        if (animator == null || !HasAnimatorParam(hash, AnimatorControllerParameterType.Bool))
            return;
        animator.SetBool(hash, value);
    }

    private void SetAnimatorTrigger(int hash)
    {
        if (animator == null || !HasAnimatorParam(hash, AnimatorControllerParameterType.Trigger))
            return;
        animator.SetTrigger(hash);
    }

    private bool HasAnimatorParam(int hash, AnimatorControllerParameterType type)
    {
        if (animator == null || animator.runtimeAnimatorController == null)
            return false;

        foreach (AnimatorControllerParameter p in animator.parameters)
        {
            if (p.nameHash == hash && p.type == type)
                return true;
        }

        return false;
    }
}
