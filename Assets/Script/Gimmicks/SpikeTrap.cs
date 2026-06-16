using System.Collections;
using UnityEngine;
using UnityEngine.Events;

public class SpikeTrap : MonoBehaviour
{
    [Header("Detect Settings")]
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private string morningStarTag = "morningstar";

    [Header("Hit Settings")]
    [SerializeField] private float hitCooldown = 0.5f;

    [Header("Events")]
    [SerializeField] private UnityEvent onPlayerHit;

    [Header("Debug")]
    [SerializeField] private bool showDebugLog = true;

    private bool canHit = true;

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryHit(other.gameObject);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        TryHit(collision.gameObject);
    }

    private void TryHit(GameObject hitObject)
    {
        if (!canHit) return;

        if (hitObject.CompareTag(morningStarTag))
        {
            return;
        }

        if (!hitObject.CompareTag(playerTag))
        {
            return;
        }

        canHit = false;

        if (showDebugLog)
        {
            Debug.Log("SpikeTrap: Player hit.");
        }

        onPlayerHit?.Invoke();

        StartCoroutine(HitCooldownRoutine());
    }

    private IEnumerator HitCooldownRoutine()
    {
        yield return new WaitForSeconds(hitCooldown);
        canHit = true;
    }
}