using UnityEngine;

/// <summary>生成したドット絵シートを再生する共通VFX。単発・断続ループの両方に対応。</summary>
[DisallowMultipleComponent]
public sealed class EnemySpriteEffect : MonoBehaviour
{
    [SerializeField] private SpriteRenderer visual;
    [SerializeField] private Sprite[] frames;
    [SerializeField, Min(1f)] private float framesPerSecond = 14f;
    [SerializeField] private bool loop;
    [SerializeField, Min(0f)] private float loopGap;
    [SerializeField, Min(0f)] private float startDelay;
    [SerializeField] private Vector2 drift;
    [SerializeField, Min(0f), Tooltip("単発VFXの終わり際を薄くする時間。0は従来のコマ再生のみ。")]
    private float fadeOutDuration;
    private float elapsed;
    private Vector3 start;
    private Color baseColor;

    private void Awake()
    {
        if (visual == null) visual = GetComponent<SpriteRenderer>();
        start = visual != null ? visual.transform.localPosition : Vector3.zero;
        baseColor = visual != null ? visual.color : Color.white;
    }
    private void OnEnable() { elapsed = 0f; Animate(); }
    private void Update() { elapsed += Time.deltaTime; Animate(); }
    private void Animate()
    {
        if (visual == null || frames == null || frames.Length == 0) return;
        float duration = frames.Length / Mathf.Max(1f, framesPerSecond);
        float time = elapsed - startDelay;
        if (time < 0f) { visual.enabled = false; return; }
        if (loop) time %= duration + Mathf.Max(0f, loopGap);
        else if (time >= duration) { Destroy(gameObject); return; }
        visual.enabled = time < duration;
        if (!visual.enabled) return;
        int frame = Mathf.Clamp(Mathf.FloorToInt(time * framesPerSecond), 0, frames.Length - 1);
        visual.sprite = frames[frame];
        Color color = baseColor;
        if (!loop && fadeOutDuration > 0f)
            color.a *= Mathf.Clamp01((duration - time) / Mathf.Min(fadeOutDuration, duration));
        visual.color = color;
        Vector3 offset = (Vector3)(drift * (frame / (float)frames.Length));
        offset.x = Mathf.Round(offset.x * 50f) / 50f; offset.y = Mathf.Round(offset.y * 50f) / 50f;
        visual.transform.localPosition = start + offset;
    }
}
