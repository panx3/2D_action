using UnityEngine;

/// <summary>
/// ゴール到達時に Player の HP を全回復する。チェックポイント位置は変更しない。
/// </summary>
[DisallowMultipleComponent]
public class GoalTrigger : MonoBehaviour
{
    [SerializeField] private bool triggerOnce = true;
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private bool debugLog;

    private bool _triggered;

    private void Awake()
    {
        if (playerHealth == null)
        {
            playerHealth = FindAnyObjectByType<PlayerHealth>(FindObjectsInactive.Exclude);
            if (playerHealth != null)
                Debug.LogWarning("[GoalTrigger] PlayerHealth was auto-found. Assign it in Inspector to avoid wrong references.", this);
            else
                Debug.LogWarning("[GoalTrigger] PlayerHealth is not assigned and could not be found.", this);
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (triggerOnce && _triggered)
            return;

        if (!PlayerColliderUtility.IsPlayerBody(other))
            return;

        _triggered = true;

        if (playerHealth != null)
        {
            playerHealth.ResetToFullHp();

            if (debugLog)
                Debug.Log("[GoalTrigger] Goal reached: HP reset to full.", this);
        }
        else
        {
            Debug.LogWarning("[GoalTrigger] PlayerHealth is not assigned.", this);
        }
    }
}
