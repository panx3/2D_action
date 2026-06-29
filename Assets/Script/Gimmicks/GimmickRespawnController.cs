using UnityEngine;

/// <summary>
/// RespawnZoneや場外落下など、ギミックによる即時位置復帰を管理する。
/// 原則としてHPは回復させず、現在HPを維持したまま指定位置へ戻す。
/// HP0死亡時の全回復リスポーンは DeathRespawnManager が担当する。
/// </summary>
public class GimmickRespawnController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform player;
    [SerializeField] private Rigidbody2D playerRigidbody;
    [SerializeField] private Transform initialRespawnPoint;

    [Header("Optional Morning Star Reset")]
    [SerializeField] private Transform morningstar;
    [SerializeField] private Rigidbody2D morningstarRigidbody;
    [SerializeField] private Vector2 morningstarOffset = new Vector2(-1.5f, 0f);

    private Vector3 currentRespawnPosition;

    private void Awake()
    {
        if (initialRespawnPoint != null)
        {
            currentRespawnPosition = initialRespawnPoint.position;
        }
        else if (player != null)
        {
            currentRespawnPosition = player.position;
        }
    }

    public void SetRespawnPoint(Vector3 position)
    {
        currentRespawnPosition = position;
        Debug.Log("Checkpoint Updated");
    }

    public void Respawn()
    {
        if (player == null) return;

        // 位置復帰のみ。HP は変更しない（全回復は DeathRespawnManager / GoalTrigger 専用）。
        player.position = currentRespawnPosition;

        if (playerRigidbody != null)
        {
            playerRigidbody.linearVelocity = Vector2.zero;
            playerRigidbody.angularVelocity = 0f;
        }

        if (morningstar != null)
        {
            morningstar.position = currentRespawnPosition + (Vector3)morningstarOffset;
        }

        if (morningstarRigidbody != null)
        {
            morningstarRigidbody.linearVelocity = Vector2.zero;
            morningstarRigidbody.angularVelocity = 0f;
        }

        Debug.Log("Player Respawned");
    }
}
