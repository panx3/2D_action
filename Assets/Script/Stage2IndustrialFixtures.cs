using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Stage2に配置済みの設備から発光、排気、接地影を出す。</summary>
[DisallowMultipleComponent]
public sealed class Stage2IndustrialFixtures : MonoBehaviour
{
    public enum Kind { Lamp, Furnace, Cooling, Vent }

    [Serializable]
    private sealed class Fixture
    {
        public SpriteRenderer source;
        public Kind kind;
        public float contactWidth;
        public float contactY;
        public int contactOrder;
        [NonSerialized] public Sprite original, ventBody;
        [NonSerialized] public ScreenLocalLight2D light;
        [NonSerialized] public ParticleSystem particles;
        [NonSerialized] public ParticleSystemRenderer particleRenderer;
        [NonSerialized] public SpriteRenderer shadow, reflection;
        [NonSerialized] public bool visible;
    }

    [SerializeField] private Fixture[] fixtures = Array.Empty<Fixture>();
    private readonly List<GameObject> effects = new List<GameObject>();
    private readonly Dictionary<int, Material> particleMaterials = new Dictionary<int, Material>();
    private readonly Dictionary<Sprite, Sprite> ventBodies = new Dictionary<Sprite, Sprite>();
    private Sprite contactMask;
    private bool initialized;

    internal void Apply(bool active, Material atlasMaterial)
    {
        active &= isActiveAndEnabled && atlasMaterial != null;
        if (active && !initialized) Initialize(atlasMaterial);
        if (!initialized) return;
        foreach (Fixture fixture in fixtures)
        {
            if (fixture.source == null) continue;
            bool visible = active && fixture.source.enabled && fixture.source.gameObject.activeInHierarchy &&
                fixture.source.color.a > 0.01f;
            if (fixture.visible == visible) continue;
            fixture.visible = visible;
            if (fixture.ventBody != null)
            {
                if (visible && fixture.source.sprite == fixture.original) fixture.source.sprite = fixture.ventBody;
                if (!visible && fixture.source.sprite == fixture.ventBody) fixture.source.sprite = fixture.original;
            }
            if (fixture.light != null) fixture.light.lightEnabled = visible;
            if (fixture.shadow != null) fixture.shadow.enabled = visible;
            if (fixture.reflection != null) fixture.reflection.enabled = visible;
            if (fixture.particles == null) continue;
            fixture.particleRenderer.enabled = visible;
            if (visible) fixture.particles.Play(false);
            else fixture.particles.Pause(false);
        }
    }

    private void Initialize(Material atlasMaterial)
    {
        var atlas = atlasMaterial.mainTexture as Texture2D;
        if (atlas == null) return;
        contactMask = Sprite.Create(atlas, new Rect(atlas.width * 0.5f, atlas.height * 0.75f, atlas.width * 0.5f, atlas.height * 0.25f),
            Vector2.one * 0.5f, atlas.width * 0.5f, 0, SpriteMeshType.FullRect);
        contactMask.hideFlags = HideFlags.HideAndDontSave;
        for (int i = 0; i < fixtures.Length; i++)
        {
            Fixture fixture = fixtures[i];
            SpriteRenderer source = fixture.source;
            if (source == null || source.sprite == null) continue;
            fixture.original = source.sprite;
            if (fixture.kind == Kind.Vent)
            {
                fixture.ventBody = GetVentBody(source.sprite);
            }
            else
            {
                fixture.light = CreateLight(fixture);
            }
            fixture.particles = CreateParticles(fixture, atlasMaterial, (uint)(813 + i * 167));
            fixture.particleRenderer = fixture.particles.GetComponent<ParticleSystemRenderer>();
            if (fixture.contactWidth > 0)
            {
                fixture.reflection = CreateContact(fixture, false);
                fixture.shadow = CreateContact(fixture, true);
            }
        }
        initialized = true;
    }

    private Sprite GetVentBody(Sprite original)
    {
        if (ventBodies.TryGetValue(original, out Sprite body)) return body;
        // Stage2's texture is 72x67. Keep the first 22 rows from the foot of
        // the device, including the copper top plate where the plume begins.
        Rect bodyRect = original.rect;
        bodyRect.height = Mathf.Min(22, bodyRect.height);
        Vector2 pivot = new Vector2(original.pivot.x / bodyRect.width, original.pivot.y / bodyRect.height);
        body = Sprite.Create(original.texture, bodyRect, pivot, original.pixelsPerUnit, 0, SpriteMeshType.FullRect);
        body.name = original.name + " Body";
        body.hideFlags = HideFlags.HideAndDontSave;
        ventBodies.Add(original, body);
        return body;
    }

    private ScreenLocalLight2D CreateLight(Fixture fixture)
    {
        bool cooling = fixture.kind == Kind.Cooling;
        bool furnace = fixture.kind == Kind.Furnace;
        var uv = new Vector2(cooling ? 0.52f : 0.48f, furnace ? 0.39f : cooling ? 0.52f : 0.75f);
        var light = MakeEffect(fixture.source, "Industrial Light", SourcePoint(fixture.source, uv))
            .AddComponent<ScreenLocalLight2D>();
        light.lightEnabled = false;
        light.environmentOnly = true;
        light.emissionSource = fixture.source;
        light.color = cooling ? new Color32(87, 214, 191, 255) : furnace
            ? new Color32(255, 167, 82, 255) : new Color32(255, 208, 132, 255);
        light.radius = cooling ? 4.6f : furnace ? 4.4f : 4;
        light.intensity = cooling ? 0.30f : furnace ? 0.36f : 0.38f;
        light.falloff = 2;
        light.flickerEnabled = true;
        light.flickerAmount = furnace ? 0.17f : 0.05f;
        light.flickerSpeed = furnace ? 1.6f : 0.82f;
        light.glowRadius = cooling ? 3.1f : furnace ? 2.9f : 2.4f;
        light.glowIntensity = cooling ? 0.20f : furnace ? 0.23f : 0.26f;
        light.glowSortingOrder = -73;
        return light;
    }

    private ParticleSystem CreateParticles(Fixture fixture, Material atlasMaterial, uint seed)
    {
        bool vent = fixture.kind == Kind.Vent;
        bool cooling = fixture.kind == Kind.Cooling;
        bool sparks = fixture.kind == Kind.Furnace;
        bool steam = vent || cooling;
        Vector2 uv = vent ? new Vector2(0.48f, 21 / 67f) : cooling ? new Vector2(0.59f, 0.2f)
            : sparks ? new Vector2(0.34f, 0.96f) : new Vector2(0.61f, 0.72f);
        var host = MakeEffect(fixture.source, "Industrial " + (vent ? "Exhaust" : cooling ? "Coolant Mist" : sparks ? "Sparks" : "Ash"),
            SourcePoint(fixture.source, uv));
        host.SetActive(false);
        var particles = host.AddComponent<ParticleSystem>();
        var main = particles.main;
        main.playOnAwake = false;
        main.loop = true;
        main.useUnscaledTime = false;
        main.gravityModifier = 0;
        main.startSpeed = 0;
        main.startLifetime = new ParticleSystem.MinMaxCurve(steam ? 2.7f : 1.7f, steam ? 3.8f : 3.1f);
        main.startSize3D = true;
        main.startSizeX = new ParticleSystem.MinMaxCurve(steam ? 0.8f : 0.14f, steam ? 1.5f : 0.26f);
        main.startSizeY = new ParticleSystem.MinMaxCurve(steam ? 0.54f : 0.14f, steam ? 0.96f : 0.25f);
        main.startSizeZ = 1;
        main.startRotation = new ParticleSystem.MinMaxCurve(-0.45f, 0.45f);
        main.startColor = cooling ? new Color(0.48f, 0.82f, 0.78f, 0.36f) : steam
            ? new Color(0.96f, 0.81f, 0.72f, 0.60f) : sparks
                ? new Color(1, 0.66f, 0.31f, 0.84f) : new Color(0.71f, 0.59f, 0.60f, 0.70f);
        main.maxParticles = vent ? 8 : cooling || sparks ? 6 : 5;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        particles.useAutoRandomSeed = false;
        particles.randomSeed = seed;

        var emission = particles.emission;
        emission.rateOverTime = vent ? 2.3f : cooling ? 1.55f : sparks ? 1.7f : 1.15f;
        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = cooling ? 0.24f : 0.09f;
        var velocity = particles.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(0.10f, vent ? 0.58f : 0.47f);
        velocity.y = new ParticleSystem.MinMaxCurve(cooling ? 0.02f : vent ? 0.51f : 0.32f,
            cooling ? 0.16f : vent ? 1.09f : sparks ? 1.05f : 0.79f);
        velocity.z = new ParticleSystem.MinMaxCurve(0, 0);

        var color = particles.colorOverLifetime;
        color.enabled = true;
        var fade = new Gradient();
        fade.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
            new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, steam ? 0.26f : 0.16f),
                new GradientAlphaKey(steam ? 0.46f : 0.86f, 0.6f), new GradientAlphaKey(0, 1) });
        color.color = fade;
        if (steam)
        {
            var expansion = particles.sizeOverLifetime;
            expansion.enabled = true;
            expansion.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.EaseInOut(0, 0.55f, 1, 1.5f));
        }
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = ParticleMaterial(atlasMaterial, steam ? 6 : sparks ? 5 : 4);
        renderer.sortingLayerID = fixture.source.sortingLayerID;
        renderer.sortingOrder = -72;
        renderer.enabled = false;
        host.SetActive(true);
        return particles;
    }

    private Material ParticleMaterial(Material atlasMaterial, int cell)
    {
        if (particleMaterials.TryGetValue(cell, out Material material)) return material;
        material = new Material(atlasMaterial) { hideFlags = HideFlags.HideAndDontSave };
        material.SetTextureScale("_MainTex", new Vector2(0.5f, 0.25f));
        material.SetTextureOffset("_MainTex", new Vector2(cell % 2 * 0.5f, cell / 2 * 0.25f));
        particleMaterials.Add(cell, material);
        return material;
    }

    private SpriteRenderer CreateContact(Fixture fixture, bool shade)
    {
        float width = fixture.contactWidth * (shade ? 0.84f : 1);
        Vector3 position = fixture.source.bounds.center;
        position.x += width * (shade ? 0.07f : -0.10f);
        position.y = fixture.contactY - (shade ? 0.045f : 0.13f);
        var host = MakeEffect(fixture.source, shade ? "Industrial Contact Shade" : "Industrial Floor Reflection", position);
        host.transform.localScale = Vector3.Scale(host.transform.localScale, new Vector3(width, shade ? 0.37f : 0.56f, 1));
        var renderer = host.AddComponent<SpriteRenderer>();
        renderer.sprite = contactMask;
        renderer.sortingLayerID = fixture.source.sortingLayerID;
        renderer.sortingOrder = fixture.contactOrder;
        renderer.color = shade ? new Color(0.075f, 0.052f, 0.104f, 0.60f) : fixture.kind == Kind.Cooling
            ? new Color(0.34f, 0.72f, 0.62f, 0.27f) : new Color(0.95f, 0.63f, 0.31f, 0.30f);
        renderer.enabled = false;
        return renderer;
    }

    private GameObject MakeEffect(SpriteRenderer source, string name, Vector3 position)
    {
        var result = new GameObject(name) { hideFlags = HideFlags.DontSave, layer = source.gameObject.layer };
        result.transform.SetPositionAndRotation(position, Quaternion.identity);
        result.transform.SetParent(source.transform, true);
        effects.Add(result);
        return result;
    }

    private static Vector3 SourcePoint(SpriteRenderer renderer, Vector2 uv)
    {
        Sprite sprite = renderer.sprite;
        Vector2 point = (Vector2.Scale(sprite.rect.size, uv) - sprite.pivot) / sprite.pixelsPerUnit;
        if (renderer.flipX) point.x = -point.x;
        if (renderer.flipY) point.y = -point.y;
        return renderer.transform.TransformPoint(point);
    }

    private void OnDisable() => Apply(false, null);

    private void OnDestroy()
    {
        foreach (GameObject effect in effects) if (effect != null) Destroy(effect);
        foreach (Material material in particleMaterials.Values) Destroy(material);
        foreach (Sprite body in ventBodies.Values) Destroy(body);
        if (contactMask != null) Destroy(contactMask);
    }
}
