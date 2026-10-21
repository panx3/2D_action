using UnityEngine;

/// <summary>元のドット絵を短時間だけその場に残す、固定数の残像。物理や元Spriteには触れない。</summary>
internal sealed class MotionSpriteEcho
{
    private const int Capacity = 2;
    private readonly SpriteRenderer source;
    private readonly Transform root;
    private readonly SpriteRenderer[] sprites = new SpriteRenderer[Capacity];
    private readonly Vector3[] positions = new Vector3[Capacity];
    private readonly Quaternion[] rotations = new Quaternion[Capacity];
    private readonly Vector3[] scales = new Vector3[Capacity];
    private readonly Color[] colors = new Color[Capacity];
    private readonly float[] remaining = new float[Capacity];
    private readonly float lifetime;
    private readonly float opacity;
    private Vector3 lastSample;
    private float sampleTimer;
    private int next;

    public MotionSpriteEcho(SpriteRenderer source, Transform owner, float lifetime, float opacity)
    {
        this.source = source;
        this.lifetime = Mathf.Max(0.02f, lifetime);
        this.opacity = Mathf.Clamp01(opacity);
        root = new GameObject("Motion Echoes").transform;
        root.SetParent(owner, false);
        for (int i = 0; i < Capacity; i++)
        {
            sprites[i] = new GameObject("Sprite Echo " + i).AddComponent<SpriteRenderer>();
            sprites[i].gameObject.layer = source.gameObject.layer;
            sprites[i].transform.SetParent(root, false);
            sprites[i].enabled = false;
        }
        lastSample = source.transform.position;
    }

    public void Tick(float deltaTime, float strength)
    {
        if (source == null || !source.enabled || source.sprite == null || !source.gameObject.activeInHierarchy)
        {
            Clear();
            return;
        }
        if (deltaTime <= 0f) return;

        for (int i = 0; i < Capacity; i++)
        {
            remaining[i] = Mathf.Max(0f, remaining[i] - deltaTime);
            var echo = sprites[i];
            echo.enabled = remaining[i] > 0f;
            if (!echo.enabled) continue;
            // 親が移動・回転しても、採取した世界座標に残す。
            echo.transform.SetPositionAndRotation(positions[i], rotations[i]);
            Vector3 inherited = root.lossyScale;
            echo.transform.localScale = new Vector3(scales[i].x / NonZero(inherited.x),
                scales[i].y / NonZero(inherited.y), scales[i].z / NonZero(inherited.z));
            Color color = colors[i];
            float fade = remaining[i] / lifetime;
            color.a *= fade * fade;
            echo.color = color;
            echo.sortingLayerID = source.sortingLayerID;
            echo.sortingOrder = source.sortingOrder - 1;
        }

        sampleTimer -= deltaTime;
        if (strength <= 0f)
        {
            sampleTimer = 0f;
            lastSample = source.transform.position;
            return;
        }
        if (sampleTimer > 0f || (source.transform.position - lastSample).sqrMagnitude < 0.0256f)
            return;

        int slot = next;
        next = (next + 1) % Capacity;
        var sample = sprites[slot];
        sample.sprite = source.sprite;
        sample.sharedMaterial = source.sharedMaterial;
        sample.flipX = source.flipX;
        sample.flipY = source.flipY;
        sample.maskInteraction = source.maskInteraction;
        positions[slot] = source.transform.position;
        rotations[slot] = source.transform.rotation;
        scales[slot] = source.transform.lossyScale;
        colors[slot] = source.color * new Color(0.82f, 0.91f, 1f, opacity * Mathf.Clamp01(strength));
        remaining[slot] = lifetime;
        lastSample = source.transform.position;
        sampleTimer = lifetime * 0.5f;
        // 採取したフレームは本体と重なるため、次のフレームから表示する。
        sample.enabled = false;
    }

    public void Clear()
    {
        sampleTimer = 0f;
        for (int i = 0; i < Capacity; i++)
        {
            remaining[i] = 0f;
            if (sprites[i] != null) sprites[i].enabled = false;
        }
        if (source != null) lastSample = source.transform.position;
    }

    public void Dispose()
    {
        if (root != null) Object.Destroy(root.gameObject);
    }

    private static float NonZero(float value) => Mathf.Abs(value) < 0.0001f ? 0.0001f : value;
}
