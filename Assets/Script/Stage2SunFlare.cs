using UnityEngine;
using UnityEngine.Rendering;

/// <summary>既存の夕日の手前にレンズ反射を描き、柱と気球船で太陽が隠れる量を反映する。</summary>
[DefaultExecutionOrder(1300)] // Sample the airship after its parallax and lens-size updates.
internal sealed class Stage2SunFlare : MonoBehaviour
{
    private const float SunViewportY = 0.53f;
    private const float FlareBandHeight = 0.24f;
    private static readonly int SourceId = Shader.PropertyToID("_Source");
    private static readonly int BandId = Shader.PropertyToID("_Band");
    private static readonly Vector2[] Samples =
    {
        Vector2.zero, new(0.65f, 0), new(-0.65f, 0), new(0, 0.65f), new(0, -0.65f),
        new(0.46f, 0.46f), new(-0.46f, 0.46f), new(0.46f, -0.46f), new(-0.46f, -0.46f)
    };

    private Camera targetCamera;
    private SpriteRenderer pillar, airship;
    private MeshRenderer flareRenderer;
    private Mesh mesh;
    private MaterialPropertyBlock properties;
    private Vector4 sun;
    private float intensity;

    internal static Vector4 SunPosition(Camera camera)
    {
        if (camera == null || !camera.isActiveAndEnabled || !camera.orthographic) return Vector4.zero;
        Vector3 center = camera.transform.position;
        float viewHeight = camera.orthographicSize * 2;
        // The light remains distant across the stage while nearer silhouettes slide in front of it.
        float offset = 8.45f - (center.x + 5.7f) * 0.035f;
        float limit = viewHeight * camera.aspect * 0.44f;
        float x = center.x + Mathf.Clamp(offset, -limit, limit);
        float y = center.y + (SunViewportY - 0.5f) * viewHeight - (center.y - 8) * 0.16f;
        float exposure = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(5, 16, DistanceToVista(center.x)));
        exposure *= Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-9, -2, center.y));
        return new Vector4(x, y, viewHeight * 0.065f, exposure);
    }

    internal static Stage2SunFlare Create(Transform parent, SpriteRenderer pillar, SpriteRenderer airship)
    {
        var root = new GameObject("Stage2 Sun Flare") { layer = parent.gameObject.layer, hideFlags = HideFlags.DontSave };
        root.transform.SetParent(parent, false);
        var flare = root.AddComponent<Stage2SunFlare>();
        flare.pillar = pillar;
        flare.airship = airship;
        flare.flareRenderer = root.AddComponent<MeshRenderer>();
        flare.flareRenderer.sortingOrder = -830; // In front of the distant city, behind the back walls and gameplay.
        flare.flareRenderer.shadowCastingMode = ShadowCastingMode.Off;
        flare.flareRenderer.receiveShadows = false;
        flare.flareRenderer.lightProbeUsage = LightProbeUsage.Off;
        flare.flareRenderer.enabled = false;
        flare.properties = new MaterialPropertyBlock();
        flare.mesh = new Mesh { name = "Stage2 Sun Flare", hideFlags = HideFlags.HideAndDontSave };
        flare.mesh.vertices = new Vector3[] { new(-0.5f, -0.5f), new(-0.5f, 0.5f), new(0.5f, 0.5f), new(0.5f, -0.5f) };
        flare.mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
        flare.mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        flare.mesh.RecalculateBounds();
        root.AddComponent<MeshFilter>().sharedMesh = flare.mesh;
        return flare;
    }

    internal void SetView(Camera camera, Vector4 position, Material material, float strength)
    {
        targetCamera = camera;
        sun = position;
        intensity = strength;
        flareRenderer.sharedMaterial = material;
        enabled = true;
    }

    private void LateUpdate()
    {
        if (targetCamera == null || !targetCamera.isActiveAndEnabled)
        {
            flareRenderer.enabled = false;
            return;
        }

        Vector3 position = new Vector3(sun.x, sun.y, transform.position.z);
        Vector3 source = targetCamera.WorldToViewportPoint(position);
        float opening = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(3.25f, 11, DistanceToVista(targetCamera.transform.position.x)));
        float edge = Mathf.Min(Mathf.Min(source.x, 1 - source.x), Mathf.Min(source.y, 1 - source.y));
        float strength = sun.w * intensity * opening * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-0.055f, 0.08f, edge));
        if (source.z <= 0 || strength <= 0.001f)
        {
            flareRenderer.enabled = false;
            return;
        }

        var stone = new Silhouette(pillar, targetCamera, false);
        var ship = new Silhouette(airship, targetCamera, true);
        float coreVisible = Transmission(position, stone, ship);
        float visibility = coreVisible;
        for (int i = 1; i < Samples.Length; i++)
        {
            Vector2 point = (Vector2)position + Samples[i] * sun.z;
            visibility += Transmission(point, stone, ship);
        }
        strength *= visibility / Samples.Length * Mathf.Lerp(0.16f, 1, coreVisible);
        if (strength <= 0.001f)
        {
            flareRenderer.enabled = false;
            return;
        }

        // Center the quad on the projected sun's height while keeping the ghost positions relative to the lens.
        float bandY = source.y - FlareBandHeight * 0.5f;
        Vector3 bottom = targetCamera.ViewportToWorldPoint(new Vector3(0, bandY, source.z));
        Vector3 top = targetCamera.ViewportToWorldPoint(new Vector3(1, bandY + FlareBandHeight, source.z));
        transform.SetPositionAndRotation((bottom + top) * 0.5f, targetCamera.transform.rotation);
        float height = targetCamera.orthographicSize * 2;
        Vector3 parentScale = transform.parent.lossyScale;
        transform.localScale = new Vector3(height * targetCamera.aspect / Mathf.Max(0.001f, Mathf.Abs(parentScale.x)),
            height * FlareBandHeight / Mathf.Max(0.001f, Mathf.Abs(parentScale.y)), 1);
        properties.SetVector(SourceId, new Vector4(source.x, source.y, strength, targetCamera.aspect));
        properties.SetVector(BandId, new Vector4(bandY, FlareBandHeight, 0, 0));
        flareRenderer.SetPropertyBlock(properties);
        flareRenderer.enabled = true;
    }

    internal void Hide()
    {
        enabled = false;
        flareRenderer.enabled = false;
    }

    private void OnDestroy()
    {
        if (mesh != null) Destroy(mesh);
    }

    private static float DistanceToVista(float x) => Mathf.Min(Mathf.Abs(x + 4), Mathf.Abs(x - 118));

    private static float Transmission(Vector2 point, in Silhouette stone, in Silhouette ship) =>
        (1 - stone.Coverage(point)) * (1 - ship.Coverage(point));

    // Approximate the opaque profiles of two known sprites in local space, including the pillar's rotation.
    private readonly struct Silhouette
    {
        private readonly Matrix4x4 toLocal;
        private readonly Bounds bounds;
        private readonly float opacity;
        private readonly bool ship;
        private readonly bool flipX, flipY;

        internal Silhouette(SpriteRenderer sprite, Camera camera, bool isShip)
        {
            this = default;
            if (sprite == null || !sprite.enabled || !sprite.gameObject.activeInHierarchy || sprite.sprite == null ||
                sprite.forceRenderingOff || (camera.cullingMask & (1 << sprite.gameObject.layer)) == 0) return;

            toLocal = sprite.transform.worldToLocalMatrix;
            bounds = sprite.sprite.bounds;
            opacity = sprite.color.a;
            flipX = sprite.flipX;
            flipY = sprite.flipY;
            ship = isShip;
        }

        internal float Coverage(Vector2 world)
        {
            if (opacity <= 0 || bounds.size.x <= 0 || bounds.size.y <= 0) return 0;
            Vector3 p = toLocal.MultiplyPoint3x4(world);
            var uv = new Vector2(((flipX ? -p.x : p.x) - bounds.min.x) / bounds.size.x,
                ((flipY ? -p.y : p.y) - bounds.min.y) / bounds.size.y);
            if (!ship) return opacity * RectangularCoverage(uv, new Vector2(0.18f, 0.01f), new Vector2(0.81f, 0.985f));

            Vector2 envelope = new Vector2((uv.x - 0.51f) / 0.49f, (uv.y - 0.69f) / 0.31f);
            float balloon = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.86f, 1.12f, envelope.sqrMagnitude));
            float gondola = RectangularCoverage(uv, new Vector2(0.14f, 0.045f), new Vector2(0.91f, 0.40f));
            return opacity * Mathf.Max(balloon, gondola);
        }

        private static float RectangularCoverage(Vector2 p, Vector2 min, Vector2 max)
        {
            float edge = Mathf.Min(Mathf.Min(p.x - min.x, max.x - p.x), Mathf.Min(p.y - min.y, max.y - p.y));
            return Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-0.035f, 0.045f, edge));
        }
    }
}
