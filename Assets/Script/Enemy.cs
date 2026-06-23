using UnityEngine;

/// <summary>
/// 敵の簡易横移動。被弾は EnemyHealth が担当。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(EnemyHealth))]
public class Enemy : MonoBehaviour
{
    [SerializeField, Header("移動速度")]
    private float _moveSpeed = 5f;

    [SerializeField, Header("接触ダメージ")]
    private int _contactDamage = 1;
    [SerializeField] private float _contactKnockback = 5f;
    [SerializeField] private float _contactCooldown = 0.7f;

    private Rigidbody2D _rigid;
    private EnemyHealth _health;
    private float _lastContactDamageTime = -999f;

    private void Awake()
    {
        _rigid = GetComponent<Rigidbody2D>();
        _health = GetComponent<EnemyHealth>();

        if (_rigid != null)
        {
            _rigid.freezeRotation = true;
            _rigid.angularVelocity = 0f;
        }
    }

    private void FixedUpdate()
    {
        if (_rigid == null)
            return;

        _rigid.freezeRotation = true;
        _rigid.angularVelocity = 0f;

        if (_health != null && _health.IsHitStunned)
            return;

        _rigid.linearVelocity = new Vector2(-_moveSpeed, _rigid.linearVelocity.y);
        TryContactDamageOverlap();
    }

    private void OnCollisionEnter2D(Collision2D collision) => TryContactDamage(collision.collider);

    private void OnCollisionStay2D(Collision2D collision) => TryContactDamage(collision.collider);

    private void TryContactDamageOverlap()
    {
        if (_contactDamage <= 0)
            return;

        Collider2D body = GetComponent<Collider2D>();
        if (body == null)
            return;

        Bounds bounds = body.bounds;
        Vector2 size = bounds.size * 1.05f;
        Collider2D hit = Physics2D.OverlapBox(
            bounds.center,
            size,
            0f,
            LayerMask.GetMask("player"));

        if (hit != null)
            TryContactDamage(hit);
    }

    private void TryContactDamage(Collider2D other)
    {
        if (!PlayerColliderUtility.IsPlayerBody(other))
            return;
        if (_health != null && _health.CurrentHp <= 0)
            return;
        if (Time.time - _lastContactDamageTime < _contactCooldown)
            return;

        PlayerHealth playerHealth = other.GetComponentInParent<PlayerHealth>();
        if (playerHealth == null)
            playerHealth = other.GetComponent<PlayerHealth>();
        if (playerHealth == null || playerHealth.IsDead || playerHealth.IsInvincible)
            return;

        Vector2 knockDir = (Vector2)(other.transform.position - transform.position);
        if (knockDir.sqrMagnitude < 0.0001f)
            knockDir = Vector2.right;
        else
            knockDir.Normalize();

        playerHealth.TakeDamage(_contactDamage, knockDir * _contactKnockback);
        _lastContactDamageTime = Time.time;
    }
}
