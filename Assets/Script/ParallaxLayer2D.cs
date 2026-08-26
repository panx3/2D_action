using UnityEngine;

/// <summary>
/// カメラ移動量に応じて、係数だけ遅れて追従する単一スプライト層。
/// </summary>
[DisallowMultipleComponent]
public class ParallaxLayer2D : MonoBehaviour
{
    [SerializeField, Range(0f, 1f)] private float _parallaxX = 0.25f;
    [SerializeField, Range(0f, 1f)] private float _parallaxY = 0.12f;

    private Transform _cameraTransform;
    private Vector3 _startLocalPosition;
    private Vector3 _cameraStartPosition;

    public float ParallaxX
    {
        get => _parallaxX;
        set => _parallaxX = Mathf.Clamp01(value);
    }

    public float ParallaxY
    {
        get => _parallaxY;
        set => _parallaxY = Mathf.Clamp01(value);
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
        transform.localPosition = _startLocalPosition + new Vector3(
            cameraDelta.x * _parallaxX,
            cameraDelta.y * _parallaxY,
            0f);
    }
}
