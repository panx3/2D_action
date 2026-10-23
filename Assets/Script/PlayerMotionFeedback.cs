using UnityEngine;

/// <summary>走行・ジャンプ・着地の土煙と、鉄球に引かれる時の薄い残像。移動・鎖の状態は読み取るだけ。</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(1200)]
public sealed class PlayerMotionFeedback : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Player player;
    [SerializeField] private MorningStarLauncher launcher;
    [SerializeField] private ChainConstraint2D chain;
    [SerializeField] private Rigidbody2D ball;
    [SerializeField] private Collider2D bodyCollider;
    [SerializeField] private SpriteRenderer bodySprite;
    [SerializeField] private Material dustMaterial;

    [Header("Pull Echo")]
    [SerializeField, Min(0f), Tooltip("鎖の引く方向への速度がこの値を超えると残像を出す")]
    private float minimumPullSpeed = 6f;
    [SerializeField, Range(0f, 0.3f)] private float echoOpacity = 0.13f;
    [SerializeField, Range(0.03f, 0.15f)] private float echoLifetime = 0.085f;

    [Header("Running Dust")]
    [SerializeField, Min(0f)] private float minimumRunSpeed = 2.5f;
    [SerializeField, Range(1f, 15f)] private float dustRate = 7f;
    [SerializeField] private LayerMask groundLayers = (1 << 0) | (1 << 6) | (1 << 10);

    [Header("Jump / Landing Dust")]
    [SerializeField, Tooltip("4列×2行の土煙。未設定ならResources/PlayerDustBurstを使用")]
    private Texture2D jumpLandingDustSheet;
    [SerializeField, Min(0.1f), Tooltip("ジャンプ・着地の土煙全体の表示倍率。走行時の土煙には影響しない")]
    private float dustBurstScale = 1.8f;
    [SerializeField, Min(0.1f)] private float jumpDustWidth = 1.4f;
    [SerializeField, Min(0.1f)] private float landingDustWidth = 1.8f;
    [SerializeField, Min(0.1f)] private float heavyLandingDustWidth = 3.2f;
    [SerializeField, Min(0f), Tooltip("大きな着地土煙へ変化し始める、頂点からの落下距離")]
    private float heavyLandingStartHeight = 3f;
    [SerializeField, Min(0.1f), Tooltip("最大サイズになる落下距離（World Unit）")]
    private float heavyLandingFullHeight = 10f;

    private sealed class DustBurst
    {
        public SpriteRenderer renderer;
        public float age, duration, opacity;
    }

    private readonly DustBurst[] bursts = new DustBurst[4];
    private Sprite[] burstFrames;
    private Transform burstRoot;
    private bool trackingAirborne;
    private float airborneApex;
    private int nextBurst;

    private Rigidbody2D body;
    private PlayerHealth health;
    private MotionSpriteEcho echoes;
    private ParticleSystem dust;
    private ParticleSystemRenderer dustRenderer;
    private System.Random visualRandom;
    private ContactFilter2D groundFilter;
    private readonly RaycastHit2D[] groundHits = new RaycastHit2D[8];
    private float dustTimer;
    private float recentThrow;

    private void Awake()
    {
        if (player == null) player = GetComponent<Player>();
        if (launcher == null) launcher = GetComponent<MorningStarLauncher>();
        if (bodyCollider == null) bodyCollider = GetComponent<Collider2D>();
        if (bodySprite == null) bodySprite = GetComponent<SpriteRenderer>();
        body = GetComponent<Rigidbody2D>();
        health = GetComponent<PlayerHealth>();
        visualRandom = new System.Random(System.Guid.NewGuid().GetHashCode());
        groundFilter = new ContactFilter2D { useTriggers = false };
        groundFilter.SetLayerMask(groundLayers);
        if (bodySprite != null)
            echoes = new MotionSpriteEcho(bodySprite, transform, echoLifetime, echoOpacity);
        if (dustMaterial != null) CreateDust();
        CreateBurstPool();
    }

    private void OnEnable()
    {
        if (player == null) player = GetComponent<Player>();
        if (player == null) return;
        player.Jumped += HandleJump;
        player.Landed += HandleLanding;
    }

    private void CreateBurstPool()
    {
        if (jumpLandingDustSheet == null)
            jumpLandingDustSheet = Resources.Load<Texture2D>("PlayerDustBurst");
        if (jumpLandingDustSheet == null) return;

        burstFrames = new Sprite[8];
        int cellWidth = jumpLandingDustSheet.width / 4;
        int cellHeight = jumpLandingDustSheet.height / 2;
        for (int i = 0; i < burstFrames.Length; i++)
        {
            burstFrames[i] = Sprite.Create(jumpLandingDustSheet,
                new Rect((i % 4) * cellWidth, (1 - i / 4) * cellHeight, cellWidth, cellHeight),
                new Vector2(0.5f, 0.1f), 32f, 0, SpriteMeshType.FullRect);
            burstFrames[i].name = "Ground dust " + i;
        }
        // プレイヤーから独立させ、離陸後も発生地点の床に残す。
        burstRoot = new GameObject("Jump and Landing Dust (pooled)").transform;
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(burstRoot.gameObject, gameObject.scene);
        for (int i = 0; i < bursts.Length; i++)
        {
            var view = new GameObject("Dust burst " + i).AddComponent<SpriteRenderer>();
            view.transform.SetParent(burstRoot, false);
            view.gameObject.layer = gameObject.layer;
            view.enabled = false;
            bursts[i] = new DustBurst { renderer = view };
        }
    }

    private bool CanShowDust => player != null && body != null && player.isActiveAndEnabled
        && player.InputEnabled && !player.IsGoalInputBlocked && (health == null || !health.IsDead)
        && bodyCollider != null && bodyCollider.enabled && bodySprite != null
        && bodySprite.enabled && bodySprite.sprite != null;

    private void HandleJump()
    {
        if (!CanShowDust || Time.deltaTime <= 0f) return;
        // 実床を確認するので、コヨーテジャンプや空中の磁石離脱では宙に土煙を出さない。
        EmitBurst(jumpDustWidth, 0.32f, 0.62f);
        airborneApex = body.position.y;
        trackingAirborne = true;
    }

    private void HandleLanding()
    {
        if (!CanShowDust || Time.deltaTime <= 0f) return;
        float height = trackingAirborne ? Mathf.Max(0f, airborneApex - body.position.y) : 0f;
        float strength = Mathf.InverseLerp(heavyLandingStartHeight,
            Mathf.Max(heavyLandingStartHeight + 0.01f, heavyLandingFullHeight), height);
        EmitBurst(Mathf.Lerp(landingDustWidth, heavyLandingDustWidth, strength),
            Mathf.Lerp(0.4f, 0.48f, strength), Mathf.Lerp(0.68f, 0.8f, strength));
        trackingAirborne = false;
    }

    private void EmitBurst(float width, float duration, float opacity)
    {
        if (burstFrames == null || !TryGetFloor(0f, out RaycastHit2D floor)) return;
        DustBurst burst = bursts[nextBurst];
        nextBurst = (nextBurst + 1) % bursts.Length;
        burst.age = 0f;
        burst.duration = duration;
        burst.opacity = opacity;
        SpriteRenderer view = burst.renderer;
        view.sprite = burstFrames[0];
        view.sortingLayerID = bodySprite.sortingLayerID;
        view.sortingOrder = bodySprite.sortingOrder + 1;
        view.flipX = visualRandom.Next(2) == 0;
        view.color = new Color(1f, 1f, 1f, opacity);
        view.transform.SetPositionAndRotation(new Vector3(floor.point.x + floor.normal.x * 0.025f,
            floor.point.y + floor.normal.y * 0.025f, transform.position.z),
            Quaternion.FromToRotation(Vector3.up, floor.normal));
        float scale = width * dustBurstScale / burstFrames[0].bounds.size.x;
        view.transform.localScale = new Vector3(scale, scale * 0.85f, 1f);
        view.enabled = true;
    }

    private void UpdateBursts()
    {
        if (!player.IsGrounded)
        {
            airborneApex = trackingAirborne ? Mathf.Max(airborneApex, body.position.y) : body.position.y;
            trackingAirborne = true;
        }
        if (burstFrames == null) return;
        foreach (DustBurst burst in bursts)
        {
            if (!burst.renderer.enabled) continue;
            burst.age += Time.deltaTime;
            float progress = burst.age / burst.duration;
            if (progress >= 1f) { burst.renderer.enabled = false; continue; }
            burst.renderer.sprite = burstFrames[Mathf.Min(7, (int)(progress * 8f))];
            float fade = 1f - Mathf.InverseLerp(0.5f, 1f, progress);
            burst.renderer.color = new Color(1f, 1f, 1f, burst.opacity * fade);
        }
    }

    private void CreateDust()
    {
        dust = new GameObject("Running Dust").AddComponent<ParticleSystem>();
        dust.transform.SetParent(transform, false);
        dust.gameObject.layer = gameObject.layer;
        dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        dust.useAutoRandomSeed = false;
        dust.randomSeed = (uint)visualRandom.Next(1, int.MaxValue);
        var main = dust.main;
        main.playOnAwake = false;
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startSpeed = 0f;
        main.gravityModifier = -0.025f;
        main.maxParticles = 32;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        var emission = dust.emission;
        emission.enabled = false;
        var shape = dust.shape;
        shape.enabled = false;
        var size = dust.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.7f, 1f, 1.7f));
        var fade = dust.colorOverLifetime;
        fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0.65f, 0.25f), new GradientAlphaKey(0f, 1f) });
        fade.color = gradient;
        dustRenderer = dust.GetComponent<ParticleSystemRenderer>();
        dustRenderer.sharedMaterial = dustMaterial;
    }

    private void LateUpdate()
    {
        if (!CanShowDust)
        {
            Clear();
            return;
        }
        if (Time.deltaTime <= 0f) return;

        if (launcher != null && launcher.State == MorningStarLauncher.MorningStarState.Thrown)
            recentThrow = 0.35f;
        else
            recentThrow = Mathf.Max(0f, recentThrow - Time.deltaTime);

        bool freeRope = launcher != null && (launcher.State == MorningStarLauncher.MorningStarState.Thrown
            || launcher.State == MorningStarLauncher.MorningStarState.Dragging
            || launcher.State == MorningStarLauncher.MorningStarState.Dropping);
        if (!freeRope)
        {
            recentThrow = 0f;
            echoes?.Clear(); // Recallや登りへ残像を持ち越さない。
        }
        else
            echoes?.Tick(Time.deltaTime, GetPullStrength());

        UpdateBursts();
        UpdateRunningDust();
    }

    private float GetPullStrength()
    {
        if (recentThrow <= 0f || chain == null || !chain.isActiveAndEnabled || ball == null)
            return 0f;
        float start = chain.MaxRopeLength * (chain.PrioritizesFlyingTrajectory ? 0.985f : chain.TensionStartRatio);
        float tautness = Mathf.InverseLerp(start, chain.MaxRopeLength, chain.CurrentRopeLength);
        if (tautness <= 0f) return 0f;
        Vector2 target = chain.RopeContactPointCount > 0 ? chain.GetRopeContactPoint(0) : ball.position;
        Vector2 direction = target - launcher.RopeAnchorWorld;
        if (player.IsGrounded) direction.y = 0f;
        float speedTowardBall = Vector2.Dot(body.linearVelocity, direction.normalized);
        return Mathf.InverseLerp(minimumPullSpeed, minimumPullSpeed + 9f, speedTowardBall) * tautness;
    }

    private void UpdateRunningDust()
    {
        dustTimer = Mathf.Max(0f, dustTimer - Time.deltaTime);
        if (dust == null || bodyCollider == null || !bodyCollider.enabled || !player.IsGrounded
            || Mathf.Abs(player.MoveInputX) < 0.1f || dustTimer > 0f)
            return;

        float offset = -Mathf.Sign(body.linearVelocity.x) * bodyCollider.bounds.size.x * 0.18f;
        if (!TryGetFloor(offset, out RaycastHit2D floor)) return;
        Vector2 tangent = new Vector2(floor.normal.y, -floor.normal.x);
        Vector2 floorVelocity = floor.rigidbody != null ? floor.rigidbody.GetPointVelocity(floor.point) : Vector2.zero;
        float speed = Vector2.Dot(body.linearVelocity - floorVelocity, tangent);
        if (Mathf.Abs(speed) < minimumRunSpeed || speed * player.MoveInputX <= 0f) return;

        dustRenderer.sortingLayerID = bodySprite.sortingLayerID;
        dustRenderer.sortingOrder = bodySprite.sortingOrder + 1;
        int motes = Mathf.Abs(speed) > 6f ? 2 : 1;
        for (int i = 0; i < motes; i++)
        {
            float variation = (float)visualRandom.NextDouble();
            var particle = new ParticleSystem.EmitParams
            {
                position = (Vector3)(floor.point + floor.normal * 0.055f + tangent * ((variation - 0.5f) * 0.18f)),
                velocity = tangent * (-Mathf.Sign(speed) * Mathf.Lerp(0.45f, 1.15f, variation))
                    + floor.normal * Mathf.Lerp(0.3f, 0.65f, (float)visualRandom.NextDouble()),
                startSize = Mathf.Lerp(0.3f, 0.46f, variation),
                startLifetime = Mathf.Lerp(0.24f, 0.36f, variation),
                startColor = new Color(0.62f, 0.66f, 0.72f, 0.24f),
                rotation = variation * 360f,
                randomSeed = (uint)visualRandom.Next(1, int.MaxValue)
            };
            particle.position += Vector3.forward * transform.position.z;
            dust.Emit(particle, 1);
        }
        dust.Play();
        dustTimer = Mathf.Lerp(0.85f, 1.15f, (float)visualRandom.NextDouble()) / Mathf.Max(1f, dustRate);
    }

    private bool TryGetFloor(float horizontalOffset, out RaycastHit2D floor)
    {
        Bounds bounds = bodyCollider.bounds;
        Vector2 origin = new Vector2(bounds.center.x + horizontalOffset, bounds.min.y + 0.12f);
        int count = Physics2D.Raycast(origin, Vector2.down, groundFilter, groundHits, 0.3f);
        floor = default;
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            RaycastHit2D hit = groundHits[i];
            if (hit.collider == bodyCollider || hit.rigidbody == body || hit.normal.y < 0.5f || hit.distance >= nearest)
                continue;
            nearest = hit.distance;
            floor = hit;
        }
        return floor.collider != null; // コヨーテタイムや段差の空中には出さない。
    }

    private void Clear()
    {
        echoes?.Clear();
        if (dust != null) dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        recentThrow = 0f;
        dustTimer = 0f;
        trackingAirborne = false;
        foreach (DustBurst burst in bursts)
            if (burst != null && burst.renderer != null) burst.renderer.enabled = false;
    }

    private void OnDisable()
    {
        if (player != null)
        {
            player.Jumped -= HandleJump;
            player.Landed -= HandleLanding;
        }
        Clear();
    }

    private void OnDestroy()
    {
        echoes?.Dispose();
        if (burstRoot != null) Destroy(burstRoot.gameObject);
        if (burstFrames != null)
            foreach (Sprite frame in burstFrames) Destroy(frame);
    }
}
