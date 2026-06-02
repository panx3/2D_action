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
    [SerializeField, Tooltip("横方向の SmoothDamp 到達時間。目安 0.25〜0.35")]
    private float _smoothTimeX = 0.3f;
    [SerializeField, Tooltip("縦方向の SmoothDamp 到達時間。目安 0.18〜0.28")]
    private float _smoothTimeY = 0.22f;

    [Header("デッドゾーン")]
    [FormerlySerializedAs("_followDeadZone")]
    [SerializeField, Tooltip("この範囲内ではカメラを動かさない（横）。目安 1.5〜3.0")]
    private float _followDeadZoneX = 2.5f;
    [SerializeField, Tooltip("この範囲内ではカメラを動かさない（縦）。目安 1.0〜2.0")]
    private float _followDeadZoneY = 1.5f;

    [Header("追従速度上限")]
    [SerializeField, Tooltip("1 フレームあたりの最大追従速度（units/s）。0 で無制限。目安 8〜12")]
    private float _maxFollowSpeed = 10f;

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

    private void LateUpdate()
    {
        if (_target == null)
            return;

        Vector3 current = transform.position;
        float targetX = _target.position.x + _offset.x;
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
    }

    /// <summary>
    /// 即座にターゲット位置へスナップ（チェックポイント切替・シーン開始時など）。
    /// </summary>
    public void SnapToTarget()
    {
        if (_target == null)
            return;

        transform.position = new Vector3(
            _target.position.x + _offset.x,
            _target.position.y + _offset.y,
            FixedZ);
        _velocityX = 0f;
        _velocityY = 0f;
    }
}
