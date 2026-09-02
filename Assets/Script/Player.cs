using System.Collections;

using UnityEngine;

using UnityEngine.InputSystem;

using UnityEngine.Serialization;



/// <summary>

/// 移動入力は Update で集約し、物理は FixedUpdate で適用。武器状態では横移動を止めない。

/// </summary>

public class Player : MonoBehaviour

{

    [Header("歩行（地上）")]

    [SerializeField, Tooltip("地上の横方向加速力")]

    private float _groundMoveForce = 70f;

    [SerializeField, Tooltip("地上の横減速（速度に比例）")]

    private float _groundLinearDragX = 8f;



    [Header("歩行（空中）")]

    [SerializeField, Range(0f, 1f), Tooltip("空中の加速力＝地上×この値")]

    private float _airMoveFactor = 0.1f;

    [SerializeField, Tooltip("空中の横減速")]

    private float _airLinearDragX = 1.5f;



    [Header("ジャンプ（Impulse）")]

    [SerializeField] private float _jumpSpeed = 8f;

    [SerializeField, Tooltip("コヨーテタイム（秒）")]

    private float _coyoteTime = 0.1f;

    [SerializeField, Tooltip("ジャンプバッファ（秒）")]

    private float _jumpBufferTime = 0.15f;



    [Header("ジャンプ物理")]

    [SerializeField] private float _fallGravityMultiplier = 4f;

    [SerializeField] private float _jumpCutMultiplier = 2f;

    [SerializeField, Tooltip("最大落下速度（負の値）")]

    private float _maxFallSpeed = -50f;



    [Header("接地判定")]

    [SerializeField, Tooltip("接地判定の基準にするPlayer本体Collider。未設定なら同じGameObjectから取得")]

    private Collider2D _groundCheckCollider;

    [SerializeField, Tooltip("床として判定するLayer（現在のStage床はDefault / Walls）")]

    private LayerMask _groundLayers = (1 << 0) | (1 << 6);

    [SerializeField, Range(0.6f, 0.8f), Tooltip("本体Collider幅に対するGroundCheck幅")]

    private float _groundCheckWidthRatio = 0.72f;

    [SerializeField, Range(0.08f, 0.15f), Tooltip("GroundCheckの高さ（World Unit）")]

    private float _groundCheckHeight = 0.12f;

    [SerializeField, Range(0f, 0.05f), Tooltip("Collider最下端へ重ねるGroundCheckの厚み")]

    private float _groundCheckVerticalOverlap = 0.02f;

    [SerializeField, Min(0f), Tooltip("Tile境界などの瞬間的な判定抜けを吸収する時間。Coyote Timeとは別管理")]

    private float _groundedGraceTime = 0.05f;



    [Header("見た目")]

    [FormerlySerializedAs("_spriteRenderer")]
    [SerializeField, Tooltip("体の SpriteRenderer。未設定なら子の SpriteRenderer を使用")]
    private SpriteRenderer _bodySprite;

    [SerializeField, Tooltip("flipX が使えない場合のみ Visual の localScale.x を反転")]
    private Transform _visualRoot;

    private Transform _weaponHandAnchor;
    private Vector3 _rightFacingHandAnchorLocalPosition;
    private bool _handAnchorFacingInitialized;
    private bool _weaponHandAnchorOverrideActive;
    private Vector3 _weaponHandAnchorOverrideRightLocalPosition;



    [Header("空中発射")]

    [SerializeField] private float _airLaunchBlinkDuration = 0.22f;

    [SerializeField] private float _airLaunchBlinkInterval = 0.05f;



    [Header("Dragging Feel")]

    [SerializeField, Tooltip("Dragging中の地上走行補助を有効にする")]

    private bool _enableDraggingMoveAssist = true;

    [SerializeField, Range(0f, 1f), Tooltip("入力開始直後に加える、地上移動力に対する補助割合")]

    private float _draggingStartAssist = 0.08f;

    [SerializeField, Range(0f, 1f), Tooltip("鉄球が入力方向へ十分動いている時の最大補助割合")]

    private float _draggingCruiseAssist = 0.28f;

    [SerializeField, Min(0f), Tooltip("Dragging補助が最大になるまでの時間")]

    private float _draggingAssistRampTime = 0.2f;

    [SerializeField, Range(1f, 1.05f), Tooltip("Dragging補助が働く速度上限の通常走行速度倍率")]

    private float _draggingMaxSpeedMultiplier = 1.05f;

    [SerializeField, Min(0.01f), Tooltip("実位置変化から求めた鉄球速度がこの値で巡航補助になる")]

    private float _draggingBallSpeedForFullAssist = 3.5f;

    [Header("効果音")]

    [SerializeField] private AudioSource _sfxAudioSource;

    [SerializeField] private AudioSource _footstepAudioSource;

    [SerializeField] private AudioClip _jumpClip;

    [SerializeField] private AudioClip _footstepGrassClip;

    [SerializeField] private AudioClip[] _jumpVoiceClips;

    [SerializeField] private AudioClip _landingClip;

    [SerializeField, Range(0f, 1f)] private float _jumpVolume = 1f;

    [SerializeField, Range(0f, 1f)] private float _footstepVolume = 0.39f;

    [SerializeField, Range(0f, 1f)] private float _jumpVoiceVolume = 1f;

    [SerializeField, Range(0f, 1f)] private float _landingVolume = 0.55f;

    [SerializeField, Min(0f)] private float _footstepMinHorizontalSpeed = 0.1f;

    [SerializeField] private PlayerHealth _playerHealth;



    private Vector2 _moveInput;
    private Vector2 _actionMoveInput;

    private Rigidbody2D _rigid;
    private MorningStarLauncher _morningStarLauncher;

    private bool _bjump;

    private Animator _anim;

    private bool _wasGrounded;

    private bool _groundStateInitialized;

    private readonly Collider2D[] _groundCheckResults = new Collider2D[8];

    private bool _isGrounded;

    private bool _rawGrounded;

    private float _groundedGraceTimer;

    private float _coyoteTimer;

    private float _jumpBufferTimer;

    private bool _jumpHeld;

    private bool _backwardAim;

    private bool _hasAimTarget;

    private float _aimFacingWorldX;

    private Coroutine _airLaunchBlinkRoutine;

    private bool _airTensionGravityAssistActive;

    private float _airTensionGravityAssistRemaining;

    private float _airTensionGravityMultiplier = 1f;

    private float _airTensionMaxFallSpeedDuringAssist = -3.5f;

    private float _airTensionAssistApexVelocity;

    private float _draggingAssistRamp;

    private float _draggingAssistInputSign;

    private Vector2 _draggingPreviousBallPosition;

    private bool _hasDraggingBallPositionSample;

    private float _draggingActualBallHorizontalSpeed;

    // MorningStarLauncherから張力を受けた時だけ使用する一時状態。
    // 通常時は1のままなので、既存の歩行Dragには影響しない。
    private float _tensionMomentumHoldTimer;

    private float _tensionMomentumDragMultiplier = 1f;

    private float _tensionMomentumDecayRate;

    // 張力由来の横移動が実質停止したとみなす速度。
    // Inspector調整値ではなく、残留Runtime状態を片付けるためだけの許容誤差。
    private const float StationaryTensionMomentumSpeedThreshold = 0.05f;

    private bool _facingRight = true;



    public float MoveInputX => _moveInput.x;

    public bool FacingRight => _facingRight;

    public Rigidbody2D Rigidbody2D => _rigid;

    public bool IsGrounded => _isGrounded;

    public event System.Action Landed;

    public bool IsBackwardAim => _backwardAim;

    public Transform WeaponHandAnchor => _weaponHandAnchor;

    public Vector3 RightFacingHandAnchorLocalPosition => _rightFacingHandAnchorLocalPosition;

    /// <summary>
    /// 鎖張力で得た横速度が通常歩行Dragで即座に消えないよう、一時的にDragを弱める。
    /// 張力がない通常走行からは呼ばれないため、通常の操作感は変化しない。
    /// </summary>
    public void PreserveTensionMomentum(float holdTime, float retentionRate, float decayRate)
    {
        _tensionMomentumHoldTimer = Mathf.Max(_tensionMomentumHoldTimer, Mathf.Max(0f, holdTime));
        float retainedDragMultiplier = 1f - Mathf.Clamp01(retentionRate);
        _tensionMomentumDragMultiplier = Mathf.Min(
            _tensionMomentumDragMultiplier,
            retainedDragMultiplier);
        _tensionMomentumDecayRate = Mathf.Max(0f, decayRate);
    }

    /// <summary>
    /// 空中射出後、本張力が発生するまでの下降猶予を開始する。
    /// 実際の補助時間は頂点付近または下降中だけ消費する。
    /// </summary>
    public void BeginAirTensionGravityAssist(
        float duration,
        float gravityMultiplier,
        float maxFallSpeed,
        float apexVelocity)
    {
        if (duration <= 0f)
        {
            EndAirTensionGravityAssist();
            return;
        }

        _airTensionGravityAssistActive = true;
        _airTensionGravityAssistRemaining = duration;
        _airTensionGravityMultiplier = Mathf.Clamp01(gravityMultiplier);
        _airTensionMaxFallSpeedDuringAssist = Mathf.Min(-0.01f, maxFallSpeed);
        _airTensionAssistApexVelocity = Mathf.Max(0f, apexVelocity);
    }

    public void EndAirTensionGravityAssist()
    {
        _airTensionGravityAssistActive = false;
        _airTensionGravityAssistRemaining = 0f;
        _airTensionGravityMultiplier = 1f;
        _airTensionAssistApexVelocity = 0f;
    }

    /// <summary>
    /// Game Start / Return Complete / Respawnで共通のDragging Feel状態へ戻す。
    /// 張力で得たPlayerの実速度は維持するが、Thrown/Dropping専用だった
    /// 低Dragタイマーは終了し、通常Draggingの入力応答へ確実に戻す。
    /// </summary>
    public void BeginDraggingMovementState()
    {
        ClearTensionMomentum();
        ResetDraggingMoveAssist();
    }

    /// <summary>
    /// Launch Poseなど、現在のSpriteだけに必要な手元位置を一時的に使用する。
    /// 右向き座標を受け取り、既存flipX方式と同じく左向きではXだけ反転する。
    /// </summary>
    public void SetWeaponHandAnchorPose(Vector2 rightFacingLocalPosition)
    {
        _weaponHandAnchorOverrideActive = true;
        _weaponHandAnchorOverrideRightLocalPosition = new Vector3(
            rightFacingLocalPosition.x,
            rightFacingLocalPosition.y,
            _rightFacingHandAnchorLocalPosition.z);
        ApplyWeaponHandAnchorFacing();
    }

    public void ClearWeaponHandAnchorPose()
    {
        _weaponHandAnchorOverrideActive = false;
        ApplyWeaponHandAnchorFacing();
    }

    /// <summary>
    /// 実射出方向へ体を向ける。SpriteRenderer.flipXと既存HandAnchor反転だけを更新し、
    /// Player Root / Visualのscaleは変更しない。ほぼ垂直なら現在方向を維持する。
    /// </summary>
    public bool SetLaunchFacing(float launchDirectionX, float horizontalThreshold = 0.1f)
    {
        if (Mathf.Abs(launchDirectionX) < Mathf.Max(0f, horizontalThreshold))
            return false;

        _facingRight = launchDirectionX > 0f;
        _backwardAim = false;

        SpriteRenderer sprite = ResolveBodySprite();
        if (sprite != null)
            sprite.flipX = !_facingRight;

        if (_anim != null && HasAnimatorParam("BackwardAim", AnimatorControllerParameterType.Bool))
            _anim.SetBool("BackwardAim", false);

        ApplyWeaponHandAnchorFacing();
        return true;
    }

    public AudioClip LastJumpVoiceClip { get; private set; }

    public int JumpVoicePlayCount { get; private set; }

    public int LandingSoundPlayCount { get; private set; }



    private void Awake()

    {

        _morningStarLauncher = GetComponent<MorningStarLauncher>();

        _rigid = GetComponent<Rigidbody2D>();

        ResolveGroundCheckCollider();

        if (_playerHealth == null)

            _playerHealth = GetComponent<PlayerHealth>();

    }



    private void OnEnable()

    {

        if (_playerHealth != null)

            _playerHealth.OnDead += HandlePlayerDead;

    }



    private void OnDisable()

    {

        if (_playerHealth != null)

            _playerHealth.OnDead -= HandlePlayerDead;



        StopFootstepAudio();

        ClearTensionMomentum();

        EndAirTensionGravityAssist();

        ResetDraggingMoveAssist();

    }



    private void Start()

    {

        if (_rigid == null)

            _rigid = GetComponent<Rigidbody2D>();

        _anim = GetComponent<Animator>();

        if (_bodySprite == null)

            _bodySprite = GetComponentInChildren<SpriteRenderer>();

        ResolveWeaponHandAnchor();

        _bjump = false;

        RefreshGroundedState();

        ConfigureAudioSources();

        ApplyMovementFacingVisual();

    }



    private void Update()

    {

        PollMovementInput();



        bool grounded = IsGrounded;

        bool landedThisFrame = _groundStateInitialized
            && !_wasGrounded
            && grounded;

        if (_anim != null)
        {
            _anim.SetBool("Walk", Mathf.Abs(_moveInput.x) > 0.01f);
            _anim.SetBool("Jump", !grounded);
            _anim.SetFloat("VerticalSpeed", _rigid != null ? _rigid.linearVelocity.y : 0f);

            if (landedThisFrame)
                _anim.SetTrigger("Land");
        }

        if (landedThisFrame)
        {
            PlayLandingSound();

            Landed?.Invoke();
        }

        _groundStateInitialized = true;
        _wasGrounded = grounded;



        UpdateFootstepAudio(grounded);



        RefreshBackwardAimFromMoveInput();

        UpdateMovementFacing();



        _coyoteTimer = Mathf.Max(0f, _coyoteTimer - Time.deltaTime);

        _jumpBufferTimer = Mathf.Max(0f, _jumpBufferTimer - Time.deltaTime);

    }



    private void FixedUpdate()

    {

        RefreshGroundedState();

        bool grounded = IsGrounded;

        bool canJump = grounded || _coyoteTimer > 0f;



        if (_jumpBufferTimer > 0f && canJump && !_bjump)

        {

            _rigid.AddForce(Vector2.up * _jumpSpeed, ForceMode2D.Impulse);

            StopFootstepAudio();

            PlayJumpSound();

            _bjump = true;

            ClearGroundedStateForJump();

            _jumpBufferTimer = 0f;

            _coyoteTimer = 0f;

        }



        ApplyHorizontalMovement(grounded);

        ApplyJumpGravity();

    }



    /// <summary>Input System コールバック＋キーボード直読み。武器中も毎フレーム更新。</summary>

    private void PollMovementInput()

    {

        float x = _actionMoveInput.x;



        Keyboard kb = Keyboard.current;

        if (kb != null)

        {

            float kx = 0f;

            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) kx -= 1f;

            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) kx += 1f;

            if (Mathf.Abs(kx) > Mathf.Abs(x))

                x = kx;

        }



        Gamepad pad = Gamepad.current;

        if (pad != null)

        {

            Vector2 stick = pad.leftStick.ReadValue();

            if (Mathf.Abs(stick.x) > Mathf.Abs(x))

                x = stick.x;

        }



        x = Mathf.Clamp(x, -1f, 1f);
        _moveInput = new Vector2(x, 0f);

    }



    private void ApplyHorizontalMovement(bool grounded)

    {

        float h = _moveInput.x;

        NormalizeStoppedDraggingMomentum();

        float moveF = grounded ? _groundMoveForce : _groundMoveForce * _airMoveFactor;

        float baseDrag = grounded ? _groundLinearDragX : _airLinearDragX;

        float draggingAssistForce = CalculateDraggingMoveAssistForce(
            h,
            grounded,
            moveF,
            baseDrag);

        float drag = baseDrag;

        drag *= UpdateTensionMomentumDragMultiplier();



        if (Mathf.Abs(h) > 0.01f)

            _rigid.AddForce(
                new Vector2(h * (moveF + draggingAssistForce), 0f),
                ForceMode2D.Force);



        _rigid.AddForce(new Vector2(-_rigid.linearVelocity.x * drag, 0f), ForceMode2D.Force);

    }

    private float CalculateDraggingMoveAssistForce(
        float horizontalInput,
        bool grounded,
        float moveForce,
        float baseDrag)

    {

        bool isGroundDragging = grounded
            && _morningStarLauncher != null
            && _morningStarLauncher.CanUseDraggingMoveAssist;

        if (!isGroundDragging || baseDrag <= 0.001f)

        {

            ResetDraggingMoveAssist();

            return 0f;

        }

        UpdateDraggingBallPositionSample();

        if (!_enableDraggingMoveAssist || Mathf.Abs(horizontalInput) <= 0.01f)
        {
            ResetDraggingAssistInput();
            return 0f;
        }

        float inputSign = Mathf.Sign(horizontalInput);
        if (_draggingAssistInputSign != 0f && inputSign != _draggingAssistInputSign)
            _draggingAssistRamp = 0f;
        _draggingAssistInputSign = inputSign;

        if (_draggingAssistRampTime <= 0f)
            _draggingAssistRamp = 1f;
        else
            _draggingAssistRamp = Mathf.MoveTowards(
                _draggingAssistRamp,
                1f,
                Time.fixedDeltaTime / _draggingAssistRampTime);

        float ballSpeedTowardInput = Mathf.Max(
            0f,
            _draggingActualBallHorizontalSpeed * inputSign);
        float ballMotion01 = Mathf.Clamp01(
            ballSpeedTowardInput / _draggingBallSpeedForFullAssist);
        float motionBasedAssist = Mathf.Lerp(
            _draggingStartAssist,
            _draggingCruiseAssist,
            ballMotion01);
        float assistRatio = Mathf.Lerp(
            _draggingStartAssist,
            motionBasedAssist,
            _draggingAssistRamp);

        float normalTargetSpeed = Mathf.Abs(horizontalInput) * moveForce / baseDrag;
        float assistedTargetSpeed = normalTargetSpeed * _draggingMaxSpeedMultiplier;
        float speedTowardInput = _rigid.linearVelocity.x * inputSign;
        float speedRoom = 1f - Mathf.InverseLerp(
            normalTargetSpeed,
            Mathf.Max(normalTargetSpeed + 0.01f, assistedTargetSpeed),
            speedTowardInput);

        return moveForce
            * assistRatio
            * speedRoom;

    }

    /// <summary>
    /// ChainConstraintがRigidbody速度を0にしていても、FixedUpdate間の実位置差から
    /// Dragging中の鉄球が実際に地面を移動した横速度を取得する。
    /// </summary>
    private void UpdateDraggingBallPositionSample()
    {
        if (!_morningStarLauncher.TryGetMorningStarPosition(out Vector2 currentPosition))
        {
            _hasDraggingBallPositionSample = false;
            _draggingActualBallHorizontalSpeed = 0f;
            return;
        }

        if (!_hasDraggingBallPositionSample)
        {
            _draggingPreviousBallPosition = currentPosition;
            _hasDraggingBallPositionSample = true;
            _draggingActualBallHorizontalSpeed = 0f;
            return;
        }

        float dt = Mathf.Max(0.0001f, Time.fixedDeltaTime);
        _draggingActualBallHorizontalSpeed =
            (currentPosition.x - _draggingPreviousBallPosition.x) / dt;
        _draggingPreviousBallPosition = currentPosition;
    }

    private void ResetDraggingAssistInput()
    {
        _draggingAssistRamp = 0f;
        _draggingAssistInputSign = 0f;
    }

    private void ResetDraggingMoveAssist()

    {

        ResetDraggingAssistInput();

        _draggingPreviousBallPosition = Vector2.zero;

        _hasDraggingBallPositionSample = false;

        _draggingActualBallHorizontalSpeed = 0f;

    }

    private float UpdateTensionMomentumDragMultiplier()

    {

        if (_tensionMomentumHoldTimer > 0f)

        {

            _tensionMomentumHoldTimer = Mathf.Max(
                0f,
                _tensionMomentumHoldTimer - Time.fixedDeltaTime);

            return _tensionMomentumDragMultiplier;

        }

        if (_tensionMomentumDragMultiplier >= 1f)

            return 1f;

        if (_tensionMomentumDecayRate <= 0f)

        {

            _tensionMomentumDragMultiplier = 1f;

            return 1f;

        }

        _tensionMomentumDragMultiplier = Mathf.MoveTowards(
            _tensionMomentumDragMultiplier,
            1f,
            _tensionMomentumDecayRate * Time.fixedDeltaTime);

        return _tensionMomentumDragMultiplier;

    }

    private void ClearTensionMomentum()

    {

        _tensionMomentumHoldTimer = 0f;

        _tensionMomentumDragMultiplier = 1f;

        _tensionMomentumDecayRate = 0f;

    }

    /// <summary>
    /// Recallを通らずDroppingのまま床を引きずる経路用の安全策。
    /// Playerが実際に停止した後まで張力用の低Dragだけを残さない。
    /// </summary>
    private void NormalizeStoppedDraggingMomentum()
    {
        if (_rigid == null || _morningStarLauncher == null)
            return;
        if (!_morningStarLauncher.CanUseDraggingMoveAssist)
            return;
        if (Mathf.Abs(_rigid.linearVelocity.x)
            > StationaryTensionMomentumSpeedThreshold)
            return;

        ClearTensionMomentum();
    }



    private void ApplyJumpGravity()

    {

        if (_airTensionGravityAssistActive && IsGrounded)
            EndAirTensionGravityAssist();

        if (TryApplyAirTensionGravityAssist())
            return;

        float vy = _rigid.linearVelocity.y;

        if (vy < 0f)

        {

            float extra = Physics2D.gravity.y * (_fallGravityMultiplier - 1f);

            _rigid.AddForce(new Vector2(0f, extra * _rigid.mass), ForceMode2D.Force);

        }

        else if (vy > 0f && !_jumpHeld)

        {

            float extra = Physics2D.gravity.y * (_jumpCutMultiplier - 1f);

            _rigid.AddForce(new Vector2(0f, extra * _rigid.mass), ForceMode2D.Force);

        }



        if (_rigid.linearVelocity.y < _maxFallSpeed)

            _rigid.linearVelocity = new Vector2(_rigid.linearVelocity.x, _maxFallSpeed);

    }

    private bool TryApplyAirTensionGravityAssist()

    {

        if (!_airTensionGravityAssistActive || _airTensionGravityAssistRemaining <= 0f)
            return false;

        float verticalSpeed = _rigid.linearVelocity.y;
        if (verticalSpeed > _airTensionAssistApexVelocity)
            return false;

        _airTensionGravityAssistRemaining = Mathf.Max(
            0f,
            _airTensionGravityAssistRemaining - Time.fixedDeltaTime);

        float normalFallGravityScale = _rigid.gravityScale
            + Mathf.Max(0f, _fallGravityMultiplier - 1f);
        float targetGravityAcceleration = Physics2D.gravity.y
            * normalFallGravityScale
            * _airTensionGravityMultiplier;
        float builtInGravityAcceleration = Physics2D.gravity.y * _rigid.gravityScale;
        float assistAcceleration = targetGravityAcceleration - builtInGravityAcceleration;
        _rigid.AddForce(
            new Vector2(0f, assistAcceleration * _rigid.mass),
            ForceMode2D.Force);

        if (_rigid.linearVelocity.y < _airTensionMaxFallSpeedDuringAssist)
        {
            _rigid.linearVelocity = new Vector2(
                _rigid.linearVelocity.x,
                _airTensionMaxFallSpeedDuringAssist);
        }

        if (_airTensionGravityAssistRemaining <= 0f)
            EndAirTensionGravityAssist();

        return true;

    }



    private void ConfigureAudioSources()

    {

        if (_footstepAudioSource == null)

            return;



        _footstepAudioSource.clip = _footstepGrassClip;

        _footstepAudioSource.loop = true;

        _footstepAudioSource.playOnAwake = false;

        _footstepAudioSource.volume = _footstepVolume;

    }



    private void UpdateFootstepAudio(bool grounded)

    {

        if (_footstepAudioSource == null || _rigid == null)

            return;



        bool isAlive = _playerHealth == null || !_playerHealth.IsDead;

        bool shouldPlay = grounded

            && !_bjump

            && isAlive

            && Mathf.Abs(_rigid.linearVelocity.x) > _footstepMinHorizontalSpeed;



        if (shouldPlay)

        {

            if (!_footstepAudioSource.isPlaying && _footstepAudioSource.clip != null)

                _footstepAudioSource.Play();

        }

        else

        {

            StopFootstepAudio();

        }

    }



    private void StopFootstepAudio()

    {

        if (_footstepAudioSource != null && _footstepAudioSource.isPlaying)

            _footstepAudioSource.Stop();

    }



    private void PlayJumpSound()

    {

        if (_sfxAudioSource != null && _jumpClip != null)

            _sfxAudioSource.PlayOneShot(_jumpClip, _jumpVolume);

        AudioClip voiceClip = GetRandomJumpVoiceClip();
        if (_sfxAudioSource == null || voiceClip == null)
            return;

        _sfxAudioSource.PlayOneShot(voiceClip, _jumpVoiceVolume);
        LastJumpVoiceClip = voiceClip;
        JumpVoicePlayCount++;

    }



    private AudioClip GetRandomJumpVoiceClip()

    {

        if (_jumpVoiceClips == null || _jumpVoiceClips.Length == 0)
            return null;

        int validClipCount = 0;
        foreach (AudioClip clip in _jumpVoiceClips)
        {
            if (clip != null)
                validClipCount++;
        }

        if (validClipCount == 0)
            return null;

        int selectedIndex = Random.Range(0, validClipCount);
        foreach (AudioClip clip in _jumpVoiceClips)
        {
            if (clip == null)
                continue;
            if (selectedIndex-- == 0)
                return clip;
        }

        return null;

    }



    private void PlayLandingSound()

    {

        if (_sfxAudioSource == null || _landingClip == null)
            return;

        _sfxAudioSource.PlayOneShot(_landingClip, _landingVolume);
        LandingSoundPlayCount++;

    }



    private void HandlePlayerDead()

    {

        StopFootstepAudio();

        ClearTensionMomentum();

        EndAirTensionGravityAssist();

        ResetDraggingMoveAssist();

    }



    private void ResolveGroundCheckCollider()

    {

        if (_groundCheckCollider == null)

            _groundCheckCollider = GetComponent<Collider2D>();

    }



    private void RefreshGroundedState()

    {

        ResolveGroundCheckCollider();

        bool wasRawGrounded = _rawGrounded;

        bool risingFromJump = _bjump

            && _rigid != null

            && _rigid.linearVelocity.y > 0.05f;

        _rawGrounded = !risingFromJump && CheckGroundOverlap();



        if (_rawGrounded)

        {

            _groundedGraceTimer = _groundedGraceTime;

            _isGrounded = true;

            _coyoteTimer = 0f;

            if (_rigid == null || _rigid.linearVelocity.y <= 0.05f)

                _bjump = false;

            return;

        }



        if (wasRawGrounded && !_bjump)

            _coyoteTimer = Mathf.Max(_coyoteTimer, _coyoteTime);



        if (risingFromJump)

        {

            _groundedGraceTimer = 0f;

            _isGrounded = false;

            return;

        }



        _groundedGraceTimer = Mathf.Max(0f, _groundedGraceTimer - Time.fixedDeltaTime);

        _isGrounded = _groundedGraceTimer > 0f;

    }



    private bool CheckGroundOverlap()

    {

        if (_groundCheckCollider == null || !_groundCheckCollider.enabled)

            return false;



        GetGroundCheckBox(_groundCheckCollider.bounds, out Vector2 center, out Vector2 size);

        ContactFilter2D filter = new ContactFilter2D();

        filter.SetLayerMask(_groundLayers);

        filter.useTriggers = false;

        int hitCount = Physics2D.OverlapBox(center, size, 0f, filter, _groundCheckResults);



        for (int i = 0; i < hitCount; i++)

        {

            Collider2D hit = _groundCheckResults[i];

            _groundCheckResults[i] = null;

            if (hit != null && hit != _groundCheckCollider && !hit.transform.IsChildOf(transform))

                return true;

        }



        return false;

    }



    private void GetGroundCheckBox(Bounds colliderBounds, out Vector2 center, out Vector2 size)

    {

        float width = Mathf.Max(0.01f, colliderBounds.size.x * _groundCheckWidthRatio);

        float height = Mathf.Max(0.01f, _groundCheckHeight);

        center = new Vector2(

            colliderBounds.center.x,

            colliderBounds.min.y - height * 0.5f + _groundCheckVerticalOverlap);

        size = new Vector2(width, height);

    }



    private void ClearGroundedStateForJump()

    {

        _rawGrounded = false;

        _isGrounded = false;

        _groundedGraceTimer = 0f;

    }



    private void OnValidate()

    {

        if (_groundLayers.value == 0)

            _groundLayers = LayerMask.GetMask("Default", "Walls");

        _groundCheckWidthRatio = Mathf.Clamp(_groundCheckWidthRatio, 0.6f, 0.8f);

        _groundCheckHeight = Mathf.Clamp(_groundCheckHeight, 0.08f, 0.15f);

        _groundCheckVerticalOverlap = Mathf.Clamp(_groundCheckVerticalOverlap, 0f, 0.05f);

        _groundedGraceTime = Mathf.Max(0f, _groundedGraceTime);

        _draggingStartAssist = Mathf.Clamp01(_draggingStartAssist);

        _draggingCruiseAssist = Mathf.Clamp(
            Mathf.Max(_draggingStartAssist, _draggingCruiseAssist),
            0f,
            1f);

        _draggingAssistRampTime = Mathf.Max(0f, _draggingAssistRampTime);

        _draggingMaxSpeedMultiplier = Mathf.Clamp(
            _draggingMaxSpeedMultiplier,
            1f,
            1.05f);

        _draggingBallSpeedForFullAssist = Mathf.Max(0.01f, _draggingBallSpeedForFullAssist);

        ResolveGroundCheckCollider();

    }



    private void OnDrawGizmosSelected()

    {

        ResolveGroundCheckCollider();

        if (_groundCheckCollider == null)

            return;



        GetGroundCheckBox(_groundCheckCollider.bounds, out Vector2 center, out Vector2 size);

        Gizmos.color = Application.isPlaying && _isGrounded ? Color.green : Color.red;

        Gizmos.DrawWireCube(center, size);

    }



    public void _OnMove(InputAction.CallbackContext context)

    {

        Vector2 v = context.ReadValue<Vector2>();

        _actionMoveInput = new Vector2(v.x, 0f);

    }



    public void _OnJump(InputAction.CallbackContext context)

    {

        if (context.performed)

        {

            _jumpHeld = true;

            _jumpBufferTimer = _jumpBufferTime;

        }

        else if (context.canceled)

        {

            _jumpHeld = false;

        }

    }



    public void ApplyExternalImpulse(Vector2 worldImpulse, ForceMode2D mode = ForceMode2D.Impulse)

    {

        if (_rigid == null) return;

        _rigid.AddForce(worldImpulse, mode);

    }



    public void ApplyMorningStarBounce(float upwardImpulse)

    {

        if (_rigid == null) return;

        _rigid.AddForce(Vector2.up * upwardImpulse, ForceMode2D.Impulse);

    }



    public void SetAimFacing(float aimWorldX, bool backwardAim)

    {

        _hasAimTarget = true;

        _aimFacingWorldX = aimWorldX;

        _backwardAim = backwardAim;

        if (_anim != null && HasAnimatorParam("BackwardAim", AnimatorControllerParameterType.Bool))

            _anim.SetBool("BackwardAim", backwardAim);

        UpdateMovementFacing();

    }



    public void ClearAimFacing()

    {

        _hasAimTarget = false;

        _backwardAim = false;

        if (_anim != null && HasAnimatorParam("BackwardAim", AnimatorControllerParameterType.Bool))

            _anim.SetBool("BackwardAim", false);

        UpdateMovementFacing();

    }



    public void PlayAirLaunchBlink()

    {

        if (ResolveBodySprite() == null) return;

        if (_airLaunchBlinkRoutine != null)

            StopCoroutine(_airLaunchBlinkRoutine);

        _airLaunchBlinkRoutine = StartCoroutine(AirLaunchBlinkRoutine());

    }



    private void RefreshBackwardAimFromMoveInput()

    {

        if (!_hasAimTarget) return;



        float moveX = _moveInput.x;

        if (Mathf.Abs(moveX) < 0.01f) return;



        float aimDirX = _aimFacingWorldX - transform.position.x;

        if (Mathf.Abs(aimDirX) < 0.01f) return;



        bool backward = Mathf.Sign(moveX) != Mathf.Sign(aimDirX);

        if (backward == _backwardAim) return;



        _backwardAim = backward;

        if (_anim != null && HasAnimatorParam("BackwardAim", AnimatorControllerParameterType.Bool))

            _anim.SetBool("BackwardAim", backward);

    }



    private void UpdateMovementFacing()

    {

        // Hook/Swing中またはLaunch Pose中にHandAnchorを左右へ瞬間移動させると、
        // 物理ロープ支点や発射Animationの向きが崩れるため、完了まで向きを固定する。
        if (_morningStarLauncher != null
            && (_morningStarLauncher.IsHookedState || _morningStarLauncher.IsLaunchPoseActive))
        {
            ApplyMovementFacingVisual();
            return;
        }

        float moveX = _moveInput.x;

        if (moveX > 0.01f)

            _facingRight = true;

        else if (moveX < -0.01f)

            _facingRight = false;



        ApplyMovementFacingVisual();

    }



    private SpriteRenderer ResolveBodySprite()

    {

        if (_bodySprite != null)

            return _bodySprite;

        return GetComponentInChildren<SpriteRenderer>();

    }



    private void ApplyMovementFacingVisual()

    {

        SpriteRenderer sprite = ResolveBodySprite();

        if (sprite != null)

        {

            sprite.flipX = !_facingRight;

        }

        else if (_visualRoot != null)

        {

            Vector3 scale = _visualRoot.localScale;

            float absX = Mathf.Abs(scale.x);

            if (absX < 1e-4f)

                absX = 1f;

            scale.x = _facingRight ? absX : -absX;

            _visualRoot.localScale = scale;

        }



        ApplyWeaponHandAnchorFacing();

    }



    private void ResolveWeaponHandAnchor()

    {

        MorningStarLauncher launcher = GetComponent<MorningStarLauncher>();
        Transform resolvedAnchor = launcher != null ? launcher.HandAnchor : null;

        if (resolvedAnchor == null || resolvedAnchor == transform)
            resolvedAnchor = transform.Find("HandAnchor");

        if (resolvedAnchor == null || resolvedAnchor == transform)
            return;

        _weaponHandAnchor = resolvedAnchor;
        _rightFacingHandAnchorLocalPosition = resolvedAnchor.localPosition;
        _handAnchorFacingInitialized = true;

    }



    private void ApplyWeaponHandAnchorFacing()

    {

        if (!_handAnchorFacingInitialized || _weaponHandAnchor == null)
            return;

        Vector3 localPosition = _weaponHandAnchorOverrideActive
            ? _weaponHandAnchorOverrideRightLocalPosition
            : _rightFacingHandAnchorLocalPosition;
        localPosition.x = _facingRight
            ? localPosition.x
            : -localPosition.x;
        _weaponHandAnchor.localPosition = localPosition;

    }



    private IEnumerator AirLaunchBlinkRoutine()

    {

        SpriteRenderer sprite = ResolveBodySprite();

        if (sprite == null)

        {

            _airLaunchBlinkRoutine = null;

            yield break;

        }



        float elapsed = 0f;

        var wait = new WaitForSeconds(_airLaunchBlinkInterval);

        while (elapsed < _airLaunchBlinkDuration)

        {

            sprite.enabled = !sprite.enabled;

            yield return wait;

            elapsed += _airLaunchBlinkInterval;

        }



        sprite.enabled = true;

        _airLaunchBlinkRoutine = null;

    }



    private static bool HasAnimatorParam(Animator anim, string name, AnimatorControllerParameterType type)

    {

        if (anim == null || anim.runtimeAnimatorController == null) return false;

        foreach (AnimatorControllerParameter p in anim.parameters)

        {

            if (p.name == name && p.type == type) return true;

        }

        return false;

    }



    private bool HasAnimatorParam(string name, AnimatorControllerParameterType type)

        => HasAnimatorParam(_anim, name, type);

}


