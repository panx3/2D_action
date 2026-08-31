using UnityEngine;
using UnityEngine.Events;

public class GoalPoint : MonoBehaviour
{
    [Header("Detect Settings")]
    [SerializeField] private string playerTag = "Player";

    [Header("Goal Settings")]
    [SerializeField] private bool requireMorningStarNearby = false;
    [SerializeField] private Transform morningStar;
    [SerializeField] private float morningStarRequiredDistance = 3f;
    [SerializeField] private bool oneShot = true;

    [Header("Visual Settings")]
    [SerializeField] private Color idleColor = Color.white;
    [SerializeField] private Color clearedColor = Color.green;
    [SerializeField] private Color lockedColor = Color.red;

    [Header("Events")]
    [SerializeField] private UnityEvent onGoalReached;

    [Header("Goal UI")]
    [SerializeField] private GoalMenuController goalMenu;

    [Header("Debug")]
    [SerializeField] private bool showDebugLog = true;

    private SpriteRenderer spriteRenderer;
    private bool isCleared = false;

    public bool IsCleared => isCleared;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (goalMenu == null)
            goalMenu = FindAnyObjectByType<GoalMenuController>(FindObjectsInactive.Include);
        UpdateVisual(idleColor);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (oneShot && isCleared) return;
        if (!other.CompareTag(playerTag)) return;

        TryReachGoal();
    }

    private void TryReachGoal()
    {
        if (requireMorningStarNearby && !IsMorningStarNearby())
        {
            UpdateVisual(lockedColor);

            if (showDebugLog)
            {
                Debug.Log("GoalPoint: MorningStar is too far.");
            }

            return;
        }

        ReachGoal();
    }

    private bool IsMorningStarNearby()
    {
        if (morningStar == null) return false;

        float distance = Vector2.Distance(transform.position, morningStar.position);
        return distance <= morningStarRequiredDistance;
    }

    private void ReachGoal()
    {
        isCleared = true;
        UpdateVisual(clearedColor);

        if (showDebugLog)
        {
            Debug.Log("GoalPoint: Stage Clear!");
        }

        if (goalMenu != null)
            goalMenu.ShowGoal();
        else
            Debug.LogWarning("[GoalPoint] GoalMenuControllerがSceneにありません。", this);

        onGoalReached?.Invoke();
    }

    private void UpdateVisual(Color color)
    {
        if (spriteRenderer == null) return;
        spriteRenderer.color = color;
    }
}
