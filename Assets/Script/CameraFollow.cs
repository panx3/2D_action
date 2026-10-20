using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.U2D;

/// <summary>
/// プレイヤーを LateUpdate で遅れて滑らかに追従する 2D 用カメラ。
/// デッドゾーン・軸別 SmoothDamp・最大追従速度で、鉄球引っ張り時の画面の急スライドを抑える。
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)]
public class CameraFollow : MonoBehaviour
{
    [Header("追従対象")]
    [SerializeField, Tooltip("追従するターゲット（通常は Player の Transform）。鉄球・鎖には切り替えない。")]
    private Transform _target;

    [Header("オフセット")]
    [SerializeField, Tooltip("ターゲットからのオフセット。Yを負にすると下側を広く映す。")]
    private Vector2 _offset = new Vector2(0f, 0.45f);

    [Header("Normal View")]
    [SerializeField, Range(0f, 0.2f), Tooltip("通常視野の固定拡張率。0.1で10%広げ、下端を保つよう中心を上へ補正する。移動速度では変化しない。")]
    private float _normalViewExpansion;

    [Header("滑らかさ（秒・小さいほど素早く追う）")]
    [FormerlySerializedAs("_smoothTime")]
    [SerializeField, Tooltip("横方向の SmoothDamp 到達時間。目安 0.16〜0.24")]
    private float _smoothTimeX = 0.25f;
    [SerializeField, Tooltip("縦方向の SmoothDamp 到達時間。目安 0.16〜0.22")]
    private float _smoothTimeY = 0.176f;

    [Header("デッドゾーン")]
    [FormerlySerializedAs("_followDeadZone")]
    [SerializeField, Tooltip("横の微小な位置揺れを吸収する範囲。大きくすると指定画面位置からずれる。")]
    private float _followDeadZoneX = 0.1f;
    [SerializeField, Tooltip("この範囲内ではカメラを動かさない（縦）。目安 1.0〜2.0")]
    private float _followDeadZoneY = 0.5f;
    [SerializeField, Min(0f), Tooltip("上方向の追従開始距離。通常ジャンプ中は高さを保ち、大きな上昇だけ追う。下方向は既存の縦デッドゾーンを使用。")]
    private float _upwardFollowDeadZone = 4f;

    [Header("追従速度上限")]
    [SerializeField, Tooltip("1 フレームあたりの最大追従速度（units/s）。0 で無制限。目安 10〜15")]
    private float _maxFollowSpeed = 12.5f;

    [Header("Directional Screen Framing")]
    [SerializeField, Range(0.3f, 0.45f), Tooltip("右へ進むときのPlayerの画面X位置。0.4=左から40%、左向きは反転して60%。")]
    private float _forwardViewportX = 0.4f;
    [SerializeField, Min(0.01f), Tooltip("Look Ahead左右切替・停止復帰のSmoothDamp時間")]
    private float _lookAheadSmoothTime = 0.65f;
    [SerializeField, Min(0f), Tooltip("移動方向の判定に使う最小速度。停止時は最後の方向を維持する。")]
    private float _lookAheadVelocityThreshold = 0.75f;
    [SerializeField, Min(0f), Tooltip("反対方向への移動がこの秒数続いてから構図を切り替える。衝突直後の短い逆流を無視。")]
    private float _directionChangeDelay = 0.12f;

    [Header("Jump / Fall Look Ahead")]
    [SerializeField, Min(0f)] private float _jumpLookAhead = 0f;
    [SerializeField, Min(0f)] private float _fallLookAhead = 1f;
    [SerializeField, Min(0.1f)] private float _jumpLookAheadFullSpeed = 8f;
    [SerializeField, Min(0.1f)] private float _fallLookAheadFullSpeed = 22f;
    [SerializeField, Min(0f), Tooltip("微小な上下速度を先読みに反映しない")]
    private float _verticalVelocityThreshold = 4f;
    [SerializeField, Min(0.01f)] private float _verticalLookAheadSmoothTime = 0.35f;
    [SerializeField, Min(0f), Tooltip("段差・瞬間的な接地抜けで縦先読みを開始しない")]
    private float _airborneLookAheadDelay = 0.08f;

    [Header("Speed Zoom (Player Velocity)")]
    [SerializeField, Min(0.1f)] private float _highSpeedThreshold = 14f;
    [SerializeField, Min(0.1f)] private float _fullZoomSpeed = 28f;
    [SerializeField, Range(0f, 0.2f), Tooltip("通常の実効Orthographic Sizeに対する最大増分。0.1 = 10%")]
    private float _maxZoomOut = 0f;
    [SerializeField, Min(0.01f)] private float _zoomSmoothTime = 0.5f;
    [SerializeField, Min(0f)] private float _zoomHoldTime = 0.18f;
    [SerializeField, Min(0f)] private float _zoomSpeedHysteresis = 2f;

    [Header("移動範囲制限（任意）")]
    [SerializeField, Tooltip("カメラ位置をステージ範囲で Clamp するか")]
    private bool _useBounds;
    [FormerlySerializedAs("_bounds")]
    [SerializeField, Tooltip("カメラ X の最小値")]
    private float _minX = -100f;
    [SerializeField, Tooltip("カメラ X の最大値")]
    private float _maxX = 100f;
    [SerializeField, Tooltip("カメラ Y の最小値")]
    private float _minY = -100f;
    [SerializeField, Tooltip("カメラ Y の最大値")]
    private float _maxY = 100f;

    [Header("暗転なしリスポーン移動")]
    [SerializeField, Min(0.1f)] private float _respawnMinDuration = 0.42f;
    [SerializeField, Min(0.1f)] private float _respawnMaxDuration = 0.68f;
    [SerializeField, Min(1f), Tooltip("最大移動時間に達する距離（units）")]
    private float _respawnMaxDurationDistance = 45f;

    private bool _respawnHeld;
    private bool _respawnTravelling;
    private Vector3 _respawnFrom;
    private Vector3 _respawnTo;
    private float _respawnElapsed;
    private float _respawnDuration;

    public bool IsRespawnTravelling => _respawnTravelling;
    public bool IsRespawnCameraHeld => _respawnHeld;
    public float RespawnTravelProgress => _respawnDuration > 0f
        ? Mathf.Clamp01(_respawnElapsed / _respawnDuration) : 0f;

    private const float FixedZ = -10f;
    private float _velocityX;
    private float _velocityY;
    private Rigidbody2D _targetRigidbody;
    private float _lookAheadOffset;
    private float _lookAheadVelocity;
    private float _framingDirection = 1f;
    private float _pendingDirection;
    private float _directionChangeElapsed;
    private float _lastGroundedY;
    private float _airPeakY;
    private Player _targetPlayer;
    private Camera _camera;
    private CameraShake2D _shake;
    private PixelPerfectCamera _pixelPerfect;
    private Vector2 _followCenter;
    private float _verticalLookAheadOffset;
    private float _verticalLookAheadVelocity;
    private float _airborneTime;
    private float _baseOrthographicSize;
    private float _referenceOrthographicSize;
    private float _upperViewOffset;
    private float _currentOrthographicSize;
    private float _zoomVelocity;
    private float _zoomDemand;
    private float _zoomHoldRemaining;
    private bool _zoomInitialized;
    private bool _restoreUpscaleRT;
    private int _screenWidth, _screenHeight;
    private Object _eventOwner;
    private bool _eventControlled;

    // Keep the existing read-only API used by older Editor checks.
    public float HorizontalLookAhead => GetFramingDistance();
    public float LookAheadSmoothTime => _lookAheadSmoothTime;
    public float CurrentLookAheadOffset => _lookAheadOffset;
    public Vector2 FollowOffset => _offset + Vector2.up * _upperViewOffset;
    public float SwingLookAheadMultiplier => 1f;
    public float ForwardViewportX => _forwardViewportX;
    public float TargetViewportX => _framingDirection > 0f ? _forwardViewportX : 1f - _forwardViewportX;
    public float CurrentVerticalLookAheadOffset => _verticalLookAheadOffset;
    public float BaseOrthographicSize => _baseOrthographicSize;
    public float UpperViewOffset => _upperViewOffset;
    public float NormalViewExpansion => _normalViewExpansion;
    public bool IsEventControlled => _eventControlled;

    private void Awake()
    {
        _camera = GetComponent<Camera>();
        _shake = GetComponent<CameraShake2D>();
        _pixelPerfect = GetComponent<PixelPerfectCamera>();
        _baseOrthographicSize = _camera != null ? _camera.orthographicSize : 5f;
        _referenceOrthographicSize = _baseOrthographicSize;
        ResolveTargetMotionSources();
        _followCenter = transform.position;
    }

    private void OnEnable()
    {
        AdoptCurrentFraming();
    }

    private void OnDisable()
    {
        RestorePixelPerfectUpscaling();
        ResetLookAhead();
    }

    private void OnValidate()
    {
        _normalViewExpansion = Mathf.Clamp(_normalViewExpansion, 0f, 0.2f);
        _forwardViewportX = Mathf.Clamp(_forwardViewportX, 0.3f, 0.45f);
        _lookAheadSmoothTime = Mathf.Max(0.01f, _lookAheadSmoothTime);
        _lookAheadVelocityThreshold = Mathf.Max(0f, _lookAheadVelocityThreshold);
        _directionChangeDelay = Mathf.Max(0f, _directionChangeDelay);
        _upwardFollowDeadZone = Mathf.Max(0f, _upwardFollowDeadZone);
        _jumpLookAhead = Mathf.Max(0f, _jumpLookAhead);
        _fallLookAhead = Mathf.Max(0f, _fallLookAhead);
        _verticalVelocityThreshold = Mathf.Max(0f, _verticalVelocityThreshold);
        _jumpLookAheadFullSpeed = Mathf.Max(_verticalVelocityThreshold + 0.1f, _jumpLookAheadFullSpeed);
        _fallLookAheadFullSpeed = Mathf.Max(_verticalVelocityThreshold + 0.1f, _fallLookAheadFullSpeed);
        _verticalLookAheadSmoothTime = Mathf.Max(0.01f, _verticalLookAheadSmoothTime);
        _airborneLookAheadDelay = Mathf.Max(0f, _airborneLookAheadDelay);
        _highSpeedThreshold = Mathf.Max(0.1f, _highSpeedThreshold);
        _fullZoomSpeed = Mathf.Max(_highSpeedThreshold + 0.1f, _fullZoomSpeed);
        _maxZoomOut = Mathf.Clamp(_maxZoomOut, 0f, 0.2f);
        _zoomSmoothTime = Mathf.Max(0.01f, _zoomSmoothTime);
        _zoomHoldTime = Mathf.Max(0f, _zoomHoldTime);
        _zoomSpeedHysteresis = Mathf.Clamp(_zoomSpeedHysteresis, 0f, _highSpeedThreshold);
    }

    private void LateUpdate()
    {
        // Pause / hit stop / result: neither follow nor lens damping may drift.
        if (Time.deltaTime <= 0f)
            return;

        if (_respawnHeld)
        {
            UpdateZoom(0f);
            if (_respawnTravelling)
            {
                _respawnElapsed = Mathf.Min(_respawnDuration, _respawnElapsed + Time.deltaTime);
                float t = RespawnTravelProgress;
                // SmootherStep: 両端の速度と加速度が0。通常追従への引継ぎで急停止しない。
                float eased = t * t * t * (t * (t * 6f - 15f) + 10f);
                transform.position = Vector3.LerpUnclamped(_respawnFrom, _respawnTo, eased);
                _respawnTravelling = t < 1f;
            }
            return;
        }

        if (_target == null)
            return;

        if (_targetRigidbody == null)
            ResolveTargetMotionSources();

        if (_eventControlled && _eventOwner == null)
            ReleaseEventControl();
        // The existing intro disables this component. Goal presentation blocks Player input.
        if (_eventControlled || (_targetPlayer != null && _targetPlayer.IsGoalInputBlocked))
        {
            AdoptCurrentFraming();
            return;
        }

        Vector2 motion = _targetRigidbody != null && _targetRigidbody.simulated
            ? _targetRigidbody.linearVelocity : Vector2.zero;
        bool grounded = _targetPlayer != null && _targetPlayer.IsGrounded;
        _airborneTime = grounded ? 0f : _airborneTime + Time.deltaTime;
        if (grounded)
            _lastGroundedY = _airPeakY = _target.position.y;
        else
            _airPeakY = Mathf.Max(_airPeakY, _target.position.y);

        float speed = motion.magnitude;
        UpdateZoom(speed);
        float highSpeed01 = Mathf.InverseLerp(_highSpeedThreshold, _fullZoomSpeed, speed);

        UpdateFramingDirection(motion.x);
        float desiredLookAhead = GetFramingDistance() * _framingDirection;
        _lookAheadOffset = Mathf.SmoothDamp(
            _lookAheadOffset,
            desiredLookAhead,
            ref _lookAheadVelocity,
            Mathf.Max(0.01f, _lookAheadSmoothTime));

        float targetY = _target.position.y + _offset.y;
        // Ignore the return half of a normal jump, but look down after walking off a
        // platform or descending from a genuinely high jump / upward launch.
        bool longFall = _target.position.y < _lastGroundedY - _followDeadZoneY
            || (_airPeakY - _lastGroundedY > _upwardFollowDeadZone
                && _target.position.y < _airPeakY - _followDeadZoneY);
        float verticalLead = 0f;
        if (!grounded && _airborneTime >= _airborneLookAheadDelay)
        {
            if (motion.y > 0f && targetY - _followCenter.y > _upwardFollowDeadZone)
                verticalLead = _jumpLookAhead * Mathf.InverseLerp(_verticalVelocityThreshold, _jumpLookAheadFullSpeed, motion.y);
            else if (motion.y < 0f && longFall)
                // A normal jump returning to its starting floor must not trigger a down/up camera bob.
                verticalLead = -_fallLookAhead * Mathf.InverseLerp(_verticalVelocityThreshold, _fallLookAheadFullSpeed, -motion.y);
        }
        _verticalLookAheadOffset = Mathf.SmoothDamp(_verticalLookAheadOffset, verticalLead,
            ref _verticalLookAheadVelocity, _verticalLookAheadSmoothTime);

        Vector3 current = transform.position;
        float targetX = _target.position.x + _offset.x;
        // Keep the body framing stable instead of collapsing its dead zone whenever Player moves.
        float desiredX = ApplyDeadZone(targetX, _followCenter.x, _followDeadZoneX);
        // Reframe a new platform while grounded; keep ordinary airborne jump arcs inside the upper band.
        float deadZoneY = !grounded && targetY > _followCenter.y ? _upwardFollowDeadZone : _followDeadZoneY;
        float desiredY = ApplyDeadZone(targetY, _followCenter.y, deadZoneY);

        float smoothX = Mathf.Max(0.0001f, _smoothTimeX);
        float smoothY = Mathf.Max(0.0001f, _smoothTimeY);
        // Compensate follow damping using actual Player motion so a sustained run
        // settles at 40% / 60%, rather than drifting toward the leading screen edge.
        desiredX += motion.x * smoothX;
        // Only fast falls need extra catch-up to keep Player visible. Swing speed must not
        // repeatedly accelerate/decelerate both camera axes.
        float fallCatchUp = motion.y < 0f ? Mathf.Lerp(1f, 1.8f, highSpeed01) : 1f;
        smoothY /= fallCatchUp;
        if (!grounded && longFall && motion.y < -_verticalVelocityThreshold)
            desiredY += motion.y * smoothY;
        if (!grounded && _camera != null && _camera.orthographic)
        {
            // Reserve headroom during powerful upward motion; normal jump arcs stay inside this band.
            float upperDistance = _camera.orthographicSize * 0.7f;
            float upwardPrediction = Mathf.Max(0f, motion.y) * smoothY;
            if (_target.position.y + upwardPrediction > _followCenter.y + upperDistance)
                desiredY = Mathf.Max(desiredY, _target.position.y - upperDistance + upwardPrediction);
        }
        _followCenter.x = Mathf.SmoothDamp(_followCenter.x, desiredX, ref _velocityX, smoothX);
        _followCenter.y = Mathf.SmoothDamp(_followCenter.y, desiredY, ref _velocityY, smoothY);
        float nextX = _followCenter.x + _lookAheadOffset;
        float nextY = _followCenter.y + _verticalLookAheadOffset + _upperViewOffset;

        nextX = ClampFollowDelta(current.x, nextX, motion.x);
        nextY = ClampFollowDelta(current.y, nextY, motion.y);

        if (_useBounds)
        {
            nextX = Mathf.Clamp(nextX, _minX, _maxX);
            nextY = Mathf.Clamp(nextY, _minY, _maxY);
        }

        transform.position = new Vector3(nextX, nextY, FixedZ);
        // Discard blocked movement so bounds / speed limiting cannot accumulate a hidden offset.
        _followCenter = new Vector2(nextX - _lookAheadOffset,
            nextY - _verticalLookAheadOffset - _upperViewOffset);
    }

    private float GetFramingDistance()
    {
        float halfHeight = _camera != null ? _camera.orthographicSize : _baseOrthographicSize;
        float aspect = _camera != null ? _camera.aspect : 16f / 9f;
        return (0.5f - _forwardViewportX) * 2f * halfHeight * aspect;
    }

    private void UpdateFramingDirection(float velocityX)
    {
        float candidate = _framingDirection;
        if (Mathf.Abs(velocityX) > _lookAheadVelocityThreshold)
            candidate = Mathf.Sign(velocityX);
        else if (Mathf.Abs(velocityX) < 0.1f && _targetPlayer != null && _targetPlayer.InputEnabled
            && Mathf.Abs(_targetPlayer.MoveInputX) > 0.2f)
            candidate = Mathf.Sign(_targetPlayer.MoveInputX);

        if (candidate == _framingDirection)
        {
            _pendingDirection = 0f;
            _directionChangeElapsed = 0f;
            return;
        }
        if (_pendingDirection != candidate)
        {
            _pendingDirection = candidate;
            _directionChangeElapsed = 0f;
        }
        _directionChangeElapsed += Time.deltaTime;
        if (_directionChangeElapsed >= _directionChangeDelay)
        {
            _framingDirection = candidate;
            _pendingDirection = 0f;
            _directionChangeElapsed = 0f;
        }
    }

    private void ResolveTargetMotionSources()
    {
        _targetRigidbody = _target != null ? _target.GetComponent<Rigidbody2D>() : null;
        _targetPlayer = _target != null ? _target.GetComponent<Player>() : null;
    }

    private void UpdateZoom(float speed)
    {
        if (_camera == null || !_camera.orthographic)
            return;

        if (_zoomInitialized && (_screenWidth != Screen.width || _screenHeight != Screen.height))
        {
            RestorePixelPerfectUpscaling();
            _zoomInitialized = false;
            return; // PixelPerfectCamera recalculates its reference projection next frame.
        }
        if (!_zoomInitialized)
        {
            _baseOrthographicSize = _referenceOrthographicSize;
            if (_pixelPerfect != null && _pixelPerfect.isActiveAndEnabled)
            {
                _baseOrthographicSize = _pixelPerfect.CorrectCinemachineOrthoSize(
                    _pixelPerfect.upscaleRT ? float.MaxValue : _baseOrthographicSize);
            }
            _baseOrthographicSize = Mathf.Max(0.01f, _baseOrthographicSize);
            // Expanding both half-height and center by the same amount keeps the
            // previous lower edge. Keep this correction outside the follow dead zone.
            float previousUpperOffset = _upperViewOffset;
            _upperViewOffset = _baseOrthographicSize * _normalViewExpansion;
            _baseOrthographicSize += _upperViewOffset;
            float heightChange = _upperViewOffset - previousUpperOffset;
            transform.position = ClampToBounds(transform.position + Vector3.up * heightChange);
            if (_respawnHeld)
            {
                _respawnFrom.y += heightChange;
                _respawnTo.y += heightChange;
            }
            _currentOrthographicSize = _baseOrthographicSize;
            _screenWidth = Screen.width;
            _screenHeight = Screen.height;
            _zoomInitialized = true;
        }

        float threshold = _zoomDemand > 0f ? _highSpeedThreshold - _zoomSpeedHysteresis : _highSpeedThreshold;
        if (speed > threshold)
        {
            float demand = Mathf.InverseLerp(_highSpeedThreshold, _fullZoomSpeed, speed);
            _zoomDemand = Mathf.Max(demand, Mathf.Min(_zoomDemand, 0.05f));
            _zoomHoldRemaining = _zoomHoldTime;
        }
        else
        {
            _zoomHoldRemaining = Mathf.Max(0f, _zoomHoldRemaining - Time.deltaTime);
            if (_zoomHoldRemaining <= 0f) _zoomDemand = 0f;
        }

        float desired = _baseOrthographicSize * (1f + _maxZoomOut * _zoomDemand);
        _currentOrthographicSize = Mathf.SmoothDamp(_currentOrthographicSize, desired,
            ref _zoomVelocity, _zoomSmoothTime);
        bool atRest = (_zoomDemand == 0f || _maxZoomOut <= 0f)
            && Mathf.Abs(_currentOrthographicSize - _baseOrthographicSize) < 0.0001f;
        if (atRest)
        {
            _currentOrthographicSize = _baseOrthographicSize;
            _zoomVelocity = 0f;
        }

        if (_pixelPerfect != null && _pixelPerfect.isActiveAndEnabled)
        {
            // The installed Built-in package explicitly supports smooth external lens blends
            // through this API. Keep its reference resolution, PPU, crop and snapping intact.
            // Render a wider view natively to avoid resampling sprites inside the low-res RT.
            if ((!atRest || _upperViewOffset > 0f) && _pixelPerfect.upscaleRT)
            {
                _restoreUpscaleRT = true;
                _pixelPerfect.upscaleRT = false;
            }
            else if (atRest && _upperViewOffset <= 0f)
                RestorePixelPerfectUpscaling();
            _pixelPerfect.CorrectCinemachineOrthoSize(_currentOrthographicSize);
        }
        _camera.orthographicSize = _currentOrthographicSize;
    }

    private void RestorePixelPerfectUpscaling()
    {
        if (_restoreUpscaleRT && _pixelPerfect != null)
            _pixelPerfect.upscaleRT = true;
        _restoreUpscaleRT = false;
    }

    private void AdoptCurrentFraming()
    {
        ResetLookAhead();
        _velocityX = _velocityY = 0f;
        _followCenter = transform.position;
        _followCenter.y -= _upperViewOffset;
        _lastGroundedY = _airPeakY = _target != null ? _target.position.y : 0f;
        _zoomDemand = _zoomHoldRemaining = _zoomVelocity = 0f;
        if (_camera != null) _currentOrthographicSize = _camera.orthographicSize;
    }

    /// <summary>Boss等の演出に位置と倍率を譲る。終了時は同じownerでEndEventControlを呼ぶ。</summary>
    public bool BeginEventControl(Object owner)
    {
        if (owner == null || _respawnHeld || (_eventControlled && _eventOwner != owner))
            return false;
        if (_shake != null) _shake.ResetShake();
        _eventOwner = owner;
        _eventControlled = true;
        return true;
    }

    public void EndEventControl(Object owner)
    {
        if (!_eventControlled || _eventOwner != owner) return;
        ReleaseEventControl();
    }

    private void ReleaseEventControl()
    {
        _eventControlled = false;
        _eventOwner = null;
        AdoptCurrentFraming();
    }

    /// <summary>
    /// ターゲットがデッドゾーンを超えた分だけカメラ目標位置をずらす。
    /// </summary>
    private static float ApplyDeadZone(float targetPos, float cameraPos, float deadZone)
    {
        if (deadZone <= 0f)
            return targetPos;

        float delta = targetPos - cameraPos;
        if (Mathf.Abs(delta) <= deadZone)
            return cameraPos;

        return targetPos - Mathf.Sign(delta) * deadZone;
    }

    private float ClampFollowDelta(float current, float next, float targetSpeed)
    {
        if (_maxFollowSpeed <= 0f)
            return next;

        float maxDelta = Mathf.Max(_maxFollowSpeed, Mathf.Abs(targetSpeed) * 1.15f) * Time.deltaTime;
        return current + Mathf.Clamp(next - current, -maxDelta, maxDelta);
    }

    /// <summary>最終的なShake合成後も既存のカメラ中心境界を維持する。</summary>
    public Vector3 ClampToBounds(Vector3 position)
    {
        if (_useBounds)
        {
            position.x = Mathf.Clamp(position.x, _minX, _maxX);
            position.y = Mathf.Clamp(position.y, _minY, _maxY);
        }
        return position;
    }

    /// <summary>
    /// 追従ターゲットを外部から差り替える（通常は Player のまま）。
    /// </summary>
    public void SetTarget(Transform target)
    {
        _target = target;
        ResolveTargetMotionSources();
        AdoptCurrentFraming();
    }

    /// <summary>
    /// 即座にターゲット位置へスナップ（チェックポイント切替・シーン開始時など）。
    /// </summary>
    public void SnapToTarget()
    {
        if (_respawnHeld || _eventControlled)
            return;
        if (_target == null)
            return;

        if (_shake != null) _shake.ResetShake();

        ResetLookAhead();

        transform.position = new Vector3(
            _target.position.x + _offset.x,
            _target.position.y + _offset.y + _upperViewOffset,
            FixedZ);
        if (_useBounds)
            transform.position = new Vector3(Mathf.Clamp(transform.position.x, _minX, _maxX),
                Mathf.Clamp(transform.position.y, _minY, _maxY), FixedZ);
        AdoptCurrentFraming();
        _currentOrthographicSize = _baseOrthographicSize;
        if (_camera != null && _zoomInitialized) _camera.orthographicSize = _baseOrthographicSize;
        RestorePixelPerfectUpscaling();
    }

    public void ResetLookAhead()
    {
        _lookAheadOffset = 0f;
        _lookAheadVelocity = 0f;
        _pendingDirection = _directionChangeElapsed = 0f;
        _verticalLookAheadOffset = _verticalLookAheadVelocity = _airborneTime = 0f;
    }

    public void HoldForRespawn()
    {
        if (_shake != null) _shake.ResetShake();
        ReleaseEventControl();
        _respawnHeld = true;
        _respawnTravelling = false;
        _respawnElapsed = _respawnDuration = 0f;
        _velocityX = _velocityY = 0f;
        ResetLookAhead();
        _followCenter = transform.position;
        _followCenter.y -= _upperViewOffset;
    }

    public float BeginRespawnTravel(Vector3 playerPosition)
    {
        HoldForRespawn();
        _respawnFrom = transform.position;
        _respawnTo = new Vector3(playerPosition.x + _offset.x,
            playerPosition.y + _offset.y + _upperViewOffset, FixedZ);
        if (_useBounds)
        {
            _respawnTo.x = Mathf.Clamp(_respawnTo.x, _minX, _maxX);
            _respawnTo.y = Mathf.Clamp(_respawnTo.y, _minY, _maxY);
        }
        float min = Mathf.Max(0.1f, _respawnMinDuration);
        _respawnDuration = Mathf.Lerp(min, Mathf.Max(min, _respawnMaxDuration),
            Mathf.Clamp01(Vector2.Distance(_respawnFrom, _respawnTo) / Mathf.Max(1f, _respawnMaxDurationDistance)));
        _respawnTravelling = true;
        return _respawnDuration;
    }

    public void EndRespawnTravel()
    {
        _respawnHeld = _respawnTravelling = false;
        AdoptCurrentFraming();
    }
}
