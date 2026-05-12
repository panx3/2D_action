using UnityEngine;

/// <summary>
/// プレイヤーを SmoothDamp でなめらかに追従する 2D 用カメラ。
/// LateUpdate で位置を更新し、Z 軸は -10 に固定する。
/// </summary>
[DisallowMultipleComponent]
public class CameraFollow : MonoBehaviour
{
    [Header("追従対象")]
    [SerializeField, Tooltip("追従するターゲット（通常は Player の Transform）")]
    private Transform _target;

    [Header("追従設定")]
    [SerializeField, Tooltip("Vector3.SmoothDamp の到達時間（秒）。小さいほど機敏に追従する。")]
    private float _smoothTime = 0.2f;
    [SerializeField, Tooltip("ターゲットからのオフセット。推奨は (0, 1.5)。")]
    private Vector2 _offset = new Vector2(0f, 1.5f);

    [Header("移動範囲制限")]
    [SerializeField, Tooltip("カメラ位置を bounds で Clamp するかどうか。")]
    private bool _useBounds = false;
    [SerializeField, Tooltip("カメラ移動範囲 (x: xMin, y: xMax, z: yMin, w: yMax)。")]
    private Vector4 _bounds = new Vector4(-100f, 100f, -100f, 100f);

    private const float FixedZ = -10f;
    private Vector3 _velocity;

    private void LateUpdate()
    {
        if (_target == null) return;

        Vector3 desired = new Vector3(
            _target.position.x + _offset.x,
            _target.position.y + _offset.y,
            FixedZ);

        Vector3 next = Vector3.SmoothDamp(transform.position, desired, ref _velocity, _smoothTime);

        if (_useBounds)
        {
            next.x = Mathf.Clamp(next.x, _bounds.x, _bounds.y);
            next.y = Mathf.Clamp(next.y, _bounds.z, _bounds.w);
        }

        next.z = FixedZ;
        transform.position = next;
    }

    /// <summary>
    /// 追従ターゲットを外部から差し替える。
    /// </summary>
    public void SetTarget(Transform target)
    {
        _target = target;
    }

    /// <summary>
    /// 即座にターゲット位置へスナップ（チェックポイント切替時などに使用）。
    /// </summary>
    public void SnapToTarget()
    {
        if (_target == null) return;
        Vector3 snap = new Vector3(_target.position.x + _offset.x, _target.position.y + _offset.y, FixedZ);
        transform.position = snap;
        _velocity = Vector3.zero;
    }
}
