using UnityEngine;

/// <summary>
/// 鎖の LineRenderer。常に手元—鉄球の2点のみ。最大長で終点をクランプする。
/// 連続チェーンテクスチャを Tile 表示する。ChainLinkVisualController は将来用に残す。
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class ChainLineController : MonoBehaviour
{
    [Header("Chain Points")]
    [SerializeField] private Transform startPoint;
    [SerializeField] private Transform endPoint;

    [Header("Morning Star（任意）")]
    [SerializeField] private MorningStarLauncher launcher;
    [SerializeField, Tooltip("launcher 未設定時に使う最大紐長")]
    private float maxRopeLength = 4.5f;

    [Header("Chain Texture")]
    [SerializeField, Tooltip("鎖1.5.png の横ピクセル数")]
    private int chainTextureWidthPx = 233;
    [SerializeField, Tooltip("鎖1.5.png の縦ピクセル数")]
    private int chainTextureHeightPx = 10;
    [SerializeField, Tooltip("鎖テクスチャの Pixels Per Unit")]
    private float chainTexturePixelsPerUnit = 32f;
    [SerializeField, Tooltip("LineRenderer の太さ。未設定時はテクスチャ高さから自動")]
    private float chainLineWidth;

    [Header("Visual")]
    [SerializeField, Tooltip("LineRenderer を使う。OFF ならリンク鎖のみ")]
    private bool drawLineRenderer = true;
    [SerializeField, Range(0f, 1f)] private float lineAlpha = 0.25f;
    [SerializeField, Tooltip("ChainLinkVisual が未設定/非表示のとき LineRenderer を自動表示")]
    private bool autoFallbackWhenLinksHidden = true;
    [SerializeField, Tooltip("ON=リンク表示中は Line を隠す（案B）。OFF=常に薄く表示（案A）")]
    private bool hideLineWhenLinksVisible = true;
    [SerializeField] private ChainLinkVisualController chainLinkVisual;
    [SerializeField] private bool useSag;
    [SerializeField] private int sagPointCount = 8;
    [SerializeField] private float sagAmount = 0.25f;

    private LineRenderer _line;
    private Color _defaultStartColor;
    private Color _defaultEndColor;
    private float _defaultStartWidth;
    private float _defaultEndWidth;
    private bool _defaultsCached;
    private bool _hookedVisualActive;
    private Color _configuredNormalColor = Color.white;
    private Color _configuredHookedColor = new Color(1f, 0.9f, 0.5f, 1f);
    private float _configuredHookedWidthMultiplier = 1.25f;
    private bool _visualConfigured;

    private void Awake()
    {
        _line = GetComponent<LineRenderer>();
        _line.positionCount = 2;
        ConfigureChainLineVisual();

        if (launcher == null)
            launcher = FindAnyObjectByType<MorningStarLauncher>();

        if (chainLinkVisual == null)
            chainLinkVisual = GetComponent<ChainLinkVisualController>();
    }

    private void ConfigureChainLineVisual()
    {
        if (_line == null)
            return;

        _line.textureMode = LineTextureMode.Tile;
        _line.alignment = LineAlignment.View;
        _line.numCapVertices = 0;
        _line.numCornerVertices = 0;
        _line.useWorldSpace = true;

        float width = GetChainLineWidth();
        _line.startWidth = width;
        _line.endWidth = width;
    }

    private float GetChainTextureWorldWidth()
    {
        if (chainTexturePixelsPerUnit <= 0f)
            return 1f;
        return chainTextureWidthPx / chainTexturePixelsPerUnit;
    }

    private float GetChainLineWidth()
    {
        if (chainLineWidth > 0.001f)
            return chainLineWidth;
        if (chainTexturePixelsPerUnit <= 0f)
            return 0.1f;
        return chainTextureHeightPx / chainTexturePixelsPerUnit;
    }

    public void ConfigureHookVisual(Color normalColor, Color hookedColor, float hookedWidthMultiplier)
    {
        _configuredNormalColor = normalColor;
        _configuredHookedColor = hookedColor;
        _configuredHookedWidthMultiplier = Mathf.Max(1f, hookedWidthMultiplier);
        _visualConfigured = true;
    }

    public void SetHookedVisual(bool hooked)
    {
        _hookedVisualActive = hooked;
        ApplyHookedLineVisual();
    }

    private void LateUpdate()
    {
        if (_line == null)
            return;

        bool ropeVisible = launcher == null || launcher.IsRopeLineVisible;
        bool linksShowing = chainLinkVisual != null && chainLinkVisual.IsDisplaying;
        bool visualReady = chainLinkVisual != null && chainLinkVisual.IsVisualReady;
        bool showLine = ShouldShowLineRenderer(ropeVisible, linksShowing, visualReady);

        _line.enabled = showLine;
        if (!showLine)
            return;

        if (launcher != null)
            maxRopeLength = launcher.MaxRopeLength;

        ApplyHookedLineVisual();

        if (!useSag)
            DrawStraightChain();
        else
            DrawSagChain();

        UpdateChainTextureTiling();
    }

    private void UpdateChainTextureTiling()
    {
        if (_line == null)
            return;

        float worldWidth = GetChainTextureWorldWidth();
        if (worldWidth <= 0.001f)
            return;

        // 1タイル = 鎖1.5.png 1枚分の横幅。距離に応じて自然な間隔で繰り返す。
        _line.textureScale = new Vector2(1f / worldWidth, 1f);

        float width = GetChainLineWidth();
        _line.startWidth = width;
        _line.endWidth = width;
    }

    private void ApplyHookedLineVisual()
    {
        if (!_defaultsCached)
        {
            _defaultStartColor = _line.startColor;
            _defaultEndColor = _line.endColor;
            _defaultStartWidth = GetChainLineWidth();
            _defaultEndWidth = GetChainLineWidth();
            _defaultsCached = true;
        }

        bool hooked = _hookedVisualActive;
        if (launcher != null && launcher.IsHookedState)
            hooked = true;

        Color normalStart = _visualConfigured ? _configuredNormalColor : _defaultStartColor;
        Color normalEnd = _visualConfigured ? _configuredNormalColor : _defaultEndColor;
        Color hookedStart = _visualConfigured ? _configuredHookedColor : normalStart;
        Color hookedEnd = _visualConfigured ? _configuredHookedColor : normalEnd;
        float mult = _visualConfigured ? _configuredHookedWidthMultiplier : 1.25f;

        Color start = hooked ? hookedStart : normalStart;
        Color end = hooked ? hookedEnd : normalEnd;
        start.a *= lineAlpha;
        end.a *= lineAlpha;
        _line.startColor = start;
        _line.endColor = end;
        float startW = hooked ? _defaultStartWidth * mult : _defaultStartWidth;
        float endW = hooked ? _defaultEndWidth * mult : _defaultEndWidth;
        _line.startWidth = startW;
        _line.endWidth = endW;
    }

    private void DrawStraightChain()
    {
        _line.positionCount = 2;
        Vector3 start = startPoint.position;
        Vector3 end = ClampEndToMaxLength(start, endPoint.position);
        _line.SetPosition(0, start);
        _line.SetPosition(1, end);
    }

    private void DrawSagChain()
    {
        Vector3 start = startPoint.position;
        Vector3 end = ClampEndToMaxLength(start, endPoint.position);
        int count = Mathf.Max(2, sagPointCount);
        _line.positionCount = count;

        for (int i = 0; i < count; i++)
        {
            float t = i / (float)(count - 1);
            Vector3 pos = Vector3.Lerp(start, end, t);
            float sag = Mathf.Sin(t * Mathf.PI) * sagAmount;
            pos.y -= sag;
            _line.SetPosition(i, pos);
        }
    }

    private Vector3 ClampEndToMaxLength(Vector3 start, Vector3 end)
    {
        float maxLen = maxRopeLength;
        if (maxLen <= 0f)
            return end;

        Vector3 off = end - start;
        float sqr = off.sqrMagnitude;
        if (sqr <= maxLen * maxLen || sqr < 1e-10f)
            return end;

        return start + off.normalized * maxLen;
    }

    public void SetPoints(Transform newStart, Transform newEnd)
    {
        startPoint = newStart;
        endPoint = newEnd;
    }

    public void SetLauncher(MorningStarLauncher newLauncher)
    {
        launcher = newLauncher;
    }

    private bool ShouldShowLineRenderer(bool ropeVisible, bool linksShowing, bool visualReady)
    {
        if (!drawLineRenderer || !ropeVisible)
            return false;

        if (startPoint == null || endPoint == null)
            return false;

        if (chainLinkVisual == null)
            return true;

        if (!visualReady)
            return autoFallbackWhenLinksHidden;

        if (linksShowing && hideLineWhenLinksVisible)
            return false;

        if (!linksShowing && autoFallbackWhenLinksHidden)
            return true;

        return !hideLineWhenLinksVisible;
    }
}
