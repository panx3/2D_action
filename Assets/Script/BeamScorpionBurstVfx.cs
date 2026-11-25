using UnityEngine;

/// <summary>発光の核と、ドット絵の輪郭に合わせた粒状の火花。</summary>
public sealed class BeamScorpionBurstVfx : MonoBehaviour
{
    public enum Style { Charge = 1, Muzzle, Impact, ClawArc }

    [SerializeField, Min(0.02f)] private float lifetime = 0.28f;
    [SerializeField] private Style style = Style.Impact;
    [SerializeField, Min(0f)] private float sparkTravel = 0.75f;
    [SerializeField] private SpriteRenderer flash;

    private static readonly int Shape = Shader.PropertyToID("_Shape");
    private static readonly int Span = Shader.PropertyToID("_Span");
    private static readonly int Progress = Shader.PropertyToID("_Progress");
    private static readonly int Arc = Shader.PropertyToID("_Arc");
    private const float PixelsPerUnit = 32f;
    private MaterialPropertyBlock properties;
    private SpriteRenderer[] renderers;
    private Color tint;
    private float elapsed, power;
    private bool playing;

    public void Play(Color color, Vector2 normal, float duration = -1f, float strength = 1f)
    {
        power = Mathf.Max(0.05f, strength);
        if (duration > 0f) lifetime = duration;
        transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(normal.y, normal.x) * Mathf.Rad2Deg);
        Initialize(color);
        Animate(0f);
    }

    public void PlayClawArc(Color color, int facing, float reach, float swingTime, float lowAngle, float highAngle)
    {
        style = Style.ClawArc;
        lifetime = swingTime + 0.14f;
        power = reach;
        Initialize(color);
        if (flash != null) flash.flipX = facing < 0;
        properties.SetVector(Arc, new Vector4(lowAngle * Mathf.Deg2Rad, highAngle * Mathf.Deg2Rad, swingTime / lifetime, 0f));
        Animate(0f);
    }

    private void Initialize(Color color)
    {
        tint = color;
        elapsed = 0f;
        properties ??= new MaterialPropertyBlock();
        renderers ??= GetComponentsInChildren<SpriteRenderer>();
        playing = true;
    }

    private void Update()
    {
        if (!playing) Play(Color.white, Vector2.right);
        elapsed += Time.deltaTime;
        Animate(Mathf.Clamp01(elapsed / lifetime));
        if (elapsed >= lifetime) Destroy(gameObject);
    }

    private void Animate(float t)
    {
        if (flash != null)
        {
            float diameter = style switch
            {
                Style.Charge => Mathf.Lerp(0.85f, 1.3f, t),
                Style.Muzzle => Mathf.Lerp(2f, 2.55f, t),
                Style.ClawArc => power * 2.42f,
                _ => Mathf.Lerp(1.05f, 1.6f, t) * Mathf.Lerp(0.55f, 1f, power)
            };
            flash.transform.localScale = new Vector3(diameter, diameter, 1f);
            flash.color = new Color(tint.r, tint.g, tint.b, Mathf.Clamp01(tint.a * (style == Style.ClawArc ? 1f : power)));
            properties.SetFloat(Shape, (float)style);
            properties.SetFloat(Span, diameter);
            properties.SetFloat(Progress, t);
            flash.SetPropertyBlock(properties);
        }

        // 飛び散る火花は粒の大きさと向きに段差を付ける。
        int spark = 0;
        foreach (SpriteRenderer sprite in renderers)
        {
            if (sprite == flash) continue;
            sprite.enabled = style != Style.ClawArc;
            if (!sprite.enabled) continue;
            AnimateSpark(sprite, spark++, t);
        }
    }

    private void AnimateSpark(SpriteRenderer sprite, int index, float t)
    {
        bool isCharge = style == Style.Charge;
        bool isImpact = style == Style.Impact;
        float sparkLife = 0.54f + (index % 5) * 0.10f;
        if (!isCharge && t >= sparkLife)
        {
            sprite.enabled = false;
            return;
        }
        float angle = isCharge ? (index * 137.5f - t * 160f) * Mathf.Deg2Rad
            : isImpact ? Mathf.Lerp(-82f, 82f, (index % 9) / 8f) * Mathf.Deg2Rad
            : (index * 137.5f) * Mathf.Deg2Rad;
        Vector2 heading = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        float flutter = 0.62f + (index % 5) * 0.19f;
        float travel = isCharge ? (1f - t) * 0.7f + 0.1f : 1f - (1f - t) * (1f - t);
        Vector2 offset = heading * ((0.09f + sparkTravel * travel) * flutter * power);
        if (!isCharge) offset.y -= t * t * (0.12f + (index % 3) * 0.06f);
        Vector2 pixelOffset = new Vector2(Mathf.Round(offset.x * PixelsPerUnit), Mathf.Round(offset.y * PixelsPerUnit)) / PixelsPerUnit;
        sprite.transform.localPosition = pixelOffset;
        float pixelAngle = Mathf.Round(angle * Mathf.Rad2Deg / 45f) * 45f;
        sprite.transform.localRotation = Quaternion.Euler(0f, 0f, pixelAngle);
        float length = isCharge ? 0.045f + travel * 0.08f : Mathf.Lerp(0.18f, 0.035f, t);
        float pixelLength = Mathf.Max(1f, Mathf.Round(length * flutter * power * PixelsPerUnit));
        float pixelHeight = t < 0.32f && index % 3 == 0 && power >= 0.75f ? 2f : 1f;
        sprite.transform.localScale = new Vector3(pixelLength / PixelsPerUnit, pixelHeight / PixelsPerUnit, 1f);
        Color color = t < 0.24f && index % 3 == 0 ? new Color(1f, 0.94f, 0.79f)
            : Color.Lerp(tint, new Color(1f, 0.57f, 0.26f), t < 0.36f ? 0.62f : 0.24f);
        color.a = tint.a;
        sprite.color = color;
    }
}
