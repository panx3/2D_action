using UnityEngine;

/// <summary>
/// カメラ移動量に応じて、係数だけ遅れて追従する単一スプライト層。
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(1000)]
public class ParallaxLayer2D : MonoBehaviour
{
    [SerializeField, Range(-1f, 1f), Tooltip("カメラへの追従率。0はワールド固定、負の値はカメラより手前の視差です。")]
    private float _parallaxX = 0.25f;
    [SerializeField, Range(-1f, 1f)] private float _parallaxY = 0.12f;

    private Transform _cameraTransform;
    private Vector3 _startLocalPosition;
    private Vector3 _cameraStartPosition;

    public float ParallaxX
    {
        get => _parallaxX;
        set => _parallaxX = Mathf.Clamp(value, -1f, 1f);
    }

    public float ParallaxY
    {
        get => _parallaxY;
        set => _parallaxY = Mathf.Clamp(value, -1f, 1f);
    }

    public void Initialize(Transform cameraTransform)
    {
        _cameraTransform = cameraTransform;
        _startLocalPosition = transform.localPosition;
        _cameraStartPosition = _cameraTransform != null ? _cameraTransform.position : Vector3.zero;
    }

    private void LateUpdate()
    {
        if (_cameraTransform == null)
            return;

        Vector3 cameraDelta = _cameraTransform.position - _cameraStartPosition;
        Vector3 offset = new Vector3(
            cameraDelta.x * _parallaxX,
            cameraDelta.y * _parallaxY,
            0f);
        // Prefabの親を拡大・回転しても視差のワールド移動量を維持する。
        transform.localPosition = _startLocalPosition +
            (transform.parent != null ? transform.parent.InverseTransformVector(offset) : offset);
    }
}
