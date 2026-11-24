using UnityEngine;
using UnityEngine.Rendering;

/// <summary>各煙突の出口を、背景と折り返しコピーの位置に合わせて描画します。</summary>
[DisallowMultipleComponent, RequireComponent(typeof(ParallaxBackgroundLayer)), DefaultExecutionOrder(1050)]
internal sealed class Stage2ChimneySmoke : MonoBehaviour
{
    [SerializeField] private Sprite sourceSprite;
    [SerializeField] private Material smokeMaterial;
    [SerializeField, Tooltip("煙突の口。xy は画像内の正規化位置、z は口の幅を画像幅で割った値。")]
    private Vector3[] outlets = System.Array.Empty<Vector3>();
    [SerializeField, Tooltip("背景に描き込まれた煙の範囲。xy が左下、zw が右上の画像内座標。")]
    private Vector4[] paintedSmoke = System.Array.Empty<Vector4>();
    [SerializeField, Range(0, 1), Tooltip("この赤の値より明るい画素を範囲内で透過します。煙突の縁を残すためのしきい値です。")]
    private float paintedSmokeMinimumRed = 0.60f;
    [SerializeField, Range(0.01f, 0.3f)] private float rise = 0.15f;
    [SerializeField, Range(0, 1)] private float opacity = 0.49f;
    [SerializeField] private Color smokeColor = new Color(0.98f, 0.76f, 0.53f, 1);

    private const int Puffs = 10;
    private const int MaskCapacity = 12;
    private static readonly int SmokeMaskId = Shader.PropertyToID("_SV_PaintedSmoke");
    private static readonly int SmokeRectsId = Shader.PropertyToID("_SV_PaintedSmokeRects");
    private readonly SpriteRenderer[] tiles = new SpriteRenderer[3];
    private readonly Vector4[] maskRects = new Vector4[MaskCapacity];
    private ParallaxBackgroundLayer layer;
    private SpriteRenderer source;
    private Material maskedMaterial;
    private MeshRenderer smokeRenderer;
    private Mesh mesh;
    private Vector3[] vertices;
    private Color32[] colors;
    private double elapsed;
    private bool drawing;

    private void Awake()
    {
        layer = GetComponent<ParallaxBackgroundLayer>();
        source = GetComponent<SpriteRenderer>();
    }

    internal void SetDrawing(bool value, Material backgroundMaterial = null)
    {
        value = value && backgroundMaterial != null && isActiveAndEnabled && source != null && sourceSprite != null && source.sprite == sourceSprite &&
            smokeMaterial != null && smokeMaterial.shader.isSupported && outlets.Length > 0;
        if (!value) backgroundMaterial = null;
        if (value == drawing && backgroundMaterial == maskedMaterial) return;

        if (maskedMaterial != null) maskedMaterial.SetVector(SmokeMaskId, Vector4.zero);
        maskedMaterial = backgroundMaterial;
        drawing = value;
        if (!drawing && smokeRenderer != null) smokeRenderer.enabled = false;
        if (maskedMaterial == null || paintedSmoke.Length == 0) return;

        int count = Mathf.Min(paintedSmoke.Length, MaskCapacity);
        System.Array.Copy(paintedSmoke, maskRects, count);
        float red = QualitySettings.activeColorSpace == ColorSpace.Linear
            ? Mathf.GammaToLinearSpace(paintedSmokeMinimumRed) : paintedSmokeMinimumRed;
        maskedMaterial.SetVectorArray(SmokeRectsId, maskRects);
        maskedMaterial.SetVector(SmokeMaskId, new Vector4(count, red, 0, 0));
    }

    private void LateUpdate()
    {
        if (!drawing) return;
        int count = layer.GetVisibleTiles(tiles);
        if (count == 0)
        {
            if (smokeRenderer != null) smokeRenderer.enabled = false;
            return;
        }
        if (mesh == null) CreateMesh();

        elapsed += Mathf.Min(Time.deltaTime, 0.1f);
        int vertex = 0;
        for (int tileIndex = 0; tileIndex < count; tileIndex++)
        {
            SpriteRenderer tile = tiles[tileIndex];
            if (tile.sprite == null) continue;
            Bounds bounds = tile.sprite.bounds;
            Matrix4x4 tileToLayer = transform.worldToLocalMatrix * tile.transform.localToWorldMatrix;
            for (int i = 0; i < outlets.Length; i++)
            {
                Vector3 outlet = outlets[i];
                Vector3 root = new Vector3(Mathf.Lerp(bounds.min.x, bounds.max.x, outlet.x),
                    Mathf.Lerp(bounds.min.y, bounds.max.y, outlet.y));
                if (tile.flipX) root.x = -root.x;
                if (tile.flipY) root.y = -root.y;
                root = tileToLayer.MultiplyPoint3x4(root);
                float width = bounds.size.x * outlet.z;
                // Repeated tiles use the same phase, so changing the center tile does not reset a plume.
                vertex = DrawPlume(vertex, root, width, bounds.size.y * rise, i);
            }
        }

        System.Array.Clear(vertices, vertex, vertices.Length - vertex);
        mesh.vertices = vertices;
        mesh.colors32 = colors;
        mesh.RecalculateBounds();
        smokeRenderer.enabled = vertex > 0;
    }

    private int DrawPlume(int vertex, Vector3 root, float width, float height, int outlet)
    {
        float period = 6.4f + outlet % 5 * 0.62f;
        for (int puff = 0; puff < Puffs; puff++)
        {
            float age = (float)((elapsed / period + (double)puff / Puffs + outlet * 0.37) % 1);
            float sway = Mathf.Sin((float)(elapsed % 100) * 1.07f + puff * 2.17f + outlet);
            float lift = age * height;
            float drift = age * age * height * 1.8f + sway * lift * 0.19f;
            var center = root + new Vector3(drift, lift + width * 0.11f, 0);
            float size = 1 + sway * age * 0.23f;
            float radiusX = width * Mathf.Lerp(0.59f, 1.95f, age) * size;
            float radiusY = width * Mathf.Lerp(0.39f, 1.18f, age) * size;
            Color tint = Color.Lerp(smokeColor, new Color(0.79f, 0.57f, 0.40f), age * 0.62f);
            tint.a = opacity * Mathf.SmoothStep(0, 1, age * 9f) * (1 - Mathf.SmoothStep(0.36f, 1, age));
            vertices[vertex] = center + new Vector3(-radiusX, -radiusY);
            vertices[vertex + 1] = center + new Vector3(-radiusX, radiusY);
            vertices[vertex + 2] = center + new Vector3(radiusX, radiusY);
            vertices[vertex + 3] = center + new Vector3(radiusX, -radiusY);
            colors[vertex] = colors[vertex + 1] = colors[vertex + 2] = colors[vertex + 3] = tint;
            vertex += 4;
        }
        return vertex;
    }

    private void CreateMesh()
    {
        var item = new GameObject("Chimney Smoke", typeof(MeshRenderer), typeof(MeshFilter)) { hideFlags = HideFlags.DontSave };
        item.transform.SetParent(transform, false);
        item.layer = gameObject.layer;
        smokeRenderer = item.GetComponent<MeshRenderer>();
        smokeRenderer.sortingLayerID = source.sortingLayerID;
        smokeRenderer.sortingOrder = source.sortingOrder + 1;
        smokeRenderer.shadowCastingMode = ShadowCastingMode.Off;
        smokeRenderer.receiveShadows = false;
        smokeRenderer.lightProbeUsage = LightProbeUsage.Off;
        smokeRenderer.sharedMaterial = smokeMaterial;

        int quads = tiles.Length * outlets.Length * Puffs;
        vertices = new Vector3[quads * 4];
        colors = new Color32[quads * 4];
        var uv = new Vector2[vertices.Length];
        var triangles = new int[quads * 6];
        for (int i = 0; i < quads; i++)
        {
            int v = i * 4, t = i * 6;
            // The existing industrial effects atlas contains a soft, pixel-edged steam puff in cell 6.
            uv[v] = new Vector2(0.004f, 0.754f);
            uv[v + 1] = new Vector2(0.004f, 0.996f);
            uv[v + 2] = new Vector2(0.496f, 0.996f);
            uv[v + 3] = new Vector2(0.496f, 0.754f);
            triangles[t] = v;
            triangles[t + 1] = v + 1;
            triangles[t + 2] = v + 2;
            triangles[t + 3] = v;
            triangles[t + 4] = v + 2;
            triangles[t + 5] = v + 3;
        }
        mesh = new Mesh { name = "Stage2 Chimney Smoke", hideFlags = HideFlags.HideAndDontSave };
        mesh.MarkDynamic();
        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.triangles = triangles;
        item.GetComponent<MeshFilter>().sharedMesh = mesh;
    }

    private void OnDisable() => SetDrawing(false);

    private void OnDestroy()
    {
        if (mesh != null) Destroy(mesh);
        if (smokeRenderer != null) Destroy(smokeRenderer.gameObject);
    }
}
