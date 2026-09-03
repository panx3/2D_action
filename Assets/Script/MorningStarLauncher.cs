using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

/// <summary>
/// モーニングスター：状態管理・発射・回収・壁刺し・スイング・鎖解除。
/// 通常時は ChainConstraint2D、Hook/Swing中は既存 DistanceJoint2D を
/// Max Distance Only のロープとして使い、手元と固定支点の最大距離を守る。
/// </summary>
public class MorningStarLauncher : MonoBehaviour
{
    private const string LauncherPoseLayerName = "Launcher Pose";

    public enum MorningStarState
    {
        Dragging,
        SpinCharging,
        RecallBeforeThrow,
        Thrown,
        Dropping, // 旧テストScene/API互換。物理はDragging(Rest)と同一。
        Returning,
        Hooked,
        Swinging
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
    [SerializeField, Tooltip("Hook/Swing中の最大距離制約。未設定ならPlayer上の既存DistanceJoint2Dを使用")]
    private DistanceJoint2D hookRopeJoint;
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

    [Header("Chain / Tension")]
    [SerializeField, Min(0.1f), Tooltip("鎖の通常時最大長。値を上げるほどBallが遠くまで遅れて追従します")]
    private float maxRopeLength = 4.5f;
    [SerializeField, Range(0.5f, 0.98f), Tooltip("最大鎖長に対して張力が立ち上がり始める割合。上げるほどたるみが長く残ります")]
    private float tensionStartRatio = 0.84f;
    [SerializeField, Min(0f), Tooltip("鎖が張った時にPlayerをBall方向へ引く基本力。上げるほど引かれ方が強くなります")]
    private float tensionStrength = 42f;
    [SerializeField, Min(0f), Tooltip("BallがPlayerから離れる相対速度に対する張力。上げるほど高速射出時の引きが強くなります")]
    private float tensionDamping = 5f;
    [SerializeField, Min(0f), Tooltip("地上でPlayerへ加える張力の上限。空中ではAir Tension Multiplierを掛けた値が上限になります")]
    private float maxTensionForce = 65f;
    [SerializeField, Range(1f, 3f), Tooltip("空中時のPlayer張力倍率。上げるほど鎖が張った後のスプリント感が強くなります")]
    private float airTensionMultiplier = 1.8f;
    [SerializeField, Min(0.01f), Tooltip("地上でBallを引き続けた時、抵抗が軽くなるまでの時間")]
    private float groundPullEaseTime = 0.2f;
    [SerializeField, Range(0.1f, 0.7f), Tooltip("地上でBallが動き出した後の抵抗倍率。小さいほど軽く引きずれます")]
    private float movingPullResistance = 0.12f;
    [SerializeField, Min(0f), Tooltip("走りジャンプの水平慣性保護を開始する離地時の最低水平速度")]
    private float runJumpMomentumThreshold = 4f;
    [SerializeField, Min(0f), Tooltip("走りジャンプ直後にBall由来の水平抵抗を弱める時間")]
    private float jumpMomentumGraceTime = 0.3f;
    [SerializeField, Range(0f, 1f), Tooltip("慣性保護開始時にPlayerの進行・上昇へ逆らう張力倍率。時間経過で通常張力へ戻ります")]
    private float runJumpTensionMultiplier = 0.25f;

    [Header("Ball Physics")]
    [SerializeField, Min(0.01f), Tooltip("Ballの質量。衝突と慣性へ影響します。大幅に上げると操作性を損ないます")]
    private float ballMass = 0.35f;
    [SerializeField, Tooltip("Rest / Dragging時の重力倍率。上げるほど地面へ押し付けられ重量感が増します")]
    private float normalBallGravityScale = 2f;
    [SerializeField, Tooltip("Flying時の重力倍率。上げるほど射出軌道が早く落下します")]
    private float thrownBallGravityScale = 2f;
    [SerializeField, Min(0f), Tooltip("地上時の速度減衰。上げるほどBallが早く止まり、下げるほど慣性が残ります")]
    private float ballGroundLinearDamping = 0.08f;
    [SerializeField, Min(0f), Tooltip("Flying時の速度減衰。上げるほど飛距離が短くなります")]
    private float ballFlyingLinearDamping = 0f;
    [SerializeField, Min(0f), Tooltip("Ballの最大速度。射出と落下の暴走防止用です")]
    private float maxBallLinearSpeed = 20f;
    [SerializeField, Min(0f), Tooltip("Ballの最大落下速度。上げるほど速い落下と強い衝突を許可します")]
    private float maxBallFallSpeed = 22f;
    [SerializeField] private float spinChargeBallGravityScale = 0f;
    [SerializeField] private float hookedBallGravityScale = 0f;

    [Header("Launch")]
    [SerializeField, Min(0f), Tooltip("通常射出速度。上げるほどBallの初速と衝突の勢いが増します")]
    private float throwSpeed = 18f;
    [SerializeField, Min(1f), Tooltip("Flying開始時に使用する鎖長倍率。Rest移行時は基準長へ安全に戻れるまで維持します")]
    private float launchRopeLengthMultiplier = 1.4f;
    [SerializeField, Tooltip("射出速度へ加える下向き成分。上げるほどBallを狙った地面へ落としやすくなります")]
    private float postThrowDownwardBias = 0.25f;

    [Header("クリック照準・発射")]
    [SerializeField] private float minAimDistance = 0.2f;
    [SerializeField] private float launchStartOffset = 0.25f;
    [SerializeField] private float aimLaunchCooldown = 0f;
    [SerializeField] private float fireInputBufferTime = 0.18f;
    [SerializeField, Range(0f, 1f), Tooltip("この値未満の横成分では現在の向きを維持")]
    private float horizontalFacingThreshold = 0.1f;

    [Header("空中射出制限")]
    [SerializeField] private bool limitAirThrows = true;
    [SerializeField, Min(0)] private int maxAirThrows = 1;

    [Header("Air Launch Assist")]
    [SerializeField, Tooltip("空中射出直後だけ、Playerの落下を短時間弱めます")]
    private bool enableAirLaunchAssist = true;
    [SerializeField, Range(0.05f, 0.3f), Tooltip("ふわっと感が続く時間。上げるほど滞空感が長くなります")]
    private float airLaunchAssistDuration = 0.16f;
    [SerializeField, Range(0f, 1f), Tooltip("Assist中に残す通常重力の割合。下げるほどふわっとしますが、0でもPlayerを上向きには飛ばしません")]
    private float airLaunchGravityMultiplier = 0.4f;
    [SerializeField, Min(0f), Tooltip("射出瞬間に弱める下向き速度。上げるほど落下中のふわっと感が強くなります")]
    private float airLaunchFallingVelocityReduction = 1.4f;
    [SerializeField, Min(0f), Tooltip("降下中の空中射出直後に許可する最大落下速度。0で上限補正なし。軽い降下には影響しません")]
    private float airLaunchMaxFallSpeedAfterShot = 3f;

    [Header("Gamepad（追加入力）")]
    [SerializeField] private bool enableGamepadInput = true;
    [SerializeField, Range(0.05f, 1f)] private float gamepadFireThreshold = 0.70f;
    [SerializeField, Range(0.05f, 0.7f)] private float gamepadResetThreshold = 0.25f;
    [SerializeField] private bool useRightStickClickForCharge = true;
    [SerializeField] private bool useLeftTriggerForSwingHold = true;

    [Header("RecallBeforeThrow（発射前の見える引き寄せ）")]
    [SerializeField] private bool useVisibleRecallBeforeThrow = true;
    [SerializeField] private float visibleRecallTime = 0.12f;
    [SerializeField] private float recallEasePower = 2f;
    [SerializeField] private float recallHoldTime = 0.04f;
    [SerializeField] private float recallStartDelay = 0f;
    [SerializeField] private float chargedRecallTimeMultiplier = 0.6f;

    [Header("Flying 終了条件")]
    [SerializeField] private float maxThrownTime = 0.45f;
    [SerializeField] private float maxThrowDistance = 4.8f;
    [SerializeField] private float dropTransitionSpeed = 2f;

    [Header("Recall")]
    [SerializeField, Min(0f), Tooltip("明示RecallでBallが手元へ戻る速度。上げるほど回収が速くなります")]
    private float returnSpeed = 21f;
    [SerializeField] private float returnFinishDistance = 0.25f;

    [Header("Hook 判定")]
    [SerializeField] private bool allowFloorHook = false;
    [SerializeField] private bool allowWallHook = true;
    [SerializeField] private bool allowCeilingHook = true;
    [SerializeField, Range(0f, 1f)] private float floorNormalThreshold = 0.5f;

    [Header("Hooked")]
    [SerializeField] private float hookMinSpeed = 2f;
    [SerializeField] private float releaseBoost = 3f;
    [FormerlySerializedAs("leftClickReThrowFromHook")]
    [SerializeField] private bool shortClickRethrowFromHook = true;
    [SerializeField] private float rehookLockoutTime = 0.15f;
    [SerializeField] private float hookStickMinTime = 0.1f;
    [SerializeField] private float holdToSwingTime = 0.5f;
    [SerializeField] private bool releaseFromSwingToThrow = true;

    [Header("HookSwing")]
    [SerializeField] private float swingPullForce = 32f;
    [SerializeField] private float swingInputForce = 12f;
    [SerializeField] private float swingRadialDamping = 0.9f;
    [SerializeField, Range(0f, 1f)] private float swingTangentKeepRate = 0.95f;
    [SerializeField] private float maxSwingSpeed = 16f;

    [Header("Magnet Swing Assist")]
    [SerializeField, Min(0f)] private float magnetSwingForce = 12f;
    [SerializeField, Min(0f)] private float magnetMaxSwingSpeed = 16f;

    [Header("Hooked フィードバック")]
    [SerializeField] private float hookedChainWidthMultiplier = 1.25f;
    [SerializeField] private Color normalChainColor = Color.white;
    [SerializeField] private Color hookedChainColor = new Color(1f, 0.9f, 0.5f, 1f);
    [SerializeField] private float hookHitStopTime = 0.04f;
    [SerializeField] private GameObject hookSparkPrefab;

    [Header("SpinCharge")]
    [SerializeField] private bool enableSpinCharge = true;
    [SerializeField] private float holdThreshold = 0.28f;
    [SerializeField] private float maxChargeTime = 1.2f;
    [SerializeField] private float spinRadius = 1f;
    [SerializeField] private float spinAngularSpeed = 720f;
    [SerializeField] private float minChargedThrowMultiplier = 1.2f;
    [SerializeField] private float maxChargedThrowMultiplier = 2.2f;
    [SerializeField] private Collider2D spinGuardCollider;

    [Header("Ground Impact Camera Shake")]
    [SerializeField] private CameraShake2D cameraShake;
    [SerializeField, Min(0f)] private float minimumGroundImpactSpeed = 7f;
    [SerializeField, Min(0f)] private float shakeDuration = 0.10f;
    [SerializeField, Min(0f)] private float minimumShakeStrength = 0.06f;
    [SerializeField, Min(0f)] private float maximumShakeStrength = 0.16f;
    [SerializeField, Min(0f)] private float maxImpactSpeed = 20f;
    [SerializeField, Min(0f)] private float shakeCooldown = 0.10f;

    [Header("Ground Impact SFX")]
    [SerializeField] private AudioSource groundImpactAudioSource;
    [SerializeField] private AudioClip groundImpactClip;
    [SerializeField, Range(0f, 1f)] private float groundImpactVolume = 0.56f;

    [Header("Launch SFX")]
    [SerializeField] private AudioSource sfxAudioSource;
    [SerializeField] private AudioClip morningStarLaunchClip;
    [SerializeField, Range(0f, 1f)] private float morningStarLaunchVolume = 0.55f;

    [Header("照準ガイド")]
    [SerializeField] private bool showAimGuide = true;
    [SerializeField] private float aimGuideMaxLength = 8f;

    [Header("Animator（任意）")]
    [SerializeField] private Animator animator;
    [SerializeField] private string backwardAimParam = "BackwardAim";
    [SerializeField] private string launchChargeParam = "LaunchCharge";
    [SerializeField] private string launchFireTrigger = "LaunchFire";
    [SerializeField] private string launchPoseActiveParam = "LaunchPoseActive";
    [SerializeField] private string launchRecoilTrigger = "LaunchRecoil";
    [SerializeField] private float launchRecoilDelay = 0.08f;

    [Header("Launch Pose Anchor")]
    [SerializeField, Tooltip("右向きの構えフレームで棒先端へ合わせたHandAnchor localPosition")]
    private Vector2 launchReadyAnchorLocalPosition = new Vector2(-1.68f, -1.15f);
    [SerializeField, Tooltip("右向きの投げ切りフレームで棒先端へ合わせたHandAnchor localPosition")]
    private Vector2 launchAnchorLocalPosition = new Vector2(2.28f, -0.05f);
    [SerializeField, Min(0f), Tooltip("構えから投げ切りAnchorへ切り替える時間。Clipの2枚目と同じ0.06秒")]
    private float launchPoseForwardFrameTime = 0.06f;
    [SerializeField, Min(0.01f), InspectorName("Air Launch Pose Hold Duration"), Tooltip("空中射出後に腕を前へ突き出したPoseを維持する時間")]
    private float launchPoseMaxHoldTime = 0.90f;
    [SerializeField, Min(0.01f), InspectorName("Ground Launch Pose Hold Duration"), Tooltip("地上射出後に腕を前へ突き出したPoseを維持する時間")]
    private float groundLaunchPoseHoldDuration = 1.35f;
    [SerializeField, Range(0.05f, 0.15f), InspectorName("Chain Anchor Follow Time"), Tooltip("Spriteや向きで棒先端が変わった際に、鎖の物理支点と見た目を新しい位置へ追従させる時間")]
    private float chainAnchorVisualFollowTime = 0.08f;

    [Header("Debug")]
    [SerializeField] private bool debugLog;

    private Rigidbody2D _playerRb;
    private MorningStarState _state = MorningStarState.Dragging;
    private float _nextLaunchTime;
    private int _airThrowsUsed;
    private Vector2 _pendingLaunchDir;
    private Vector2 _recallStartPosition;
    private Vector2 _recallTargetPosition;
    private float _recallTimer;
    private float _recallHoldTimer;
    private float _recallDelayTimer;
    private float _activeRecallDuration;
    private float _pendingThrowSpeedMultiplier = 1f;
    private float _thrownElapsed;
    private Vector2 _hookPoint;
    private bool _isHooked;
    private MagnetPoint _attachedMagnet;
    private float _rehookLockoutTimer;
    private float _fireBufferTimer;
    private Vector2 _bufferedAimDirection;
    private float _recoilTriggerTime = -1f;
    private float _hookedAtTime = -1f;
    private float _fireHoldTime;
    private bool _isFireHolding;
    private float _hookClickHoldTimer;
    private bool _hookClickHolding;
    private bool _requireReleaseBeforeHookClick;
    private float _chargeTime;
    private float _spinAngle;
    private Vector2 _pendingAimDirection;
    private Vector2 _lastValidAimDirection;
    private bool _rightStickReady = true;
    private Vector2 _lastGamepadAimDirection = Vector2.right;
    private bool _gamepadFirePressedThisFrame;
    private float _lastCharge01;
    private float _lastSpeedMultiplier = 1f;
    private float _nextGroundImpactShakeTime;
    private Coroutine _hitStopRoutine;
    private float _savedTimeScale = 1f;
    private readonly Dictionary<EntityId, float> _lastCombatHitTimeByColliderId = new Dictionary<EntityId, float>();
    private Color _defaultLineColor = Color.white;
    private float _defaultLineWidth;
    private bool _lineVisualDefaultsCached;
    private RigidbodyConstraints2D _restBallConstraints;
    private CollisionDetectionMode2D _restBallCollisionDetectionMode;
    private RigidbodyInterpolation2D _restBallInterpolation;
    private bool _launchPoseActive;
    private bool _launchForwardAnchorApplied;
    private float _launchPoseElapsed;
    private float _activeLaunchPoseHoldDuration;
    private Vector3 _visualRopeAnchorLocal;
    private Vector3 _visualRopeAnchorLocalVelocity;
    private bool _visualRopeAnchorInitialized;
    private int _visualRopeAnchorUpdatedFrame = -1;
    private bool _playerLandingSubscribed;
    private PlayerHealth _playerHealth;
    private bool _playerDeathSubscribed;
    private bool _launchRopeLengthActive;
    private float _airLaunchAssistRemaining;

    public float LastGroundImpactSpeed { get; private set; }
    public float LastGroundImpactShakeStrength { get; private set; }
    public int GroundImpactShakeCount { get; private set; }
    public int GroundImpactSoundCount { get; private set; }
    private int _hashBackwardAim;
    private int _hashLaunchCharge;
    private int _hashLaunchFire;
    private int _hashLaunchPoseActive;
    private int _hashLaunchRecoil;

    public MorningStarState State => _state;
    public MorningStarState CurrentState => _state;
    public float MaxRopeLength => GetEffectiveRopeLength();
    public float BaseMaxRopeLength => Mathf.Max(0.1f, maxRopeLength);
    public float LaunchRopeLengthMultiplier => Mathf.Max(1f, launchRopeLengthMultiplier);
    public bool IsLaunchRopeLengthActive => _launchRopeLengthActive;
    public float RestBallMass => Mathf.Max(0.01f, ballMass);
    public float RestBallGravityScale => normalBallGravityScale;
    public float RestBallLinearDamping => ballGroundLinearDamping;
    public bool IsAirLaunchAssistActive => _airLaunchAssistRemaining > 0f;
    public float AirTensionMultiplier => airTensionMultiplier;
    public float LastCharge01 => _lastCharge01;
    public float LastSpeedMultiplier => _lastSpeedMultiplier;
    public Transform HandAnchor => handAnchor;
    public Vector2 RopeAnchorWorld => GetPlayerRopeAnchorWorld();
    public Vector2 VisualRopeAnchorWorld => GetVisualRopeAnchorWorld();
    public int RopeContactPointCount => chainConstraint != null
        ? chainConstraint.RopeContactPointCount
        : 0;
    public bool IsLaunchPoseActive => _launchPoseActive;
    public Vector2 LaunchReadyAnchorLocalPosition => launchReadyAnchorLocalPosition;
    public Vector2 LaunchAnchorLocalPosition => launchAnchorLocalPosition;
    public float LaunchPoseMaxHoldTime => launchPoseMaxHoldTime;
    public float GroundLaunchPoseHoldDuration => groundLaunchPoseHoldDuration;

    public Vector2 GetRopeContactPoint(int index)
    {
        return chainConstraint != null
            ? chainConstraint.GetRopeContactPoint(index)
            : Vector2.zero;
    }

    public bool IsRopeLineVisible =>
        _state == MorningStarState.Dragging
        || _state == MorningStarState.SpinCharging
        || _state == MorningStarState.RecallBeforeThrow
        || _state == MorningStarState.Thrown
        || _state == MorningStarState.Dropping
        || _state == MorningStarState.Returning
        || _state == MorningStarState.Hooked
        || _state == MorningStarState.Swinging;

    public bool IsHookedState =>
        _state == MorningStarState.Hooked || _state == MorningStarState.Swinging;

    /// <summary>
    /// Playerのリスポーン後に、鉄球・鎖・入力ラッチを共通Restへ戻す。
    /// ゲーム開始時・Recall完了時と同じDragging初期化経路で物理設定を再構築する。
    /// </summary>
    public void ResetForRespawn()
    {
        EndLaunchPose();
        SetChainConstraintActive(false);
        SetLaunchRopeLengthActive(false);
        _airThrowsUsed = 0;
        _nextLaunchTime = 0f;
        _fireBufferTimer = 0f;
        _bufferedAimDirection = Vector2.zero;
        _pendingLaunchDir = Vector2.zero;
        _pendingAimDirection = Vector2.zero;
        _gamepadFirePressedThisFrame = false;
        _recoilTriggerTime = -1f;

        Gamepad pad = Gamepad.current;
        _rightStickReady = pad == null
            || pad.rightStick.ReadValue().magnitude <= gamepadResetThreshold;

        if (morningStarRb != null)
        {
            morningStarRb.linearVelocity = Vector2.zero;
            morningStarRb.angularVelocity = 0f;
            morningStarRb.WakeUp();
        }

        EnterDraggingState(true);

        if (animator != null)
        {
            if (HasAnimatorParam(_hashLaunchFire, AnimatorControllerParameterType.Trigger))
                animator.ResetTrigger(_hashLaunchFire);
            if (HasAnimatorParam(_hashLaunchRecoil, AnimatorControllerParameterType.Trigger))
                animator.ResetTrigger(_hashLaunchRecoil);
        }

        if (player != null)
            player.ClearAimFacing();
    }

    /// <summary>
    /// MagnetPoint が鉄球を吸着完了状態へ移すための入口。
    /// 通常の壁 Hook と同じ固定・スイング・再射出経路を使う。
    /// </summary>
    public bool TryAttachToMagnet(MagnetPoint magnet, Rigidbody2D targetBody, Vector2 anchorPosition)
    {
        if (magnet == null || targetBody == null || targetBody != morningStarRb)
            return false;

        if (_state == MorningStarState.RecallBeforeThrow
            || _state == MorningStarState.Returning
            || _state == MorningStarState.SpinCharging)
        {
            return false;
        }

        if (_isHooked)
        {
            if (_attachedMagnet != magnet)
                return false;

            _hookPoint = anchorPosition;
            return true;
        }

        BeginHook(anchorPosition);
        _attachedMagnet = magnet;
        BeginSwinging();
        GrantMagnetEscapeThrow();
        if (_isHooked && _state == MorningStarState.Swinging)
            EndLaunchPose();
        return _isHooked && _state == MorningStarState.Swinging;
    }

    public bool IsAttachedToMagnet(MagnetPoint magnet, Rigidbody2D targetBody)
    {
        return magnet != null
            && targetBody != null
            && targetBody == morningStarRb
            && _attachedMagnet == magnet
            && _isHooked
            && (_state == MorningStarState.Hooked || _state == MorningStarState.Swinging);
    }

    private void Awake()
    {
        MigrateLegacyDefaults();

        if (usePlayerOnSameObject && playerRigidbody2D == null)
            playerRigidbody2D = GetComponent<Rigidbody2D>();
        if (hookRopeJoint == null)
            hookRopeJoint = GetComponent<DistanceJoint2D>();
        if (hookRopeJoint != null)
            hookRopeJoint.enabled = false;
        if (player == null && playerRigidbody2D != null)
            player = playerRigidbody2D.GetComponent<Player>();
        if (_playerHealth == null)
            _playerHealth = GetComponent<PlayerHealth>();
        if (sfxAudioSource == null)
        {
            Transform sfxTransform = transform.Find("SfxAudioSource");
            if (sfxTransform != null)
                sfxAudioSource = sfxTransform.GetComponent<AudioSource>();
        }
        if (cameraShake == null && Camera.main != null)
            cameraShake = Camera.main.GetComponent<CameraShake2D>();
        if (animator == null)
            animator = GetComponent<Animator>();
        if (handAnchor == null)
            handAnchor = transform;

        _hashBackwardAim = Animator.StringToHash(backwardAimParam);
        _hashLaunchCharge = Animator.StringToHash(launchChargeParam);
        _hashLaunchFire = Animator.StringToHash(launchFireTrigger);
        _hashLaunchPoseActive = Animator.StringToHash(launchPoseActiveParam);
        _hashLaunchRecoil = Animator.StringToHash(launchRecoilTrigger);

        // AnimatorControllerのSerialized Weightが欠落していても、
        // 実射出Triggerを受けるVisualレイヤーを確実に有効化する。
        if (animator != null)
        {
            int launcherPoseLayer = animator.GetLayerIndex(LauncherPoseLayerName);
            if (launcherPoseLayer >= 0)
                animator.SetLayerWeight(launcherPoseLayer, 1f);
        }
    }

    private void OnValidate()
    {
        launchRopeLengthMultiplier = Mathf.Max(1f, launchRopeLengthMultiplier);
        horizontalFacingThreshold = Mathf.Clamp01(horizontalFacingThreshold);
        ballMass = Mathf.Max(0.01f, ballMass);
        ballGroundLinearDamping = Mathf.Max(0f, ballGroundLinearDamping);
        ballFlyingLinearDamping = Mathf.Max(0f, ballFlyingLinearDamping);
        tensionStartRatio = Mathf.Clamp(tensionStartRatio, 0.5f, 0.98f);
        tensionStrength = Mathf.Max(0f, tensionStrength);
        tensionDamping = Mathf.Max(0f, tensionDamping);
        maxTensionForce = Mathf.Max(0f, maxTensionForce);
        airTensionMultiplier = Mathf.Max(1f, airTensionMultiplier);
        groundPullEaseTime = Mathf.Max(0.01f, groundPullEaseTime);
        movingPullResistance = Mathf.Clamp(movingPullResistance, 0.1f, 0.7f);
        runJumpMomentumThreshold = Mathf.Max(0f, runJumpMomentumThreshold);
        jumpMomentumGraceTime = Mathf.Max(0f, jumpMomentumGraceTime);
        runJumpTensionMultiplier = Mathf.Clamp01(runJumpTensionMultiplier);
        airLaunchAssistDuration = Mathf.Max(0f, airLaunchAssistDuration);
        airLaunchGravityMultiplier = Mathf.Clamp01(airLaunchGravityMultiplier);
        airLaunchFallingVelocityReduction = Mathf.Max(0f, airLaunchFallingVelocityReduction);
        airLaunchMaxFallSpeedAfterShot = Mathf.Max(0f, airLaunchMaxFallSpeedAfterShot);
        launchPoseMaxHoldTime = Mathf.Max(0.01f, launchPoseMaxHoldTime);
        groundLaunchPoseHoldDuration = Mathf.Max(0.01f, groundLaunchPoseHoldDuration);
        chainAnchorVisualFollowTime = Mathf.Clamp(chainAnchorVisualFollowTime, 0.05f, 0.15f);
        if (hookableLayers.value == 0)
            hookableLayers = LayerMask.GetMask("Walls", "Default");
        if (enemyLayers.value == 0)
            enemyLayers = LayerMask.GetMask("Enemy");
        if (breakableLayers.value == 0)
            breakableLayers = LayerMask.GetMask("Walls");
        SyncRopeLengthToConstraint();
    }

    private void OnEnable()
    {
        SubscribeToPlayerLanding();
        SubscribeToPlayerDeath();
    }

    private void Start()
    {
        SubscribeToPlayerLanding();
        SubscribeToPlayerDeath();

        _playerRb = playerRigidbody2D;
        EnsureHookRopeJoint();
        SetHookRopeJointActive(false);
        if (morningStarRb == null)
        {
            GameObject head = GameObject.FindGameObjectWithTag("morningstar");
            if (head != null)
                morningStarRb = head.GetComponent<Rigidbody2D>();
        }

        if (morningStarRb != null)
        {
            _restBallConstraints = morningStarRb.constraints;
            _restBallCollisionDetectionMode = morningStarRb.collisionDetectionMode;
            _restBallInterpolation = morningStarRb.interpolation;
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
            lineRenderer.enabled = chainLineController == null;
        }

        if (aimGuideLineRenderer != null)
        {
            aimGuideLineRenderer.positionCount = 2;
            aimGuideLineRenderer.enabled = false;
        }

        EnterDraggingState(true);
    }

    private void OnDisable()
    {
        UnsubscribeFromPlayerLanding();
        UnsubscribeFromPlayerDeath();
        StopAirLaunchAssist();
        EndLaunchPose();
        SetLaunchRopeLengthActive(false);
        SetHookRopeJointActive(false);
    }

    private void Update()
    {
        if (morningStarRb == null)
            return;

        UpdateLaunchPose();
        _rehookLockoutTimer = Mathf.Max(0f, _rehookLockoutTimer - Time.deltaTime);

        UpdateGamepadInputState();
        ProcessRecoilTrigger();
        UpdateFallbackLineRenderer();

        // 飛翔中、または手元から離れたRestの回収は明示操作だけ。
        if ((_state == MorningStarState.Thrown
             || _state == MorningStarState.Dropping
             || (_state == MorningStarState.Dragging && !IsBallAtReturnSocket()))
            && WasReleasePressedThisFrame())
        {
            BeginReturn();
            return;
        }

        // 1. Release（Hooked / HookSwing）
        if ((_state == MorningStarState.Hooked || _state == MorningStarState.Swinging)
            && WasReleasePressedThisFrame())
        {
            BeginRelease();
            return;
        }

        // 2. Hooked / HookSwing 専用（Fire Buffer に入れない）
        if (_state == MorningStarState.Hooked)
        {
            HandleHookedClickInput();
            return;
        }

        if (_state == MorningStarState.Swinging)
        {
            HandleSwingingInput();
            return;
        }

        if (_state == MorningStarState.SpinCharging)
        {
            HandleSpinChargingReleaseInput();
            return;
        }

        // 3. Dragging / Dropping
        if (_state == MorningStarState.Dragging || _state == MorningStarState.Dropping)
        {
            if (enableSpinCharge)
                HandleSpinChargeInput();
            else
                HandleSimpleFireInput();
        }

        // 4. Returning / RecallBeforeThrow / Thrown の Fire Buffer
        ProcessBufferedFireInputForRestrictedStates();

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
        UpdateAirLaunchAssist(dt);
        switch (_state)
        {
            case MorningStarState.Dragging:
                TryRestoreBaseRopeLength();
                break;

            case MorningStarState.SpinCharging:
                UpdateSpinChargingFixed(dt);
                break;

            case MorningStarState.RecallBeforeThrow:
                UpdateRecallBeforeThrow(dt);
                break;

            case MorningStarState.Thrown:
                UpdateThrown(dt, hand);
                break;

            case MorningStarState.Dropping:
                TryRestoreBaseRopeLength();
                break;

            case MorningStarState.Returning:
                UpdateReturning(dt);
                break;

            case MorningStarState.Hooked:
                UpdateHookedFixed(dt);
                break;

            case MorningStarState.Swinging:
                UpdateSwingingFixed(dt);
                break;
        }

        LimitBallFallSpeed();
    }

    /// <summary>
    /// 状態変更の唯一の入口。Rigidbody2Dと鎖の設定はここからだけ変更する。
    /// </summary>
    private void TransitionToState(MorningStarState nextState)
    {
        if (nextState != MorningStarState.Thrown)
            StopAirLaunchAssist();

        _state = nextState;

        bool useLaunchLength = nextState == MorningStarState.Thrown
            || ShouldKeepLaunchRopeLengthAfterFlight(nextState);
        SetLaunchRopeLengthActive(useLaunchLength);

        bool useFreeRope = nextState == MorningStarState.Dragging
            || nextState == MorningStarState.Thrown
            || nextState == MorningStarState.Dropping;
        SetChainConstraintActive(useFreeRope);
        if (chainConstraint != null)
            chainConstraint.SetFlyingTrajectoryPriority(nextState == MorningStarState.Thrown);

        bool useHookRope = (nextState == MorningStarState.Hooked
                            || nextState == MorningStarState.Swinging)
                           && _isHooked;
        SetHookRopeJointActive(useHookRope);

        ConfigureBallForState(nextState);
    }

    private void MigrateLegacyDefaults()
    {
        if ((launchReadyAnchorLocalPosition - new Vector2(-2.03f, -1.02f)).sqrMagnitude < 0.0001f)
            launchReadyAnchorLocalPosition = new Vector2(-1.68f, -1.15f);

        if ((launchAnchorLocalPosition - new Vector2(2.87f, 0.39f)).sqrMagnitude < 0.0001f)
            launchAnchorLocalPosition = new Vector2(2.28f, -0.05f);

        // 旧既定値の組だけを軽いDragging設定へ移行し、個別調整値は保持する。
        if (Mathf.Approximately(groundPullEaseTime, 0.45f)
            && Mathf.Approximately(movingPullResistance, 0.35f))
        {
            groundPullEaseTime = 0.20f;
            movingPullResistance = 0.12f;
        }
    }

    private void ConfigureBallForState(MorningStarState state)
    {
        if (morningStarRb == null)
            return;

        // すべての状態を同じ基準値から構築し、前状態の物理値を持ち越さない。
        morningStarRb.bodyType = RigidbodyType2D.Dynamic;
        morningStarRb.mass = Mathf.Max(0.01f, ballMass);
        morningStarRb.constraints = _restBallConstraints;
        morningStarRb.collisionDetectionMode = _restBallCollisionDetectionMode;
        morningStarRb.interpolation = _restBallInterpolation;
        morningStarRb.gravityScale = normalBallGravityScale;
        morningStarRb.linearDamping = ballGroundLinearDamping;

        switch (state)
        {
            case MorningStarState.SpinCharging:
                morningStarRb.gravityScale = spinChargeBallGravityScale;
                morningStarRb.linearDamping = 0f;
                break;

            case MorningStarState.RecallBeforeThrow:
            case MorningStarState.Returning:
                morningStarRb.gravityScale = 0f;
                morningStarRb.linearDamping = 0f;
                break;

            case MorningStarState.Thrown:
                morningStarRb.gravityScale = thrownBallGravityScale;
                morningStarRb.linearDamping = ballFlyingLinearDamping;
                break;

            case MorningStarState.Dropping:
                // 旧Scene/外部コードとの互換用。物理はRestと完全に同じ。
                morningStarRb.gravityScale = normalBallGravityScale;
                morningStarRb.linearDamping = ballGroundLinearDamping;
                break;

            case MorningStarState.Hooked:
            case MorningStarState.Swinging:
                morningStarRb.gravityScale = hookedBallGravityScale;
                morningStarRb.linearDamping = 0f;
                break;
        }
    }

    private void LimitBallFallSpeed()
    {
        if (morningStarRb == null || maxBallFallSpeed <= 0f)
            return;

        Vector2 velocity = morningStarRb.linearVelocity;
        if (velocity.y < -maxBallFallSpeed)
        {
            velocity.y = -maxBallFallSpeed;
            morningStarRb.linearVelocity = velocity;
        }
    }

    private void BeginAirLaunchAssist(bool launchedInAir)
    {
        StopAirLaunchAssist();
        if (!launchedInAir || !enableAirLaunchAssist || _playerRb == null)
            return;

        _airLaunchAssistRemaining = airLaunchAssistDuration;

        Vector2 velocity = _playerRb.linearVelocity;
        if (velocity.y < 0f)
        {
            if (airLaunchFallingVelocityReduction > 0f)
            {
                velocity.y = Mathf.Min(
                    0f,
                    velocity.y + airLaunchFallingVelocityReduction);
            }

            if (airLaunchMaxFallSpeedAfterShot > 0f)
                velocity.y = Mathf.Max(velocity.y, -airLaunchMaxFallSpeedAfterShot);

            _playerRb.linearVelocity = velocity;
        }

    }

    private void UpdateAirLaunchAssist(float dt)
    {
        if (_airLaunchAssistRemaining <= 0f)
            return;
        if (_state != MorningStarState.Thrown || IsPlayerGrounded())
        {
            StopAirLaunchAssist();
            return;
        }

        _airLaunchAssistRemaining = Mathf.Max(0f, _airLaunchAssistRemaining - dt);

        // Rigidbody設定は変更せず、短時間だけ通常重力の一部を相殺する。
        // 上向き速度は与えないため、二段ジャンプにはならない。
        float gravityCompensation = -Physics2D.gravity.y
            * (1f - airLaunchGravityMultiplier)
            * _playerRb.mass;
        if (gravityCompensation > 0f)
            _playerRb.AddForce(Vector2.up * gravityCompensation, ForceMode2D.Force);
    }

    private void StopAirLaunchAssist()
    {
        _airLaunchAssistRemaining = 0f;
    }

    private void EnterDraggingState(bool snapBallToSocket)
    {
        EndLaunchPose();
        _isHooked = false;
        _attachedMagnet = null;
        _hookPoint = Vector2.zero;
        _recallTimer = 0f;
        _recallHoldTimer = 0f;
        _recallDelayTimer = 0f;
        _pendingThrowSpeedMultiplier = 1f;
        _pendingLaunchDir = Vector2.zero;
        _thrownElapsed = 0f;
        _lastCharge01 = 0f;
        _lastSpeedMultiplier = 1f;
        _fireHoldTime = 0f;
        _isFireHolding = false;
        _hookClickHoldTimer = 0f;
        _hookClickHolding = false;
        _requireReleaseBeforeHookClick = false;
        _chargeTime = 0f;
        SetSpinGuardActive(false);
        ApplyHookedChainVisual(false);
        SetAnimatorBool(_hashLaunchCharge, false);

        TransitionToState(MorningStarState.Dragging);

        if (snapBallToSocket && morningStarRb != null)
            SnapBallToSocket(zeroVelocity: true);

        TryConsumeBufferedFire();
    }

    private bool WasFirePressedThisFrame()
    {
        Mouse mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            return true;

        return _gamepadFirePressedThisFrame;
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

    private void BufferFireInput(Vector2 aimDir)
    {
        if (aimDir.sqrMagnitude <= 0.001f)
            return;

        _bufferedAimDirection = aimDir.normalized;
        _fireBufferTimer = fireInputBufferTime;
        _pendingAimDirection = _bufferedAimDirection;
    }

    private void ProcessBufferedFireInputForRestrictedStates()
    {
        if (_state != MorningStarState.Returning
            && _state != MorningStarState.RecallBeforeThrow
            && _state != MorningStarState.Thrown)
            return;

        if (!WasFirePressedThisFrame())
            return;

        Vector2 aimDir = CalculateAimDirection();
        if (aimDir.sqrMagnitude > 0.001f)
            BufferFireInput(aimDir);
    }

    private void HandleHookedClickInput()
    {
        Mouse mouse = Mouse.current;

        // 投擲クリックの押しっぱなしを Hook 後の短押し再射出と誤認しない
        if (_requireReleaseBeforeHookClick)
        {
            if (!((mouse != null && mouse.leftButton.isPressed) || IsGamepadSwingHoldPressed()))
                _requireReleaseBeforeHookClick = false;
            return;
        }

        bool holdPressedThisFrame = (mouse != null && mouse.leftButton.wasPressedThisFrame)
            || WasGamepadSwingHoldPressedThisFrame();
        bool holdPressed = (mouse != null && mouse.leftButton.isPressed)
            || IsGamepadSwingHoldPressed();

        if (holdPressedThisFrame)
        {
            _hookClickHolding = true;
            _hookClickHoldTimer = 0f;
        }

        if (_hookClickHolding && holdPressed)
        {
            _hookClickHoldTimer += Time.deltaTime;

            Vector2 aim = CalculateAimDirectionFromHook();
            if (aim.sqrMagnitude > 0.001f)
                _pendingAimDirection = aim;

            if (_hookClickHoldTimer >= holdToSwingTime)
            {
                BeginSwinging();
                _hookClickHolding = false;
            }
        }

        if (_hookClickHolding && ((mouse != null && mouse.leftButton.wasReleasedThisFrame) || WasGamepadSwingHoldReleasedThisFrame()))
        {
            if (_state == MorningStarState.Hooked
                && _hookClickHoldTimer < holdToSwingTime
                && shortClickRethrowFromHook)
            {
                Vector2 aimDir = CalculateAimDirection();
                if (aimDir.sqrMagnitude <= 0.001f)
                    aimDir = _pendingAimDirection;
                if (aimDir.sqrMagnitude > 0.001f)
                    BeginReThrowFromHook(aimDir);
            }

            _hookClickHolding = false;
            _hookClickHoldTimer = 0f;
        }
    }

    private void HandleSwingingInput()
    {
        Mouse mouse = Mouse.current;
        bool swingHold = (mouse != null && mouse.leftButton.isPressed)
            || IsGamepadSwingHoldPressed();

        // Magnetへ到達した瞬間に残っている発射入力を、即時再射出と誤認しない。
        if (_requireReleaseBeforeHookClick)
        {
            if (!swingHold)
                _requireReleaseBeforeHookClick = false;
            return;
        }

        if (swingHold)
        {
            Vector2 aim = CalculateAimDirection();
            if (aim.sqrMagnitude > 0.001f)
                _pendingAimDirection = aim;
        }

        bool swingReleased = (mouse != null && mouse.leftButton.wasReleasedThisFrame)
            || WasGamepadSwingHoldReleasedThisFrame();
        if (!swingReleased)
            return;

        if (releaseFromSwingToThrow)
        {
            Vector2 aimDir = CalculateAimDirection();
            if (aimDir.sqrMagnitude <= 0.001f)
                aimDir = _pendingAimDirection;
            if (aimDir.sqrMagnitude > 0.001f)
                BeginReThrowFromHook(aimDir);
        }
        else
        {
            TransitionToState(MorningStarState.Hooked);
            _hookClickHolding = false;
            _hookClickHoldTimer = 0f;
        }
    }

    private void HandleSpinChargeInput()
    {
        if (TryHandleGamepadFireTriggerInput())
            return;

        if (TryHandleGamepadSpinChargeInput())
            return;

        HandleMouseSpinChargeInput();
    }

    private bool TryHandleGamepadFireTriggerInput()
    {
        if (!enableGamepadInput || !_gamepadFirePressedThisFrame)
            return false;

        Gamepad pad = Gamepad.current;
        if (pad == null || pad.rightStickButton.isPressed)
            return false;

        Vector2 aim = CalculateAimDirection();
        if (aim.sqrMagnitude <= 0.001f)
            return false;

        FireImmediate(aim);
        return true;
    }

    private bool TryHandleGamepadSpinChargeInput()
    {
        if (!enableGamepadInput || !useRightStickClickForCharge)
            return false;

        Gamepad pad = Gamepad.current;
        if (pad == null)
            return false;

        if (pad.rightStickButton.wasPressedThisFrame)
        {
            _fireHoldTime = 0f;
            _isFireHolding = true;
            Vector2 aim = CalculateAimDirection();
            if (aim.sqrMagnitude > 0.001f)
                _pendingAimDirection = aim;
            return true;
        }

        if (pad.rightStickButton.isPressed && _isFireHolding)
        {
            _fireHoldTime += Time.deltaTime;

            Vector2 aim = CalculateAimDirection();
            if (aim.sqrMagnitude > 0.001f)
                _pendingAimDirection = aim;

            if (_fireHoldTime >= holdThreshold)
                BeginSpinCharging();

            return true;
        }

        if (pad.rightStickButton.wasReleasedThisFrame && _isFireHolding)
        {
            _isFireHolding = false;

            if ((_state == MorningStarState.Dragging || _state == MorningStarState.Dropping)
                && _fireHoldTime >= 0f
                && _fireHoldTime < holdThreshold
                && _pendingAimDirection.sqrMagnitude > 0.001f)
            {
                FireImmediate(_pendingAimDirection);
            }

            _fireHoldTime = -1f;
            return true;
        }

        return false;
    }

    private void HandleMouseSpinChargeInput()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null)
            return;

        if (mouse.leftButton.wasPressedThisFrame)
        {
            _fireHoldTime = 0f;
            _isFireHolding = true;
            Vector2 aim = CalculateAimDirection();
            if (aim.sqrMagnitude > 0.001f)
                _pendingAimDirection = aim;
        }

        if (mouse.leftButton.isPressed && _isFireHolding)
        {
            _fireHoldTime += Time.deltaTime;

            Vector2 aim = CalculateAimDirection();
            if (aim.sqrMagnitude > 0.001f)
                _pendingAimDirection = aim;

            if (_fireHoldTime >= holdThreshold)
                BeginSpinCharging();
        }

        if (mouse.leftButton.wasReleasedThisFrame && _isFireHolding)
        {
            _isFireHolding = false;

            if ((_state == MorningStarState.Dragging || _state == MorningStarState.Dropping)
                && _fireHoldTime >= 0f
                && _fireHoldTime < holdThreshold
                && _pendingAimDirection.sqrMagnitude > 0.001f)
            {
                FireImmediate(_pendingAimDirection);
            }

            _fireHoldTime = -1f;
        }
    }

    private void HandleSimpleFireInput()
    {
        if (TryHandleGamepadFireTriggerInput())
            return;

        Mouse mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame)
            return;

        Vector2 aim = CalculateAimDirection();
        if (aim.sqrMagnitude > 0.001f)
            FireImmediate(aim);
    }

    private void FireImmediate(Vector2 aimDir)
    {
        if (Time.time < _nextLaunchTime)
            return;
        if (_state != MorningStarState.Dragging && _state != MorningStarState.Dropping)
            return;

        _lastSpeedMultiplier = 1f;
        BeginRecallBeforeThrow(aimDir.normalized);
    }

    private void HandleSpinChargingReleaseInput()
    {
        Mouse mouse = Mouse.current;
        Vector2 aim = CalculateAimDirection();
        if (aim.sqrMagnitude > 0.001f)
            _pendingAimDirection = aim;

        bool mouseReleased = mouse != null && mouse.leftButton.wasReleasedThisFrame;
        bool gamepadReleased = enableGamepadInput
            && Gamepad.current != null
            && Gamepad.current.rightStickButton.wasReleasedThisFrame;
        if (gamepadReleased)
        {
            _pendingAimDirection = _lastGamepadAimDirection.sqrMagnitude > 0.001f
                ? _lastGamepadAimDirection.normalized
                : Vector2.right;
            _rightStickReady = false;
        }

        if (mouseReleased || gamepadReleased)
            ExecuteChargedThrow();
    }

    private void BeginSpinCharging()
    {
        if (!enableSpinCharge)
            return;
        if (_state != MorningStarState.Dragging && _state != MorningStarState.Dropping)
            return;
        if (Time.time < _nextLaunchTime)
            return;
        if (!CanStartAnotherThrow())
            return;

        TransitionToState(MorningStarState.SpinCharging);
        _chargeTime = 0f;
        _spinAngle = 0f;
        _fireHoldTime = -1f;
        _isFireHolding = false;
        SetSpinGuardActive(true);
        SetAnimatorBool(_hashLaunchCharge, true);
        LogDebug("MorningStar SpinCharge");

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
        LogDebug("MorningStar Charged Throw");
        BeginRecallBeforeThrow(aimDir, chargedRecallTimeMultiplier, _lastSpeedMultiplier);
    }

    private void SetSpinGuardActive(bool active)
    {
        if (spinGuardCollider != null)
            spinGuardCollider.enabled = active;
    }

    private void LogDebug(string message)
    {
        if (debugLog)
            Debug.Log(message);
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

        if (TryGetGamepadAimDirection(out Vector2 gamepadDir))
        {
            _lastValidAimDirection = gamepadDir;
            return gamepadDir;
        }

        if (!TryGetPointerScreen(out Vector2 screenPos))
        {
            if (_lastValidAimDirection.sqrMagnitude > 1e-6f)
                return _lastValidAimDirection;
            return Vector2.zero;
        }

        Vector2 mouseWorld = WorldFromScreen(screenPos);
        Vector2 dir = mouseWorld - origin;

        if (dir.sqrMagnitude < minAimDistance * minAimDistance)
        {
            if (_lastValidAimDirection.sqrMagnitude > 1e-6f)
                return _lastValidAimDirection;
            return Vector2.zero;
        }

        _lastValidAimDirection = dir.normalized;
        return _lastValidAimDirection;
    }

    private void UpdateGamepadInputState()
    {
        _gamepadFirePressedThisFrame = false;

        if (!enableGamepadInput)
            return;

        Gamepad pad = Gamepad.current;
        if (pad == null)
        {
            _rightStickReady = true;
            return;
        }

        Vector2 stick = pad.rightStick.ReadValue();
        float magnitude = stick.magnitude;

        if (magnitude > 0.0001f)
        {
            _lastGamepadAimDirection = stick.normalized;
            _pendingAimDirection = _lastGamepadAimDirection;
        }

        if (magnitude >= gamepadFireThreshold)
        {
            if (_rightStickReady && !pad.rightStickButton.isPressed)
            {
                _gamepadFirePressedThisFrame = true;
                _rightStickReady = false;
            }
        }
        else if (magnitude <= gamepadResetThreshold)
        {
            _rightStickReady = true;
        }

    }

    private bool TryGetGamepadAimDirection(out Vector2 direction)
    {
        direction = Vector2.zero;

        if (!enableGamepadInput)
            return false;

        Gamepad pad = Gamepad.current;
        if (pad == null)
            return false;

        Vector2 stick = pad.rightStick.ReadValue();
        float magnitude = stick.magnitude;
        if (magnitude <= gamepadResetThreshold)
            return false;

        direction = stick.normalized;
        _lastGamepadAimDirection = direction;
        return true;
    }

    private bool IsGamepadSwingHoldPressed()
    {
        if (!enableGamepadInput)
            return false;

        Gamepad pad = Gamepad.current;
        if (pad == null)
            return false;

        if (pad.leftShoulder.isPressed)
            return true;

        if (useLeftTriggerForSwingHold && pad.leftTrigger.ReadValue() > 0.05f)
            return true;

        return false;
    }

    private bool WasGamepadSwingHoldPressedThisFrame()
    {
        if (!enableGamepadInput)
            return false;

        Gamepad pad = Gamepad.current;
        if (pad == null)
            return false;

        if (pad.leftShoulder.wasPressedThisFrame)
            return true;

        if (useLeftTriggerForSwingHold)
            return pad.leftTrigger.wasPressedThisFrame;

        return false;
    }

    private bool WasGamepadSwingHoldReleasedThisFrame()
    {
        if (!enableGamepadInput)
            return false;

        Gamepad pad = Gamepad.current;
        if (pad == null)
            return false;

        if (pad.leftShoulder.wasReleasedThisFrame)
            return true;

        if (useLeftTriggerForSwingHold)
            return pad.leftTrigger.wasReleasedThisFrame;

        return false;
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

    private void BeginSwinging()
    {
        if (_state != MorningStarState.Hooked || !_isHooked)
            return;

        TransitionToState(MorningStarState.Swinging);
        _hookClickHolding = false;
        _hookClickHoldTimer = 0f;

        LogDebug("MorningStar HookSwing");
    }

    private void BeginRelease()
    {
        ReleaseHookAndBeginReturn(true);
    }

    /// <summary>
    /// Magnet吸着中だけ通常のRelease boostなしで拘束を解除し、Player側のJumpへ制御を返す。
    /// </summary>
    public bool TryReleaseMagnetForJump()
    {
        if (_attachedMagnet == null)
            return false;

        return ReleaseHookAndBeginReturn(false);
    }

    private bool ReleaseHookAndBeginReturn(bool applyReleaseBoost)
    {
        if ((_state != MorningStarState.Hooked && _state != MorningStarState.Swinging) || !_isHooked)
            return false;

        EndLaunchPose();
        if (applyReleaseBoost)
            ApplyReleaseBoostToPlayer();
        SetHookRopeJointActive(false);

        _isHooked = false;
        _attachedMagnet = null;
        _hookPoint = Vector2.zero;

        if (morningStarRb != null)
        {
            morningStarRb.linearVelocity = Vector2.zero;
            morningStarRb.angularVelocity = 0f;
            morningStarRb.WakeUp();
        }

        _rehookLockoutTimer = rehookLockoutTime;
        _hookClickHolding = false;
        _hookClickHoldTimer = 0f;
        _requireReleaseBeforeHookClick = false;
        ApplyHookedChainVisual(false);

        // Hook解除後にSwingingのままだと回収が拒否されるため、互換Rest状態を経由する。
        TransitionToState(MorningStarState.Dropping);
        BeginReturn();
        return true;
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

        TryPlayGroundImpactCameraShake(collision);

        if ((_state == MorningStarState.Thrown || _state == MorningStarState.Dropping)
            && HasFloorContact(collision)
            && morningStarRb.linearVelocity.y < -dropTransitionSpeed)
        {
            LogDebug("MorningStar Heavy Land");
        }

        if (TryProcessCombatHit(collision))
            return;

        if (_state != MorningStarState.Thrown)
            return;
        if (_rehookLockoutTimer > 0f)
            return;

        if (HasFloorContact(collision) && !allowFloorHook)
        {
            BeginDropAfterThrow();
            return;
        }

        if (!ShouldHookFromContact(collision))
            return;

        float speed = morningStarRb.linearVelocity.magnitude;
        if (speed < hookMinSpeed)
            return;

        Vector2 hookPoint = GetBestHookPoint(collision);
        if (hookPoint.sqrMagnitude < 1e-8f)
            hookPoint = morningStarRb.position;

        BeginHook(hookPoint);
    }

    private bool CanStateDealCombatDamage()
    {
        return _state == MorningStarState.Dragging
            || _state == MorningStarState.SpinCharging
            || _state == MorningStarState.Thrown
            || _state == MorningStarState.Dropping
            || _state == MorningStarState.Hooked
            || _state == MorningStarState.Swinging;
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

    private bool ShouldHookFromContact(Collision2D collision)
    {
        if (_state != MorningStarState.Thrown)
            return false;
        if (_rehookLockoutTimer > 0f)
            return false;
        if (collision.collider == null)
            return false;
        if (collision.collider.CompareTag("Player"))
            return false;

        int otherLayerMask = 1 << collision.gameObject.layer;
        if ((hookableLayers.value & otherLayerMask) == 0)
            return false;

        if (collision.contactCount == 0)
            return allowWallHook || allowCeilingHook;

        foreach (ContactPoint2D contact in collision.contacts)
        {
            if (IsHookableContactNormal(contact.normal))
                return true;
        }

        return false;
    }

    private void TryPlayGroundImpactCameraShake(Collision2D collision)
    {
        if (!IsFloorTagged(collision.collider)
            || collision.collider.GetComponentInParent<IMorningStarHitReceiver>() != null
            || Time.time < _nextGroundImpactShakeTime)
            return;

        float impactSpeed = GetGroundNormalImpactSpeed(collision);
        if (impactSpeed < minimumGroundImpactSpeed)
            return;

        if (cameraShake == null && Camera.main != null)
            cameraShake = Camera.main.GetComponent<CameraShake2D>();
        if (groundImpactAudioSource == null)
            groundImpactAudioSource = OneShotAudioUtility.FindWorldImpactSource();
        if (cameraShake == null)
            return;

        if (!cameraShake.enabled)
            cameraShake.enabled = true;

        float upperImpactSpeed = Mathf.Max(minimumGroundImpactSpeed, maxImpactSpeed);
        float impact01 = Mathf.InverseLerp(minimumGroundImpactSpeed, upperImpactSpeed, impactSpeed);
        float minStrength = Mathf.Min(minimumShakeStrength, maximumShakeStrength);
        float maxStrength = Mathf.Max(minimumShakeStrength, maximumShakeStrength);
        float strength = Mathf.Lerp(minStrength, maxStrength, impact01);

        if (OneShotAudioUtility.Play2D(
                groundImpactAudioSource,
                groundImpactClip,
                groundImpactVolume,
                morningStarRb != null ? morningStarRb.position : transform.position))
        {
            GroundImpactSoundCount++;
        }

        cameraShake.Shake(shakeDuration, strength);
        _nextGroundImpactShakeTime = Time.time + shakeCooldown;
        LastGroundImpactSpeed = impactSpeed;
        LastGroundImpactShakeStrength = strength;
        GroundImpactShakeCount++;
    }

    private float GetGroundNormalImpactSpeed(Collision2D collision)
    {
        float impactSpeed = 0f;
        Vector2 relativeVelocity = collision.relativeVelocity;

        foreach (ContactPoint2D contact in collision.contacts)
        {
            if (contact.normal.y <= floorNormalThreshold)
                continue;

            Vector2 normal = contact.normal.normalized;
            impactSpeed = Mathf.Max(impactSpeed, Mathf.Abs(Vector2.Dot(relativeVelocity, normal)));
        }

        return impactSpeed;
    }

    private static bool IsFloorTagged(Collider2D collider)
    {
        for (Transform current = collider.transform; current != null; current = current.parent)
        {
            if (current.CompareTag("Floor"))
                return true;
        }

        return false;
    }

    private bool IsHookableContactNormal(Vector2 normal)
    {
        if (normal.sqrMagnitude < 1e-6f)
            return false;

        Vector2 n = normal.normalized;
        bool isFloor = n.y > floorNormalThreshold;
        bool isCeiling = n.y < -floorNormalThreshold;
        bool isWall = Mathf.Abs(n.x) > 0.5f;

        if (isFloor && !allowFloorHook)
            return false;
        if (isCeiling && !allowCeilingHook)
            return false;
        if (isWall && !allowWallHook)
            return false;

        return isFloor || isCeiling || isWall;
    }

    public bool HasFloorContact(Collision2D collision)
    {
        if (collision.contactCount == 0)
            return false;

        foreach (ContactPoint2D contact in collision.contacts)
        {
            if (contact.normal.y > floorNormalThreshold)
                return true;
        }

        return false;
    }

    private static Vector2 GetBestHookPoint(Collision2D collision)
    {
        if (collision.contactCount == 0)
            return Vector2.zero;

        Vector2 hookPoint = collision.GetContact(0).point;
        if (hookPoint.sqrMagnitude < 1e-8f && collision.rigidbody != null)
            hookPoint = collision.rigidbody.position;

        return hookPoint;
    }

    private void BeginHook(Vector2 hookPoint)
    {
        // Hook/Magnet Swingは常に通常鎖長。Constraintを先に外して急補正を防ぐ。
        SetChainConstraintActive(false);
        SetLaunchRopeLengthActive(false);
        _hookPoint = hookPoint;
        _isHooked = true;
        _attachedMagnet = null;
        _hookedAtTime = Time.time;
        _thrownElapsed = 0f;
        _fireBufferTimer = 0f;
        _hookClickHoldTimer = 0f;
        _hookClickHolding = false;
        _requireReleaseBeforeHookClick = Mouse.current != null && Mouse.current.leftButton.isPressed;

        morningStarRb.position = _hookPoint;
        morningStarRb.linearVelocity = Vector2.zero;
        morningStarRb.angularVelocity = 0f;
        morningStarRb.WakeUp();

        TransitionToState(MorningStarState.Hooked);

        _rehookLockoutTimer = Mathf.Max(rehookLockoutTime, hookStickMinTime);
        SetAnimatorBool(_hashLaunchCharge, false);
        SetSpinGuardActive(false);
        ApplyHookedChainVisual(true);
        PlayHookFeedback();
        LogDebug("MorningStar Hooked");
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
        if ((_state != MorningStarState.Hooked && _state != MorningStarState.Swinging) || !_isHooked)
            return;
        if (aimDir.sqrMagnitude < 1e-6f)
            return;
        if (Time.time < _nextLaunchTime)
            return;
        if (Time.time - _hookedAtTime < hookStickMinTime)
            return;
        if (!CanStartAnotherThrow())
            return;

        Vector2 d = aimDir.normalized;
        Vector2 hand = GetHandWorld();
        Vector2 worldTarget = (Vector2)morningStarRb.position + d * Mathf.Max(minAimDistance, maxThrowDistance);
        UpdateAimFacing(worldTarget);
        ShowClickAimVisuals(worldTarget, hand);

        _isHooked = false;
        SetHookRopeJointActive(false);
        _attachedMagnet = null;
        _hookPoint = Vector2.zero;
        _rehookLockoutTimer = rehookLockoutTime;
        ApplyHookedChainVisual(false);
        _lastSpeedMultiplier = 1f;
        _fireBufferTimer = 0f;
        _hookClickHolding = false;
        _hookClickHoldTimer = 0f;
        _requireReleaseBeforeHookClick = false;

        BeginRecallBeforeThrow(d);
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

        PinBallAtHook();
    }

    private void UpdateSwingingFixed(float dt)
    {
        if (morningStarRb == null)
        {
            EnterDraggingState(false);
            return;
        }

        if (_state != MorningStarState.Swinging || !_isHooked)
            return;

        PinBallAtHook();
        ApplySwingPullToPlayer();
    }

    private void PinBallAtHook()
    {
        morningStarRb.position = _hookPoint;
        morningStarRb.linearVelocity = Vector2.zero;
        morningStarRb.angularVelocity = 0f;
    }

    private void ApplySwingPullToPlayer()
    {
        if (_playerRb == null)
            return;

        Vector2 ropeStart = GetPlayerRopeAnchorWorld();
        Vector2 toHook = _hookPoint - ropeStart;
        float distance = toHook.magnitude;
        if (distance <= 0.01f)
            return;

        Vector2 dirToHook = toHook / distance;
        Vector2 v = _playerRb.linearVelocity;
        float ropeLength = GetEffectiveRopeLength();
        bool ropeIsTaut = distance >= Mathf.Max(0.1f, ropeLength - 0.05f);

        if (_attachedMagnet != null)
        {
            // 最大距離とRadial方向の解決はDistanceJoint2Dへ任せる。
            // ここでは鎖が張った時だけ、入力を接線方向へ変換して補助する。
            if (!ropeIsTaut)
                return;

            Vector2 magnetTangent = new Vector2(dirToHook.y, -dirToHook.x);
            if (magnetTangent.x < 0f)
                magnetTangent = -magnetTangent;

            float magnetMoveX = player != null ? player.MoveInputX : 0f;
            if (Mathf.Abs(magnetMoveX) > 0.01f && magnetSwingForce > 0f)
                _playerRb.AddForce(magnetTangent * magnetMoveX * magnetSwingForce, ForceMode2D.Force);

            if (magnetMaxSwingSpeed > 0f)
            {
                float tangentSpeed = Vector2.Dot(v, magnetTangent);
                float limitedTangentSpeed = Mathf.Clamp(
                    tangentSpeed,
                    -magnetMaxSwingSpeed,
                    magnetMaxSwingSpeed);
                if (!Mathf.Approximately(tangentSpeed, limitedTangentSpeed))
                    _playerRb.linearVelocity = v + magnetTangent * (limitedTangentSpeed - tangentSpeed);
            }

            return;
        }

        // 鎖がたるんでいる間はPlayerを支点へ吸い込まない。
        // 最大長付近だけ既存Forceを張力として使い、外向き速度を抑える。
        if (ropeIsTaut)
        {
            _playerRb.AddForce(dirToHook * swingPullForce, ForceMode2D.Force);

            Vector2 awayDir = -dirToHook;
            float awaySpeed = Vector2.Dot(v, awayDir);
            if (awaySpeed > 0f)
                v -= awayDir * awaySpeed * swingRadialDamping;
        }

        Vector2 tangent = new Vector2(-dirToHook.y, dirToHook.x);
        float moveX = player != null ? player.MoveInputX : 0f;
        if (Mathf.Abs(moveX) > 0.01f)
            _playerRb.AddForce(tangent * moveX * swingInputForce, ForceMode2D.Force);

        float radialAlong = Vector2.Dot(v, dirToHook);
        float tangentAlong = Vector2.Dot(v, tangent);
        v = dirToHook * radialAlong + tangent * (tangentAlong * swingTangentKeepRate);

        if (maxSwingSpeed > 0f && v.sqrMagnitude > maxSwingSpeed * maxSwingSpeed)
            v = v.normalized * maxSwingSpeed;

        _playerRb.linearVelocity = v;
    }

    private bool IsPlayerGrounded()
    {
        return player != null && player.IsGrounded;
    }

    private void SubscribeToPlayerLanding()
    {
        if (_playerLandingSubscribed)
            return;

        if (player == null && playerRigidbody2D != null)
            player = playerRigidbody2D.GetComponent<Player>();

        if (player == null)
            return;

        player.Landed += HandlePlayerLanded;
        _playerLandingSubscribed = true;
    }

    private void UnsubscribeFromPlayerLanding()
    {
        if (!_playerLandingSubscribed)
            return;

        if (player != null)
            player.Landed -= HandlePlayerLanded;
        _playerLandingSubscribed = false;
    }

    private void HandlePlayerLanded()
    {
        _airThrowsUsed = 0;
    }

    private void SubscribeToPlayerDeath()
    {
        if (_playerDeathSubscribed)
            return;

        if (_playerHealth == null)
            _playerHealth = GetComponent<PlayerHealth>();
        if (_playerHealth == null)
            return;

        _playerHealth.OnDead += HandlePlayerDead;
        _playerDeathSubscribed = true;
    }

    private void UnsubscribeFromPlayerDeath()
    {
        if (!_playerDeathSubscribed)
            return;

        if (_playerHealth != null)
            _playerHealth.OnDead -= HandlePlayerDead;
        _playerDeathSubscribed = false;
    }

    private void HandlePlayerDead()
    {
        StopAirLaunchAssist();
        EndLaunchPose();
        SetChainConstraintActive(false);
        SetHookRopeJointActive(false);
        SetLaunchRopeLengthActive(false);
    }

    private bool CanStartAnotherThrow()
    {
        if (!limitAirThrows || IsPlayerGrounded())
            return true;

        return _airThrowsUsed < Mathf.Max(0, maxAirThrows);
    }

    private bool TryConsumeAirThrow()
    {
        if (!limitAirThrows || IsPlayerGrounded())
            return true;

        int allowedAirThrows = Mathf.Max(0, maxAirThrows);

        if (_airThrowsUsed >= allowedAirThrows)
            return false;

        _airThrowsUsed++;
        return true;
    }

    public void GrantMagnetEscapeThrow()
    {
        if (!limitAirThrows)
            return;

        int allowedAirThrows = Mathf.Max(0, maxAirThrows);

        if (allowedAirThrows <= 0)
            return;

        // maxAirThrows = 1 の場合、
        // 次の空中射出を1回だけ可能にする。
        _airThrowsUsed = Mathf.Min(
            _airThrowsUsed,
            allowedAirThrows - 1
        );
    }

    private void BeginRecallBeforeThrow(
        Vector2 launchDir,
        float recallTimeMultiplier = 1f,
        float throwSpeedMultiplier = 1f)
    {
        if (launchDir.sqrMagnitude < 1e-6f || morningStarRb == null)
            return;

        if (!TryConsumeAirThrow())
            return;

        // 前回Launch Poseが残っている場合も、新しいRecall開始時点で必ず解除する。
        EndLaunchPose();
        SetChainConstraintActive(false);
        SetLaunchRopeLengthActive(false);

        Vector2 hand = GetHandWorld();
        Vector2 d = launchDir.normalized;
        Vector2 worldTarget = hand + d * Mathf.Max(minAimDistance, maxThrowDistance);

        UpdateAimFacing(worldTarget);
        FacePlayerForLaunch(d);
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

        TransitionToState(MorningStarState.RecallBeforeThrow);

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

        bool launchedInAir = !IsPlayerGrounded();
        Vector2 d = _pendingLaunchDir.sqrMagnitude > 1e-12f ? _pendingLaunchDir.normalized : Vector2.right;
        FacePlayerForLaunch(d);
        Vector2 origin = GetThrowOriginPosition();

        morningStarRb.position = origin;
        morningStarRb.linearVelocity = Vector2.zero;
        morningStarRb.angularVelocity = 0f;

        TransitionToState(MorningStarState.Thrown);

        float speed = throwSpeed * _pendingThrowSpeedMultiplier;
        if (maxBallLinearSpeed > 0f)
            speed = Mathf.Min(speed, maxBallLinearSpeed);

        float offset = Mathf.Max(0f, launchStartOffset);
        morningStarRb.position = origin + d * offset;
        Vector2 launchVelocity = d * speed;
        if (postThrowDownwardBias > 0f)
            launchVelocity += Vector2.down * postThrowDownwardBias;
        if (maxBallLinearSpeed > 0f && launchVelocity.sqrMagnitude > maxBallLinearSpeed * maxBallLinearSpeed)
            launchVelocity = launchVelocity.normalized * maxBallLinearSpeed;

        morningStarRb.linearVelocity = launchVelocity;
        morningStarRb.WakeUp();
        BeginAirLaunchAssist(launchedInAir);
        PlayMorningStarLaunchSound();

        _thrownElapsed = 0f;
        _lastSpeedMultiplier = _pendingThrowSpeedMultiplier;
        SetAnimatorBool(_hashLaunchCharge, false);
        BeginLaunchPose(launchedInAir);
        SetAnimatorTrigger(_hashLaunchFire);
        _recoilTriggerTime = Time.time + launchRecoilDelay;

        if (aimLaunchCooldown > 0f)
            _nextLaunchTime = Time.time + aimLaunchCooldown;
    }

    private void PlayMorningStarLaunchSound()
    {
        if (sfxAudioSource != null && morningStarLaunchClip != null)
            sfxAudioSource.PlayOneShot(morningStarLaunchClip, morningStarLaunchVolume);
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
        float flightEndDistance = Mathf.Max(maxThrowDistance, GetEffectiveRopeLength());
        if (dist >= flightEndDistance && !IsMorningStarTouchingHookable())
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

        // 飛翔終了後は履歴依存のDropping物理を残さず、その場で共通Restへ戻る。
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

        ContactPoint2D[] contacts = new ContactPoint2D[8];
        int count = ballCol.GetContacts(filter, contacts);
        for (int i = 0; i < count; i++)
        {
            if (IsHookableContactNormal(contacts[i].normal))
                return true;
        }

        return false;
    }

    private void BeginReturn()
    {
        if (_state == MorningStarState.Returning)
            return;
        if ((_state == MorningStarState.Hooked || _state == MorningStarState.Swinging) && _isHooked)
            return;

        bool canManualReturn = _state == MorningStarState.Thrown
            || _state == MorningStarState.Dropping
            || (_state == MorningStarState.Dragging && !IsBallAtReturnSocket());
        if (!canManualReturn)
            return;

        EndLaunchPose();
        SetChainConstraintActive(false);
        SetLaunchRopeLengthActive(false);

        _isHooked = false;
        _attachedMagnet = null;
        _hookPoint = Vector2.zero;
        TransitionToState(MorningStarState.Returning);
        _rehookLockoutTimer = Mathf.Max(_rehookLockoutTimer, rehookLockoutTime);

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

        // Game Start / Respawnと同じDragging初期化経路を必ず通す。
        EnterDraggingState(true);
    }

    private bool IsBallAtReturnSocket()
    {
        return morningStarRb == null
            || Vector2.Distance(morningStarRb.position, GetThrowSocketWorld())
            <= returnFinishDistance;
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

    private void EnsureHookRopeJoint()
    {
        if (hookRopeJoint != null || _playerRb == null)
            return;

        hookRopeJoint = _playerRb.GetComponent<DistanceJoint2D>();
        if (hookRopeJoint == null)
            hookRopeJoint = _playerRb.gameObject.AddComponent<DistanceJoint2D>();
    }

    private void SetHookRopeJointActive(bool active)
    {
        EnsureHookRopeJoint();
        if (hookRopeJoint == null)
            return;

        if (!active || !_isHooked || _playerRb == null)
        {
            hookRopeJoint.enabled = false;
            return;
        }

        // connectedBody == null の場合、connectedAnchorはworld固定点になる。
        // MagnetPointは鉄球だけをこの同じ支点へ固定する。
        hookRopeJoint.connectedBody = null;
        hookRopeJoint.autoConfigureConnectedAnchor = false;
        hookRopeJoint.autoConfigureDistance = false;
        hookRopeJoint.enableCollision = false;
        hookRopeJoint.maxDistanceOnly = true;
        hookRopeJoint.anchor = _playerRb.transform.InverseTransformPoint(GetHandWorld());
        hookRopeJoint.connectedAnchor = _hookPoint;
        hookRopeJoint.distance = GetEffectiveRopeLength();
        if (!hookRopeJoint.enabled)
            hookRopeJoint.enabled = true;
    }

    private void SyncRopeLengthToConstraint()
    {
        if (chainConstraint != null)
        {
            chainConstraint.SetMaxRopeLength(GetEffectiveRopeLength());
            chainConstraint.ConfigureTension(
                tensionStartRatio,
                tensionStrength,
                tensionDamping,
                maxTensionForce,
                airTensionMultiplier,
                groundPullEaseTime,
                movingPullResistance,
                runJumpMomentumThreshold,
                jumpMomentumGraceTime,
                runJumpTensionMultiplier,
                chainAnchorVisualFollowTime);
            if (chainLineController != null)
            {
                chainConstraint.ConfigureTerrainPath(
                    chainLineController.GroundLayerMask,
                    chainLineController.ChainCollisionRadius,
                    chainLineController.CollisionSkin);
            }
            chainConstraint.MaxBallSpeed = maxBallLinearSpeed;
        }
    }

    private void SetLaunchRopeLengthActive(bool active)
    {
        _launchRopeLengthActive = active;
        SyncRopeLengthToConstraint();

        // Hook/Magnet用Jointは通常長でのみ使用するが、状態変更と同フレームでも整合させる。
        if (hookRopeJoint != null && hookRopeJoint.enabled)
            hookRopeJoint.distance = GetEffectiveRopeLength();
    }

    private bool ShouldKeepLaunchRopeLengthAfterFlight(MorningStarState nextState)
    {
        if (!_launchRopeLengthActive
            || (nextState != MorningStarState.Dragging && nextState != MorningStarState.Dropping))
        {
            return false;
        }

        return GetCurrentRopeSpan() > BaseMaxRopeLength + 0.01f;
    }

    private void TryRestoreBaseRopeLength()
    {
        if (!_launchRopeLengthActive
            || (_state != MorningStarState.Dragging && _state != MorningStarState.Dropping)
            || GetCurrentRopeSpan() > BaseMaxRopeLength + 0.01f)
        {
            return;
        }

        SetLaunchRopeLengthActive(false);
    }

    private float GetCurrentRopeSpan()
    {
        float directDistance = morningStarRb != null
            ? Vector2.Distance(GetHandWorld(), morningStarRb.position)
            : 0f;
        float pathDistance = chainConstraint != null
            ? chainConstraint.CurrentRopeLength
            : 0f;
        return Mathf.Max(directDistance, pathDistance);
    }

    private float GetEffectiveRopeLength()
    {
        float multiplier = _launchRopeLengthActive
            ? Mathf.Max(1f, launchRopeLengthMultiplier)
            : 1f;
        return Mathf.Max(0.1f, maxRopeLength) * multiplier;
    }

    private Vector2 GetHandWorld()
    {
        return handAnchor != null ? (Vector2)handAnchor.position : (Vector2)transform.position;
    }

    private Vector2 GetPlayerRopeAnchorWorld()
    {
        // HandAnchorはUpdateで左右反転するため、Physics step間ではJoint anchorと
        // 1フレームだけ異なることがある。Hook中の物理・表示は実際のJoint支点を正とする。
        if (_isHooked && hookRopeJoint != null && hookRopeJoint.enabled && _playerRb != null)
            return _playerRb.transform.TransformPoint(hookRopeJoint.anchor);

        return GetHandWorld();
    }

    private Vector2 GetVisualRopeAnchorWorld()
    {
        Vector2 targetWorld = GetPlayerRopeAnchorWorld();
        Transform playerTransform = _playerRb != null ? _playerRb.transform : transform;
        Vector3 targetLocal = playerTransform.InverseTransformPoint(targetWorld);

        if (!Application.isPlaying)
            return targetWorld;

        if (!_visualRopeAnchorInitialized)
        {
            _visualRopeAnchorLocal = targetLocal;
            _visualRopeAnchorLocalVelocity = Vector3.zero;
            _visualRopeAnchorInitialized = true;
        }
        else if (_visualRopeAnchorUpdatedFrame != Time.frameCount)
        {
            _visualRopeAnchorLocal = Vector3.SmoothDamp(
                _visualRopeAnchorLocal,
                targetLocal,
                ref _visualRopeAnchorLocalVelocity,
                Mathf.Max(0.01f, chainAnchorVisualFollowTime));
        }

        _visualRopeAnchorUpdatedFrame = Time.frameCount;
        return playerTransform.TransformPoint(_visualRopeAnchorLocal);
    }

    private void UpdateFallbackLineRenderer()
    {
        if (chainLineController != null || lineRenderer == null || morningStarRb == null)
            return;

        if (chainLineController != null)
        {
            lineRenderer.enabled = false;
            return;
        }

        lineRenderer.enabled = IsRopeLineVisible;
        if (!lineRenderer.enabled)
            return;

        lineRenderer.positionCount = 2;
        Vector3 start = GetVisualRopeAnchorWorld();
        Vector3 end = morningStarRb.position;
        lineRenderer.SetPosition(0, start);
        lineRenderer.SetPosition(1, end);
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

    private void FacePlayerForLaunch(Vector2 launchDirection)
    {
        if (player == null)
            return;

        player.SetLaunchFacing(launchDirection.x, horizontalFacingThreshold);
        SetAnimatorBool(_hashBackwardAim, false);
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
        if (cam == null)
            return true;

        return cam.pixelRect.Contains(screenPos);
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
        if (_state == MorningStarState.Thrown
            || _state == MorningStarState.Dropping
            || (_state == MorningStarState.Dragging && !IsBallAtReturnSocket()))
            BeginReturn();
        else if (_state == MorningStarState.Hooked || _state == MorningStarState.Swinging)
            BeginRelease();
    }

    private void BeginLaunchPose(bool launchedInAir)
    {
        _launchPoseActive = true;
        _launchForwardAnchorApplied = false;
        _launchPoseElapsed = 0f;
        _activeLaunchPoseHoldDuration = launchedInAir
            ? launchPoseMaxHoldTime
            : groundLaunchPoseHoldDuration;
        SetAnimatorBool(_hashLaunchPoseActive, true);

        if (player != null)
            player.SetWeaponHandAnchorPose(launchReadyAnchorLocalPosition);
    }

    private void UpdateLaunchPose()
    {
        if (!_launchPoseActive)
            return;

        _launchPoseElapsed += Time.deltaTime;

        if (!_launchForwardAnchorApplied
            && _launchPoseElapsed >= Mathf.Max(0f, launchPoseForwardFrameTime))
        {
            _launchForwardAnchorApplied = true;
            if (player != null)
                player.SetWeaponHandAnchorPose(launchAnchorLocalPosition);
        }

        if (_launchPoseElapsed >= Mathf.Max(0.01f, _activeLaunchPoseHoldDuration))
            EndLaunchPose();
    }

    private void EndLaunchPose()
    {
        _launchPoseActive = false;
        _launchForwardAnchorApplied = false;
        _launchPoseElapsed = 0f;
        _activeLaunchPoseHoldDuration = 0f;
        SetAnimatorBool(_hashLaunchPoseActive, false);

        if (player != null)
            player.ClearWeaponHandAnchorPose();
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
