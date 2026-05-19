using UnityEngine;

public class RespawnController : MonoBehaviour
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