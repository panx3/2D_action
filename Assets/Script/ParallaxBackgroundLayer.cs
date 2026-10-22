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
    [SerializeField, Range(0f, 1f), Tooltip("縦方向の視差の倍率。小さくすると、上昇・下降時の画面内での背景移動を穏やかにします。")]
    private float verticalParallaxMultiplier = 0.25f;
    [SerializeField] private Vector2 screenOffset;
    [SerializeField, Min(1f)] private float coverPadding = 1.05f;
    [SerializeField, Tooltip("左右反転した画像を隣接表示し、横方向の視差移動を止めずに続けます。")]
    private bool loopHorizontally;

    private SpriteRenderer spriteRenderer;
    private Vector3 cameraOrigin;
    private float depth;
    private bool originalFlipX;
    private SpriteRenderer leftCopy;
    private SpriteRenderer rightCopy;
    private float? visualScrollFactor;
    private float lastCoverScale = -1f;
    private Vector2 lastCameraTravel;

    public float ScrollFactor => parallaxFactor;
    public bool LoopsHorizontally => loopHorizontally;
    public void SetVisualScrollFactor(float? factor) => visualScrollFactor = factor;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        originalFlipX = spriteRenderer.flipX;
        if (targetCamera == null || !targetCamera.isActiveAndEnabled)
            targetCamera = Camera.main;

        if (targetCamera == null || spriteRenderer.sprite == null)
        {
            return;
        }

        cameraOrigin = targetCamera.transform.position;
        depth = transform.position.z;
        UpdateCoverScale();
        UpdatePosition();
    }

    private void LateUpdate()
    {
        if (spriteRenderer == null || spriteRenderer.sprite == null)
            return;
        if (targetCamera == null || !targetCamera.isActiveAndEnabled)
        {
            targetCamera = Camera.main;
            if (targetCamera == null) return;
            cameraOrigin = targetCamera.transform.position - (Vector3)lastCameraTravel;
            depth = transform.position.z;
            lastCoverScale = -1f;
        }

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
        if (!Mathf.Approximately(scale, lastCoverScale))
        {
            transform.localScale = new Vector3(scale, scale, 1f);
            lastCoverScale = scale;
        }
    }

    private void UpdatePosition()
    {
        Vector3 cameraPosition = targetCamera.transform.position;
        Vector2 cameraTravel = (Vector2)(cameraPosition - cameraOrigin);
        lastCameraTravel = cameraTravel;
        Vector2 relativeOffset = screenOffset - new Vector2(
            cameraTravel.x * (visualScrollFactor ?? parallaxFactor),
            cameraTravel.y * (visualScrollFactor ?? parallaxFactor) * verticalParallaxMultiplier);

        Vector2 scaledSize = Vector2.Scale(spriteRenderer.sprite.bounds.size, transform.lossyScale);
        float viewHeight = targetCamera.orthographicSize * 2f;
        float viewWidth = viewHeight * targetCamera.aspect;
        float maxX = Mathf.Max(0f, (scaledSize.x - viewWidth) * 0.5f);
        float maxY = Mathf.Max(0f, (scaledSize.y - viewHeight) * 0.5f);
        int centerTile = 0;
        if (loopHorizontally && scaledSize.x > 0.001f)
        {
            // カメラに最も近いタイルを中心にする。折り返し時には左右のコピーと
            // 同じ絵柄・位置に入れ替わるため、移動の停止や画面内のポップを起こさない。
            int wrappedTiles = Mathf.FloorToInt((relativeOffset.x + scaledSize.x * 0.5f) / scaledSize.x);
            relativeOffset.x -= wrappedTiles * scaledSize.x;
            centerTile = -wrappedTiles;
        }
        else
            relativeOffset.x = Mathf.Clamp(relativeOffset.x, -maxX, maxX);
        relativeOffset.y = Mathf.Clamp(relativeOffset.y, -maxY, maxY);

        transform.position = new Vector3(
            cameraPosition.x + relativeOffset.x,
            cameraPosition.y + relativeOffset.y,
            depth);
        UpdateLoopCopies(centerTile);
    }

    private void UpdateLoopCopies(int centerTile)
    {
        bool show = loopHorizontally && spriteRenderer.enabled;
        if (loopHorizontally)
        {
            if (leftCopy == null) leftCopy = CreateLoopCopy("Background Loop Left");
            if (rightCopy == null) rightCopy = CreateLoopCopy("Background Loop Right");
            spriteRenderer.flipX = originalFlipX ^ ((centerTile & 1) != 0);
            ConfigureCopy(leftCopy, -1, centerTile - 1, show);
            ConfigureCopy(rightCopy, 1, centerTile + 1, show);
        }
        else
        {
            spriteRenderer.flipX = originalFlipX;
            if (leftCopy != null) leftCopy.enabled = false;
            if (rightCopy != null) rightCopy.enabled = false;
        }
    }

    private SpriteRenderer CreateLoopCopy(string objectName)
    {
        // 描画専用のコピー2枚だけを使い回す。Scene/Prefabには保存しない。
        var copy = new GameObject(objectName) { hideFlags = HideFlags.DontSave };
        copy.transform.SetParent(transform, false);
        return copy.AddComponent<SpriteRenderer>();
    }

    private void ConfigureCopy(SpriteRenderer copy, int side, int tileIndex, bool show)
    {
        copy.transform.localPosition = new Vector3(side * spriteRenderer.sprite.bounds.size.x, 0f, 0f);
        copy.sprite = spriteRenderer.sprite;
        copy.gameObject.layer = gameObject.layer;
        copy.sharedMaterial = spriteRenderer.sharedMaterial;
        copy.color = spriteRenderer.color;
        copy.sortingLayerID = spriteRenderer.sortingLayerID;
        copy.sortingOrder = spriteRenderer.sortingOrder;
        copy.maskInteraction = spriteRenderer.maskInteraction;
        copy.spriteSortPoint = spriteRenderer.spriteSortPoint;
        copy.flipX = originalFlipX ^ ((tileIndex & 1) != 0);
        copy.flipY = spriteRenderer.flipY;
        copy.enabled = show;
    }

    private void OnDisable()
    {
        if (leftCopy != null) leftCopy.enabled = false;
        if (rightCopy != null) rightCopy.enabled = false;
        if (spriteRenderer != null) spriteRenderer.flipX = originalFlipX;
    }

    private void OnDestroy()
    {
        DestroyCopy(leftCopy);
        DestroyCopy(rightCopy);
    }

    private static void DestroyCopy(SpriteRenderer copy)
    {
        if (copy == null) return;
        if (Application.isPlaying) Destroy(copy.gameObject);
        else DestroyImmediate(copy.gameObject);
    }
}
