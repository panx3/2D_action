using UnityEngine;

public class RespawnZone : MonoBehaviour
{
    [Header("Detect Settings")]
    [SerializeField] private string playerTag = "Player";

    [Header("References")]
    [SerializeField] private RespawnController respawnController;

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag(playerTag)) return;

        if (respawnController != null)
        {
            respawnController.Respawn();
        }
    }
}