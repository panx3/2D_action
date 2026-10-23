using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Playerが場外や危険領域に入ったとき、落下専用演出を経て復帰する。
/// HPペナルティは既存の復帰処理へ委譲する。
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
