using UnityEngine;

/// <summary>Colliderを持たない、Goal結晶専用の演出破片。</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer), typeof(Rigidbody2D))]
public sealed class CrystalFragment : MonoBehaviour
{
    [SerializeField, Min(0f)] private float fadeDuration = 0.35f;

    private SpriteRenderer _spriteRenderer;
    private Color _baseColor;
    private float _lifetime = 1.5f;
    private float _elapsed;

    private void Awake()
    {
        _spriteRenderer = GetComponent<SpriteRenderer>();
        _baseColor = _spriteRenderer.color;
    }

    public void Initialize(float lifetime)
    {
        _lifetime = Mathf.Max(0.05f, lifetime);
        _elapsed = 0f;
        if (_spriteRenderer != null)
            _spriteRenderer.color = _baseColor;
    }

    private void Update()
    {
        _elapsed += Time.unscaledDeltaTime;
        float fadeStart = Mathf.Max(0f, _lifetime - fadeDuration);
        if (_spriteRenderer != null && _elapsed >= fadeStart)
        {
            float alpha = fadeDuration > 0f
                ? 1f - Mathf.Clamp01((_elapsed - fadeStart) / fadeDuration)
                : 0f;
            Color color = _baseColor;
            color.a *= alpha;
            _spriteRenderer.color = color;
        }

        if (_elapsed >= _lifetime)
            Destroy(gameObject);
    }
}
