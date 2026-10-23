using UnityEngine;

/// <summary>少コマ風の発光・火花。子のSpriteRendererは後から画像に差し替え可能。</summary>
public sealed class BeamScorpionBurstVfx : MonoBehaviour
{
    [SerializeField, Min(0.02f)] private float lifetime = 0.22f;
    [SerializeField] private bool charge;
    [SerializeField, Min(0f)] private float sparkTravel = 0.5f;
    private SpriteRenderer[] sprites;
    private Vector3[] positions, scales;
    private Color tint;
    private float elapsed;
    public void Play(Color color, Vector2 normal, float duration = -1f)
    {
        tint = color;
        if (duration > 0f) lifetime = duration;
        transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(normal.y, normal.x) * Mathf.Rad2Deg);
        sprites = GetComponentsInChildren<SpriteRenderer>();
        positions = new Vector3[sprites.Length];
        scales = new Vector3[sprites.Length];
        for (int i = 0; i < sprites.Length; i++) { positions[i] = sprites[i].transform.localPosition; scales[i] = sprites[i].transform.localScale; }
        Animate(0f);
    }
    private void Update()
    {
        if (sprites == null) { Play(Color.white, Vector2.right); }
        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / lifetime);
        Animate(t);
        if (elapsed >= lifetime) Destroy(gameObject);
    }
    private void Animate(float t)
    {
        float step = Mathf.Floor(t * 5f) / 5f;
        for (int i = 0; i < sprites.Length; i++)
        {
            Transform item = sprites[i].transform;
            float size = charge ? Mathf.Lerp(0.25f, 1f, step) : Mathf.Lerp(1f, 0.45f, step);
            item.localScale = scales[i] * size;
            Vector3 p = charge ? positions[i] * (1f - step * 0.6f)
                : positions[i] + positions[i].normalized * (step * sparkTravel);
            p.x = Mathf.Round(p.x * 50f) / 50f; p.y = Mathf.Round(p.y * 50f) / 50f;
            item.localPosition = p;
            Color c = i == 0 ? Color.Lerp(tint, Color.white, 0.85f) : tint;
            c.a = (charge ? 0.45f + step * 0.55f : 1f - step) * (i == 1 ? 0.4f : 1f);
            sprites[i].color = c;
        }
    }
}
