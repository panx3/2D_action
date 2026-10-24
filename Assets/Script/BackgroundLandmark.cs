using UnityEngine;

/// <summary>ゴールを基準に配置する、終盤で姿を現す描画専用の遠景。</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer), typeof(ParallaxLayer2D))]
[DefaultExecutionOrder(1100)]
public sealed class BackgroundLandmark : MonoBehaviour
{
    [SerializeField] private Camera targetCamera;
    [SerializeField] private Transform player;
    [SerializeField, Tooltip("この地点から見たときに、Scene上の配置と一致します。")]
    private Transform goal;
    [SerializeField, Min(0f), Tooltip("Playerがゴールの何unit手前に来たら見え始めるか。")]
    private float revealDistance = 24f;
    [SerializeField, Min(0f), Tooltip("Playerがゴールの何unit手前で完全に見えるか。")]
    private float fullyVisibleDistance = 8f;

    private SpriteRenderer spriteRenderer;
    private Color originalColor;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        originalColor = spriteRenderer.color;
        if (targetCamera == null) targetCamera = Camera.main;
        if (player == null)
        {
            foreach (var candidate in FindObjectsByType<Player>(FindObjectsSortMode.None))
                if (candidate.gameObject.scene == gameObject.scene) { player = candidate.transform; break; }
        }
        if (goal == null)
        {
            foreach (var candidate in FindObjectsByType<GoalPoint>(FindObjectsSortMode.None))
                if (candidate.gameObject.scene == gameObject.scene) { goal = candidate.transform; break; }
        }

        var parallax = GetComponent<ParallaxLayer2D>();
        if (targetCamera != null)
        {
            if (goal != null)
            {
                Vector3 travel = targetCamera.transform.position - goal.position;
                transform.position += new Vector3(travel.x * parallax.ParallaxX, travel.y * parallax.ParallaxY, 0f);
            }
            parallax.Initialize(targetCamera.transform);
        }
        ApplyVisibility();
    }

    private void OnEnable()
    {
        if (spriteRenderer != null) ApplyVisibility();
    }

    private void LateUpdate() => ApplyVisibility();

    private void ApplyVisibility()
    {
        float visibility = player != null && goal != null
            ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(goal.position.x - revealDistance,
                goal.position.x - fullyVisibleDistance, player.position.x))
            : 1f;
        Color color = originalColor;
        color.a *= visibility;
        spriteRenderer.color = color;
    }

    private void OnDisable()
    {
        if (spriteRenderer != null) spriteRenderer.color = originalColor;
    }

    private void OnValidate()
    {
        fullyVisibleDistance = Mathf.Max(0f, fullyVisibleDistance);
        revealDistance = Mathf.Max(fullyVisibleDistance + 0.01f, revealDistance);
    }
}
