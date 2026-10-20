using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(1000)]
public class CameraShake2D : MonoBehaviour
{
    [Header("Impact Envelope")]
    [SerializeField, Min(0f), Tooltip("既存の衝突速度→強度マッピングに掛ける倍率")]
    private float _intensity = 1f;
    [SerializeField, Min(0f)] private float _maximumStrength = 0.18f;
    [SerializeField, Min(0.01f)] private float _maximumDuration = 0.18f;
    [SerializeField, Range(1f, 4f), Tooltip("大きく開始して素早く減衰。1=直線、2=二乗")]
    private float _decayPower = 2f;
    [SerializeField, Min(0f), Tooltip("短時間の連続要求を一つの振動にまとめる")]
    private float _mergeInterval = 0.05f;

    private float _remainingDuration;
    private float _duration;
    private float _strength;
    private float _lastRequestTime = float.NegativeInfinity;
    private Vector3 _appliedOffset;
    private CameraFollow _follow;

    private void Awake() => _follow = GetComponent<CameraFollow>();

    public bool IsShaking => _remainingDuration > 0f;
    public float CurrentStrength => IsShaking ? _strength : 0f;
    public float CurrentAmplitude => IsShaking && _duration > 0f
        ? _strength * Mathf.Pow(_remainingDuration / _duration, _decayPower) : 0f;

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
        if (_follow != null && (_follow.IsEventControlled || _follow.IsRespawnCameraHeld))
            return;

        strength = Mathf.Min(_maximumStrength, strength * _intensity);
        duration = Mathf.Min(_maximumDuration, duration);
        if (strength <= 0f)
            return;

        // Weaker contacts never restart a strong envelope. Requests for the same
        // impact merge by maximum rather than adding offsets or extending its tail.
        if (IsShaking)
        {
            if (Time.time - _lastRequestTime < _mergeInterval)
            {
                if (strength <= _strength) return;
                _strength = strength;
                _duration = Mathf.Max(_duration, duration);
                _remainingDuration = Mathf.Max(_remainingDuration, duration);
                return;
            }
            if (strength <= CurrentAmplitude) return;
        }
        _lastRequestTime = Time.time;
        _remainingDuration = _duration = duration;
        _strength = strength;
    }

    /// <summary>
    /// Respawn等のカメラスナップ前に、現在加算済みのShake Offsetを安全に除去する。
    /// </summary>
    public void ResetShake()
    {
        RemoveAppliedOffset();
        _remainingDuration = 0f;
        _duration = 0f;
        _strength = 0f;
        _lastRequestTime = float.NegativeInfinity;
    }

    private void LateUpdate()
    {
        // Update removes the previous offset even while paused. Do not add a new
        // random offset when the scaled timer cannot advance (pause/clear/hit stop).
        if (_remainingDuration <= 0f || Time.deltaTime <= 0f)
            return;
        if (_follow != null && (_follow.IsEventControlled || _follow.IsRespawnCameraHeld))
            return;

        Vector2 randomOffset = Random.insideUnitCircle * CurrentAmplitude;
        _appliedOffset = new Vector3(randomOffset.x, randomOffset.y, 0f);
        Vector3 basePosition = transform.position;
        Vector3 shaken = basePosition + _appliedOffset;
        if (_follow != null)
            shaken = _follow.ClampToBounds(shaken);
        _appliedOffset = shaken - basePosition;
        transform.position = shaken;
    }

    private void OnDisable()
    {
        ResetShake();
    }

    private void RemoveAppliedOffset()
    {
        if (_appliedOffset == Vector3.zero)
            return;

        transform.position -= _appliedOffset;
        _appliedOffset = Vector3.zero;
    }
}
