using System;
using UnityEngine;
using UnityEngine.Rendering;

public enum EnemyHealthGaugeStyle
{
    Normal,
    Miniboss
}

/// <summary>敵から独立したワールド表示。被弾から破砕までを一つの時間軸で進める。</summary>
[DisallowMultipleComponent]
public sealed class EnemyMiniHealthGauge : MonoBehaviour
{
    internal const string ResourcePath = "Feedback/MiniHealthGauge";

    [Serializable]
    private sealed class Style
    {
        public Sprite frame;
        [Tooltip("フレームの左下を原点とした内窓のピクセル座標。")]
        public RectInt opening;
        [Min(0.1f)] public float width = 1.8f;
        [Range(0, 16)] public int sparkCount = 4;
        [Min(0f)] public float sparkStrength = 1f;
        [Range(4, 24)] public int fragmentCount = 10;
        [Min(0.08f)] public float defeatDuration = 0.3f;
    }

    [Header("Artwork")]
    [SerializeField] private Style normal = new Style();
    [SerializeField] private Style miniboss = new Style
    {
        width = 3.5f, sparkCount = 6, sparkStrength = 1.15f, fragmentCount = 16, defeatDuration = 0.5f
    };
    [SerializeField] private Sprite backgroundSprite, fillSprite, trailSprite, sparkSprite;
    [SerializeField] private Material flashMaterial;

    [Header("Visibility and HP (seconds)")]
    [SerializeField, Min(0f)] private float displayTime = 3f;
    [SerializeField, Min(0f)] private float fadeInTime = 0.05f;
    [SerializeField, Min(0f)] private float fadeOutTime = 0.2f;
    [SerializeField, Min(0f)] private float fillTime = 0.1f;
    [SerializeField, Min(0f)] private float trailDelay = 0.15f;
    [SerializeField, Min(0f)] private float trailTime = 0.25f;

    [Header("Impact")]
    [SerializeField, Min(0f)] private float flashTime = 0.06f;
    [SerializeField, Range(0f, 1f)] private float flashStrength = 0.76f;
    [SerializeField, Min(0f)] private float shakeTime = 0.12f;
    [SerializeField, Min(0f)] private float shakeStrength = 0.047f;
    [SerializeField, Range(0f, 0.5f)] private float popStrength = 0.1f;

    private static readonly Color[] SparkColors =
    {
        new Color32(255, 251, 233, 255),
        new Color32(255, 224, 141, 255),
        new Color32(255, 181, 116, 255)
    };
    private readonly System.Random random = new System.Random();
    private Style style;
    private Transform motion;
    private SpriteRenderer frame, background, fill, trail, frameFlash, backgroundFlash;
    private Particle[] sparks, fragments;
    private int nextSpark;
    private float pixel, barLeft, barY, barWidth, barHeight;
    private float hitAt, fillFrom, trailFrom, target, fillRatio, trailRatio, alphaFrom, opacity;
    private float flashAt = float.NegativeInfinity;
    private float deathDrainTime, deathFlashTime;

    public bool IsDefeating { get; private set; }

    internal void Initialize(EnemyHealthGaugeStyle kind, int layer, int order)
    {
        style = kind == EnemyHealthGaugeStyle.Miniboss ? miniboss : normal;
        pixel = style.width / style.frame.rect.width;
        Vector2 pivot = style.frame.pivot;
        barLeft = (style.opening.xMin - pivot.x) * pixel;
        barY = (style.opening.center.y - pivot.y) * pixel;
        barWidth = style.opening.width * pixel;
        barHeight = style.opening.height * pixel;

        var group = gameObject.AddComponent<SortingGroup>();
        group.sortingLayerID = layer;
        group.sortingOrder = order;
        motion = new GameObject("Artwork").transform;
        motion.SetParent(transform, false);
        background = Part("Background", backgroundSprite, motion, 0);
        trail = Part("DamageTrail", trailSprite, motion, 1);
        fill = Part("Fill", fillSprite, motion, 2);
        frame = Part("Frame", style.frame, motion, 3);
        frameFlash = Part("FrameFlash", style.frame, motion, 4);
        backgroundFlash = Part("InteriorFlash", backgroundSprite, motion, 4);
        frameFlash.sharedMaterial = backgroundFlash.sharedMaterial = flashMaterial;
        frame.transform.localScale = frameFlash.transform.localScale = Vector3.one * (pixel * style.frame.pixelsPerUnit);
        PlaceStrip(background, barLeft, barWidth);
        PlaceStrip(backgroundFlash, barLeft, barWidth);
        sparks = new Particle[Mathf.Max(1, style.sparkCount * 3)];
        gameObject.SetActive(false);
    }

    internal void Impact(float previous, float remaining, bool defeat)
    {
        if (IsDefeating) return;
        if (!gameObject.activeSelf)
        {
            fillRatio = trailRatio = previous;
            gameObject.SetActive(true);
        }

        hitAt = Time.time;
        fillFrom = Mathf.Max(remaining, fillRatio);
        trailFrom = Mathf.Max(fillFrom, trailRatio);
        target = remaining;
        alphaFrom = opacity;
        IsDefeating = defeat;
        if (defeat)
        {
            deathDrainTime = fillFrom > 0f ? Mathf.Min(fillTime, style.defeatDuration * 0.4f) : 0f;
            deathFlashTime = Mathf.Min(flashTime, style.defeatDuration * 0.22f);
        }
        else
        {
            if (hitAt - flashAt >= flashTime + 0.025f) flashAt = hitAt;
            EmitSparks(barLeft + barWidth * target, 0f, style.sparkCount, hitAt, 0.19f);
        }
        Draw(hitAt);
    }

    internal void Hide()
    {
        opacity = 0f;
        flashAt = float.NegativeInfinity;
        foreach (var spark in sparks)
            if (spark.renderer != null) spark.renderer.enabled = false;
        gameObject.SetActive(false);
    }

    private void LateUpdate() => Draw(Time.time);

    private void Draw(float now)
    {
        float age = now - hitAt;
        if (IsDefeating && age >= style.defeatDuration)
        {
            Destroy(gameObject);
            return;
        }
        if (IsDefeating && age >= deathDrainTime + deathFlashTime)
        {
            if (fragments == null) Shatter(now, style.defeatDuration - age);
            DrawParticles(fragments, now);
            DrawParticles(sparks, now);
            return;
        }
        if (!IsDefeating && age >= displayTime + fadeOutTime)
        {
            Hide();
            return;
        }

        float drain = Progress(age, IsDefeating ? deathDrainTime : fillTime);
        fillRatio = Mathf.Lerp(fillFrom, target, Mathf.SmoothStep(0f, 1f, drain));
        float catchUp = IsDefeating ? drain : Progress(age - trailDelay, trailTime);
        trailRatio = Mathf.Max(fillRatio, Mathf.Lerp(trailFrom, target, catchUp));
        PlaceStrip(fill, barLeft, barWidth * fillRatio);
        PlaceStrip(trail, barLeft, barWidth * trailRatio);

        float fadeIn = Progress(age, fadeInTime);
        opacity = IsDefeating ? 1f
            : Mathf.Lerp(alphaFrom, 1f, 1f - (1f - fadeIn) * (1f - fadeIn)) * (1f - Progress(age - displayTime, fadeOutTime));
        SetOpacity(frame, opacity);
        SetOpacity(background, opacity);
        SetOpacity(fill, opacity);
        SetOpacity(trail, opacity);

        float flash = IsDefeating ? (age >= deathDrainTime ? Mathf.Min(1f, flashStrength * 1.35f) : 0f)
            : flashStrength * (1f - Progress(now - flashAt, flashTime));
        frameFlash.enabled = backgroundFlash.enabled = flash > 0f;
        SetOpacity(frameFlash, opacity * flash);
        SetOpacity(backgroundFlash, opacity * flash);

        float motionProgress = Progress(age, shakeTime);
        float shake = (1f - motionProgress) * shakeStrength;
        motion.localPosition = new Vector3(Mathf.Sin(age * 145f) * shake, Mathf.Sin(age * 193f) * shake * 0.46f, 0f);
        float pop = motionProgress < 1f ? popStrength * Mathf.Sin(Mathf.PI * motionProgress) : 0f;
        motion.localScale = Vector3.one * (1f + pop);
        DrawParticles(sparks, now);
    }

    private void Shatter(float now, float lifetime)
    {
        motion.gameObject.SetActive(false);
        int count = Mathf.Clamp(style.fragmentCount, 4, 24);
        fragments = new Particle[count];
        Rect rect = style.frame.textureRect;
        int leftWidth = style.opening.xMin;
        int rightStart = style.opening.xMax;
        int splitY = Mathf.FloorToInt(style.opening.center.y);
        fragments[0] = Shard(new Rect(0f, 0f, leftWidth, rect.height), now, lifetime);
        fragments[1] = Shard(new Rect(rightStart, 0f, rect.width - rightStart, rect.height), now, lifetime);
        int topParts = (count - 1) / 2;
        ShatterRail(2, topParts, splitY, rect.height - splitY, now, lifetime);
        ShatterRail(2 + topParts, count - 2 - topParts, 0, splitY, now, lifetime);
        EmitSparks(barLeft + barWidth * 0.5f, barWidth, style.sparkCount * 2, now, Mathf.Min(0.24f, lifetime));
    }

    private void ShatterRail(int start, int count, float y, float height, float now, float lifetime)
    {
        for (int i = 0; i < count; i++)
        {
            int left = style.opening.xMin + Mathf.RoundToInt(style.opening.width * i / (float)count);
            int right = style.opening.xMin + Mathf.RoundToInt(style.opening.width * (i + 1) / (float)count);
            fragments[start + i] = Shard(new Rect(left, y, right - left, height), now, lifetime);
        }
    }

    private Particle Shard(Rect area, float now, float lifetime)
    {
        Vector2 origin = (area.center - style.frame.pivot) * pixel;
        area.position += style.frame.textureRect.position;
        Sprite sprite = Sprite.Create(style.frame.texture, area, Vector2.one * 0.5f, 1f / pixel, 0, SpriteMeshType.FullRect);
        var renderer = Part("MetalFragment", sprite, transform, 3);
        Vector2 velocity = new Vector2(origin.x / style.width * 2.9f + Between(-0.5f, 0.5f),
            Between(0.85f, 2.15f) + Mathf.Sign(origin.y) * 0.4f);
        float gravity = 4.1f / Mathf.Max(0.08f, lifetime);
        return new Particle(renderer, origin, velocity, Color.white, now, lifetime, gravity, Between(-390f, 390f));
    }

    private void EmitSparks(float x, float spread, int count, float now, float lifetime)
    {
        for (int i = 0; i < count; i++)
        {
            int slot = nextSpark++ % sparks.Length;
            var renderer = sparks[slot].renderer;
            if (renderer == null) renderer = Part("Spark", sparkSprite, transform, 5);
            renderer.enabled = true;
            renderer.transform.localScale = Vector3.one * (Between(0.42f, 0.85f) * style.sparkStrength);
            float offset = spread > 0f ? spread * ((i + 0.5f) / count - 0.5f) : Between(-0.027f, 0.027f);
            Vector2 origin = new Vector2(x + offset, barY + Between(-0.035f, 0.035f));
            Vector2 velocity = new Vector2(Between(-1.15f, 0.9f), Between(0.8f, 2.15f)) * style.sparkStrength;
            sparks[slot] = new Particle(renderer, origin, velocity, SparkColors[i % SparkColors.Length],
                now, lifetime * Between(0.72f, 1f), 7f, 0f);
        }
    }

    private static void DrawParticles(Particle[] particles, float now)
    {
        foreach (var particle in particles)
        {
            if (particle.renderer == null || !particle.renderer.enabled) continue;
            float age = now - particle.start;
            float progress = Progress(age, particle.lifetime);
            if (progress >= 1f)
            {
                particle.renderer.enabled = false;
                continue;
            }
            particle.renderer.transform.SetLocalPositionAndRotation(
                particle.origin + particle.velocity * age + Vector2.down * (0.5f * particle.gravity * age * age),
                Quaternion.Euler(0f, 0f, particle.spin * age));
            Color color = particle.color;
            color.a = 1f - progress * progress;
            particle.renderer.color = color;
        }
    }

    private void OnDestroy()
    {
        if (fragments == null) return;
        foreach (var fragment in fragments)
            if (fragment.sprite != null) Destroy(fragment.sprite);
    }

    private void PlaceStrip(SpriteRenderer renderer, float x, float width)
    {
        renderer.enabled = width > 0f;
        renderer.transform.localPosition = new Vector3(x + width * 0.5f, barY, 0f);
        Vector2 size = renderer.sprite.bounds.size;
        renderer.transform.localScale = new Vector3(width / size.x, barHeight / size.y, 1f);
    }

    private static SpriteRenderer Part(string name, Sprite sprite, Transform parent, int order)
    {
        var renderer = new GameObject(name).AddComponent<SpriteRenderer>();
        renderer.transform.SetParent(parent, false);
        renderer.sprite = sprite;
        renderer.sortingOrder = order;
        return renderer;
    }

    private static void SetOpacity(SpriteRenderer renderer, float alpha)
    {
        renderer.color = new Color(1f, 1f, 1f, alpha);
    }

    private static float Progress(float age, float duration) =>
        age < 0f ? 0f : duration > 0f ? Mathf.Clamp01(age / duration) : 1f;

    private float Between(float min, float max) => Mathf.Lerp(min, max, (float)random.NextDouble());

    private readonly struct Particle
    {
        public readonly SpriteRenderer renderer;
        public readonly Sprite sprite;
        public readonly Vector2 origin, velocity;
        public readonly Color color;
        public readonly float start, lifetime, gravity, spin;

        public Particle(SpriteRenderer renderer, Vector2 origin, Vector2 velocity, Color color,
            float start, float lifetime, float gravity, float spin)
        {
            this.renderer = renderer;
            sprite = renderer.sprite;
            this.origin = origin;
            this.velocity = velocity;
            this.color = color;
            this.start = start;
            this.lifetime = lifetime;
            this.gravity = gravity;
            this.spin = spin;
        }
    }
}
