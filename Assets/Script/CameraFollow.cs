using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// プレイヤーを LateUpdate で遅れて滑らかに追従する 2D 用カメラ。
/// デッドゾーン・軸別 SmoothDamp・最大追従速度で、鉄球引っ張り時の画面の急スライドを抑える。
/// </summary>
[DisallowMultipleComponent]
public class CameraFollow : MonoBehaviour
{
    [Header("追従対象")]
    [SerializeField, Tooltip("追従するターゲット（通常は Player の Transform）。鉄球・鎖には切り替えない。")]
    private Transform _target;

    [Header("オフセット")]
    [SerializeField, Tooltip("ターゲットからのオフセット。推奨は (0, 1.5)。")]
    private Vector2 _offset = new Vector2(0f, 1.5f);

    [Header("滑らかさ（秒・小さいほど素早く追う）")]
    [FormerlySerializedAs("_smoothTime")]
    [SerializeField, Tooltip("横方向の SmoothDamp 到達時間。目安 0.16〜0.24")]
    private float _smoothTimeX = 0.24f;
    [SerializeField, Tooltip("縦方向の SmoothDamp 到達時間。目安 0.16〜0.22")]
    private float _smoothTimeY = 0.176f;

    [Header("デッドゾーン")]
    [FormerlySerializedAs("_followDeadZone")]
    [SerializeField, Tooltip("この範囲内ではカメラを動かさない（横）。目安 1.5〜3.0")]
    private float _followDeadZoneX = 2.5f;
    [SerializeField, Tooltip("この範囲内ではカメラを動かさない（縦）。目安 1.0〜2.0")]
    private float _followDeadZoneY = 1.5f;

    [Header("追従速度上限")]
    [SerializeField, Tooltip("1 フレームあたりの最大追従速度（units/s）。0 で無制限。目安 10〜15")]
    private float _maxFollowSpeed = 12.5f;

    [Header("Horizontal Look Ahead")]
    [SerializeField, Min(0f), Tooltip("実際の水平移動方向へ先読みする距離")]
    private float _horizontalLookAhead = 2.2f;
    [SerializeField, Min(0.01f), Tooltip("Look Ahead左右切替・停止復帰のSmoothDamp時間")]
    private float _lookAheadSmoothTime = 0.15f;
    [SerializeField, Min(0f), Tooltip("この水平速度未満ではLook Aheadを0へ戻す")]
    private float _lookAheadVelocityThreshold = 0.1f;
    [SerializeField, Range(0f, 1f), Tooltip("Hook/Magnet Swing中のLook Ahead倍率")]
    private float _swingLookAheadMultiplier = 0.35f;

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

    private const float FixedZ = -10f;
    private float _velocityX;
    private float _velocityY;
    private Rigidbody2D _targetRigidbody;
    private MorningStarLauncher _targetLauncher;
    private float _lookAheadOffset;
    private float _lookAheadVelocity;

    public float HorizontalLookAhead => _horizontalLookAhead;
    public float LookAheadSmoothTime => _lookAheadSmoothTime;
    public float CurrentLookAheadOffset => _lookAheadOffset;
    public Vector2 FollowOffset => _offset;
    public float SwingLookAheadMultiplier => _swingLookAheadMultiplier;

    private void Awake()
    {
        ResolveTargetMotionSources();
    }

    private void OnValidate()
    {
        _horizontalLookAhead = Mathf.Max(0f, _horizontalLookAhead);
        _lookAheadSmoothTime = Mathf.Max(0.01f, _lookAheadSmoothTime);
        _lookAheadVelocityThreshold = Mathf.Max(0f, _lookAheadVelocityThreshold);
        _swingLookAheadMultiplier = Mathf.Clamp01(_swingLookAheadMultiplier);
    }

    private void LateUpdate()
    {
        if (_target == null)
            return;

        if (_targetRigidbody == null)
            ResolveTargetMotionSources();

        float desiredLookAhead = GetDesiredLookAhead();
        _lookAheadOffset = Mathf.SmoothDamp(
            _lookAheadOffset,
            desiredLookAhead,
            ref _lookAheadVelocity,
            Mathf.Max(0.01f, _lookAheadSmoothTime));

        Vector3 current = transform.position;
        float targetX = _target.position.x + _offset.x + _lookAheadOffset;
        float targetY = _target.position.y + _offset.y;

        float desiredX = ApplyDeadZone(targetX, current.x, _followDeadZoneX);
        float desiredY = ApplyDeadZone(targetY, current.y, _followDeadZoneY);

        float smoothX = Mathf.Max(0.0001f, _smoothTimeX);
        float smoothY = Mathf.Max(0.0001f, _smoothTimeY);
        float nextX = Mathf.SmoothDamp(current.x, desiredX, ref _velocityX, smoothX);
        float nextY = Mathf.SmoothDamp(current.y, desiredY, ref _velocityY, smoothY);

        nextX = ClampFollowDelta(current.x, nextX);
        nextY = ClampFollowDelta(current.y, nextY);

        if (_useBounds)
        {
            nextX = Mathf.Clamp(nextX, _minX, _maxX);
            nextY = Mathf.Clamp(nextY, _minY, _maxY);
        }

        transform.position = new Vector3(nextX, nextY, FixedZ);
    }

    private float GetDesiredLookAhead()
    {
        if (_targetRigidbody == null)
            return 0f;

        float velocityX = _targetRigidbody.linearVelocity.x;
        if (Mathf.Abs(velocityX) <= _lookAheadVelocityThreshold)
            return 0f;

        float multiplier = _targetLauncher != null && _targetLauncher.IsHookedState
            ? _swingLookAheadMultiplier
            : 1f;
        return Mathf.Sign(velocityX) * _horizontalLookAhead * multiplier;
    }

    private void ResolveTargetMotionSources()
    {
        _targetRigidbody = _target != null ? _target.GetComponent<Rigidbody2D>() : null;
        _targetLauncher = _target != null ? _target.GetComponent<MorningStarLauncher>() : null;
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

    private float ClampFollowDelta(float current, float next)
    {
        if (_maxFollowSpeed <= 0f)
            return next;

        float maxDelta = _maxFollowSpeed * Time.deltaTime;
        return current + Mathf.Clamp(next - current, -maxDelta, maxDelta);
    }

    /// <summary>
    /// 追従ターゲットを外部から差り替える（通常は Player のまま）。
    /// </summary>
    public void SetTarget(Transform target)
    {
        _target = target;
        ResolveTargetMotionSources();
        ResetLookAhead();
    }

    /// <summary>
    /// 即座にターゲット位置へスナップ（チェックポイント切替・シーン開始時など）。
    /// </summary>
    public void SnapToTarget()
    {
        if (_target == null)
            return;

        CameraShake2D cameraShake = GetComponent<CameraShake2D>();
        if (cameraShake != null)
            cameraShake.ResetShake();

        ResetLookAhead();

        transform.position = new Vector3(
            _target.position.x + _offset.x,
            _target.position.y + _offset.y,
            FixedZ);
        _velocityX = 0f;
        _velocityY = 0f;
    }

    public void ResetLookAhead()
    {
        _lookAheadOffset = 0f;
        _lookAheadVelocity = 0f;
    }
}
