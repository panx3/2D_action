using UnityEngine;

/// <summary>Checkpoint確定後の音・紋章・ピクセル光。復帰処理には触れない。</summary>
[DisallowMultipleComponent]
public sealed class CheckpointFeedback : MonoBehaviour
{
    [Header("Activation Assets")]
    [SerializeField] private Sprite activationHalo;
    [SerializeField] private AudioClip activationClip;
    [SerializeField, Range(0f, 1f)] private float activationVolume = 0.65f;

    [Header("Presentation")]
    [SerializeField, Min(0.3f)] private float burstDuration = 1.1f;
    [SerializeField] private Color pink = new Color(1f, 0.24f, 0.65f, 1f);
    [SerializeField] private Color cyan = new Color(0.38f, 0.95f, 1f, 1f);

    private const int QuadCapacity = 128;
    private const int SparkCount = 36;
    private readonly Vector3[] _vertices = new Vector3[QuadCapacity * 4];
    private readonly Color[] _colors = new Color[QuadCapacity * 4];
    private Transform _vfxRoot;
    private Mesh _mesh;
    private Material _material;
    private SpriteRenderer _halo;
    private SpriteRenderer _echo;
    private Checkpoint _checkpoint;
    private Vector3 _crystalCenter;
    private float _unit, _pixel, _clock;
    private int _quadCount;
    private bool _played;

    public int ActivationPlayCount { get; private set; }
    public int SoundPlayCount { get; private set; }
    public bool IsCelebrating => _played && _clock < burstDuration;

    private void Awake()
    {
        _checkpoint = GetComponent<Checkpoint>();
    }

    public void PlayActivation()
    {
        if (_played || !isActiveAndEnabled || _checkpoint == null || !_checkpoint.IsActivated)
            return;

        _played = true;
        _clock = 0f;
        ActivationPlayCount++;
        BuildWorldEffects();
        if (activationVolume > 0f && OneShotAudioUtility.Play2D(
            OneShotAudioUtility.FindWorldImpactSource(), activationClip, activationVolume, transform.position))
            SoundPlayCount++;
        DrawWorldEffects();
    }

    private void Update()
    {
        if (!_played)
            return;
        // ポーズ中は装飾も停止。入力やTime.timeScaleの所有権は持たない。
        if (Time.timeScale <= 0f)
        {
            return;
        }
        _clock += Time.deltaTime;
        DrawWorldEffects();
    }

    private void BuildWorldEffects()
    {
        var source = GetComponent<SpriteRenderer>();
        Bounds bounds = source != null && source.sprite != null
            ? source.sprite.bounds : new Bounds(Vector3.zero, Vector3.one);
        _unit = Mathf.Max(0.1f, bounds.size.y);
        _pixel = _unit / 64f;
        Transform crystal = transform.Find("GlowTopCrystal");
        var crystalRenderer = crystal != null ? crystal.GetComponent<SpriteRenderer>() : null;
        _crystalCenter = crystalRenderer != null
            ? transform.InverseTransformPoint(crystalRenderer.bounds.center)
            : bounds.center + Vector3.up * (_unit * 0.32f);
        _crystalCenter.z = 0f;

        _vfxRoot = new GameObject("Checkpoint Light (Runtime)").transform;
        _vfxRoot.SetParent(transform, false);
        var filter = _vfxRoot.gameObject.AddComponent<MeshFilter>();
        var renderer = _vfxRoot.gameObject.AddComponent<MeshRenderer>();
        _mesh = new Mesh { name = "Checkpoint pixel light" };
        _mesh.MarkDynamic();
        var triangles = new int[QuadCapacity * 6];
        for (int i = 0; i < QuadCapacity; i++)
        {
            int v = i * 4, t = i * 6;
            triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2;
            triangles[t + 3] = v; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
        }
        _mesh.vertices = _vertices;
        _mesh.colors = _colors;
        _mesh.triangles = triangles;
        filter.sharedMesh = _mesh;
        _material = new Material(Shader.Find("Sprites/Default")) { name = "Checkpoint light (Runtime)" };
        _material.mainTexture = Texture2D.whiteTexture;
        renderer.sharedMaterial = _material;
        renderer.sortingLayerID = source != null ? source.sortingLayerID : 0;
        renderer.sortingOrder = (source != null ? source.sortingOrder : 0) + 4;
        if (activationHalo != null)
        {
            _halo = CreateHalo("Activation Sigil", renderer.sortingLayerID, renderer.sortingOrder - 2);
            _echo = CreateHalo("Sigil Echo", renderer.sortingLayerID, renderer.sortingOrder - 1);
        }
    }

    private SpriteRenderer CreateHalo(string name, int layer, int order)
    {
        var renderer = new GameObject(name).AddComponent<SpriteRenderer>();
        renderer.transform.SetParent(_vfxRoot, false);
        renderer.transform.localPosition = _crystalCenter;
        renderer.sprite = activationHalo;
        renderer.sortingLayerID = layer;
        renderer.sortingOrder = order;
        return renderer;
    }

    private void DrawWorldEffects()
    {
        if (_mesh == null) return;
        _quadCount = 0;
        float t = Mathf.Clamp01(_clock / Mathf.Max(0.3f, burstDuration));
        float settle = Mathf.SmoothStep(0f, 1f, t);
        if (_halo != null)
        {
            SetHalo(_halo, Mathf.Lerp(0.25f, 1.7f, 1f - Mathf.Pow(1f - t, 3f)),
                Color.white, Mathf.Clamp01((1f - t) * 2f) * 0.95f);
            float echoT = Mathf.Clamp01((t - 0.13f) / 0.87f);
            SetHalo(_echo, Mathf.Lerp(0.18f, 1.35f, Mathf.SmoothStep(0f, 1f, echoT)),
                Color.white, t > 0.13f ? (1f - echoT) * 0.42f : 0f);
        }
        if (t < 1f)
        {
            // 決定論的な散り方。UnityEngine.Randomの共有状態を変えない。
            for (int i = 0; i < SparkCount; i++)
            {
                float angle = i * 2.399963f;
                float speed = 0.4f + (i % 7) * 0.095f;
                Vector3 direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                Vector3 center = _crystalCenter + direction * (_unit * (0.08f + settle * speed));
                center.y += _unit * t * (0.18f + (i % 3) * 0.09f);
                Color color = i % 5 == 0 ? cyan : i % 3 == 0 ? Color.white : pink;
                color.a *= Mathf.Clamp01((1f - t) * 2.4f);
                float size = _pixel * (i % 4 == 0 ? 2f : 1f);
                AddQuad(center, new Vector2(size, size), color);
                if (i % 6 == 0)
                    AddQuad(center, new Vector2(size * 0.5f, size * 3f), color);
            }
            float flash = Mathf.Clamp01(1f - t * 4.8f);
            Color flashColor = Color.white; flashColor.a = flash * 0.9f;
            AddQuad(_crystalCenter, new Vector2(_unit * 0.65f, _pixel * 1.5f), flashColor);
            AddQuad(_crystalCenter, new Vector2(_pixel * 1.5f, _unit * 0.85f), flashColor);
            Color beamColor = pink; beamColor.a = (1f - t) * 0.22f;
            AddQuad(_crystalCenter + Vector3.up * (_unit * 0.46f),
                new Vector2(_pixel * 3f * (1f - t), _unit * 0.95f), beamColor);
        }

        // 記録後は小さな上昇粒だけを残す。
        for (int i = 0; i < 6; i++)
        {
            float age = Mathf.Repeat(_clock * 0.38f + i / 6f, 1f);
            Color color = i % 2 == 0 ? pink : cyan;
            color.a = Mathf.Sin(age * Mathf.PI) * 0.62f;
            Vector3 center = _crystalCenter + new Vector3(
                Mathf.Sin(i * 2.4f + age * 1.3f) * _unit * 0.22f, (age - 0.3f) * _unit * 0.48f, 0f);
            AddQuad(center, new Vector2(_pixel, _pixel * 2f), color);
        }
        for (int i = _quadCount * 4; i < _vertices.Length; i++)
        {
            _vertices[i] = Vector3.zero;
            _colors[i] = Color.clear;
        }
        _mesh.vertices = _vertices;
        _mesh.colors = _colors;
        _mesh.RecalculateBounds();
    }

    private void SetHalo(SpriteRenderer renderer, float size, Color color, float alpha)
    {
        renderer.enabled = alpha > 0.005f;
        renderer.transform.localScale = Vector3.one * (_unit * size / Mathf.Max(0.01f, activationHalo.bounds.size.y));
        color.a = alpha;
        renderer.color = color;
    }

    private void AddQuad(Vector3 center, Vector2 size, Color color)
    {
        if (_quadCount >= QuadCapacity) return;
        center.x = Mathf.Round(center.x / _pixel) * _pixel;
        center.y = Mathf.Round(center.y / _pixel) * _pixel;
        Vector3 half = new Vector3(size.x * 0.5f, size.y * 0.5f, 0f);
        int v = _quadCount++ * 4;
        _vertices[v] = center + new Vector3(-half.x, -half.y, 0f);
        _vertices[v + 1] = center + new Vector3(-half.x, half.y, 0f);
        _vertices[v + 2] = center + new Vector3(half.x, half.y, 0f);
        _vertices[v + 3] = center + new Vector3(half.x, -half.y, 0f);
        for (int i = 0; i < 4; i++) _colors[v + i] = color;
    }

    private void OnDisable()
    {
        if (_vfxRoot != null) _vfxRoot.gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        if (_vfxRoot != null) _vfxRoot.gameObject.SetActive(true);
    }

    private void OnDestroy()
    {
        if (_mesh != null) Destroy(_mesh);
        if (_material != null) Destroy(_material);
        if (_vfxRoot != null) Destroy(_vfxRoot.gameObject);
    }
}
