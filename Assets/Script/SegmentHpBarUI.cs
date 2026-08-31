using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// PlayerHealth と連動する連続量HPバー。
/// Fill画像自体は伸縮せず、RectMask2Dの幅を右側から狭めて表示する。
/// </summary>
[DisallowMultipleComponent]
public class SegmentHpBarUI : MonoBehaviour
{
    // 260x35 の元Sprite上で、中央ゲージが占めるPixel領域（x=62..205 / y=13..19）。
    // Frameを基準に正規化して使うため、FrameのRectTransformは一切変更しない。
    private static readonly Vector4 GaugeRegionNormalized = new Vector4(
        62f / 260f,
        13f / 35f,
        144f / 260f,
        7f / 35f);

    [Header("Health")]
    [SerializeField, Tooltip("既存PlayerHealth。未設定時だけPlay中に検索する")]
    private PlayerHealth _playerHealth;

    [Header("Editable UI Hierarchy")]
    [SerializeField] private RectTransform _hpBarRoot;
    [SerializeField] private Image _emptyBar;
    [SerializeField] private RectTransform _damageMask;
    [SerializeField] private Image _damageFill;
    [SerializeField] private RectTransform _hpMask;
    [SerializeField] private Image _hpFill;
    [SerializeField] private Image _frame;

    [Header("HP Smooth")]
    [SerializeField, Min(0f), Tooltip("現在HP表示が実HPへ到達する時間（秒）")]
    private float _hpSmoothDuration = 0.25f;

    [Header("Damage Follow")]
    [SerializeField, Min(0f), Tooltip("DamageFillが減り始めるまでの待機時間（秒）")]
    private float _damageDelay = 0.10f;
    [SerializeField, Min(0f), Tooltip("DamageFillが実HPへ到達する時間（秒）")]
    private float _damageSmoothDuration = 0.25f;

    [Header("Damage Shake")]
    [SerializeField, Min(0f)] private float _damageShakeDuration = 0.14f;
    [SerializeField, Tooltip("被弾時の最大揺れ幅（UI Pixel）")]
    private Vector2 _damageShakeAmount = new Vector2(4f, 1.5f);

    private Vector2 _hpMaskFullSize;
    private Vector2 _damageMaskFullSize;
    private float _targetHpNormalized = 1f;
    private float _displayHpNormalized = 1f;
    private float _damageHpNormalized = 1f;
    private float _hpMoveSpeed;
    private float _damageMoveSpeed;
    private float _damageDelayRemaining;
    private bool _displayInitialized;
    private bool _damageEventPending;
    private Vector2 _initialAnchoredPosition;
    private float _shakeElapsed;
    private bool _isShaking;
    private bool _shakeBaseCached;

    public float TargetHpNormalized => _targetHpNormalized;
    public float DisplayHpNormalized => _displayHpNormalized;
    public float DamageHpNormalized => _damageHpNormalized;
    public float HpSmoothDuration => _hpSmoothDuration;
    public float DamageDelay => _damageDelay;
    public float DamageSmoothDuration => _damageSmoothDuration;
    public float DamageShakeDuration => _damageShakeDuration;
    public Vector2 DamageShakeAmount => _damageShakeAmount;
    public bool IsShaking => _isShaking;
    public Vector2 InitialAnchoredPosition => _initialAnchoredPosition;
    public float MaxShakeOffsetObserved { get; private set; }
    public float HpMaskWidth => _hpMask != null ? _hpMask.sizeDelta.x : 0f;
    public float DamageMaskWidth => _damageMask != null ? _damageMask.sizeDelta.x : 0f;
    public float FullMaskWidth => _hpMaskFullSize.x;

    private void Awake()
    {
        ResolvePlayerHealth();
        ResolveVisualReferences();
        AlignVisualsToFrame();
        CacheMaskSizes();
        CacheShakeRoot();
    }

    private void Start()
    {
        ResolvePlayerHealth();
        BindHealthEvents();
        RefreshFromHealth();
    }

    private void OnEnable()
    {
        ResolvePlayerHealth();
        BindHealthEvents();
        RefreshFromHealth();
    }

    private void OnDisable()
    {
        UnbindHealthEvents();
        FinishDamageShake();
    }

    private void OnValidate()
    {
        _hpSmoothDuration = Mathf.Max(0f, _hpSmoothDuration);
        _damageDelay = Mathf.Max(0f, _damageDelay);
        _damageSmoothDuration = Mathf.Max(0f, _damageSmoothDuration);
        _damageShakeDuration = Mathf.Max(0f, _damageShakeDuration);
        _damageShakeAmount.x = Mathf.Max(0f, _damageShakeAmount.x);
        _damageShakeAmount.y = Mathf.Max(0f, _damageShakeAmount.y);
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        UpdateDisplayedHp(dt);
        UpdateDamageFill(dt);
        UpdateDamageShake(dt);
    }

    private void ResolvePlayerHealth()
    {
        if (_playerHealth != null || !Application.isPlaying)
            return;

        _playerHealth = FindAnyObjectByType<PlayerHealth>(FindObjectsInactive.Exclude);
    }

    private void ResolveVisualReferences()
    {
        if (_hpBarRoot == null)
            _hpBarRoot = FindRect("HPBarRoot");
        if (_emptyBar == null)
            _emptyBar = FindImage("EmptyBar");
        if (_damageMask == null)
            _damageMask = FindRect("DamageMask");
        if (_damageFill == null)
            _damageFill = FindImage("DamageFill");
        if (_hpMask == null)
            _hpMask = FindRect("HpMask");
        if (_hpFill == null)
            _hpFill = FindImage("HpFill");
        if (_frame == null)
            _frame = FindImage("Frame");
    }

    private RectTransform FindRect(string objectName)
    {
        RectTransform[] rects = GetComponentsInChildren<RectTransform>(true);
        for (int i = 0; i < rects.Length; i++)
        {
            if (rects[i].name == objectName)
                return rects[i];
        }

        return null;
    }

    private Image FindImage(string objectName)
    {
        Image[] images = GetComponentsInChildren<Image>(true);
        for (int i = 0; i < images.Length; i++)
        {
            if (images[i].name == objectName)
                return images[i];
        }

        return null;
    }

    /// <summary>
    /// 完成済みFrameの表示矩形を基準に、中央ゲージだけをPixel比率で位置合わせする。
    /// Frame自身のAnchor / Offset / Pivot / Scaleには触れない。
    /// </summary>
    [ContextMenu("Align Gauge Visuals To Frame")]
    public void AlignVisualsToFrame()
    {
        ResolveVisualReferences();
        if (_hpBarRoot == null || _frame == null)
            return;

        RectTransform frameRect = _frame.rectTransform;
        Vector3[] worldCorners = new Vector3[4];
        frameRect.GetWorldCorners(worldCorners);

        Vector3 frameBottomLeft = _hpBarRoot.InverseTransformPoint(worldCorners[0]);
        Vector3 frameTopLeft = _hpBarRoot.InverseTransformPoint(worldCorners[1]);
        Vector3 frameTopRight = _hpBarRoot.InverseTransformPoint(worldCorners[2]);

        float frameLeft = frameTopLeft.x;
        float frameTop = frameTopLeft.y;
        float frameWidth = frameTopRight.x - frameTopLeft.x;
        float frameHeight = frameTopLeft.y - frameBottomLeft.y;
        if (frameWidth <= 0f || frameHeight <= 0f)
            return;

        float gaugeLeft = frameLeft + frameWidth * GaugeRegionNormalized.x;
        float gaugeTop = frameTop - frameHeight * GaugeRegionNormalized.y;
        float gaugeWidth = frameWidth * GaugeRegionNormalized.z;
        float gaugeHeight = frameHeight * GaugeRegionNormalized.w;

        ConfigureGaugeRect(_emptyBar != null ? _emptyBar.rectTransform : null,
            gaugeLeft, gaugeTop, gaugeWidth, gaugeHeight);
        ConfigureGaugeRect(_damageMask, gaugeLeft, gaugeTop, gaugeWidth, gaugeHeight);
        ConfigureGaugeRect(_hpMask, gaugeLeft, gaugeTop, gaugeWidth, gaugeHeight);

        ConfigureFillRect(_damageFill != null ? _damageFill.rectTransform : null,
            frameLeft - gaugeLeft, frameTop - gaugeTop, frameWidth, frameHeight);
        ConfigureFillRect(_hpFill != null ? _hpFill.rectTransform : null,
            frameLeft - gaugeLeft, frameTop - gaugeTop, frameWidth, frameHeight);

        CacheMaskSizes();
        ApplyDisplayedHp();
    }

    private void ConfigureGaugeRect(
        RectTransform rect,
        float left,
        float top,
        float width,
        float height)
    {
        if (rect == null || _hpBarRoot == null)
            return;

        Rect rootRect = _hpBarRoot.rect;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;
        rect.anchoredPosition = new Vector2(left - rootRect.xMin, top - rootRect.yMax);
        rect.sizeDelta = new Vector2(width, height);
    }

    private static void ConfigureFillRect(
        RectTransform rect,
        float leftOffset,
        float topOffset,
        float width,
        float height)
    {
        if (rect == null)
            return;

        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;
        rect.anchoredPosition = new Vector2(leftOffset, topOffset);
        rect.sizeDelta = new Vector2(width, height);
    }

    private void CacheMaskSizes()
    {
        if (_hpMask != null)
            _hpMaskFullSize = _hpMask.sizeDelta;
        if (_damageMask != null)
            _damageMaskFullSize = _damageMask.sizeDelta;

        if (_hpMaskFullSize.x <= 0f && _damageMaskFullSize.x > 0f)
            _hpMaskFullSize = _damageMaskFullSize;
        if (_damageMaskFullSize.x <= 0f && _hpMaskFullSize.x > 0f)
            _damageMaskFullSize = _hpMaskFullSize;
    }

    private void BindHealthEvents()
    {
        if (_playerHealth == null)
            return;

        _playerHealth.OnHealthChanged -= HandleHealthChanged;
        _playerHealth.OnHealthChanged += HandleHealthChanged;
        _playerHealth.OnDamaged -= HandleDamaged;
        _playerHealth.OnDamaged += HandleDamaged;
    }

    private void UnbindHealthEvents()
    {
        if (_playerHealth == null)
            return;

        _playerHealth.OnHealthChanged -= HandleHealthChanged;
        _playerHealth.OnDamaged -= HandleDamaged;
    }

    private void HandleDamaged()
    {
        // PlayerHealth.OnDamagedは無敵判定を通過し、実際にHPが減った時だけ発火する。
        _damageEventPending = true;
        StartDamageShake();
    }

    private void HandleHealthChanged(int currentHp, int maxHp)
    {
        SetHp(currentHp, maxHp);
    }

    private void RefreshFromHealth()
    {
        if (_playerHealth != null)
            SetHp(_playerHealth.CurrentHp, _playerHealth.MaxHp);
    }

    public void SetHp(int currentHp, int maxHp)
    {
        maxHp = Mathf.Max(1, maxHp);
        currentHp = Mathf.Clamp(currentHp, 0, maxHp);
        float normalized = currentHp / (float)maxHp;

        if (!_displayInitialized)
        {
            _displayInitialized = true;
            _targetHpNormalized = normalized;
            _displayHpNormalized = normalized;
            _damageHpNormalized = normalized;
            _hpMoveSpeed = 0f;
            _damageMoveSpeed = 0f;
            _damageDelayRemaining = 0f;
            ApplyDisplayedHp();
            return;
        }

        bool lostHp = normalized < _targetHpNormalized - 0.0001f;
        _targetHpNormalized = normalized;

        float hpDistance = Mathf.Abs(_displayHpNormalized - _targetHpNormalized);
        _hpMoveSpeed = _hpSmoothDuration > 0f
            ? hpDistance / _hpSmoothDuration
            : hpDistance;

        if (lostHp)
        {
            _damageDelayRemaining = _damageEventPending ? _damageDelay : 0f;
            float damageDistance = Mathf.Abs(_damageHpNormalized - _targetHpNormalized);
            _damageMoveSpeed = _damageSmoothDuration > 0f
                ? damageDistance / _damageSmoothDuration
                : damageDistance;
        }
        else
        {
            // 回復・Respawnでは遅延Damage表示を残さない。
            _damageDelayRemaining = 0f;
            _damageHpNormalized = normalized;
            _damageMoveSpeed = 0f;
        }

        _damageEventPending = false;
        ApplyDisplayedHp();
    }

    private void UpdateDisplayedHp(float dt)
    {
        if (!_displayInitialized)
            return;

        _displayHpNormalized = MoveNormalized(
            _displayHpNormalized,
            _targetHpNormalized,
            _hpMoveSpeed,
            _hpSmoothDuration,
            dt);
        ApplyMaskWidth(_hpMask, _hpMaskFullSize, _displayHpNormalized);
    }

    private void UpdateDamageFill(float dt)
    {
        if (!_displayInitialized)
            return;

        if (_damageDelayRemaining > 0f)
        {
            _damageDelayRemaining = Mathf.Max(0f, _damageDelayRemaining - dt);
            return;
        }

        _damageHpNormalized = MoveNormalized(
            _damageHpNormalized,
            _targetHpNormalized,
            _damageMoveSpeed,
            _damageSmoothDuration,
            dt);
        ApplyMaskWidth(_damageMask, _damageMaskFullSize, _damageHpNormalized);
    }

    private static float MoveNormalized(
        float current,
        float target,
        float speed,
        float duration,
        float dt)
    {
        if (Mathf.Abs(current - target) <= 0.0001f)
            return target;
        if (duration <= 0f)
            return target;

        float next = Mathf.MoveTowards(current, target, Mathf.Max(0.0001f, speed) * dt);
        return Mathf.Abs(next - target) <= 0.0001f ? target : next;
    }

    private void ApplyDisplayedHp()
    {
        ApplyMaskWidth(_hpMask, _hpMaskFullSize, _displayHpNormalized);
        ApplyMaskWidth(_damageMask, _damageMaskFullSize, _damageHpNormalized);
    }

    private static void ApplyMaskWidth(RectTransform mask, Vector2 fullSize, float normalized)
    {
        if (mask == null || fullSize.x <= 0f)
            return;

        Vector2 size = fullSize;
        size.x *= Mathf.Clamp01(normalized);
        mask.sizeDelta = size;
    }

    private void CacheShakeRoot()
    {
        if (_hpBarRoot == null)
            return;

        _initialAnchoredPosition = _hpBarRoot.anchoredPosition;
        _shakeBaseCached = true;
    }

    private void StartDamageShake()
    {
        if (_hpBarRoot == null)
            return;
        if (!_shakeBaseCached)
            CacheShakeRoot();

        _hpBarRoot.anchoredPosition = _initialAnchoredPosition;
        _shakeElapsed = 0f;
        MaxShakeOffsetObserved = 0f;
        _isShaking = _damageShakeDuration > 0f
            && _damageShakeAmount.sqrMagnitude > 0.0001f;
        if (_isShaking)
            ApplyDamageShakeOffset(0.08f);
    }

    private void UpdateDamageShake(float dt)
    {
        if (!_isShaking || _hpBarRoot == null)
            return;

        _shakeElapsed += dt;
        if (_shakeElapsed >= _damageShakeDuration)
        {
            FinishDamageShake();
            return;
        }

        float progress = Mathf.Clamp01(_shakeElapsed / Mathf.Max(0.0001f, _damageShakeDuration));
        ApplyDamageShakeOffset(progress);
    }

    private void ApplyDamageShakeOffset(float progress)
    {
        float envelope = 1f - Mathf.Clamp01(progress);
        Vector2 offset = new Vector2(
            Mathf.Sin(progress * Mathf.PI * 8f) * _damageShakeAmount.x,
            Mathf.Sin(progress * Mathf.PI * 11f + 0.7f) * _damageShakeAmount.y) * envelope;
        MaxShakeOffsetObserved = Mathf.Max(MaxShakeOffsetObserved, offset.magnitude);
        _hpBarRoot.anchoredPosition = _initialAnchoredPosition + offset;
    }

    private void FinishDamageShake()
    {
        if (_hpBarRoot != null && _shakeBaseCached)
            _hpBarRoot.anchoredPosition = _initialAnchoredPosition;
        _shakeElapsed = 0f;
        _isShaking = false;
    }
}
