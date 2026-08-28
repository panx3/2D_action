using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// タイトル専用の表示アニメーション。Player本体やPhysicsには依存しない。
/// </summary>
[DisallowMultipleComponent]
public sealed class TitleHeroIdleDisplay : MonoBehaviour
{
    [SerializeField] private Image heroImage;
    [SerializeField] private Sprite[] idleFrames;
    [SerializeField, Min(0.04f)] private float frameDuration = 0.13f;
    [SerializeField, Min(0f)] private float bobDistance = 3f;
    [SerializeField, Min(0.1f)] private float bobPeriod = 2.2f;
    [SerializeField, Range(0f, 0.05f)] private float breathingScale = 0.012f;

    private RectTransform _rectTransform;
    private Vector2 _basePosition;
    private Vector3 _baseScale;

    private void Awake()
    {
        _rectTransform = transform as RectTransform;
        if (_rectTransform != null)
            _basePosition = _rectTransform.anchoredPosition;
        _baseScale = transform.localScale;
    }

    private void OnEnable()
    {
        if (_rectTransform == null)
            _rectTransform = transform as RectTransform;
        if (_rectTransform != null)
            _basePosition = _rectTransform.anchoredPosition;
        _baseScale = transform.localScale;
    }

    private void Update()
    {
        float time = Time.unscaledTime;

        if (heroImage != null && idleFrames != null && idleFrames.Length > 0)
        {
            int frame = Mathf.FloorToInt(time / Mathf.Max(0.04f, frameDuration)) % idleFrames.Length;
            if (idleFrames[frame] != null)
                heroImage.sprite = idleFrames[frame];
        }

        float phase = time * Mathf.PI * 2f / Mathf.Max(0.1f, bobPeriod);
        if (_rectTransform != null)
            _rectTransform.anchoredPosition = _basePosition + Vector2.up * (Mathf.Sin(phase) * bobDistance);

        float breath = 1f + Mathf.Sin(phase) * breathingScale;
        transform.localScale = Vector3.Scale(_baseScale, new Vector3(breath, breath, 1f));
    }
}
