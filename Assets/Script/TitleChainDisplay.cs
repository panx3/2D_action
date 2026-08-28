using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// タイトル画面用の非Physics鎖。複数のUI Imageを弧状に並べ、START時に張力を表現する。
/// </summary>
[DisallowMultipleComponent]
public sealed class TitleChainDisplay : MonoBehaviour
{
    [SerializeField] private RectTransform startAnchor;
    [SerializeField] private RectTransform endAnchor;
    [SerializeField] private RectTransform[] segments;
    [SerializeField, Min(0f)] private float idleSag = 46f;
    [SerializeField, Min(1f)] private float thickness = 15f;
    [SerializeField, Min(0f)] private float idleSway = 3f;

    private RectTransform _root;
    private float _tension;
    private float _alpha = 1f;

    public float Tension => _tension;

    private void Awake()
    {
        _root = transform as RectTransform;
        RefreshNow();
    }

    private void LateUpdate()
    {
        RefreshNow();
    }

    public void SetTension(float tension)
    {
        _tension = Mathf.Clamp01(tension);
    }

    public void SetAlpha(float alpha)
    {
        _alpha = Mathf.Clamp01(alpha);
        ApplyAlpha();
    }

    public void RefreshNow()
    {
        if (_root == null)
            _root = transform as RectTransform;
        if (_root == null || startAnchor == null || endAnchor == null || segments == null || segments.Length == 0)
            return;

        Vector2 start = _root.InverseTransformPoint(startAnchor.position);
        Vector2 end = _root.InverseTransformPoint(endAnchor.position);
        float sag = idleSag * (1f - _tension);
        float sway = Mathf.Sin(Time.unscaledTime * 1.35f) * idleSway * (1f - _tension);

        for (int i = 0; i < segments.Length; i++)
        {
            RectTransform segment = segments[i];
            if (segment == null)
                continue;

            float t0 = i / (float)segments.Length;
            float t1 = (i + 1f) / segments.Length;
            Vector2 p0 = EvaluatePoint(start, end, t0, sag, sway);
            Vector2 p1 = EvaluatePoint(start, end, t1, sag, sway);
            Vector2 delta = p1 - p0;

            segment.anchoredPosition = (p0 + p1) * 0.5f;
            segment.sizeDelta = new Vector2(delta.magnitude + 2f, thickness);
            segment.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        }
    }

    private static Vector2 EvaluatePoint(Vector2 start, Vector2 end, float t, float sag, float sway)
    {
        Vector2 point = Vector2.Lerp(start, end, t);
        float arc = Mathf.Sin(Mathf.PI * t);
        point.y -= sag * arc;
        point.x += sway * arc;
        return point;
    }

    private void ApplyAlpha()
    {
        if (segments == null)
            return;

        foreach (RectTransform segment in segments)
        {
            if (segment == null)
                continue;
            Graphic graphic = segment.GetComponent<Graphic>();
            if (graphic == null)
                continue;
            Color color = graphic.color;
            color.a = _alpha;
            graphic.color = color;
        }
    }
}
