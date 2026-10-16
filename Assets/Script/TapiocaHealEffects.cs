using UnityEngine;

/// <summary>表示専用の2D演出。取得時に親から離れ、アイテム消滅後も最後まで再生する。</summary>
[DisallowMultipleComponent]
public sealed class TapiocaHealEffects : MonoBehaviour
{
    [Header("Visual / Pixel Assets")]
    [SerializeField] private SpriteRenderer itemVisual;
    [SerializeField] private Material silhouetteMaterial;
    [SerializeField] private Sprite dotSprite;
    [SerializeField] private Sprite starSprite;
    [SerializeField] private Color pink = new Color(1f, 0.22f, 0.65f, 1f);
    [SerializeField] private Color cyan = new Color(0.2f, 0.8f, 1f, 1f);

    [Header("Idle (world units / game seconds)")]
    [SerializeField, Min(0f)] private float floatAmplitude = 0.15f;
    [SerializeField, Min(0.1f)] private float floatPeriod = 2.8f;
    [SerializeField, Range(0f, 2f)] private float outlinePixels = 1f;
    [SerializeField, Range(0f, 1f)] private float outlineOpacity = 0.45f;
    [SerializeField, Min(0f)] private float blinkSpeed = 1.4f;
    [SerializeField, Range(0, 16)] private int idleDotCount = 4;
    [SerializeField, Range(0, 4)] private int starCount = 2;
    [SerializeField, Min(0f)] private float orbitPadding = 0.15f;
    [SerializeField, Min(0f)] private float orbitSpeed = 0.12f;
    [SerializeField, Min(0.001f)] private float dotSize = 0.045f;
    [SerializeField, Min(0.001f)] private float starSize = 0.1f;
    [SerializeField, Min(0.1f)] private float glintInterval = 3f;
    [SerializeField, Min(0.01f)] private float glintDuration = 0.12f;
    [SerializeField, Range(0f, 1f)] private float glintOpacity = 0.3f;

    [Header("Pickup")]
    [SerializeField, Min(0.01f)] private float popDuration = 0.16f;
    [SerializeField, Min(1f)] private float popScale = 1.3f;
    [SerializeField, Range(0, 48)] private int burstCount = 14;
    [SerializeField] private Vector2 burstSpeed = new Vector2(0.8f, 1.8f);
    [SerializeField, Min(0.01f)] private float burstLifetime = 0.55f;

    private SpriteRenderer[] _outlines;
    private SpriteRenderer[] _idleParticles;
    private SpriteRenderer[] _burstParticles;
    private Vector3[] _burstPositions;
    private Vector3[] _burstVelocities;
    private SpriteRenderer _glint;
    private Vector3 _restPosition;
    private Vector3 _visualScale;
    private Color _visualColor;
    private float _pixelStep;
    private float _age;
    private bool _collected;

    private void Awake()
    {
        if (itemVisual == null || itemVisual.sprite == null)
            return;
        _restPosition = transform.localPosition;
        _visualScale = itemVisual.transform.localScale;
        _visualColor = itemVisual.color;
        _pixelStep = 1f / Mathf.Max(1f, itemVisual.sprite.pixelsPerUnit);
        _age = Random.Range(0f, Mathf.Max(0.1f, glintInterval));

        // 既存の白シルエットShaderで元Spriteのアルファ形状だけを描く。
        if (silhouetteMaterial != null)
        {
            _outlines = new SpriteRenderer[8];
            int index = 0;
            for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
            {
                if (x == 0 && y == 0) continue;
                var outline = MakeRenderer("PixelOutline", itemVisual.sprite, itemVisual.transform, -1, silhouetteMaterial);
                outline.transform.localPosition = new Vector3(x, y, 0f) * (_pixelStep * outlinePixels);
                _outlines[index++] = outline;
            }
            _glint = MakeRenderer("SurfaceGlint", itemVisual.sprite, itemVisual.transform, 1, silhouetteMaterial);
        }

        int dots = Mathf.Clamp(idleDotCount, 0, 16);
        int stars = Mathf.Clamp(starCount, 0, 4);
        _idleParticles = new SpriteRenderer[dots + stars];
        for (int i = 0; i < _idleParticles.Length; i++)
            _idleParticles[i] = MakePixel(i < dots ? dotSprite : starSprite, i < dots ? dotSize : starSize);
    }

    private void Update()
    {
        if (itemVisual == null || itemVisual.sprite == null)
            return;
        _age += Time.deltaTime;
        if (_collected) UpdatePickup();
        else UpdateIdle();
    }

    private void UpdateIdle()
    {
        float scaleY = Mathf.Max(0.0001f, Mathf.Abs(transform.lossyScale.y));
        float bob = Mathf.Sin(_age * Mathf.PI * 2f / Mathf.Max(0.1f, floatPeriod)) * floatAmplitude / scaleY;
        transform.localPosition = _restPosition + Vector3.up * Snap(bob);

        float blink = Mathf.Round((0.65f + 0.35f * Mathf.Sin(_age * blinkSpeed * Mathf.PI * 2f)) * 3f) / 3f;
        SetOutlineAlpha(outlineOpacity * blink);
        if (_glint != null)
        {
            float time = _age % Mathf.Max(0.1f, glintInterval);
            float alpha = time < glintDuration ? Mathf.Sin(time / Mathf.Max(0.01f, glintDuration) * Mathf.PI) * glintOpacity : 0f;
            _glint.color = new Color(1f, 1f, 1f, alpha);
        }

        Vector2 extent = itemVisual.sprite.bounds.extents;
        float padding = orbitPadding / Mathf.Max(0.0001f, Mathf.Abs(transform.lossyScale.x));
        for (int i = 0; i < _idleParticles.Length; i++)
        {
            float angle = (_age * orbitSpeed + i / (float)Mathf.Max(1, _idleParticles.Length)) * Mathf.PI * 2f;
            _idleParticles[i].transform.localPosition = new Vector3(
                Snap(Mathf.Cos(angle) * (extent.x + padding)), Snap(Mathf.Sin(angle) * (extent.y + padding)), 0f);
            float flicker = Mathf.Sin(_age * 3f + i * 2.3f) > 0f ? 0.6f : 0.15f;
            Color color = i % 2 == 0 ? pink : cyan;
            color.a *= flicker;
            _idleParticles[i].color = color;
        }
    }

    public void PlayPickup()
    {
        if (_collected || itemVisual == null)
            return;
        _collected = true;
        // worldPositionStaysで見た目・倍率を保つ。取得Colliderは親側に残す。
        transform.SetParent(null, true);
        _age = 0f;
        if (_glint != null) _glint.enabled = false;
        if (_idleParticles != null)
            foreach (var particle in _idleParticles) particle.enabled = false;

        int count = Mathf.Clamp(burstCount, 0, 48);
        _burstParticles = new SpriteRenderer[count];
        _burstPositions = new Vector3[count];
        _burstVelocities = new Vector3[count];
        float minSpeed = Mathf.Max(0f, Mathf.Min(burstSpeed.x, burstSpeed.y));
        float maxSpeed = Mathf.Max(minSpeed, Mathf.Max(burstSpeed.x, burstSpeed.y));
        for (int i = 0; i < count; i++)
        {
            _burstParticles[i] = MakePixel(dotSprite, dotSize);
            _burstParticles[i].color = pink;
            float angle = (i + Random.value) / Mathf.Max(1, count) * Mathf.PI * 2f;
            Vector3 velocity = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * Random.Range(minSpeed, maxSpeed);
            _burstVelocities[i] = transform.InverseTransformVector(velocity);
        }
    }

    private void UpdatePickup()
    {
        float progress = Mathf.Clamp01(_age / Mathf.Max(0.01f, popDuration));
        float eased = 1f - (1f - progress) * (1f - progress);
        itemVisual.transform.localScale = _visualScale * Mathf.Lerp(1f, popScale, eased);
        Color color = _visualColor;
        color.a *= 1f - progress;
        itemVisual.color = color;
        SetOutlineAlpha(outlineOpacity * (1f - progress));

        float remaining = 1f - Mathf.Clamp01(_age / Mathf.Max(0.01f, burstLifetime));
        for (int i = 0; i < _burstParticles.Length; i++)
        {
            _burstPositions[i] += _burstVelocities[i] * Time.deltaTime;
            _burstVelocities[i] *= Mathf.Exp(-2f * Time.deltaTime);
            var p = _burstPositions[i];
            _burstParticles[i].transform.localPosition = new Vector3(Snap(p.x), Snap(p.y), 0f);
            color = pink;
            color.a *= remaining;
            _burstParticles[i].color = color;
        }
        if (_age >= Mathf.Max(popDuration, burstLifetime))
            Destroy(gameObject);
    }

    private void SetOutlineAlpha(float alpha)
    {
        if (_outlines == null) return;
        for (int i = 0; i < _outlines.Length; i++)
        {
            Color color = i % 2 == 0 ? pink : cyan;
            color.a *= alpha;
            _outlines[i].color = color;
        }
    }

    private float Snap(float value) => Mathf.Round(value / _pixelStep) * _pixelStep;

    private SpriteRenderer MakePixel(Sprite sprite, float worldSize)
    {
        var renderer = MakeRenderer("Pixel", sprite, transform, 2, itemVisual.sharedMaterial);
        if (sprite != null)
        {
            float size = Mathf.Max(0.001f, sprite.bounds.size.x * Mathf.Abs(transform.lossyScale.x));
            renderer.transform.localScale = Vector3.one * (worldSize / size);
        }
        return renderer;
    }

    private SpriteRenderer MakeRenderer(string name, Sprite sprite, Transform parent, int order, Material material)
    {
        var go = new GameObject(name);
        go.layer = gameObject.layer;
        go.transform.SetParent(parent, false);
        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sharedMaterial = material;
        renderer.sortingLayerID = itemVisual.sortingLayerID;
        renderer.sortingOrder = itemVisual.sortingOrder + order;
        if (sprite == itemVisual.sprite)
        {
            renderer.flipX = itemVisual.flipX;
            renderer.flipY = itemVisual.flipY;
        }
        return renderer;
    }
}
