using UnityEngine;

/// <summary>Stage2の背景と背面壁に重ねる、視差の異なる蒸気・煤の2層。</summary>
internal sealed class IndustrialAtmosphereField : System.IDisposable
{
    private readonly DriftLayer far;
    private readonly DriftLayer near;
    private Camera previousCamera;
    private Vector2 previousPosition;
    private double clock;
    private int motionFrame = -1;

    internal IndustrialAtmosphereField(Transform owner)
    {
        far = new DriftLayer(owner, "Industrial Air - Skyline", -840, 4, 9, 3, 0.32f);
        near = new DriftLayer(owner, "Industrial Air - Back Walls", -74, 2, 3, 1, 0.60f);
    }

    internal void Tick(Camera camera, ScreenVisualProfile.IndustrialAirSettings settings, ScreenVisualProfile.Quality quality)
    {
        if (camera == null || !camera.isActiveAndEnabled || !camera.orthographic)
        {
            Hide();
            return;
        }

        float height = camera.orthographicSize * 2;
        Vector2 size = new Vector2(height * camera.aspect, height);
        if (size.x <= 0 || size.y <= 0)
        {
            Hide();
            return;
        }

        Vector2 center = camera.transform.position;
        Vector2 travel = Vector2.Scale(center - previousPosition, new Vector2(1 / size.x, 1 / size.y));
        if (camera != previousCamera || travel.sqrMagnitude > 1)
            travel = Vector2.zero; // Camera cuts only recenter the field.
        previousCamera = camera;
        previousPosition = center;
        if (motionFrame != Time.frameCount)
        {
            clock += Mathf.Clamp(Time.deltaTime, 0, 0.1f);
            motionFrame = Time.frameCount;
        }

        float density = Mathf.Clamp(settings.particleDensity, 0, 1.5f);
        if (quality == ScreenVisualProfile.Quality.Low) density *= 0.5f;
        else if (quality == ScreenVisualProfile.Quality.Medium) density *= 0.75f;
        float pixel = height / Mathf.Max(1, camera.pixelHeight);
        far.Draw(center, size, travel, pixel, clock, settings, density);
        near.Draw(center, size, travel, pixel, clock, settings, density);
    }

    internal void Hide()
    {
        previousCamera = null;
        far.Hide();
        near.Hide();
    }

    public void Dispose()
    {
        far.Dispose();
        near.Dispose();
    }

    private sealed class DriftLayer : System.IDisposable
    {
        private const int Capacity = 24;
        private const float HorizontalSpan = 1.48f;
        private const float VerticalSpan = 1.22f;
        private static readonly Color SteamGold = new Color32(255, 212, 153, 255);
        private static readonly Color SteamShadow = new Color32(152, 130, 176, 255);
        private static readonly Color Ember = new Color32(255, 185, 108, 255);
        private static readonly Color Soot = new Color32(171, 151, 162, 255);

        private readonly Vector3[] vertices = new Vector3[Capacity * 4];
        private readonly Color32[] colors = new Color32[Capacity * 4];
        private readonly Vector2[] uv = new Vector2[Capacity * 4];
        private readonly Mesh mesh;
        private readonly MeshRenderer renderer;
        private readonly int steamCount, sootCount, emberCount;
        private readonly float parallax;
        private Vector2 offset;
        private int used;

        internal DriftLayer(Transform owner, string name, int sortingOrder, int steamCount, int sootCount, int emberCount,
            float parallax)
        {
            this.steamCount = steamCount;
            this.sootCount = sootCount;
            this.emberCount = emberCount;
            this.parallax = parallax;
            var root = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)) { hideFlags = HideFlags.DontSave };
            root.transform.SetParent(owner, false);
            root.layer = owner.gameObject.layer;
            renderer = root.GetComponent<MeshRenderer>();
            renderer.enabled = false;
            renderer.sortingOrder = sortingOrder;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mesh = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave };
            mesh.MarkDynamic();
            root.GetComponent<MeshFilter>().sharedMesh = mesh;
            var indices = new int[Capacity * 6];
            for (int i = 0; i < Capacity; i++)
            {
                int v = i * 4, t = i * 6;
                indices[t] = v;
                indices[t + 1] = v + 1;
                indices[t + 2] = v + 2;
                indices[t + 3] = v;
                indices[t + 4] = v + 2;
                indices[t + 5] = v + 3;
            }
            mesh.vertices = vertices;
            mesh.triangles = indices;
        }

        internal void Draw(Vector2 center, Vector2 view, Vector2 travel, float pixel, double clock,
            ScreenVisualProfile.IndustrialAirSettings settings, float density)
        {
            offset.x = Mathf.Repeat(offset.x - travel.x * parallax, HorizontalSpan);
            offset.y = Mathf.Repeat(offset.y - travel.y * parallax, VerticalSpan);
            used = 0;
            float light = Mathf.Clamp01(settings.sunsetStrength);
            Color gold = Color.Lerp(SteamShadow, SteamGold, 0.3f + 0.7f * light);
            Color violet = SteamShadow;
            for (int i = 0; i < steamCount && settings.steamOpacity > 0; i++)
            {
                float seed = Mathf.Repeat(i * 0.618034f + parallax, 1);
                Vector2 position = Position(new Vector2(0.17f + i * 0.386f + parallax * 0.28f,
                        0.22f + i * 0.237f + parallax * 0.44f),
                    new Vector2(Mathf.Lerp(0.022f, 0.039f, seed), Mathf.Lerp(0.003f, 0.008f, seed)), clock);
                float wave = Wave(clock, 0.33f, seed * 15);
                position.y += wave * 0.019f;
                float alpha = settings.steamOpacity * Mathf.Lerp(0.8f, 1, seed) * EdgeFade(position);
                gold.a = alpha * (0.92f + 0.08f * wave);
                violet.a = gold.a * 0.66f;
                var size = new Vector2(view.x * Mathf.Lerp(0.29f, 0.44f, seed),
                    view.y * Mathf.Lerp(0.054f, 0.10f, seed));
                Quad(position, view, size, -0.11f + wave * 0.06f, (i + (steamCount == 2 ? 2 : 0)) % 4, gold, violet);
            }
            DrawFlecks(Mathf.RoundToInt(sootCount * density), false, view, pixel, clock);
            DrawFlecks(Mathf.RoundToInt(emberCount * density), true, view, pixel, clock);
            System.Array.Clear(vertices, used * 4, (Capacity - used) * 4);
            renderer.transform.SetPositionAndRotation(new Vector3(center.x, center.y, 0), Quaternion.identity);
            Vector3 parentScale = renderer.transform.parent.lossyScale;
            renderer.transform.localScale = new Vector3(1 / Mathf.Max(0.001f, Mathf.Abs(parentScale.x)),
                1 / Mathf.Max(0.001f, Mathf.Abs(parentScale.y)), 1);
            renderer.sharedMaterial = settings.driftMaterial;
            mesh.vertices = vertices;
            mesh.colors32 = colors;
            mesh.uv = uv;
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(view.x * 1.96f, view.y * 1.4f, 1));
            renderer.enabled = used > 0;
        }

        private void DrawFlecks(int count, bool ember, Vector2 view, float pixel, double clock)
        {
            for (int i = 0; i < count; i++)
            {
                float seed = Mathf.Repeat(i * 0.618034f + parallax + (ember ? 0.18f : 0.48f), 1);
                Vector2 position = Position(new Vector2(0.095f + i * 0.4139f + seed * 0.34f,
                        0.18f + i * 0.717f + parallax),
                    new Vector2(0.012f + 0.018f * seed, (ember ? 0.035f : 0.017f) + 0.014f * seed), clock);
                float wave = Wave(clock, ember ? 1.4f : 0.66f, seed * 9);
                position.x += wave * 0.014f;
                Color tint = ember ? Ember : Soot;
                tint.a = (ember ? 0.72f + wave * 0.20f : 0.58f) * EdgeFade(position);
                float size = pixel * Mathf.Lerp(5, 9, seed);
                Quad(position, view, new Vector2(size * 1.3f, size), seed + wave * 0.45f,
                    ember ? 5 : 4, tint, tint);
            }
        }

        private Vector2 Position(Vector2 start, Vector2 speed, double clock) => new Vector2(
            (float)((start.x + clock * speed.x + offset.x) % HorizontalSpan) - 0.24f,
            (float)((start.y + clock * speed.y + offset.y) % VerticalSpan) - 0.11f);

        private static float EdgeFade(Vector2 position) =>
            Mathf.SmoothStep(0, 1, Mathf.Min(position.x + 0.24f, 1.24f - position.x) / 0.12f) *
            Mathf.SmoothStep(0, 1, Mathf.Min(position.y + 0.11f, 1.11f - position.y) / 0.12f);

        private static float Wave(double clock, float speed, float phase) =>
            Mathf.Sin((float)((clock * speed + phase) % (2 * System.Math.PI)));

        private void Quad(Vector2 position, Vector2 view, Vector2 size, float angle, int cell, Color bottom, Color top)
        {
            int v = used++ * 4;
            Vector2 center = Vector2.Scale(position - Vector2.one * 0.5f, view);
            var axis = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 x = axis * (size.x * 0.5f);
            Vector2 y = new Vector2(-axis.y, axis.x) * (size.y * 0.5f);
            vertices[v] = center - x - y;
            vertices[v + 1] = center - x + y;
            vertices[v + 2] = center + x + y;
            vertices[v + 3] = center + x - y;
            colors[v] = colors[v + 3] = bottom;
            colors[v + 1] = colors[v + 2] = top;
            float u = cell % 2 * 0.5f, t = cell / 2 * 0.25f;
            uv[v] = new Vector2(u, t);
            uv[v + 1] = new Vector2(u, t + 0.25f);
            uv[v + 2] = new Vector2(u + 0.5f, t + 0.25f);
            uv[v + 3] = new Vector2(u + 0.5f, t);
        }

        internal void Hide() => renderer.enabled = false;

        public void Dispose()
        {
            Object.Destroy(mesh);
            if (renderer != null) Object.Destroy(renderer.gameObject);
        }
    }
}
