using UnityEngine;

/// <summary>鉄球のチャージ・強攻撃の見た目と音だけを担当。物理と入力はLauncherが所有する。</summary>
[DisallowMultipleComponent]
public sealed class MorningStarChargeFeedback : MonoBehaviour
{
    private const int RingSegments = 48;
    private static readonly Color Ice = new Color(0.45f, 0.88f, 1f);
    private SpriteRenderer source;
    private SpriteRenderer whiteOverlay;
    private SpriteRenderer halo;
    private LineRenderer burstRing;
    private readonly LineRenderer[] streaks = new LineRenderer[8];
    private TrailRenderer trail;
    private Gradient normalTrailGradient;
    private Gradient chargedTrailGradient;
    private Material material;
    private Texture2D haloTexture;
    private Sprite haloSprite;
    private AudioSource audioSource;
    private AudioClip readySound;
    private AudioClip powerSound;
    private Transform effectRoot;
    private float radius;
    private float progress;
    private bool charging;
    private bool readySignalled;
    private bool flying;
    private float clock;
    private float burstTime;
    private float burstDuration;
    private float burstSize;
    private Vector3 burstPosition;

    public void Initialize(AudioSource routingSource)
    {
        if (effectRoot != null)
            return;

        source = GetComponentInChildren<SpriteRenderer>();
        if (source == null)
            return;
        Shader shader = Resources.Load<Shader>("MorningStarChargeWhite");
        if (shader == null)
            return;

        material = new Material(shader) { name = "MorningStarCharge (Runtime)" };
        radius = Mathf.Max(source.bounds.extents.x, source.bounds.extents.y);
        effectRoot = new GameObject("Charge Feedback").transform;
        effectRoot.SetParent(transform, false);

        whiteOverlay = new GameObject("White Charge").AddComponent<SpriteRenderer>();
        whiteOverlay.transform.SetParent(source.transform, false);
        whiteOverlay.sharedMaterial = material;
        whiteOverlay.sortingLayerID = source.sortingLayerID;
        whiteOverlay.sortingOrder = source.sortingOrder + 1;

        haloTexture = new Texture2D(64, 64, TextureFormat.RGBA32, false) { name = "Charge Halo", filterMode = FilterMode.Bilinear };
        var pixels = new Color[64 * 64];
        for (int y = 0; y < 64; y++)
        for (int x = 0; x < 64; x++)
        {
            float distance = new Vector2((x - 31.5f) / 31.5f, (y - 31.5f) / 31.5f).magnitude;
            pixels[y * 64 + x] = new Color(1f, 1f, 1f, Mathf.Pow(Mathf.Clamp01(1f - distance), 2f));
        }
        haloTexture.SetPixels(pixels);
        haloTexture.Apply(false, true);
        haloSprite = Sprite.Create(haloTexture, new Rect(0, 0, 64, 64), Vector2.one * 0.5f, 64);
        halo = new GameObject("Charge Halo").AddComponent<SpriteRenderer>();
        halo.transform.SetParent(effectRoot, false);
        halo.sprite = haloSprite;
        halo.sharedMaterial = material;
        halo.sortingLayerID = source.sortingLayerID;
        halo.sortingOrder = source.sortingOrder - 1;

        burstRing = MakeLine("Power Ring", radius * 0.07f);
        for (int i = 0; i < streaks.Length; i++)
            streaks[i] = MakeLine("Charge Spark " + i, radius * 0.04f);

        trail = new GameObject("Charged Trail").AddComponent<TrailRenderer>();
        trail.transform.SetParent(effectRoot, false);
        trail.sharedMaterial = material;
        trail.sortingLayerID = source.sortingLayerID;
        trail.sortingOrder = source.sortingOrder - 1;
        trail.minVertexDistance = radius * 0.12f;
        trail.autodestruct = false;
        trail.emitting = false;
        trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
        chargedTrailGradient = new Gradient();
        chargedTrailGradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Ice, 0.45f) },
            new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0f, 1f) });
        normalTrailGradient = new Gradient();
        normalTrailGradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Ice, 0.45f) },
            new[] { new GradientAlphaKey(0.18f, 0f), new GradientAlphaKey(0f, 1f) });
        trail.colorGradient = normalTrailGradient;

        audioSource = effectRoot.gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;
        if (routingSource != null)
            audioSource.outputAudioMixerGroup = routingSource.outputAudioMixerGroup;
        readySound = MakeSound("Charge Ready", false);
        powerSound = MakeSound("Power Attack", true);
        ResetFeedback();
    }

    public void BeginCharge()
    {
        ResetFeedback();
        charging = true;
    }

    public void SetCharge(float value)
    {
        progress = Mathf.Clamp01(value);
        if (progress < 1f || readySignalled || effectRoot == null)
            return;
        readySignalled = true;
        Burst(transform.position, 1.65f, 0.24f);
        audioSource.PlayOneShot(readySound, 0.32f);
    }

    public void PrepareThrow(float value)
    {
        charging = false;
        flying = false;
        progress = value;
        if (trail != null)
        {
            trail.emitting = false;
            trail.Clear(); // Recallの瞬間移動を残光でつながない。
        }
    }

    public void PlayLaunch(Vector2 direction, float charge01)
    {
        progress = charge01;
        charging = false;
        flying = true;
        if (effectRoot == null)
            return;
        trail.Clear();
        bool charged = charge01 > 0f;
        trail.widthMultiplier = radius * (charge01 >= 1f ? 1.15f : charged ? 0.6f : 0.35f);
        trail.time = charge01 >= 1f ? 0.18f : charged ? 0.1f : 0.055f;
        trail.colorGradient = charged ? chargedTrailGradient : normalTrailGradient;
        trail.emitting = flying;
        if (charge01 >= 1f)
        {
            Burst(transform.position - (Vector3)direction * radius * 0.6f, 2.1f, 0.18f);
            audioSource.PlayOneShot(powerSound, 0.42f);
        }
    }

    public void PlayImpact(Vector2 point, Vector2 direction)
    {
        if (effectRoot == null)
            return;
        Burst(point - direction * radius * 0.1f, 2.8f, 0.22f);
        audioSource.PlayOneShot(powerSound, 0.55f);
    }

    public void StopChargeAndFlight()
    {
        progress = 0f;
        charging = false;
        flying = false;
        if (trail != null)
            trail.emitting = false;
    }

    public void ResetFeedback()
    {
        StopChargeAndFlight();
        readySignalled = false;
        burstTime = 0f;
        clock = 0f;
        if (effectRoot == null)
            return;
        whiteOverlay.enabled = false;
        halo.enabled = false;
        burstRing.enabled = false;
        foreach (var streak in streaks)
            streak.enabled = false;
        trail.Clear();
        audioSource.Stop();
    }

    private LineRenderer MakeLine(string label, float width)
    {
        var line = new GameObject(label).AddComponent<LineRenderer>();
        line.transform.SetParent(effectRoot, false);
        line.sharedMaterial = material;
        line.useWorldSpace = true;
        line.widthMultiplier = width;
        line.numCapVertices = 2;
        line.sortingLayerID = source.sortingLayerID;
        line.sortingOrder = source.sortingOrder + 2;
        line.enabled = false;
        return line;
    }

    private void Burst(Vector3 position, float size, float duration)
    {
        burstPosition = position;
        burstSize = size;
        burstDuration = duration;
        burstTime = duration;
    }

    private void LateUpdate()
    {
        if (effectRoot == null || source == null)
            return;
        clock += Time.deltaTime;
        float pulse = progress >= 1f ? 0.88f + Mathf.Sin(clock * 9f) * 0.12f : 1f;
        whiteOverlay.sprite = source.sprite;
        whiteOverlay.flipX = source.flipX;
        whiteOverlay.flipY = source.flipY;
        whiteOverlay.enabled = progress > 0f;
        whiteOverlay.color = new Color(1f, 1f, 1f, progress * (flying ? 0.55f : 0.65f) * pulse);
        halo.enabled = progress > 0f;
        halo.transform.position = source.bounds.center;
        float diameter = radius * (3f + progress * 0.6f);
        Vector3 parentScale = effectRoot.lossyScale;
        halo.transform.localScale = new Vector3(diameter / Mathf.Abs(parentScale.x), diameter / Mathf.Abs(parentScale.y), 1f);
        halo.color = new Color(Ice.r, Ice.g, Ice.b, progress * 0.5f * pulse);

        // Burstはヒットストップ中も短く展開し、当たった瞬間を見せる。
        burstTime = Mathf.Max(0f, burstTime - Time.unscaledDeltaTime);
        burstRing.enabled = burstTime > 0f;
        if (burstTime > 0f)
        {
            float t = 1f - burstTime / burstDuration;
            float distance = radius * Mathf.Lerp(0.65f, burstSize, 1f - (1f - t) * (1f - t));
            DrawArc(burstRing, burstPosition, distance, 1f, 0f);
            burstRing.startColor = burstRing.endColor = new Color(1f, 1f, 1f, 1f - t);
            for (int i = 0; i < streaks.Length; i++)
            {
                float angle = (i * 45f + 22.5f) * Mathf.Deg2Rad;
                Vector3 direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                DrawStreak(streaks[i], burstPosition + direction * distance * 0.65f,
                    burstPosition + direction * distance * (i % 2 == 0 ? 1.3f : 1f), new Color(Ice.r, Ice.g, Ice.b, 1f - t));
            }
        }
        else
        {
            for (int i = 0; i < streaks.Length; i++)
            {
                streaks[i].enabled = charging && progress > 0.08f;
                if (!streaks[i].enabled)
                    continue;
                float phase = Mathf.Repeat(clock * (0.7f + progress * 0.8f) + i / 8f, 1f);
                float angle = (i * 45f + clock * 35f) * Mathf.Deg2Rad;
                Vector3 direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                float distance = radius * Mathf.Lerp(2.2f, 1.05f, phase);
                DrawStreak(streaks[i], source.bounds.center + direction * distance,
                    source.bounds.center + direction * (distance + radius * 0.2f),
                    new Color(Ice.r, Ice.g, Ice.b, Mathf.Sin(phase * Mathf.PI) * progress * 0.7f));
            }
        }
    }

    private static void DrawStreak(LineRenderer line, Vector3 start, Vector3 end, Color color)
    {
        line.enabled = true;
        line.positionCount = 2;
        line.SetPosition(0, start);
        line.SetPosition(1, end);
        line.startColor = line.endColor = color;
    }

    private static void DrawArc(LineRenderer line, Vector3 center, float size, float amount, float startAngle)
    {
        int segments = Mathf.Max(1, Mathf.CeilToInt(RingSegments * amount));
        line.positionCount = segments + 1;
        for (int i = 0; i <= segments; i++)
        {
            float angle = (startAngle + 360f * amount * i / segments) * Mathf.Deg2Rad;
            line.SetPosition(i, center + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * size);
        }
    }

    private static AudioClip MakeSound(string label, bool power)
    {
        const int rate = 22050;
        float duration = power ? 0.17f : 0.2f;
        var samples = new float[Mathf.CeilToInt(rate * duration)];
        float phase = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float t = (float)i / samples.Length;
            float frequency = power ? Mathf.Lerp(230f, 55f, Mathf.Sqrt(t)) : (t < 0.4f ? 880f : 1320f);
            phase += 2f * Mathf.PI * frequency / rate;
            float noise = Mathf.Sin(i * 78.233f) * 0.2f;
            float envelope = Mathf.Min(1f, t * 80f) * Mathf.Pow(1f - t, power ? 2f : 1.5f);
            samples[i] = (Mathf.Sin(phase) * 0.65f + (power ? noise : Mathf.Sin(phase * 2f) * 0.15f)) * envelope;
        }
        AudioClip clip = AudioClip.Create(label, samples.Length, 1, rate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private void OnDisable() => ResetFeedback();

    private void OnDestroy()
    {
        if (whiteOverlay != null) Destroy(whiteOverlay.gameObject);
        if (effectRoot != null) Destroy(effectRoot.gameObject);
        if (material != null) Destroy(material);
        if (haloSprite != null) Destroy(haloSprite);
        if (haloTexture != null) Destroy(haloTexture);
        if (readySound != null) Destroy(readySound);
        if (powerSound != null) Destroy(powerSound);
    }
}
