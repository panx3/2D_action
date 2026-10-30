using UnityEngine;

/// <summary>安定した円範囲と鉄球の Collider を照合する電源。物理状態は変更しない。</summary>
public sealed class TeslaCoil : MonoBehaviour
{
    [SerializeField] private Vector2 center = new Vector2(0f, 1.9f);
    [SerializeField, Min(0.05f)] private float radius = 1f;
    [SerializeField, Min(0f)] private float effectIntensity = 1f;
    [SerializeField] private SpriteRenderer body;
    [SerializeField] private AudioClip dischargeSound;
    [SerializeField, Range(0f, 1f)] private float dischargeVolume = 0.5f;
    [SerializeField, Min(0.1f)] private float dischargeMaxDistance = 15f;
    private ElectricArcEffect arc, contactArc;
    private AudioSource dischargeAudio;
    private Transform contactedBall;
    public Vector2 CenterWorld => transform.TransformPoint(center);
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
    public void SetBallInRange(bool value, Transform ball = null) { BallInRange = value; contactedBall = value ? ball : null; }
    private void Awake()
    {
        arc = gameObject.AddComponent<ElectricArcEffect>();
        contactArc = gameObject.AddComponent<ElectricArcEffect>();
        if (dischargeSound != null)
        {
            dischargeAudio = gameObject.AddComponent<AudioSource>();
            dischargeAudio.playOnAwake = false;
            dischargeAudio.clip = dischargeSound;
            dischargeAudio.loop = true;
            dischargeAudio.volume = dischargeVolume;
            dischargeAudio.spatialBlend = 1f;
            dischargeAudio.maxDistance = dischargeMaxDistance;
        }
    }
    private void OnEnable() { if (dischargeAudio != null) dischargeAudio.Play(); }
    private void LateUpdate()
    {
        if (body != null) body.color = BallInRange ? new Color(1f, 0.95f, 0.73f) : Color.white;
        arc.DrawBurst(transform.TransformPoint(center), radius, effectIntensity * (BallInRange ? 1.5f : 1f));
        contactArc.DrawBolt(CenterWorld, contactedBall != null ? (Vector2)contactedBall.position : CenterWorld, BallInRange && contactedBall != null ? effectIntensity : 0f);
    }
    private void OnDisable() { if (dischargeAudio != null) dischargeAudio.Stop(); if (arc != null) arc.Hide(); if (contactArc != null) contactArc.Hide(); if (body != null) body.color = Color.white; BallInRange = false; contactedBall = null; }
    private void OnDrawGizmosSelected() { Gizmos.color = Color.yellow; Gizmos.matrix = transform.localToWorldMatrix; Gizmos.DrawWireSphere(center, radius); }
}

