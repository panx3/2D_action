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
    [SerializeField] private float segmentScale = 0.12f;
    [SerializeField] private int sortingOrder = 5;

    [Header("Sag")]
    [SerializeField] private bool useSag = true;
    [SerializeField] private float maxSagAmount = 0.45f;
    [SerializeField] private float sagSlackMultiplier = 0.35f;
    [SerializeField] private float fallbackMaxRopeLength = 4.5f;

    [Header("State Sag")]
    [SerializeField] private float tautDistanceRate = 0.9f;
    [SerializeField] private float hookedSagMultiplier = 0f;
    [SerializeField] private float thrownSagMultiplier = 0.15f;
    [SerializeField] private float draggingSagMultiplier = 1f;
    [SerializeField] private float droppingSagMultiplier = 0.7f;
    [SerializeField] private float transitionSagMultiplier = 0.2f;
    [SerializeField] private float spinChargeSagMultiplier = 0f;

    [Header("Color")]
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color hookedColor = new Color(1f, 0.95f, 0.65f, 1f);

    [Header("Future Wrap Points")]
    [SerializeField] private List<Transform> wrapPoints = new List<Transform>();

    private readonly List<SpriteRenderer> _links = new List<SpriteRenderer>();
    private readonly List<Vector3> _pathPoints = new List<Vector3>(8);

    private void Awake()
    {
        if (launcher == null)
            launcher = GetComponentInParent<MorningStarLauncher>();

        BuildPool();
        HideAllLinks();
    }

    private void LateUpdate()
    {
        if (startPoint == null || endPoint == null || chainLinkSprite == null)
        {
            HideAllLinks();
            return;
        }

        if (launcher != null && !launcher.IsRopeLineVisible)
        {
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

    private void UpdatePathPoints()
    {
        _pathPoints.Clear();

        Vector3 start = startPoint.position;
        Vector3 end = endPoint.position;
        _pathPoints.Add(start);

        for (int i = 0; i < wrapPoints.Count; i++)
        {
            if (wrapPoints[i] != null)
                _pathPoints.Add(wrapPoints[i].position);
        }

        if (useSag && wrapPoints.Count == 0)
        {
            Vector3 mid = Vector3.Lerp(start, end, 0.5f);
            mid.y -= CalculateSagAmount(start, end);
            _pathPoints.Add(mid);
        }

        _pathPoints.Add(end);
    }

    private float CalculateSagAmount(Vector3 start, Vector3 end)
    {
        if (!useSag)
            return 0f;

        float maxLen = launcher != null ? launcher.MaxRopeLength : fallbackMaxRopeLength;
        float distance = Vector2.Distance(start, end);
        float slack = Mathf.Max(0f, maxLen - distance);
        float tension01 = Mathf.InverseLerp(maxLen * tautDistanceRate, maxLen, distance);
        float sag = slack * sagSlackMultiplier * (1f - tension01);
        sag *= GetStateSagMultiplier();
        return Mathf.Min(maxSagAmount, Mathf.Max(0f, sag));
    }

    private float GetStateSagMultiplier()
    {
        if (launcher == null)
            return 1f;

        switch (launcher.CurrentState)
        {
            case MorningStarLauncher.MorningStarState.Hooked:
            case MorningStarLauncher.MorningStarState.Swinging:
                return hookedSagMultiplier;

            case MorningStarLauncher.MorningStarState.Thrown:
                return thrownSagMultiplier;

            case MorningStarLauncher.MorningStarState.Dropping:
                return droppingSagMultiplier;

            case MorningStarLauncher.MorningStarState.Dragging:
                return draggingSagMultiplier;

            case MorningStarLauncher.MorningStarState.SpinCharging:
                return spinChargeSagMultiplier;

            case MorningStarLauncher.MorningStarState.RecallBeforeThrow:
            case MorningStarLauncher.MorningStarState.Returning:
                return transitionSagMultiplier;

            default:
                return 1f;
        }
    }

    private void DrawLinksAlongPath()
    {
        float length = GetPathLength();
        if (length <= 0.001f || segmentSpacing <= 0.001f)
        {
            HideAllLinks();
            return;
        }

        int needed = Mathf.Clamp(Mathf.CeilToInt(length / segmentSpacing), 1, _links.Count);
        Color linkColor = launcher != null && launcher.IsHookedState ? hookedColor : normalColor;

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
            link.sortingOrder = sortingOrder;
            link.color = linkColor;
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

    private void OnValidate()
    {
        maxSegments = Mathf.Max(0, maxSegments);
        segmentSpacing = Mathf.Max(0.001f, segmentSpacing);
        segmentScale = Mathf.Max(0.001f, segmentScale);
        tautDistanceRate = Mathf.Clamp01(tautDistanceRate);
    }
}
