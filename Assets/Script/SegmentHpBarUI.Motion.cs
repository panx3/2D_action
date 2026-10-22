using UnityEngine;

public partial class SegmentHpBarUI
{
    [Header("Subtle Movement Feedback")]
    [SerializeField, Tooltip("移動速度を読み取り、HPゲージだけを少し遅れて動かす")]
    private bool _movementFeedback = true;
    [SerializeField, Tooltip("移動による最大オフセット（Canvas上のPixel）。被弾の揺れは別途加算")]
    private Vector2 _movementAmount = new Vector2(8f, 5f);
    [SerializeField, Range(0.05f, 0.5f)] private float _movementSmoothTime = 0.22f;
    [SerializeField, Range(0f, 2f), Tooltip("地上を走っている時だけ加わる小さな上下動")]
    private float _runningBob = 1.1f;

    [Header("Hanging Chain")]
    [SerializeField] private bool _showHangingChain = true;
    [SerializeField, Range(8f, 24f), Tooltip("最も長い鎖の高さ（元HPバー画像のPixel換算）。3本の比率は生成画像を維持")]
    private float _hangingChainHeight = 20.5f;
    [SerializeField, Range(0f, 8f), Tooltip("HPバーの絵に重ねる高さ（元画像のPixel換算）")]
    private float _chainAttachmentLift = 4f;
    [SerializeField, Range(0f, 15f)] private float _chainSwayAngle = 12f;

    [Header("Draped Chain")]
    [SerializeField] private bool _showDrapedChain = true;
    [SerializeField, Range(4f, 24f), Tooltip("両端を固定した鎖のたわみ（元画像のPixel換算）")]
    private float _drapedChainSag = 12f;

    private Player _motionPlayer;
    private Rigidbody2D _motionBody;
    private readonly HpBarChainGraphic[] _hangingChains = new HpBarChainGraphic[3];
    private readonly Sprite[] _hangingChainSprites = new Sprite[3];
    private HpBarChainGraphic _drapedChain;
    private static readonly string[] ChainSpriteNames = { "HpChain_Short", "HpChain_Long", "HpChain_Medium" };
    private readonly float[] _chainAngles = new float[3];
    private readonly float[] _chainAngularVelocities = new float[3];
    private static readonly Vector2[] ChainAttachments = {
        new Vector2(0.165f, 0.12f), // Heart tip.
        new Vector2(0.315f, 0.17f), // Left metal loop.
        new Vector2(0.755f, 0.18f)  // Right metal loop.
    };
    private static readonly float[] ChainLengthRatios = { 0.6f, 1f, 0.8f };
    private static readonly Vector2 DrapedStartAnchor = new Vector2(0.515f, 0.17f);
    private static readonly Vector2 DrapedEndAnchor = new Vector2(0.72f, 0.18f);
    private Vector2 _movementOffset;
    private Vector2 _movementDamping;
    private Vector2 _previousMotionVelocity;
    private Vector2 _filteredMotionVelocity;
    private Vector2 _previousMotionPosition;
    private float _runningPhase;
    private float _groundedWeight;
    private int _motionRecoveryVersion;
    private bool _motionSampled;

    private void InitializeMotionFeedback()
    {
        ResolveMotionPlayer();
        if (_hpBarRoot == null || _frame == null)
            return;

        // A nested Canvas overrides rounding only for the moving HP artwork. The parent's
        // scale, render mode, CRT conversion and all other HUDs retain their settings.
        Canvas motionCanvas = _hpBarRoot.GetComponent<Canvas>();
        if (motionCanvas == null) motionCanvas = _hpBarRoot.gameObject.AddComponent<Canvas>();
        motionCanvas.overridePixelPerfect = true;
        motionCanvas.pixelPerfect = false;

        Sprite[] atlas = Resources.LoadAll<Sprite>("UI/HpBarHangingChains");
        for (int i = 0; i < _hangingChains.Length; i++)
        {
            foreach (Sprite sprite in atlas)
                if (sprite.name == ChainSpriteNames[i]) _hangingChainSprites[i] = sprite;
            if (_hangingChainSprites[i] == null)
            {
                Debug.LogError("HP chain artwork is missing: " + ChainSpriteNames[i], this);
                continue;
            }
            if (_hangingChains[i] != null) continue;
            var chain = new GameObject("Hanging HP Chain " + (i + 1), typeof(RectTransform),
                typeof(CanvasRenderer), typeof(HpBarChainGraphic));
            chain.layer = _hpBarRoot.gameObject.layer;
            chain.transform.SetParent(_frame.transform, false);
            _hangingChains[i] = chain.GetComponent<HpBarChainGraphic>();
            _hangingChains[i].raycastTarget = false;
            _hangingChains[i].SetArtwork(_hangingChainSprites[i]);
            RectTransform rect = _hangingChains[i].rectTransform;
            rect.anchorMin = rect.anchorMax = ChainAttachments[i];
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = Vector2.zero;
        }
        if (_drapedChain == null && _hangingChainSprites[0] != null)
        {
            var chain = new GameObject("Draped HP Chain", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(HpBarChainGraphic));
            chain.layer = _hpBarRoot.gameObject.layer;
            chain.transform.SetParent(_frame.transform, false);
            chain.transform.SetAsFirstSibling();
            _drapedChain = chain.GetComponent<HpBarChainGraphic>();
            _drapedChain.SetArtwork(_hangingChainSprites[0]);
            _drapedChain.raycastTarget = false;
            RectTransform rect = _drapedChain.rectTransform;
            rect.anchorMin = rect.anchorMax = DrapedStartAnchor;
            rect.pivot = new Vector2(0f, 1f);
        }
        UpdateHangingChain();
    }

    private void ResolveMotionPlayer()
    {
        if (_playerHealth == null || _motionPlayer != null)
            return;
        _motionPlayer = _playerHealth.GetComponent<Player>();
        _motionBody = _playerHealth.GetComponent<Rigidbody2D>();
    }

    private void LateUpdate()
    {
        // Scaled time freezes both the gauge and its chain during Pause / hit-stop.
        float dt = Mathf.Min(Time.deltaTime, 0.1f);
        if (dt <= 0f) return;
        ResolveMotionPlayer();
        if (_motionBody == null || _playerHealth == null || !_playerHealth.isActiveAndEnabled
            || _playerHealth.IsDead || _playerHealth.IsRespawning
            || (_motionPlayer != null && (!_motionPlayer.InputEnabled || _motionPlayer.IsGoalInputBlocked)))
        {
            ResetMotionFeedback();
            return;
        }

        Vector2 position = _motionBody.position;
        if (!_motionSampled || _motionRecoveryVersion != _playerHealth.RecoveryVersion
            || (position - _previousMotionPosition).sqrMagnitude > 9f)
        {
            ResetMotionFeedback();
            _motionSampled = true;
            _motionRecoveryVersion = _playerHealth.RecoveryVersion;
            _previousMotionVelocity = _filteredMotionVelocity = _motionBody.linearVelocity;
            _groundedWeight = _motionPlayer != null && _motionPlayer.IsGrounded ? 1f : 0f;
        }
        _previousMotionPosition = position;
        TickMotionFeedback(dt, _motionBody.linearVelocity,
            _motionPlayer != null && _motionPlayer.IsGrounded);
    }

    private void TickMotionFeedback(float dt, Vector2 velocity, bool grounded)
    {
        if (dt <= 0f) return;
        // Rigidbody velocity changes on physics ticks. Filter it before feeding render-time
        // motion and the pendulums so fixed-step edges never become visible UI impulses.
        _filteredMotionVelocity = Vector2.Lerp(_filteredMotionVelocity, velocity, 1f - Mathf.Exp(-dt / 0.075f));
        velocity = _filteredMotionVelocity;
        float speed = Mathf.Abs(velocity.x);
        _groundedWeight = Mathf.MoveTowards(_groundedWeight, grounded ? 1f : 0f, dt * 7f);
        if (grounded && speed > 0.25f)
            _runningPhase = Mathf.Repeat(_runningPhase + speed * dt * 2.2f, Mathf.PI * 2f);
        float bob = Mathf.Sin(_runningPhase) * _runningBob * Mathf.Clamp01(speed / 4f) * _groundedWeight;
        Vector2 amount = new Vector2(Mathf.Max(0f, _movementAmount.x), Mathf.Max(0f, _movementAmount.y));
        Vector2 target = _movementFeedback ? new Vector2(
            Mathf.Clamp(-velocity.x * 0.85f, -amount.x, amount.x),
            Mathf.Clamp(-velocity.y * 0.30f + bob, -amount.y, amount.y)) : Vector2.zero;
        _movementOffset = Vector2.SmoothDamp(_movementOffset, target, ref _movementDamping,
            Mathf.Max(0.05f, _movementSmoothTime), Mathf.Infinity, dt);

        // A damped cosmetic pendulum. No Rigidbody, forces, or gameplay subscriptions.
        float maxAngle = Mathf.Clamp(_chainSwayAngle, 0f, 15f);
        float acceleration = Mathf.Clamp((velocity.x - _previousMotionVelocity.x) / dt, -80f, 80f);
        float targetAngle = Mathf.Clamp(velocity.x * 0.5f, -maxAngle * 0.45f, maxAngle * 0.45f);
        // Longer chains lag more. Substeps keep the cosmetic springs stable on slow frames.
        int steps = Mathf.Max(1, Mathf.CeilToInt(dt * 120f));
        float step = dt / steps;
        for (int i = 0; i < _hangingChains.Length; i++)
        {
            float spring = 38f / ChainLengthRatios[i];
            float damping = 1.5f * Mathf.Sqrt(spring);
            for (int j = 0; j < steps; j++)
            {
                _chainAngularVelocities[i] += ((targetAngle - _chainAngles[i]) * spring
                    - _chainAngularVelocities[i] * damping + acceleration * 0.5f) * step;
                _chainAngles[i] = Mathf.Clamp(_chainAngles[i] + _chainAngularVelocities[i] * step,
                    -maxAngle, maxAngle);
            }
        }
        _previousMotionVelocity = velocity;
        ApplyRootPose();
        UpdateHangingChain();
    }

    private void ApplyRootPose()
    {
        if (_hpBarRoot != null && _shakeBaseCached)
            _hpBarRoot.anchoredPosition = _initialAnchoredPosition + _movementOffset + _damageShakeOffset;
    }

    private void UpdateHangingChain()
    {
        if (_frame == null || _hangingChainSprites[1] == null) return;
        float framePixel = _frame.rectTransform.rect.width / 260f;
        float scale = framePixel * Mathf.Clamp(_hangingChainHeight, 8f, 24f)
            / _hangingChainSprites[1].rect.height;
        Vector2 attachmentOffset = Vector2.up * (Mathf.Clamp(_chainAttachmentLift, 0f, 8f) * framePixel);
        for (int i = 0; i < _hangingChains.Length; i++)
        {
            if (_hangingChains[i] == null) continue;
            _hangingChains[i].gameObject.SetActive(_showHangingChain);
            _hangingChains[i].rectTransform.anchoredPosition = attachmentOffset;
            // The corrected right strand has three full-size links. A small overall
            // scale difference keeps the three silhouettes at distinct lengths.
            float strandScale = scale * (i == 2 ? 0.9f : 1f);
            _hangingChains[i].SetPose(_hangingChainSprites[i].rect.size * strandScale, _chainAngles[i]);
        }
        if (_drapedChain != null)
        {
            _drapedChain.gameObject.SetActive(_showHangingChain && _showDrapedChain);
            _drapedChain.rectTransform.anchoredPosition = attachmentOffset;
            Vector2 end = Vector2.Scale(DrapedEndAnchor - DrapedStartAnchor, _frame.rectTransform.rect.size);
            float sag = framePixel * Mathf.Clamp(_drapedChainSag, 4f, 24f);
            float width = _hangingChainSprites[0].rect.width * scale;
            _drapedChain.SetDrapedPose(end, sag, width, _chainAngles[1]);
        }
    }

    private void ResetMotionFeedback()
    {
        _movementOffset = _movementDamping = Vector2.zero;
        _previousMotionVelocity = _filteredMotionVelocity = Vector2.zero;
        _runningPhase = _groundedWeight = 0f;
        for (int i = 0; i < _chainAngles.Length; i++)
            _chainAngles[i] = _chainAngularVelocities[i] = 0f;
        _motionSampled = false;
        ApplyRootPose();
        UpdateHangingChain();
    }
}
