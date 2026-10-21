using UnityEngine;

/// <summary>描画専用の前景。視差移動は既存のParallaxLayer2Dへ委譲する。</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer), typeof(ParallaxLayer2D))]
[DefaultExecutionOrder(1100)]
public sealed class ForegroundElement : MonoBehaviour
{
    public enum MovementMode { WorldFixed, Parallax }

    [Header("Rendering")]
    [SerializeField, Tooltip("既存UIの順序を保つため、標準PrefabはDefaultを使用します。")]
    private string sortingLayerName = "Default";
    [SerializeField] private int sortingOrder = 30;
    [SerializeField] private Color tint = new Color(0.86f, 0.88f, 0.94f, 1f);
    [SerializeField, Range(0f, 1f)] private float opacity = 1f;

    [Header("Movement")]
    [SerializeField, Tooltip("地面に接する柱はWorldFixed推奨。Parallaxは接地しない装飾向けです。")]
    private MovementMode movement = MovementMode.Parallax;
    [SerializeField, Tooltip("未指定なら、このSceneで使われるMain Cameraを取得します。")]
    private Camera targetCamera;
    [SerializeField, Range(0f, 2f), Tooltip("画面上の移動倍率。1=ワールド固定、1超=カメラ手前。Parallax時だけ使用。")]
    private float horizontalMultiplier = 1.25f;
    [SerializeField, Range(0f, 2f)] private float verticalMultiplier = 1f;

    [Header("Visibility")]
    [SerializeField, Tooltip("Playerの範囲と重なる間だけ薄くします。衝突判定は作成しません。")]
    private bool fadeWhenPlayerCovered = true;
    [SerializeField, Range(0f, 1f)] private float coveredOpacity = 0.6f;
    [SerializeField, Min(0.01f)] private float fadeDuration = 0.15f;

    private SpriteRenderer spriteRenderer;
    private ParallaxLayer2D parallax;
    private Collider2D playerBoundsSource;
    private float coverageAlpha = 1f;

    private void OnEnable() => ApplySettings();

    private void Start()
    {
        foreach (Player player in FindObjectsByType<Player>(FindObjectsSortMode.None))
        {
            if (player.gameObject.scene != gameObject.scene) continue;
            playerBoundsSource = player.GetComponent<Collider2D>();
            break;
        }
    }

    private void LateUpdate()
    {
        if (!fadeWhenPlayerCovered || spriteRenderer == null || playerBoundsSource == null)
            return;

        Bounds prop = spriteRenderer.bounds;
        Bounds player = playerBoundsSource.bounds;
        bool overlaps = prop.min.x < player.max.x && prop.max.x > player.min.x &&
            prop.min.y < player.max.y && prop.max.y > player.min.y;
        coverageAlpha = Mathf.MoveTowards(coverageAlpha, overlaps ? coveredOpacity : 1f,
            (1f - coveredOpacity) * Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeDuration));
        ApplyColor();
    }

    private void OnDisable()
    {
        if (parallax != null) parallax.enabled = false;
    }

    private void OnValidate()
    {
        opacity = Mathf.Clamp01(opacity);
        horizontalMultiplier = Mathf.Clamp(horizontalMultiplier, 0f, 2f);
        verticalMultiplier = Mathf.Clamp(verticalMultiplier, 0f, 2f);
        coveredOpacity = Mathf.Clamp01(coveredOpacity);
        fadeDuration = Mathf.Max(0.01f, fadeDuration);
        ApplySettings();
    }

    [ContextMenu("Apply Foreground Settings")]
    public void ApplySettings()
    {
        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
        if (parallax == null) parallax = GetComponent<ParallaxLayer2D>();
        if (spriteRenderer == null || parallax == null) return;

        spriteRenderer.sortingLayerName = sortingLayerName;
        spriteRenderer.sortingOrder = sortingOrder;
        coverageAlpha = 1f;
        ApplyColor();

        parallax.ParallaxX = 1f - horizontalMultiplier;
        parallax.ParallaxY = 1f - verticalMultiplier;
        // 固定配置では視差処理もCamera参照も不要。
        bool animate = Application.isPlaying && isActiveAndEnabled &&
            movement == MovementMode.Parallax &&
            (horizontalMultiplier != 1f || verticalMultiplier != 1f);
        Camera camera = animate ? (targetCamera != null ? targetCamera : Camera.main) : null;
        parallax.Initialize(camera != null ? camera.transform : null);
        parallax.enabled = animate && camera != null;
    }

    private void ApplyColor()
    {
        Color color = tint;
        color.a *= opacity * coverageAlpha;
        spriteRenderer.color = color;
    }
}
