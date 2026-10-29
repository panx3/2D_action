using UnityEngine;

/// <summary>安定した円範囲と鉄球の Collider を照合する電源。物理状態は変更しない。</summary>
public sealed class TeslaCoil : MonoBehaviour
{
    [SerializeField] private Vector2 center = new Vector2(0f, 2.55f);
    [SerializeField, Min(0.05f)] private float radius = 1f;
    [SerializeField, Min(0f)] private float effectIntensity = 1f;
    [SerializeField] private SpriteRenderer body;
    private ElectricArcEffect arc;
    public bool BallInRange { get; private set; }

    public bool Contains(Rigidbody2D ball)
    {
        if (!isActiveAndEnabled || ball == null || !ball.gameObject.activeInHierarchy || !ball.simulated) return false;
        Vector2 point = transform.TransformPoint(center);
        float worldRadius = radius * Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y));
        foreach (Collider2D c in ball.GetComponentsInChildren<Collider2D>())
            if (c.enabled && c.gameObject.activeInHierarchy && c.attachedRigidbody == ball &&
                (c.ClosestPoint(point) - point).sqrMagnitude <= worldRadius * worldRadius) return true;
        return false;
    }
    public void SetBallInRange(bool value) { BallInRange = value; }
    private void Awake() { arc = gameObject.AddComponent<ElectricArcEffect>(); }
    private void LateUpdate()
    {
        if (body != null) body.color = BallInRange ? new Color(1f, 0.95f, 0.73f) : Color.white;
        arc.DrawBurst(transform.TransformPoint(center), radius * 0.75f, effectIntensity * (BallInRange ? 1.5f : 1f));
    }
    private void OnDisable() { if (arc != null) arc.Hide(); BallInRange = false; }
    private void OnDrawGizmosSelected() { Gizmos.color = Color.yellow; Gizmos.matrix = transform.localToWorldMatrix; Gizmos.DrawWireSphere(center, radius); }
}

