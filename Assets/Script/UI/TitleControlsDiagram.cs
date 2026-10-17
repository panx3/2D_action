using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Illustrates launch, travel, then player pull without touching gameplay physics.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class TitleControlsDiagram : MaskableGraphic
{
    [SerializeField] private RectTransform girl;
    [SerializeField] private RectTransform ball;
    [SerializeField] private TMP_Text stepLabel;
    private Vector2 girlPosition;
    private Vector2 ballPosition;
    private float visibility;
    private int lastStep = -1;
    private double startedAt;
    private static readonly string[] Steps = { "01  AIM", "02  THROW", "03  FOLLOW" };

    protected override void OnEnable()
    {
        base.OnEnable();
        startedAt = Time.unscaledTimeAsDouble;
        lastStep = -1;
    }

    private void Update()
    {
        float t = (float)((Time.unscaledTimeAsDouble - startedAt) % 8d);
        float flight = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(1.6f, 3.5f, t));
        float follow = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(3.6f, 6.2f, t));
        girlPosition = Vector2.Lerp(new Vector2(-145, -54), new Vector2(65, 20), follow);
        ballPosition = Vector2.Lerp(new Vector2(-70, -68), new Vector2(145, 68), flight);
        ballPosition.y += Mathf.Sin(flight * Mathf.PI) * 28;
        visibility = Mathf.Min(Mathf.Clamp01(t * 4), Mathf.Clamp01((8f - t) * 2));
        if (girl != null) girl.anchoredPosition = girlPosition;
        if (ball != null)
        {
            ball.anchoredPosition = ballPosition;
            ball.localRotation = Quaternion.Euler(0, 0, -flight * 100);
        }
        int step = t < 1.6f ? 0 : t < 3.6f ? 1 : 2;
        if (stepLabel != null && step != lastStep) { stepLabel.text = Steps[step]; lastStep = step; }
        if (girl != null) girl.GetComponent<UnityEngine.UI.Image>().color = new Color(1, 1, 1, visibility);
        if (ball != null) ball.GetComponent<UnityEngine.UI.Image>().color = new Color(1, 1, 1, visibility);
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Color quiet = new Color(.57f, .73f, .87f, .28f);
        // Dotted suggested trajectory stays visible as the example loops.
        for (int i = 0; i < 22; i++)
        {
            float t = i / 21f;
            Vector2 p = Vector2.Lerp(new Vector2(-70, -68), new Vector2(145, 68), t);
            p.y += Mathf.Sin(t * Mathf.PI) * 28;
            Line(vh, p, p + Vector2.right * 3, 2, quiet);
        }
        Vector2 start = girlPosition + new Vector2(12, -8);
        Vector2 end = ballPosition + new Vector2(-17, 0);
        float sag = Mathf.Lerp(24, 3, Mathf.Clamp01(Vector2.Distance(start, end) / 250));
        Color chain = new Color(.9f, .84f, .94f, .8f * visibility);
        for (int i = 0; i < 20; i++)
        {
            float t = i / 20f, next = (i + .66f) / 20f;
            Vector2 a = Vector2.Lerp(start, end, t) + Vector2.down * (Mathf.Sin(t * Mathf.PI) * sag);
            Vector2 b = Vector2.Lerp(start, end, next) + Vector2.down * (Mathf.Sin(next * Mathf.PI) * sag);
            Line(vh, a, b, 2.3f, chain);
        }
        Line(vh, new Vector2(-185, -115), new Vector2(-70, -115), 2, quiet);
        Line(vh, new Vector2(80, -28), new Vector2(190, -28), 2, quiet);
    }

    private static void Line(VertexHelper vh, Vector2 a, Vector2 b, float width, Color tint)
    {
        Vector2 d = (b - a).normalized;
        Vector2 n = new Vector2(-d.y, d.x) * width * .5f;
        int start = vh.currentVertCount;
        vh.AddVert(a - n, tint, Vector2.zero); vh.AddVert(a + n, tint, Vector2.zero);
        vh.AddVert(b + n, tint, Vector2.zero); vh.AddVert(b - n, tint, Vector2.zero);
        vh.AddTriangle(start, start + 1, start + 2); vh.AddTriangle(start, start + 2, start + 3);
    }
}
