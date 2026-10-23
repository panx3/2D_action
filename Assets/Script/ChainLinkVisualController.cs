using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 見た目専用の鎖リンク表示。物理は ChainConstraint2D / MorningStarLauncher 側を維持する。
/// </summary>
public class ChainLinkVisualController : MonoBehaviour
{
    [Header("Chain Points")]
    [SerializeField] private Transform startPoint;
    [SerializeField] private Transform endPoint;
    [SerializeField] private MorningStarLauncher launcher;
    [SerializeField] private Sprite chainLinkSprite;

    [Header("Link Pool")]
    [SerializeField] private int maxSegments = 80;
    [SerializeField] private float segmentSpacing = 0.16f;
    [SerializeField] private float segmentScale = 1.8f;
    [SerializeField] private int sortingOrder = 8;
    [SerializeField] private string sortingLayerName = "Default";
    [SerializeField] private bool autoScaleFromSprite = true;

    [Header("Sag")]
    [SerializeField] private bool useSag = true;
    [SerializeField, Min(2)] private int sagSegments = 16;
    [SerializeField] private float maxSagAmount = 1.5f;
    [SerializeField] private float sagSlackMultiplier = 0.6f;
    [SerializeField] private float fallbackMaxRopeLength = 4.5f;

    [Header("Visual Ground Collision")]
    [SerializeField] private bool avoidGroundPenetration = true;
    [SerializeField, Tooltip("Player接地判定と同じ Default + Walls")]
    private LayerMask groundLayerMask = (1 << 0) | (1 << 6);
    [SerializeField, Min(0.001f)] private float chainCollisionRadius = 0.14f;
    [SerializeField, Min(0f)] private float collisionSkin = 0.01f;

    [Header("Color")]
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color hookedColor = new Color(1f, 0.95f, 0.65f, 1f);

    [Header("Future Wrap Points")]
    [SerializeField] private List<Transform> wrapPoints = new List<Transform>();

    [Header("Debug")]
    [SerializeField] private bool debugLog;

    private readonly List<SpriteRenderer> _links = new List<SpriteRenderer>();
    private readonly List<Vector3> _pathPoints = new List<Vector3>(20);
    private bool _loggedInit;
    private bool _loggedLauncherResolve;
    private int _lastActiveLinkCount;
    private readonly RaycastHit2D[] _collisionHits = new RaycastHit2D[8];
    private ContactFilter2D _groundFilter;

    public bool IsDisplaying { get; private set; }
    public bool IsVisualReady =>
        startPoint != null && endPoint != null && chainLinkSprite != null;
    public int LastCollisionAdjustedPointCount { get; private set; }

    private void Awake()
    {
        RefreshGroundFilter();
        ResolveLauncher();
        EnsureSegmentScale();
        BuildPool();
        HideAllLinks();
    }

    private void Start()
    {
        ResolveLauncher();
        EnsureSegmentScale();
        RefreshLinkSprites();
        LogInitStateOnce();
    }

    private void ResolveLauncher()
    {
        if (launcher != null)
            return;

        launcher = GetComponentInParent<MorningStarLauncher>();
        if (launcher == null)
            launcher = FindAnyObjectByType<MorningStarLauncher>(FindObjectsInactive.Exclude);

        if (_loggedLauncherResolve)
            return;

        _loggedLauncherResolve = true;
        if (launcher != null)
            Debug.LogWarning("[ChainLinkVisualController] MorningStarLauncher was auto-found. Assign it in Inspector to avoid wrong references.", this);
        else
            Debug.LogWarning("[ChainLinkVisualController] MorningStarLauncher is not assigned and could not be found.", this);
    }

    private void LateUpdate()
    {
        if (!IsVisualReady)
        {
            IsDisplaying = false;
            HideAllLinks();
            LogMissingSetupOnce();
            return;
        }

        if (launcher != null && !launcher.IsRopeLineVisible)
        {
            IsDisplaying = false;
            HideAllLinks();
            return;
        }

        UpdatePathPoints();
        DrawLinksAlongPath();
    }

    private void BuildPool()
    {
        int targetCount = Mathf.Max(0, maxSegments);

        for (int i = _links.Count; i < targetCount; i++)
        {
            GameObject linkObject = new GameObject($"ChainLink_{i:00}");
            linkObject.transform.SetParent(transform, false);

            SpriteRenderer renderer = linkObject.AddComponent<SpriteRenderer>();
            renderer.sprite = chainLinkSprite;
            renderer.sortingLayerName = sortingLayerName;
            renderer.sortingOrder = sortingOrder;
            renderer.color = normalColor;
            linkObject.transform.localScale = Vector3.one * segmentScale;

            _links.Add(renderer);
        }

        for (int i = targetCount; i < _links.Count; i++)
        {
            if (_links[i] != null)
                _links[i].gameObject.SetActive(false);
        }
    }

    private void RefreshLinkSprites()
    {
        for (int i = 0; i < _links.Count; i++)
        {
            if (_links[i] == null)
                continue;

            _links[i].sprite = chainLinkSprite;
            _links[i].sortingLayerName = sortingLayerName;
            _links[i].sortingOrder = sortingOrder;
        }
    }

    private void EnsureSegmentScale()
    {
        if (!autoScaleFromSprite || chainLinkSprite == null)
            return;

        float spriteWorldSize = Mathf.Max(chainLinkSprite.bounds.size.x, chainLinkSprite.bounds.size.y);
        if (spriteWorldSize <= 0.0001f)
            return;

        float targetSize = segmentSpacing * 0.85f;
        float currentSize = spriteWorldSize * segmentScale;
        if (currentSize < targetSize * 0.5f)
            segmentScale = targetSize / spriteWorldSize;
    }

    private void UpdatePathPoints()
    {
        _pathPoints.Clear();

        Vector3 start = launcher != null ? (Vector3)launcher.VisualRopeAnchorWorld : startPoint.position;
        Vector3 end = endPoint.position;
        int physicsContactCount = launcher != null ? launcher.RopeContactPointCount : 0;
        if (physicsContactCount > 0)
        {
            _pathPoints.Add(start);
            for (int i = 0; i < physicsContactCount; i++)
                _pathPoints.Add(launcher.GetRopeContactPoint(i));
            _pathPoints.Add(end);
            LastCollisionAdjustedPointCount = physicsContactCount;
            return;
        }

        if (launcher != null || wrapPoints.Count == 0)
        {
            int count = avoidGroundPenetration ? Mathf.Max(3, sagSegments) : Mathf.Max(2, sagSegments);
            float sag = useSag ? CalculateSagAmount(start, end) : 0f;
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)(count - 1);
                Vector3 point = Vector3.Lerp(start, end, t);
                point.y -= sag * (4f * t * (1f - t));
                _pathPoints.Add(point);
            }

            ResolveGroundCollision();
            return;
        }

        _pathPoints.Add(start);
        for (int i = 0; i < wrapPoints.Count; i++)
        {
            if (wrapPoints[i] != null)
                _pathPoints.Add(wrapPoints[i].position);
        }
        _pathPoints.Add(end);
        ResolveGroundCollision();
    }

    private void ResolveGroundCollision()
    {
        LastCollisionAdjustedPointCount = avoidGroundPenetration
            ? ChainVisualCollision2D.Resolve(
                _pathPoints,
                chainCollisionRadius,
                collisionSkin,
                _groundFilter,
                _collisionHits)
            : 0;
    }

    private void RefreshGroundFilter()
    {
        _groundFilter = new ContactFilter2D();
        _groundFilter.SetLayerMask(groundLayerMask);
        _groundFilter.useTriggers = false;
    }

    private float CalculateSagAmount(Vector3 start, Vector3 end)
    {
        if (!useSag)
            return 0f;

        float maxLen = launcher != null ? launcher.MaxRopeLength : fallbackMaxRopeLength;
        float distance = Vector2.Distance(start, end);
        float slack = Mathf.Max(0f, maxLen - distance);
        float sag = slack * sagSlackMultiplier;
        return Mathf.Min(maxSagAmount, Mathf.Max(0f, sag));
    }

    private void DrawLinksAlongPath()
    {
        float length = GetPathLength();
        if (length <= 0.001f || segmentSpacing <= 0.001f)
        {
            IsDisplaying = false;
            HideAllLinks();
            return;
        }

        int needed = Mathf.Clamp(Mathf.CeilToInt(length / segmentSpacing), 1, _links.Count);
        Color linkColor = launcher != null && launcher.IsHookedState ? hookedColor : normalColor;
        IsDisplaying = needed > 0;

        for (int i = 0; i < _links.Count; i++)
        {
            SpriteRenderer link = _links[i];
            if (link == null)
                continue;

            bool active = i < needed;
            link.gameObject.SetActive(active);
            if (!active)
                continue;

            float distance = Mathf.Min(length, (i + 0.5f) * segmentSpacing);
            Vector3 pos = SamplePath(distance);
            Vector3 next = SamplePath(Mathf.Min(length, distance + segmentSpacing * 0.25f));
            Vector3 dir = next - pos;
            if (dir.sqrMagnitude < 0.0001f)
                dir = Vector3.right;

            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            if ((i & 1) == 1)
                angle += 90f;

            Transform linkTransform = link.transform;
            linkTransform.position = pos;
            linkTransform.rotation = Quaternion.Euler(0f, 0f, angle);
            linkTransform.localScale = Vector3.one * segmentScale;
            link.sprite = chainLinkSprite;
            link.sortingLayerName = sortingLayerName;
            link.sortingOrder = sortingOrder;
            link.color = linkColor;
        }

        if (debugLog && needed != _lastActiveLinkCount)
        {
            _lastActiveLinkCount = needed;
            Debug.Log($"[ChainLinkVisual] activeLinks={needed} length={length:F2} scale={segmentScale:F2}", this);
        }
    }

    private float GetPathLength()
    {
        float length = 0f;
        for (int i = 1; i < _pathPoints.Count; i++)
            length += Vector3.Distance(_pathPoints[i - 1], _pathPoints[i]);
        return length;
    }

    private Vector3 SamplePath(float distance)
    {
        if (_pathPoints.Count == 0)
            return transform.position;
        if (_pathPoints.Count == 1)
            return _pathPoints[0];

        float remaining = Mathf.Max(0f, distance);
        for (int i = 1; i < _pathPoints.Count; i++)
        {
            Vector3 a = _pathPoints[i - 1];
            Vector3 b = _pathPoints[i];
            float segmentLength = Vector3.Distance(a, b);
            if (segmentLength <= 0.0001f)
                continue;

            if (remaining <= segmentLength)
                return Vector3.Lerp(a, b, remaining / segmentLength);

            remaining -= segmentLength;
        }

        return _pathPoints[_pathPoints.Count - 1];
    }

    private void HideAllLinks()
    {
        for (int i = 0; i < _links.Count; i++)
        {
            if (_links[i] != null)
                _links[i].gameObject.SetActive(false);
        }
    }

    private void LogInitStateOnce()
    {
        if (_loggedInit || !debugLog)
            return;

        _loggedInit = true;
        Debug.Log(
            $"[ChainLinkVisual] init ready={IsVisualReady} sprite={(chainLinkSprite != null ? chainLinkSprite.name : "null")} " +
            $"start={(startPoint != null ? startPoint.name : "null")} end={(endPoint != null ? endPoint.name : "null")} " +
            $"launcher={(launcher != null ? launcher.name : "null")} scale={segmentScale:F2}",
            this);
    }

    private void LogMissingSetupOnce()
    {
        if (!debugLog || _loggedInit)
            return;

        if (chainLinkSprite == null)
            Debug.LogWarning("[ChainLinkVisual] chainLinkSprite is not assigned.", this);
        if (startPoint == null)
            Debug.LogWarning("[ChainLinkVisual] startPoint is not assigned.", this);
        if (endPoint == null)
            Debug.LogWarning("[ChainLinkVisual] endPoint is not assigned.", this);
    }

    private void OnValidate()
    {
        maxSegments = Mathf.Max(0, maxSegments);
        segmentSpacing = Mathf.Max(0.001f, segmentSpacing);
        segmentScale = Mathf.Max(0.001f, segmentScale);
        sagSegments = Mathf.Max(2, sagSegments);
        maxSagAmount = Mathf.Max(0f, maxSagAmount);
        sagSlackMultiplier = Mathf.Max(0f, sagSlackMultiplier);
        chainCollisionRadius = Mathf.Max(0.001f, chainCollisionRadius);
        collisionSkin = Mathf.Max(0f, collisionSkin);
        RefreshGroundFilter();
        EnsureSegmentScale();
    }
}
