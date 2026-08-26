using System.Collections;
using UnityEngine;

public class SpikeTrap : MonoBehaviour
{
    [Header("Detect Settings")]
    [SerializeField] private string morningStarTag = "morningstar";

    [Header("Damage Settings")]
    [SerializeField] private int damage = 1;

    [SerializeField, Tooltip("棘に当たった時にPlayerへ与えるノックバック")]
    private Vector2 knockbackImpulse = new Vector2(0f, 6f);

    [SerializeField, Tooltip("触れ続けた時に、次のダメージが入るまでの間隔")]
    private float hitCooldown = 0.8f;

    [Header("Debug")]
    [SerializeField] private bool showDebugLog = true;

    private bool canHit = true;

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryHit(other);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        TryHit(other);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        TryHit(collision.collider);
    }

    private void OnCollisionStay2D(Collision2D collision)
    {
        TryHit(collision.collider);
    }

    private void TryHit(Collider2D other)
    {
        if (!canHit || other == null)
            return;

        // 鉄球には反応しない
        if (IsMorningStar(other))
            return;

        // Player本人・子オブジェクトのどちらに触れても取得する
        PlayerHealth playerHealth = other.GetComponentInParent<PlayerHealth>();

        if (playerHealth == null)
            return;

        // 死亡中・無敵時間中は追加ダメージを入れない
        if (playerHealth.IsDead || playerHealth.IsInvincible)
            return;

        canHit = false;

        playerHealth.TakeDamage(damage, knockbackImpulse);

        if (showDebugLog)
        {
            Debug.Log(
                $"SpikeTrap: Player damaged. Damage={damage}",
                this
            );
        }

        StartCoroutine(HitCooldownRoutine());
    }

    private bool IsMorningStar(Collider2D other)
    {
        if (other.CompareTag(morningStarTag))
            return true;

        if (other.attachedRigidbody != null &&
            other.attachedRigidbody.CompareTag(morningStarTag))
        {
            return true;
        }

        return false;
    }

    private IEnumerator HitCooldownRoutine()
    {
        yield return new WaitForSeconds(hitCooldown);
        canHit = true;
    }
}