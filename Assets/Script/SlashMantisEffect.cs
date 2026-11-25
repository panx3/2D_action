using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class SlashMantisEffect : MonoBehaviour
{
    internal const float SizeMultiplier = 3f;
    private const float PlaybackSpeed = 1.5f;

    private SpriteRenderer visual;
    private Sprite[] frames;
    private Vector3 fullScale;
    private Color tint;
    private float duration;
    private float elapsed;

    internal static void Play(Sprite sprite, SpriteRenderer sortReference, Scene scene,
        Vector2 position, int facing, Vector2 worldSize, float lifetime, Sprite[] animationFrames = null, Color? tint = null)
    {
        bool hasAnimation = animationFrames != null && animationFrames.Length > 0 && animationFrames[0] != null;
        if (hasAnimation) sprite = animationFrames[0];
        if (sprite == null) return;

        var instance = new GameObject(sprite.name);
        if (instance.scene != scene)
            SceneManager.MoveGameObjectToScene(instance, scene);
        instance.transform.position = position;
        var effect = instance.AddComponent<SlashMantisEffect>();
        effect.visual = instance.AddComponent<SpriteRenderer>();
        effect.visual.sprite = sprite;
        if (hasAnimation) effect.frames = animationFrames;
        effect.visual.flipX = facing < 0;
        effect.visual.sortingLayerID = sortReference != null ? sortReference.sortingLayerID : 0;
        effect.visual.sortingOrder = sortReference != null ? sortReference.sortingOrder + 1 : 6;
        Vector3 size = sprite.bounds.size;
        Vector2 scaledSize = worldSize * SizeMultiplier;
        effect.fullScale = new Vector3(scaledSize.x / size.x, scaledSize.y / size.y, 1f);
        effect.duration = lifetime;
        effect.tint = tint ?? Color.white;
        effect.ApplyPose(0f);
    }

    private void Update()
    {
        elapsed += Time.deltaTime * PlaybackSpeed;
        if (elapsed >= duration)
        {
            Destroy(gameObject);
            return;
        }
        ApplyPose(elapsed / duration);
    }

    private void ApplyPose(float progress)
    {
        if (frames != null)
        {
            int frame = Mathf.Min(frames.Length - 1, Mathf.FloorToInt(progress * frames.Length));
            if (frames[frame] != null) visual.sprite = frames[frame];
        }
        transform.localScale = fullScale * Mathf.Lerp(0.9f, 1.06f, progress);
        Color color = tint;
        color.a *= 1f - Mathf.InverseLerp(0.2f, 1f, progress);
        visual.color = color;
    }
}
