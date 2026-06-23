using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Playerが通過した復帰地点を登録するCheckpoint。
/// DeathRespawnManagerには死亡リスポーン用の地点を登録し、
/// GimmickRespawnControllerにはRespawnZone等の即時復帰用地点を登録する。
/// </summary>
public class Checkpoint : MonoBehaviour
{
    [Header("Detect Settings")]
    [SerializeField] private string playerTag = "Player";

    [Header("References")]
    [FormerlySerializedAs("respawnController")]
    [SerializeField] private GimmickRespawnController gimmickRespawnController;
    [FormerlySerializedAs("respawnManager")]
    [SerializeField] private DeathRespawnManager deathRespawnManager;
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
            respawnPoint = transform;

        if (deathRespawnManager == null)
        {
            deathRespawnManager = FindAnyObjectByType<DeathRespawnManager>(FindObjectsInactive.Exclude);
            if (deathRespawnManager != null)
                Debug.LogWarning("[Checkpoint] DeathRespawnManager was auto-found. Assign it in Inspector to avoid wrong references.", this);
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

        if (deathRespawnManager != null)
            deathRespawnManager.RegisterCheckpoint(respawnPoint.position);

        if (gimmickRespawnController != null)
            gimmickRespawnController.SetRespawnPoint(respawnPoint.position);

        UpdateVisual();
    }

    private void UpdateVisual()
    {
        if (spriteRenderer == null) return;

        spriteRenderer.color = isActivated ? activeColor : inactiveColor;
    }
}
