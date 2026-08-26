using UnityEngine;

/// <summary>
/// Orthographic Cameraを基準に、縦横比を維持したCover表示と穏やかな視差移動を行う背景レイヤー。
/// CameraFollowには手を加えず、このオブジェクト側だけをLateUpdateで追従させる。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
[DefaultExecutionOrder(1000)]
public sealed class ParallaxBackgroundLayer : MonoBehaviour
{
    [SerializeField] private Camera targetCamera;
    [SerializeField, Range(0f, 0.5f)] private float parallaxFactor;
    [SerializeField] private Vector2 screenOffset;
    [SerializeField, Min(1f)] private float coverPadding = 1.05f;

    private SpriteRenderer spriteRenderer;
    private Vector3 cameraOrigin;
    private float depth;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (targetCamera == null)
            targetCamera = Camera.main;

        if (targetCamera == null || spriteRenderer.sprite == null)
        {
            enabled = false;
            return;
        }

        cameraOrigin = targetCamera.transform.position;
        depth = transform.position.z;
        UpdateCoverScale();
        UpdatePosition();
    }

    private void LateUpdate()
    {
        if (targetCamera == null || spriteRenderer == null || spriteRenderer.sprite == null)
            return;

        UpdateCoverScale();
        UpdatePosition();
    }

    private void UpdateCoverScale()
    {
        Vector2 spriteSize = spriteRenderer.sprite.bounds.size;
        float viewHeight = targetCamera.orthographicSize * 2f;
        float viewWidth = viewHeight * targetCamera.aspect;
        float coverScale = Mathf.Max(viewWidth / spriteSize.x, viewHeight / spriteSize.y);
        float scale = coverScale * coverPadding;
        transform.localScale = new Vector3(scale, scale, 1f);
    }

    private void UpdatePosition()
    {
        Vector3 cameraPosition = targetCamera.transform.position;
        Vector2 cameraTravel = (Vector2)(cameraPosition - cameraOrigin);
        Vector2 relativeOffset = screenOffset - cameraTravel * parallaxFactor;

        Vector2 scaledSize = Vector2.Scale(spriteRenderer.sprite.bounds.size, transform.lossyScale);
        float viewHeight = targetCamera.orthographicSize * 2f;
        float viewWidth = viewHeight * targetCamera.aspect;
        float maxX = Mathf.Max(0f, (scaledSize.x - viewWidth) * 0.5f);
        float maxY = Mathf.Max(0f, (scaledSize.y - viewHeight) * 0.5f);
        relativeOffset.x = Mathf.Clamp(relativeOffset.x, -maxX, maxX);
        relativeOffset.y = Mathf.Clamp(relativeOffset.y, -maxY, maxY);

        transform.position = new Vector3(
            cameraPosition.x + relativeOffset.x,
            cameraPosition.y + relativeOffset.y,
            depth);
    }
}
