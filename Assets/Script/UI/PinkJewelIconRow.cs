using UnityEngine;

/// <summary>Three placement-ordered icons; no text, notifications, input, or time ownership.</summary>
[DisallowMultipleComponent]
public sealed class PinkJewelIconRow : MonoBehaviour
{
    [SerializeField] private bool showFinalResult;
    [SerializeField] private Sprite collectedIcon;
    [SerializeField] private Sprite emptyIcon;
    [SerializeField] private UnityEngine.UI.Image[] icons;
    [SerializeField] private UnityEngine.UI.Image[] glints;
    [SerializeField, Range(0.1f, 0.8f)] private float highlightDuration = 0.36f;
    [SerializeField, Range(1f, 1.3f)] private float highlightScale = 1.12f;
    private readonly float[] highlights = new float[3];
    private PinkJewelCollection collection;
    private int displayedMask = -1;
    public int DisplayedMask => displayedMask;

    private void OnEnable()
    {
        Bind();
        Refresh(false);
    }

    private void Bind()
    {
        if (collection != null || PinkJewelCollection.Current == null) return;
        collection = PinkJewelCollection.Current;
        collection.Changed += OnChanged;
    }

    private void OnChanged(int mask) => Refresh(!showFinalResult);

    private void Refresh(bool animate)
    {
        int mask = collection == null ? 0 : showFinalResult ? collection.ResultMask : collection.CollectedMask;
        for (int i = 0; i < icons.Length && i < 3; i++)
        {
            bool acquired = (mask & (1 << i)) != 0;
            if (animate && acquired && displayedMask >= 0 && (displayedMask & (1 << i)) == 0)
                highlights[i] = highlightDuration;
            icons[i].sprite = acquired ? collectedIcon : emptyIcon;
        }
        displayedMask = mask;
    }

    private void Update()
    {
        Bind();
        int mask = collection == null ? 0 : showFinalResult ? collection.ResultMask : collection.CollectedMask;
        if (mask != displayedMask) Refresh(false);
        for (int i = 0; i < icons.Length && i < 3; i++)
        {
            highlights[i] = Mathf.Max(0f, highlights[i] - Time.unscaledDeltaTime);
            float t = highlightDuration > 0f ? highlights[i] / highlightDuration : 0f;
            float pulse = Mathf.Sin(t * Mathf.PI);
            icons[i].rectTransform.localScale = Vector3.one * Mathf.Lerp(1f, highlightScale, pulse);
            if (i < glints.Length && glints[i] != null)
                glints[i].color = new Color(1f, 0.88f, 0.96f, pulse * 0.95f);
        }
    }

    private void OnDisable()
    {
        if (collection != null) collection.Changed -= OnChanged;
        collection = null;
        for (int i = 0; i < icons.Length && i < 3; i++)
        {
            highlights[i] = 0f;
            icons[i].rectTransform.localScale = Vector3.one;
            if (i < glints.Length && glints[i] != null) glints[i].color = Color.clear;
        }
    }
}
