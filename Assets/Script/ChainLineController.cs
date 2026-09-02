using UnityEngine;

/// <summary>
/// 鎖の LineRenderer。手元—鉄球間の余長をWorld Down方向の曲線として描画する。
/// 物理制約とは分離し、既存の連続チェーンテクスチャを Tile 表示する。
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
    [SerializeField, Tooltip("最大長に余裕があるとき鎖を重力方向へたわませる")]
    private bool useSag = true;
    [SerializeField, Min(2)] private int sagSegments = 16;
    [SerializeField, Min(0f)] private float sagStrength = 0.6f;
    [SerializeField, Min(0f)] private float maxSag = 1.5f;

    [Header("Sorting")]
    [SerializeField] private string sortingLayerName = "Default";
    [SerializeField] private int sortingOrder = 8;
    [SerializeField, Min(1)] private int morningStarOrderOffset = 1;

    [Header("Visual Ground Collision")]
    [SerializeField, Tooltip("Sag/直線の描画Pointだけを床・壁から押し出す。物理挙動には影響しない")]
    private bool avoidGroundPenetration = true;
    [SerializeField, Tooltip("Player接地判定と同じ Default + Walls")]
    private LayerMask groundLayerMask = (1 << 0) | (1 << 6);
    [SerializeField, Min(0.001f), Tooltip("鎖幅0.3125の約45%。中心線を鎖半径分だけCollider表面から離す")]
    private float chainCollisionRadius = 0.14f;
    [SerializeField, Min(0f)] private float collisionSkin = 0.01f;

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
    private readonly RaycastHit2D[] _collisionHits = new RaycastHit2D[8];
    private Vector3[] _visualPoints = new Vector3[16];
    private ContactFilter2D _groundFilter;

    public int SagSegments => Mathf.Max(2, sagSegments);
    public float SagStrength => sagStrength;
    public float MaxSag => maxSag;
    public float LastSagAmount { get; private set; }
    public float LastDirectDistance { get; private set; }
    public int LastCollisionAdjustedPointCount { get; private set; }
    public float ChainCollisionRadius => chainCollisionRadius;
    public float CollisionSkin => collisionSkin;
    public LayerMask GroundLayerMask => groundLayerMask;
    public string SortingLayerName => sortingLayerName;
    public int SortingOrder => sortingOrder;

    private void Awake()
    {
        _line = GetComponent<LineRenderer>();
        _line.positionCount = 2;
        RefreshGroundFilter();
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
        _line.sortingLayerName = sortingLayerName;
        _line.sortingOrder = sortingOrder;

        float width = GetChainLineWidth();
        _line.startWidth = width;
        _line.endWidth = width;

        EnsureMorningStarRendersInFront();
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
        Vector3 start = launcher != null ? (Vector3)launcher.VisualRopeAnchorWorld : startPoint.position;
        Vector3 end = endPoint.position;
        if (TryDrawWrappedChain(start, end))
            return;

        end = ClampEndToMaxLength(start, end);
        LastDirectDistance = Vector3.Distance(start, end);
        LastSagAmount = 0f;
        BuildAndDrawVisualPath(start, end, 0f);
    }

    private void DrawSagChain()
    {
        Vector3 start = launcher != null ? (Vector3)launcher.VisualRopeAnchorWorld : startPoint.position;
        Vector3 end = endPoint.position;
        if (TryDrawWrappedChain(start, end))
            return;

        end = ClampEndToMaxLength(start, end);
        int count = Mathf.Max(2, sagSegments);
        float directDistance = Vector3.Distance(start, end);
        float resolvedSag = CalculateSagForDistance(directDistance);

        LastDirectDistance = directDistance;
        LastSagAmount = resolvedSag;
        BuildAndDrawVisualPath(start, end, resolvedSag);
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

    private bool TryDrawWrappedChain(Vector3 start, Vector3 end)
    {
        int contactCount = launcher != null ? launcher.RopeContactPointCount : 0;
        if (contactCount <= 0)
            return false;

        int pointCount = contactCount + 2;
        EnsureVisualPointCapacity(pointCount);
        _visualPoints[0] = start;
        for (int i = 0; i < contactCount; i++)
            _visualPoints[i + 1] = launcher.GetRopeContactPoint(i);
        _visualPoints[pointCount - 1] = end;

        float pathLength = 0f;
        for (int i = 1; i < pointCount; i++)
            pathLength += Vector3.Distance(_visualPoints[i - 1], _visualPoints[i]);

        LastDirectDistance = pathLength;
        LastSagAmount = 0f;
        LastCollisionAdjustedPointCount = contactCount;
        _line.positionCount = pointCount;
        for (int i = 0; i < pointCount; i++)
            _line.SetPosition(i, _visualPoints[i]);
        return true;
    }

    private void BuildAndDrawVisualPath(Vector3 start, Vector3 end, float sag)
    {
        int count = avoidGroundPenetration ? Mathf.Max(3, sagSegments) : (sag > 0f ? Mathf.Max(2, sagSegments) : 2);
        EnsureVisualPointCapacity(count);

        for (int i = 0; i < count; i++)
        {
            float t = i / (float)(count - 1);
            Vector3 point = Vector3.Lerp(start, end, t);
            point.y -= sag * (4f * t * (1f - t));
            _visualPoints[i] = point;
        }

        LastCollisionAdjustedPointCount = avoidGroundPenetration
            ? ChainVisualCollision2D.Resolve(
                _visualPoints,
                count,
                chainCollisionRadius,
                collisionSkin,
                _groundFilter,
                _collisionHits)
            : 0;

        _line.positionCount = count;
        for (int i = 0; i < count; i++)
            _line.SetPosition(i, _visualPoints[i]);
    }

    private void EnsureVisualPointCapacity(int count)
    {
        if (_visualPoints != null && _visualPoints.Length >= count)
            return;

        _visualPoints = new Vector3[Mathf.Max(2, count)];
    }

    private void RefreshGroundFilter()
    {
        _groundFilter = new ContactFilter2D();
        _groundFilter.SetLayerMask(groundLayerMask);
        _groundFilter.useTriggers = false;
    }

    private void EnsureMorningStarRendersInFront()
    {
        if (endPoint == null)
            return;

        SpriteRenderer[] renderers = endPoint.GetComponentsInChildren<SpriteRenderer>(true);
        int frontOrder = sortingOrder + Mathf.Max(1, morningStarOrderOffset);
        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (renderer == null)
                continue;

            renderer.sortingLayerName = sortingLayerName;
            if (renderer.sortingOrder < frontOrder)
                renderer.sortingOrder = frontOrder;
        }
    }

    public float CalculateSagForDistance(float directDistance)
    {
        float slack = Mathf.Max(0f, maxRopeLength - Mathf.Max(0f, directDistance));
        return Mathf.Min(maxSag, slack * sagStrength);
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

    private void OnValidate()
    {
        sagSegments = Mathf.Max(2, sagSegments);
        sagStrength = Mathf.Max(0f, sagStrength);
        maxSag = Mathf.Max(0f, maxSag);
        morningStarOrderOffset = Mathf.Max(1, morningStarOrderOffset);
        chainCollisionRadius = Mathf.Max(0.001f, chainCollisionRadius);
        collisionSkin = Mathf.Max(0f, collisionSkin);
        RefreshGroundFilter();

        if (_line == null)
            _line = GetComponent<LineRenderer>();
        ConfigureChainLineVisual();
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
