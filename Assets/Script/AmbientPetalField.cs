using UnityEngine;

/// <summary>Camera周辺の花びらを固定配列と1枚のMeshで描画。生成は起動時だけ。</summary>
internal sealed class AmbientPetalField : System.IDisposable
{
    private const int Capacity = 48;
    private struct Petal
    {
        public Vector2 position;
        public float seed, depth, age;
    }
    private readonly Petal[] petals = new Petal[Capacity];
    private readonly Vector3[] vertices = new Vector3[Capacity * 4];
    private readonly Color32[] colors = new Color32[Capacity * 4];
    private readonly Vector2[] uvs = new Vector2[Capacity * 4];
    private readonly int[] triangles = new int[Capacity * 6];
    private readonly GameObject root;
    private readonly Mesh mesh;
    private readonly MeshRenderer renderer;
    private Camera previousCamera;
    private Vector2 previousCameraPosition;
    private bool initialized;
    private double clock;

    internal int VisibleCount { get; private set; }

    internal AmbientPetalField(Transform parent, Material material)
    {
        root = new GameObject("Screen Visual Pink Petals", typeof(MeshFilter), typeof(MeshRenderer)) { hideFlags = HideFlags.DontSave };
        root.transform.SetParent(parent, false);
        renderer = root.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.sortingOrder = -650; // In front of backgrounds/dust, behind terrain, characters and HUD.
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        mesh = new Mesh { name = "Ambient Pink Petals", hideFlags = HideFlags.HideAndDontSave };
        mesh.MarkDynamic();
        root.GetComponent<MeshFilter>().sharedMesh = mesh;
        for (int i = 0; i < Capacity; i++)
        {
            int v = i * 4, t = i * 6;
            triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2;
            triangles[t + 3] = v; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
            int variant = i % 4;
            float x = (variant % 2) * 0.5f, y = (variant / 2) * 0.5f;
            // New atlas was composed with each petal occupying most of a cell.
            uvs[v] = new Vector2(x, y); uvs[v + 1] = new Vector2(x, y + 0.5f);
            uvs[v + 2] = new Vector2(x + 0.5f, y + 0.5f); uvs[v + 3] = new Vector2(x + 0.5f, y);
        }
        mesh.vertices = vertices; mesh.colors32 = colors; mesh.uv = uvs; mesh.triangles = triangles;
        renderer.enabled = false;
    }

    internal void Tick(Camera camera, ScreenVisualProfile.PetalSettings settings, ScreenVisualProfile.Quality quality, bool active, float deltaTime)
    {
        if (!active || settings == null || !settings.enabled || camera == null || !camera.orthographic || settings.count <= 0 || settings.opacity <= 0)
        { Hide(); return; }
        float height = camera.orthographicSize * 2;
        float width = height * camera.aspect;
        if (height <= 0 || width <= 0) { Hide(); return; }
        Vector2 cameraPosition = camera.transform.position;
        Vector2 travel = initialized ? cameraPosition - previousCameraPosition : Vector2.zero;
        bool reset = !initialized || camera != previousCamera || travel.sqrMagnitude > height * height * 4;
        if (reset)
        {
            for (int i = 0; i < Capacity; i++)
            {
                float seed = Mathf.Repeat(i * 0.618034f + 0.29f, 1);
                petals[i] = new Petal
                {
                    seed = seed, depth = Mathf.Repeat(i * 0.381966f + 0.13f, 1), age = 3,
                    position = new Vector2((Mathf.Repeat(i * 0.754877f + 0.17f, 1) - 0.5f) * width * 1.12f,
                        (Mathf.Repeat(i * 0.569841f + 0.31f, 1) - 0.5f) * height * 1.12f)
                };
            }
            travel = Vector2.zero;
            initialized = true;
        }
        previousCamera = camera; previousCameraPosition = cameraPosition;
        float dt = Mathf.Clamp(deltaTime, 0, 0.1f);
        clock += dt;
        // Camera-normalized sizes retain the generated pixel silhouettes at any view width.
        float pixel = height / Mathf.Max(1, camera.pixelHeight);
        int count = Mathf.Clamp(Mathf.RoundToInt(settings.count * (quality == ScreenVisualProfile.Quality.Low ? 0.4f : quality == ScreenVisualProfile.Quality.Medium ? 0.7f : 1)), 0, Capacity);
        int visible = 0;
        float xLimit = width * 0.65f, yLimit = height * 0.65f;
        float gust = 1 + 0.2f * Wave(0.37f, 0) + 0.12f * Wave(0.79f, 2);
        for (int i = 0; i < Capacity; i++)
        {
            int v = i * 4;
            if (i >= count)
            {
                vertices[v] = vertices[v + 1] = vertices[v + 2] = vertices[v + 3] = Vector3.zero;
                colors[v] = colors[v + 1] = colors[v + 2] = colors[v + 3] = Color.clear;
                continue;
            }
            Petal p = petals[i];
            float speed = Mathf.Lerp(0.6f, 1.25f, p.depth);
            float frequency = Mathf.Lerp(1.1f, 1.85f, p.seed);
            float phase = p.seed * 31;
            p.position -= travel * Mathf.Lerp(0.24f, 0.62f, p.depth);
            p.position += new Vector2(settings.windSpeed * speed * gust,
                -settings.fallSpeed * speed + Wave(frequency * 0.73f, phase * 0.73f) * settings.flutter * 0.28f) * dt;
            bool wrapped = false;
            if (p.position.x > xLimit) { p.position.x -= xLimit * 2; wrapped = true; }
            else if (p.position.x < -xLimit) { p.position.x += xLimit * 2; wrapped = true; }
            if (p.position.y < -yLimit) { p.position.y += yLimit * 2; wrapped = true; }
            else if (p.position.y > yLimit) { p.position.y -= yLimit * 2; wrapped = true; }
            p.age = wrapped ? 0 : p.age + dt;
            Vector2 center = p.position + new Vector2(Wave(frequency, phase) * 0.2f, Wave(frequency * 0.71f, phase * 0.71f) * 0.18f) * settings.flutter;
            float marginX = (xLimit - Mathf.Abs(center.x)) / Mathf.Max(0.001f, width * 0.12f);
            float marginY = (yLimit - Mathf.Abs(center.y)) / Mathf.Max(0.001f, height * 0.12f);
            float alpha = settings.opacity * Mathf.Lerp(0.52f, 1, p.depth) * Mathf.SmoothStep(0, 1, Mathf.Min(marginX, marginY)) * Mathf.SmoothStep(0, 1, p.age);
            float size = settings.sizePixels * pixel * Mathf.Lerp(0.65f, 1.25f, p.depth);
            float angle = p.seed * 2.8f - 1.4f + Wave(frequency * 0.55f, phase * 0.55f) * settings.flutter;
            Vector2 axis = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            // Alternating broad/edge-on silhouettes read as petals turning in the breeze.
            float turn = Mathf.Lerp(0.2f, 1, Mathf.Abs(Wave(frequency * 0.85f, phase * 0.85f + Mathf.PI * 0.5f)));
            Vector2 halfX = axis * (size * 0.5f * turn);
            Vector2 halfY = new Vector2(-axis.y, axis.x) * (size * 0.5f);
            vertices[v] = center - halfX - halfY; vertices[v + 1] = center - halfX + halfY;
            vertices[v + 2] = center + halfX + halfY; vertices[v + 3] = center + halfX - halfY;
            Color tint = settings.color; tint.a *= alpha;
            colors[v] = colors[v + 1] = colors[v + 2] = colors[v + 3] = tint;
            if (alpha > 0.1f && Mathf.Abs(center.x) < width * 0.5f && Mathf.Abs(center.y) < height * 0.5f) visible++;
            petals[i] = p;
        }
        VisibleCount = visible;
        root.transform.position = new Vector3(cameraPosition.x, cameraPosition.y, 1);
        root.transform.rotation = Quaternion.identity;
        // The scene owner normally has unit scale, but do not let its scale deform the field.
        Vector3 parentScale = root.transform.parent.lossyScale;
        root.transform.localScale = new Vector3(1 / Mathf.Max(0.001f, Mathf.Abs(parentScale.x)), 1 / Mathf.Max(0.001f, Mathf.Abs(parentScale.y)), 1);
        mesh.vertices = vertices; mesh.colors32 = colors;
        mesh.bounds = new Bounds(Vector3.zero, new Vector3(width * 1.5f, height * 1.5f, 2));
        renderer.enabled = true;
    }

    // Keep long idle sessions continuous; reduce each phase independently rather
    // than resetting the whole wind clock and visibly snapping every petal.
    private float Wave(float frequency, float phase) => Mathf.Sin((float)((clock * frequency + phase) % (System.Math.PI * 2)));

    internal void Hide() { renderer.enabled = false; VisibleCount = 0; }
    public void Dispose()
    {
        if (Application.isPlaying) { Object.Destroy(mesh); Object.Destroy(root); }
        else { Object.DestroyImmediate(mesh); Object.DestroyImmediate(root); }
    }
}
