using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(1000)]
public class CameraShake2D : MonoBehaviour
{
    private float _remainingDuration;
    private float _strength;
    private Vector3 _appliedOffset;

    public bool IsShaking => _remainingDuration > 0f;
    public float CurrentStrength => IsShaking ? _strength : 0f;

    private void Update()
    {
        // 前フレームの揺れを CameraFollow の LateUpdate より先に外す。
        // これにより追従計算へランダムなオフセットを混ぜない。
        RemoveAppliedOffset();

        if (_remainingDuration > 0f)
            _remainingDuration = Mathf.Max(0f, _remainingDuration - Time.deltaTime);
    }

    public void Shake(float duration, float strength)
    {
        if (!isActiveAndEnabled || duration <= 0f || strength <= 0f)
            return;

        RemoveAppliedOffset();
        _remainingDuration = duration;
        _strength = strength;
    }

    /// <summary>
    /// Respawn等のカメラスナップ前に、現在加算済みのShake Offsetを安全に除去する。
    /// </summary>
    public void ResetShake()
    {
        RemoveAppliedOffset();
        _remainingDuration = 0f;
        _strength = 0f;
    }

    private void LateUpdate()
    {
        if (_remainingDuration <= 0f)
            return;

        Vector2 randomOffset = Random.insideUnitCircle * _strength;
        _appliedOffset = new Vector3(randomOffset.x, randomOffset.y, 0f);
        transform.localPosition += _appliedOffset;
    }

    private void OnDisable()
    {
        ResetShake();
    }

    private void RemoveAppliedOffset()
    {
        if (_appliedOffset == Vector3.zero)
            return;

        transform.localPosition -= _appliedOffset;
        _appliedOffset = Vector3.zero;
    }
}
