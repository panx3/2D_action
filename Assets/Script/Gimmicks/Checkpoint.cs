using UnityEngine;

public class Checkpoint : MonoBehaviour
{
    [Header("Detect Settings")]
    [SerializeField] private string playerTag = "Player";

    [Header("References")]
    [SerializeField] private RespawnController respawnController;
    [SerializeField] private Transform respawnPoint;

    [Header("Visual Settings")]
    [SerializeField] private Color inactiveColor = Color.gray;
    [SerializeField] private Color activeColor = Color.cyan;

    private SpriteRenderer spriteRenderer;
    private bool isActivated = false;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();

        if (respawnPoint == null)
        {
            respawnPoint = transform;
        }

        UpdateVisual();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag(playerTag)) return;
        if (!PlayerColliderUtility.IsPlayerBody(other)) return;

        Activate();
    }

    private void Activate()
    {
        isActivated = true;

        if (respawnController != null)
        {
            respawnController.SetRespawnPoint(respawnPoint.position);
        }

        UpdateVisual();
    }

    private void UpdateVisual()
    {
        if (spriteRenderer == null) return;

        spriteRenderer.color = isActivated ? activeColor : inactiveColor;
    }
}