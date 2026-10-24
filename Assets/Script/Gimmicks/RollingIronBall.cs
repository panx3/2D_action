using UnityEngine;

/// <summary>移動・回転はRigidbody2Dに任せ、十分な接近速度で触れた敵だけを圧殺する。</summary>
[DisallowMultipleComponent, SelectionBase]
[RequireComponent(typeof(Rigidbody2D), typeof(CircleCollider2D))]
public sealed class RollingIronBall : MonoBehaviour
{
    [SerializeField, Min(0.05f), Tooltip("鉄球自身が敵へ向かう速さ。この値以上なら共通の撃破処理を呼ぶ。")]
    private float minimumCrushSpeed = 0.7f;

    private Rigidbody2D body;
    private Vector2 velocityBeforePhysics;
    public float MinimumCrushSpeed => minimumCrushSpeed;

    private void Awake() => body = GetComponent<Rigidbody2D>();
    private void OnEnable()
    {
        velocityBeforePhysics = body != null ? body.linearVelocity : Vector2.zero;
        foreach (var fragment in FindObjectsByType<EnemyFragment>(FindObjectsSortMode.None))
            foreach (var collider in fragment.GetComponentsInChildren<Collider2D>())
                IgnoreFragmentCollision(collider);
    }

    public void IgnoreFragmentCollision(Collider2D fragment)
    {
        if (fragment == null) return;
        foreach (var collider in GetComponentsInChildren<Collider2D>())
            Physics2D.IgnoreCollision(collider, fragment);
    }
    private void FixedUpdate() => velocityBeforePhysics = body.linearVelocity;
    private void OnDisable() => velocityBeforePhysics = Vector2.zero;
    private void OnCollisionEnter2D(Collision2D collision) => TryCrush(collision);
    private void OnCollisionStay2D(Collision2D collision) => TryCrush(collision);

    private void TryCrush(Collision2D collision)
    {
        if (!isActiveAndEnabled || collision.collider == null || collision.contactCount == 0)
            return;
        EnemyHealth enemy = collision.collider.GetComponentInParent<EnemyHealth>();
        if (enemy == null || enemy.IsDeathHandled || enemy.CurrentHp <= 0)
            return;

        for (int i = 0; i < collision.contactCount; i++)
        {
            ContactPoint2D contact = collision.GetContact(i);
            Vector2 towardEnemy = -contact.normal;
            // 敵が静止鉄球へ走り込んだ速度や、空回りだけでの撃破を数えない。
            // 衝突後に速度が落ちても、直前の鉄球の勢いで判定する。
            float approach = Mathf.Max(Vector2.Dot(velocityBeforePhysics, towardEnemy),
                Vector2.Dot(body.linearVelocity, towardEnemy));
            if (approach < minimumCrushSpeed)
                continue;
            enemy.TryCrush(contact.point, towardEnemy * approach);
            return;
        }
    }
}
