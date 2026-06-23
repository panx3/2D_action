using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Playerが場外や危険領域に入ったとき、現在HPを維持したまま
/// GimmickRespawnControllerの復帰地点へ戻すトリガー。
/// HP0死亡時の全回復処理は行わない。
/// </summary>
public class RespawnZone : MonoBehaviour
{
    [Header("Detect Settings")]
    [SerializeField] private string playerTag = "Player";

    [Header("References")]
    [FormerlySerializedAs("respawnController")]
    [SerializeField] private GimmickRespawnController gimmickRespawnController;

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag(playerTag)) return;
        if (!PlayerColliderUtility.IsPlayerBody(other)) return;

        if (gimmickRespawnController != null)
        {
            gimmickRespawnController.Respawn();
        }
    }
}
