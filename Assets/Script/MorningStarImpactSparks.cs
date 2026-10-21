using UnityEngine;

/// <summary>投擲の衝撃と接地中の擦れを描画する。物理と状態遷移はLauncherが所有する。</summary>
[DisallowMultipleComponent]
public sealed class MorningStarImpactSparks : MonoBehaviour
{
    [SerializeField] private Material sparkMaterial;
    [SerializeField] private Material emberMaterial;
    [SerializeField] private Material sparkTrailMaterial;
    [SerializeField] private Material dustMaterial;
    [SerializeField] private Material impactFlashMaterial;
    [SerializeField] private SpriteRenderer ballRenderer;
    [Header("投擲の衝撃")]
    [SerializeField, Min(0f)] private float minimumImpactSpeed = 3.5f;
    [SerializeField, Min(0f)] private float cooldown = 0.09f;
    [SerializeField, Range(1, 32)] private int minimumSparks = 18;
    [SerializeField, Range(8, 64)] private int maximumSparks = 32;
    [Header("引きずり中の擦れ")]
    [SerializeField, Min(0.1f)] private float minimumScrapeSpeed = 0.65f;
    [SerializeField, Range(1f, 16f), Tooltip("接地して移動している間の小さな砂ぼこりの発生頻度")]
    private float scrapeRate = 8f;
    [Header("火花の接触判定（描画専用）")]
    [SerializeField] private bool collideWithTerrain = true;
    [SerializeField] private LayerMask particleCollisionLayers = (1 << 0) | (1 << 6) | (1 << 10);
    [SerializeField, Range(0f, 0.7f)] private float emberBounce = 0.32f;

    private ParticleSystem sparks;
    private ParticleSystem embers;
    private ParticleSystem dust;
    private ParticleSystem flash;
    private MorningStarLauncher launcher;
    private Rigidbody2D body;
    private float nextImpactTime;
    private float nextScrapeTime;
    private System.Random visualRandom;
    private readonly float[] sprayDirections = new float[3];
    public int BurstCount { get; private set; }
    public int ScrapeBurstCount { get; private set; }

    public void Initialize(MorningStarLauncher owner) => launcher = owner;

    private void Awake()
    {
        if (sparkMaterial == null) return;
        visualRandom = new System.Random(System.Guid.NewGuid().GetHashCode());
        body = GetComponent<Rigidbody2D>();
        if (ballRenderer == null) ballRenderer = GetComponentInChildren<SpriteRenderer>();
        sparks = CreateSystem("Impact Sparks", sparkMaterial, 256, true);
        var main = sparks.main;
        main.gravityModifier = 0.65f;
        var color = sparks.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] {
            new GradientColorKey(new Color(1f, 0.99f, 0.90f), 0f),
            new GradientColorKey(new Color(1f, 0.86f, 0.43f), 0.24f),
            new GradientColorKey(new Color(1f, 0.38f, 0.07f), 0.70f),
            new GradientColorKey(new Color(0.45f, 0.08f, 0.01f), 1f)
        }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.4f), new GradientAlphaKey(0f, 1f) });
        color.color = gradient;
        var size = sparks.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.15f));
        var renderer = sparks.GetComponent<ParticleSystemRenderer>();
        renderer.lengthScale = 2.4f;
        renderer.velocityScale = 0.045f;
        ConfigureTerrainCollision(sparks, 0.18f, 0.35f);

        embers = CreateSystem("Metal Embers", emberMaterial != null ? emberMaterial : sparkMaterial, 128, false);
        var emberMain = embers.main;
        emberMain.gravityModifier = 1.45f;
        var emberColor = embers.colorOverLifetime;
        emberColor.enabled = true;
        emberColor.color = gradient;
        var emberSize = embers.sizeOverLifetime;
        emberSize.enabled = true;
        emberSize.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.35f));
        var rotation = embers.rotationOverLifetime;
        rotation.enabled = true;
        rotation.z = new ParticleSystem.MinMaxCurve(-5f, 5f);
        ConfigureTerrainCollision(embers, emberBounce, 0.18f);
        if (sparkTrailMaterial != null)
        {
            var trails = embers.trails;
            trails.enabled = true;
            trails.ratio = 0.55f;
            trails.lifetime = new ParticleSystem.MinMaxCurve(0.045f, 0.085f);
            trails.minVertexDistance = 0.04f;
            trails.worldSpace = true;
            trails.dieWithParticles = true;
            trails.inheritParticleColor = true;
            trails.sizeAffectsWidth = true;
            trails.sizeAffectsLifetime = false;
            trails.widthOverTrail = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.55f, 1f, 0f));
            embers.GetComponent<ParticleSystemRenderer>().trailMaterial = sparkTrailMaterial;
        }

        dust = CreateSystem("Ground Dust", dustMaterial != null ? dustMaterial : sparkMaterial, 96, false);
        var dustMain = dust.main;
        dustMain.gravityModifier = -0.025f;
        var dustSize = dust.sizeOverLifetime;
        dustSize.enabled = true;
        dustSize.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.65f, 1f, 1.8f));
        SetFade(dust, 0.75f);
        if (impactFlashMaterial != null)
        {
            flash = CreateSystem("Contact Flash", impactFlashMaterial, 8, false);
            var flashMain = flash.main;
            flashMain.startSize3D = true;
            SetFade(flash, 0.15f);
        }
    }

    private void ConfigureTerrainCollision(ParticleSystem particles, float bounce, float lifetimeLoss)
    {
        var collision = particles.collision;
        collision.enabled = collideWithTerrain;
        collision.type = ParticleSystemCollisionType.World;
        collision.mode = ParticleSystemCollisionMode.Collision2D;
        collision.quality = ParticleSystemCollisionQuality.High;
        collision.collidesWith = particleCollisionLayers;
        collision.enableDynamicColliders = true;
        collision.bounce = bounce;
        collision.dampen = 0.3f;
        collision.lifetimeLoss = lifetimeLoss;
        collision.radiusScale = 0.16f;
        collision.minKillSpeed = 0.45f;
        collision.maxCollisionShapes = 128;
        // 火花側だけが反応する。Player・鉄球・鎖を押さず、命中通知も発生させない。
        collision.colliderForce = 0f;
        collision.sendCollisionMessages = false;
    }

    private float Range(float minimum, float maximum) =>
        Mathf.Lerp(minimum, maximum, (float)visualRandom.NextDouble());

    private uint ParticleSeed() => (uint)visualRandom.Next(1, int.MaxValue);

    private ParticleSystem CreateSystem(string effectName, Material material, int capacity, bool stretch)
    {
        var child = new GameObject(effectName);
        child.transform.SetParent(transform, false);
        var particles = child.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        particles.useAutoRandomSeed = false;
        particles.randomSeed = ParticleSeed();
        var main = particles.main;
        main.playOnAwake = false;
        main.loop = true;
        main.startSpeed = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = capacity;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        var emission = particles.emission;
        emission.enabled = false;
        var shape = particles.shape;
        shape.enabled = false;
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.renderMode = stretch ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
        renderer.cameraVelocityScale = 0f;
        renderer.sortingLayerID = ballRenderer != null ? ballRenderer.sortingLayerID : 0;
        renderer.sortingOrder = ballRenderer != null ? ballRenderer.sortingOrder + 2 : 4;
        return particles;
    }

    private static void SetFade(ParticleSystem particles, float hold)
    {
        var color = particles.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, hold), new GradientAlphaKey(0f, 1f) });
        color.color = gradient;
    }

    public void TryPlay(Collision2D collision, MorningStarLauncher launcher)
    {
        if (!isActiveAndEnabled || sparks == null || launcher == null || collision.collider == null
            || collision.contactCount == 0 || Time.time < nextImpactTime)
            return;
        var state = launcher.State;
        if (state != MorningStarLauncher.MorningStarState.Thrown
            && state != MorningStarLauncher.MorningStarState.Dropping) return;
        if (collision.collider.GetComponentInParent<Player>() != null) return;

        float speed = 0f;
        ContactPoint2D contact = collision.GetContact(0);
        for (int i = 0; i < collision.contactCount; i++)
        {
            var candidate = collision.GetContact(i);
            float approach = Mathf.Abs(Vector2.Dot(collision.relativeVelocity, candidate.normal));
            if (approach > speed) { speed = approach; contact = candidate; }
        }
        if (speed < minimumImpactSpeed) return;
        Vector2 normal = contact.normal;
        if (Vector2.Dot(normal, (Vector2)transform.position - contact.point) < 0f) normal = -normal;
        float strength = Mathf.InverseLerp(minimumImpactSpeed, 25f, speed);
        EmitImpact(contact.point, normal, strength, -collision.relativeVelocity, IsTerrain(collision.collider));
        nextImpactTime = Time.time + cooldown;
    }

    private void EmitImpact(Vector2 point, Vector2 normal, float strength, Vector2 incomingVelocity, bool hitTerrain)
    {
        int lowerCount = Mathf.Clamp(minimumSparks, 1, maximumSparks);
        int count = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(lowerCount, maximumSparks, strength) * Range(0.85f, 1.15f)), lowerCount, maximumSparks);
        float baseAngle = Mathf.Atan2(normal.y, normal.x) * Mathf.Rad2Deg;
        Vector2 tangent = new Vector2(-normal.y, normal.x);
        float shear = Mathf.Clamp(Vector2.Dot(incomingVelocity, tangent) * 0.8f, -20f, 20f);
        float spread = Range(100f, 155f);
        float burstTilt = Range(-16f, 16f) + shear;
        int lobes = visualRandom.Next(2, 4);
        for (int i = 0; i < lobes; i++)
            sprayDirections[i] = Range(-spread * 0.5f, spread * 0.5f);
        for (int i = 0; i < count; i++)
        {
            // 等間隔の扇形を避け、数本の不揃いな束と散発的な筋を混ぜる。
            // 方向・速度・太さ・寿命は別々に抽選し、ゲーム共通のRandom状態を変えない。
            float direction = Range(0f, 1f) < 0.72f
                ? sprayDirections[visualRandom.Next(lobes)] + Range(-10f, 10f)
                : Range(-spread * 0.5f, spread * 0.5f);
            float angle = Mathf.Clamp(direction + burstTilt, -84f, 84f) * Mathf.Deg2Rad;
            Vector2 velocity = (normal * Mathf.Cos(angle) + tangent * Mathf.Sin(angle))
                * Range(7f, 18f) * Mathf.Lerp(0.8f, 1.15f, strength);
            sparks.Emit(new ParticleSystem.EmitParams {
                position = point + normal * Range(0.035f, 0.085f) + tangent * Range(-0.10f, 0.10f),
                velocity = velocity,
                startLifetime = Range(0.14f, 0.34f),
                startSize = Range(0.085f, 0.16f),
                startColor = new Color(1f, Range(0.90f, 1f), Range(0.70f, 1f), Range(0.85f, 1f)),
                randomSeed = ParticleSeed()
            }, 1);
        }
        // 長い光の筋とは別に、小さな火種が弧を描いて落ち、床や壁で跳ねて冷える。
        int emberCount = visualRandom.Next(7, 14) + Mathf.RoundToInt(strength * 3f);
        for (int i = 0; i < emberCount; i++)
        {
            float angle = Range(-82f, 82f) * Mathf.Deg2Rad;
            embers.Emit(new ParticleSystem.EmitParams {
                position = point + normal * Range(0.04f, 0.10f) + tangent * Range(-0.10f, 0.10f),
                velocity = (normal * Mathf.Cos(angle) + tangent * Mathf.Sin(angle)) * Range(2.4f, 7.5f),
                startLifetime = Range(0.35f, 0.85f),
                startSize = Range(0.075f, 0.17f),
                rotation = Range(0f, 360f),
                startColor = new Color(1f, Range(0.78f, 1f), Range(0.55f, 0.9f), 1f),
                randomSeed = ParticleSeed()
            }, 1);
        }
        embers.Play();
        if (flash != null)
        {
            float flashWidth = Range(0.75f, 1.10f) * Mathf.Lerp(0.8f, 1.1f, strength);
            flash.Emit(new ParticleSystem.EmitParams {
                position = point + normal * 0.10f,
                startColor = new Color(1f, 0.97f, 0.85f, 0.9f),
                startLifetime = Range(0.045f, 0.075f),
                startSize3D = new Vector3(flashWidth, flashWidth * Range(0.35f, 0.5f), 1f),
                rotation = baseAngle + 90f + Range(-18f, 18f),
                randomSeed = ParticleSeed()
            }, 1);
            flash.Play();
        }
        if (normal.y > 0.5f && hitTerrain)
            EmitDust(point, normal, 0f, 5, true);
        sparks.Play();
        BurstCount++;
    }

    private void OnCollisionStay2D(Collision2D collision)
    {
        if (launcher == null || body == null || dust == null || !isActiveAndEnabled
            || Time.timeScale <= 0f || Time.time < nextScrapeTime || !IsTerrain(collision.collider)) return;
        var state = launcher.State;
        if (state != MorningStarLauncher.MorningStarState.Dragging
            && state != MorningStarLauncher.MorningStarState.Dropping) return;
        ContactPoint2D floor = default;
        bool grounded = false;
        for (int i = 0; i < collision.contactCount; i++)
        {
            var candidate = collision.GetContact(i);
            if (candidate.normal.y <= 0.5f) continue;
            if (!grounded || candidate.normal.y > floor.normal.y) floor = candidate;
            grounded = true;
        }
        if (!grounded) return;
        Vector2 tangent = new Vector2(floor.normal.y, -floor.normal.x);
        Vector2 floorVelocity = collision.collider.attachedRigidbody != null
            ? collision.collider.attachedRigidbody.GetPointVelocity(floor.point) : Vector2.zero;
        float slide = Vector2.Dot(body.linearVelocity - floorVelocity, tangent);
        if (Mathf.Abs(slide) < minimumScrapeSpeed) return;

        EmitDust(floor.point, floor.normal, slide, 2, false);
        // 火花は砂ぼこり3回につき1粒。停止・空中・Recallでは接触があっても出さない。
        if (ScrapeBurstCount % 3 == 0 && Range(0f, 1f) < 0.8f)
        {
            embers.Emit(new ParticleSystem.EmitParams {
                position = floor.point + floor.normal * 0.06f,
                velocity = -tangent * Mathf.Sign(slide) * Range(1.1f, 2.4f) + floor.normal * Range(0.8f, 1.5f),
                startColor = new Color(1f, 0.8f, 0.45f, 0.65f),
                startLifetime = Range(0.16f, 0.24f),
                startSize = Range(0.065f, 0.10f),
                rotation = Range(0f, 360f),
                randomSeed = ParticleSeed()
            }, 1);
            embers.Play();
        }
        nextScrapeTime = Time.time + Range(0.8f, 1.2f) / Mathf.Max(1f, scrapeRate);
        ScrapeBurstCount++;
    }

    private void EmitDust(Vector2 point, Vector2 normal, float slide, int count, bool impact)
    {
        Vector2 tangent = new Vector2(normal.y, -normal.x);
        for (int i = 0; i < count; i++)
        {
            float seed = Range(0f, 1f);
            float drift = impact ? Mathf.Lerp(-2f, 2f, seed) : -Mathf.Sign(slide) * (0.4f + seed * 0.6f);
            dust.Emit(new ParticleSystem.EmitParams {
                position = point + normal * 0.06f + tangent * Mathf.Lerp(-0.12f, 0.12f, seed),
                velocity = tangent * drift + normal * (0.35f + seed * 0.5f),
                startColor = new Color(0.62f, 0.66f, 0.72f, impact ? 0.4f : 0.27f),
                startLifetime = impact ? 0.35f : 0.3f,
                startSize = Mathf.Lerp(0.28f, impact ? 0.65f : 0.43f, seed),
                rotation = seed * 360f
            }, 1);
        }
        dust.Play();
    }

    private static bool IsTerrain(Collider2D collider)
    {
        if (collider == null || collider.isTrigger) return false;
        if (collider.gameObject.layer == 6 || collider.gameObject.layer == 10) return true;
        for (Transform current = collider.transform; current != null; current = current.parent)
            if (current.CompareTag("Floor")) return true;
        return false;
    }

    private void OnDisable()
    {
        if (sparks != null) sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (embers != null) embers.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (dust != null) dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (flash != null) flash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        nextImpactTime = 0f;
        nextScrapeTime = 0f;
    }
}
