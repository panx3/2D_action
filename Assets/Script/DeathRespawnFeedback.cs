using System.Collections.Generic;
using UnityEngine;

/// <summary>物理に触れない電子分解／再構築。Sprite複製2枚と固定容量のピクセルMeshを再利用する。</summary>
[DisallowMultipleComponent]
public sealed class DeathRespawnFeedback : MonoBehaviour
{
    [Header("電子ピクセル")]
    [SerializeField] private Color _pink = new Color(1f, 0.18f, 0.58f, 1f);
    [SerializeField] private Color _cyan = new Color(0.2f, 0.94f, 1f, 1f);
    [SerializeField, Min(8f), Tooltip("キャラクターの見た目の高さあたりのVFXピクセル数")]
    private float _pixelsPerHeight = 40f;
    [SerializeField, Range(16, 64)] private int _bodyPixelCount = 44;
    [Header("効果音（任意・未設定なら無音）")]
    [SerializeField] private AudioClip _dissolveSound;
    [SerializeField] private AudioClip _rebuildSound;
    [SerializeField, Range(0f, 1f)] private float _volume = 0.5f;

    private const int Capacity = 192;
    private struct Pixel
    {
        public Vector3 from, to;
        public Vector2 size;
        public Color color;
        public float age, life;
        public bool converge;
    }
    private readonly Pixel[] _pixels = new Pixel[Capacity];
    private readonly Vector3[] _vertices = new Vector3[Capacity * 4];
    private readonly Color[] _colors = new Color[Capacity * 4];
    private readonly List<Renderer> _hidden = new List<Renderer>();
    private readonly List<bool> _wasHidden = new List<bool>();
    private readonly SpriteRenderer[] _ghosts = new SpriteRenderer[2];
    private MaterialPropertyBlock _block;
    private SpriteRenderer _bodySource, _ballSource;
    private Player _player;
    private MorningStarLauncher _launcher;
    private Camera _camera;
    private Transform _root;
    private Mesh _mesh;
    private Material _pixelMaterial, _glitchMaterial;
    private AudioSource _audio;
    private float _pixelSize, _clock, _duration;
    private bool _presenting, _rebuilding, _falling;
    private int _nextPixel;
    private uint _random = 71237;

    public bool IsPresenting => _presenting;
    public bool IsRebuilding => _presenting && _rebuilding;
    public int ActivePixelCount { get; private set; }

    private void EnsureResources(SpriteRenderer source)
    {
        if (_root != null) return;
        _block = new MaterialPropertyBlock();
        _root = new GameObject("Electronic Respawn VFX (pooled)").transform;
        _root.SetParent(transform, false);
        // Meshの頂点はワールド座標。演出管理オブジェクトの位置には追従させない。
        _root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        _root.localScale = Vector3.one;
        var filter = _root.gameObject.AddComponent<MeshFilter>();
        var renderer = _root.gameObject.AddComponent<MeshRenderer>();
        _mesh = new Mesh { name = "Respawn pixel pool" };
        _mesh.MarkDynamic();
        var triangles = new int[Capacity * 6];
        for (int i = 0; i < Capacity; i++)
        {
            int v = i * 4, t = i * 6;
            triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2;
            triangles[t + 3] = v; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
        }
        _mesh.vertices = _vertices;
        _mesh.colors = _colors;
        _mesh.triangles = triangles;
        filter.sharedMesh = _mesh;
        _pixelMaterial = new Material(Shader.Find("Sprites/Default")) { name = "Respawn pixels (Runtime)" };
        _pixelMaterial.mainTexture = Texture2D.whiteTexture;
        renderer.sharedMaterial = _pixelMaterial;
        renderer.sortingLayerID = source.sortingLayerID;
        renderer.sortingOrder = source.sortingOrder + 8;
        var shader = Resources.Load<Shader>("RespawnGlitch");
        if (shader != null) _glitchMaterial = new Material(shader) { name = "Respawn glitch (Runtime)" };
        for (int i = 0; i < _ghosts.Length; i++)
        {
            _ghosts[i] = new GameObject(i == 0 ? "Girl glitch" : "Ball glitch").AddComponent<SpriteRenderer>();
            _ghosts[i].transform.SetParent(_root, false);
            if (_glitchMaterial != null) _ghosts[i].sharedMaterial = _glitchMaterial;
            _ghosts[i].enabled = false;
        }
        _audio = _root.gameObject.AddComponent<AudioSource>();
        _audio.playOnAwake = false;
        _audio.spatialBlend = 0f;
        var routing = source.GetComponentInChildren<AudioSource>();
        if (routing != null) _audio.outputAudioMixerGroup = routing.outputAudioMixerGroup;
    }

    public void PlayDeath(Player player, MorningStarLauncher launcher, bool falling, float duration)
    {
        Clear();
        _player = player;
        _launcher = launcher;
        _bodySource = FindBodySprite(player);
        _ballSource = launcher != null && launcher.BallBody != null
            ? launcher.BallBody.GetComponentInChildren<SpriteRenderer>() : null;
        if (_bodySource == null) return;
        EnsureResources(_bodySource);
        _camera = Camera.main;
        _pixelSize = Mathf.Max(0.015625f, _bodySource.sprite.bounds.size.y * Mathf.Abs(_bodySource.transform.lossyScale.y)
            / Mathf.Max(8f, _pixelsPerHeight));
        HideTree(player.transform);
        if (launcher != null && launcher.BallBody != null) HideTree(launcher.BallBody.transform);
        if (launcher != null && launcher.ChainVisualRoot != null) HideTree(launcher.ChainVisualRoot);
        _falling = falling;
        StartPresentation(false, duration);
        Vector3 center = SpriteCenter(_bodySource);
        if (falling)
        {
            // 本体が画面外でも下端の安全域にだけ撃墜バーストを表示する。
            if (_camera != null)
            {
                // PixelPerfectCameraは描画直前にworldToCameraMatrixを更新する。
                // 死亡判定時に前フレームの行列を使わず、現在の姿勢・表示範囲で端を求める。
                Vector3 local = _camera.transform.InverseTransformPoint(center);
                float halfHeight = _camera.orthographicSize;
                float halfWidth = halfHeight * _camera.aspect;
                local.x = Mathf.Clamp(local.x, -halfWidth * .88f, halfWidth * .88f);
                local.y = Mathf.Clamp(local.y, -halfHeight * .88f, halfHeight * .8f);
                center = _camera.transform.TransformPoint(local);
            }
            for (int i = 0; i < 18; i++)
            {
                Vector3 p = center + new Vector3((Random01() - .5f) * _pixelSize * 14f, Random01() * _pixelSize * 9f, 0f);
                Add(p, p + Vector3.down * _pixelSize * (8f + Random01() * 22f),
                    new Vector2(_pixelSize * (1f + i % 2), _pixelSize * (4f + i % 7)), PickColor(i), .22f + Random01() * .13f);
            }
            Burst(center, _bodyPixelCount, _pixelSize * 11f, false);
            Ring(center, _pixelSize * 13f, .28f, .34f);
        }
        else
        {
            Burst(center, _bodyPixelCount, _pixelSize * 15f, false);
            Ring(center, _pixelSize * 17f, .28f, .7f);
            for (int i = 0; i < 8; i++)
            {
                Vector3 p = center + Vector3.up * _pixelSize * (i - 4) * 3f;
                Add(p, p + Vector3.right * (i % 2 == 0 ? 1 : -1) * _pixelSize * 10f,
                    new Vector2(_pixelSize * (5 + i % 4), _pixelSize), PickColor(i), .16f + i * .012f);
            }
        }
        if (_ballSource != null && Visible(SpriteCenter(_ballSource)))
        {
            Burst(SpriteCenter(_ballSource), 16, _pixelSize * 8f, false);
            Ring(SpriteCenter(_ballSource), _pixelSize * 8f, .23f, .8f);
        }
        ChainPixels(false);
        if (_audio != null && _dissolveSound != null) _audio.PlayOneShot(_dissolveSound, _volume);
        UpdatePresentation();
    }

    public void PlayRebuild(float duration)
    {
        // 鎖登り中は専用の子Spriteを表示するが、Rest復元後は通常Spriteへ戻る。
        if (_player != null) _bodySource = FindBodySprite(_player);
        if (_bodySource == null || _root == null) return;
        StartPresentation(true, duration);
        Burst(SpriteCenter(_bodySource), _bodyPixelCount, _pixelSize * 21f, true);
        if (_ballSource != null) Burst(SpriteCenter(_ballSource), 16, _pixelSize * 9f, true);
        ChainPixels(true);
        if (_audio != null && _rebuildSound != null) _audio.PlayOneShot(_rebuildSound, _volume);
        UpdatePresentation();
    }

    private void StartPresentation(bool rebuilding, float duration)
    {
        _presenting = true;
        _rebuilding = rebuilding;
        _duration = Mathf.Max(.05f, duration);
        _clock = 0f;
        CopySprite(_bodySource, _ghosts[0]);
        CopySprite(_ballSource, _ghosts[1]);
    }

    private void CopySprite(SpriteRenderer source, SpriteRenderer ghost)
    {
        ghost.enabled = source != null && source.sprite != null;
        if (!ghost.enabled) return;
        ghost.sprite = source.sprite;
        ghost.flipX = source.flipX;
        ghost.flipY = source.flipY;
        ghost.color = Color.white;
        ghost.sortingLayerID = source.sortingLayerID;
        ghost.sortingOrder = source.sortingOrder + 3;
        ghost.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
        ghost.transform.localScale = source.transform.lossyScale;
    }

    private void HideTree(Transform root)
    {
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (_hidden.Contains(renderer)) continue;
            _hidden.Add(renderer);
            _wasHidden.Add(renderer.forceRenderingOff);
            renderer.forceRenderingOff = true;
        }
    }

    private void Update()
    {
        if (_root == null || (!_presenting && ActivePixelCount == 0)) return;
        float before = _clock;
        if (_presenting)
        {
            _clock += Time.deltaTime;
            if (_rebuilding && before < _duration * .65f && _clock >= _duration * .65f)
            {
                Ring(SpriteCenter(_bodySource), _pixelSize * 17f, .3f, .55f);
                if (_ballSource != null) Ring(SpriteCenter(_ballSource), _pixelSize * 8f, .24f, .75f);
            }
            UpdatePresentation();
        }
        DrawPixels(Time.deltaTime);
    }

    private void UpdatePresentation()
    {
        float t = Mathf.Clamp01(_clock / _duration);
        for (int i = 0; i < _ghosts.Length; i++)
        {
            var ghost = _ghosts[i];
            if (ghost == null || ghost.sprite == null) continue;
            ghost.enabled = _presenting && (_rebuilding || t < .95f);
            Rect rect = ghost.sprite.textureRect;
            Texture texture = ghost.sprite.texture;
            _block.Clear();
            _block.SetVector("_UVRect", new Vector4(rect.x / texture.width, rect.y / texture.height, rect.width / texture.width, rect.height / texture.height));
            _block.SetVector("_Pixels", new Vector4(rect.width, rect.height, 0f, 0f));
            _block.SetFloat("_Progress", _rebuilding ? 1f - t : Mathf.InverseLerp(.12f, .9f, t));
            _block.SetFloat("_Phase", _clock);
            _block.SetFloat("_Flash", _rebuilding ? Mathf.Max(0f, 1f - Mathf.Abs(t - .75f) / .25f) * .3f : 1f - Mathf.Clamp01(t * 5f));
            _block.SetFloat("_Rebuild", _rebuilding ? Mathf.Sin(t * Mathf.PI) : 0f);
            _block.SetFloat("_Fall", _falling && !_rebuilding ? 1f : 0f);
            ghost.SetPropertyBlock(_block);
        }
    }

    private void Burst(Vector3 center, int count, float radius, bool converge)
    {
        for (int i = 0; i < count; i++)
        {
            float angle = Random01() * Mathf.PI * 2f;
            Vector3 direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
            Vector3 inner = center + direction * radius * Random01() * .25f;
            Vector3 outer = center + direction * radius * (.35f + Random01() * .65f);
            float size = _pixelSize * (i % 4 == 0 ? 2f : 1f);
            Add(converge ? outer : inner, converge ? inner : outer, Vector2.one * size, PickColor(i),
                converge ? _duration * (.6f + Random01() * .4f) : .2f + Random01() * .22f, converge);
        }
    }

    private void Ring(Vector3 center, float radius, float life, float flatten)
    {
        for (int i = 0; i < 24; i++)
        {
            float a = i * Mathf.PI / 12f;
            Vector3 dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a) * flatten, 0f);
            Add(center + dir * radius * .25f, center + dir * radius, Vector2.one * _pixelSize,
                i % 6 == 0 ? Color.white : _cyan, life);
        }
    }

    private void ChainPixels(bool converge)
    {
        if (_launcher == null || _ballSource == null) return;
        Vector3 hand = _launcher.VisualRopeAnchorWorld;
        Vector3 ball = SpriteCenter(_ballSource);
        for (int i = 0; i < 12; i++)
        {
            Vector3 p = Vector3.Lerp(hand, ball, i / 11f);
            if (!converge && !Visible(p)) continue;
            Vector3 other = p + new Vector3(0f, _pixelSize * (i % 2 == 0 ? 4f : -4f), 0f);
            Add(converge ? other : p, converge ? p : other, Vector2.one * _pixelSize, PickColor(i), .22f, converge);
        }
    }

    private bool Visible(Vector3 p)
    {
        if (_camera == null) return true;
        Vector3 local = _camera.transform.InverseTransformPoint(p);
        float halfHeight = _camera.orthographicSize;
        return local.z > 0f && Mathf.Abs(local.x) < halfHeight * _camera.aspect && Mathf.Abs(local.y) < halfHeight;
    }

    // forceRenderingOff中のRenderer.boundsは更新されないことがある。
    // 分解・再構築とも、描画キャッシュから独立した実際のSprite位置を使う。
    private static Vector3 SpriteCenter(SpriteRenderer source)
    {
        Vector3 local = source.sprite != null ? source.sprite.bounds.center : Vector3.zero;
        if (source.flipX) local.x = -local.x;
        if (source.flipY) local.y = -local.y;
        return source.transform.TransformPoint(local);
    }

    private static SpriteRenderer FindBodySprite(Player player)
    {
        var main = player.GetComponent<SpriteRenderer>();
        if (main != null && main.sprite != null) return main;
        SpriteRenderer best = null;
        float height = 0f;
        foreach (var candidate in player.GetComponentsInChildren<SpriteRenderer>())
        {
            if (candidate.sprite == null || !candidate.enabled) continue;
            float size = candidate.sprite.bounds.size.y * Mathf.Abs(candidate.transform.lossyScale.y);
            if (size <= height) continue;
            height = size;
            best = candidate;
        }
        return best;
    }

    private Color PickColor(int i) => i % 7 == 0 ? Color.white : i % 2 == 0 ? _cyan : _pink;
    private float Random01()
    {
        _random ^= _random << 13; _random ^= _random >> 17; _random ^= _random << 5;
        return (_random & 0x00ffffff) / 16777216f;
    }

    private void Add(Vector3 from, Vector3 to, Vector2 size, Color color, float life, bool converge = false)
    {
        _pixels[_nextPixel] = new Pixel { from = from, to = to, size = size, color = color, life = life, converge = converge };
        _nextPixel = (_nextPixel + 1) % Capacity;
    }

    private void DrawPixels(float delta)
    {
        ActivePixelCount = 0;
        for (int i = 0; i < Capacity; i++)
        {
            ref Pixel pixel = ref _pixels[i];
            int v = i * 4;
            if (pixel.life <= 0f || pixel.age >= pixel.life)
            {
                for (int j = 0; j < 4; j++) _vertices[v + j] = Vector3.zero;
                continue;
            }
            ActivePixelCount++;
            pixel.age += delta;
            float t = Mathf.Clamp01(pixel.age / pixel.life);
            float eased = pixel.converge ? t * t : 1f - (1f - t) * (1f - t);
            Vector3 p = Vector3.Lerp(pixel.from, pixel.to, eased);
            p.x = Mathf.Round(p.x / _pixelSize) * _pixelSize;
            p.y = Mathf.Round(p.y / _pixelSize) * _pixelSize;
            Vector2 half = pixel.size * .5f;
            _vertices[v] = p + new Vector3(-half.x, -half.y, 0f);
            _vertices[v + 1] = p + new Vector3(-half.x, half.y, 0f);
            _vertices[v + 2] = p + new Vector3(half.x, half.y, 0f);
            _vertices[v + 3] = p + new Vector3(half.x, -half.y, 0f);
            Color color = pixel.color;
            color.a *= 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.45f, 1f, t));
            for (int j = 0; j < 4; j++) _colors[v + j] = color;
        }
        _mesh.vertices = _vertices;
        _mesh.colors = _colors;
        _mesh.RecalculateBounds();
    }

    public void EndPresentation()
    {
        _presenting = false;
        foreach (var ghost in _ghosts) if (ghost != null) ghost.enabled = false;
        for (int i = 0; i < _hidden.Count; i++) if (_hidden[i] != null) _hidden[i].forceRenderingOff = _wasHidden[i];
        _hidden.Clear();
        _wasHidden.Clear();
    }

    public void Clear()
    {
        EndPresentation();
        System.Array.Clear(_pixels, 0, _pixels.Length);
        _nextPixel = 0;
        if (_mesh != null) DrawPixels(0f);
        if (_audio != null) _audio.Stop();
    }

    private void OnDisable() => Clear();

    private void OnDestroy()
    {
        if (_mesh != null) Destroy(_mesh);
        if (_pixelMaterial != null) Destroy(_pixelMaterial);
        if (_glitchMaterial != null) Destroy(_glitchMaterial);
        if (_root != null) Destroy(_root.gameObject);
    }
}
