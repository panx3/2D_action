using UnityEngine;

/// <summary>安定した円範囲と鉄球の Collider を照合する電源。物理状態は変更しない。</summary>
public sealed class TeslaCoil : MonoBehaviour
{
    internal static readonly System.Collections.Generic.List<TeslaCoil> ActiveSources = new System.Collections.Generic.List<TeslaCoil>();
    [SerializeField] private Vector2 center = new Vector2(0f, 1.9f);
    [SerializeField, Min(0.05f)] private float radius = 1f;
    [SerializeField, Min(0f)] private float effectIntensity = 1f;
    [SerializeField] private SpriteRenderer body;
    [SerializeField] private AudioClip dischargeSound;
    [SerializeField, Range(0f, 1f)] private float dischargeVolume = 0.5f;
    [SerializeField, Min(0.1f)] private float dischargeMaxDistance = 15f;
    [Header("鉄球の弱い保持（力の適用はMorningStarLauncherが所有）")]
    [SerializeField] private bool holdEnabled = true;
    [SerializeField, Tooltip("コイル下側の保持目標。放電範囲の中心とは独立しています")]
    private Vector2 holdPoint = new Vector2(0f, 0.85f);
    [SerializeField, Min(0f)] private float captureSpeed = 2f;
    [SerializeField, Min(0f)] private float holdSpring = 4f;
    [SerializeField, Min(0f)] private float holdDamping = 3f;
    [SerializeField, Min(0f)] private float holdGravityLimit = 1.25f;
    private ElectricArcEffect arc, contactArc;
    private AudioSource dischargeAudio;
    private Transform contactedBall;
    private SpriteRenderer contactedBallVisual;
    public Vector2 CenterWorld => transform.TransformPoint(center);
    public Vector2 HoldPointWorld => transform.TransformPoint(holdPoint);
    public bool BallInRange { get; private set; }
    public bool HoldEnabled => holdEnabled && isActiveAndEnabled;
    public float CaptureSpeed => captureSpeed;

    public Vector2 GetHoldAcceleration(Rigidbody2D ball)
    {
        Vector2 gravity = Physics2D.gravity * ball.gravityScale;
        Vector2 acceleration = (HoldPointWorld - ball.position) * holdSpring - ball.linearVelocity * holdDamping - gravity;
        return Vector2.ClampMagnitude(acceleration, gravity.magnitude * holdGravityLimit);
    }

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
    public void SetBallInRange(bool value, Transform ball = null)
    {
        BallInRange = value;
        if (contactedBall != ball) contactedBallVisual = ball != null ? ElectricArcEffect.BallVisual(ball.GetComponent<Rigidbody2D>()) : null;
        contactedBall = value ? ball : null;
    }
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
    private void OnEnable() { if (!ActiveSources.Contains(this)) ActiveSources.Add(this); if (dischargeAudio != null) dischargeAudio.Play(); }
    private void LateUpdate()
    {
        if (body != null) body.color = BallInRange ? new Color(1f, 0.95f, 0.73f) : Color.white;
        arc.DrawOutline(body, effectIntensity * (BallInRange ? 1.5f : 1f), CenterWorld,
            BallInRange ? (Vector2?)HoldPointWorld : null, 0.6f);
        contactArc.DrawBetweenOutlines(body, contactedBallVisual, BallInRange && contactedBall != null ? effectIntensity : 0f);
    }
    private void OnDisable() { ActiveSources.Remove(this); if (dischargeAudio != null) dischargeAudio.Stop(); if (arc != null) arc.Hide(); if (contactArc != null) contactArc.Hide(); if (body != null) body.color = Color.white; BallInRange = false; contactedBall = null; }
    private void OnDrawGizmosSelected() { Gizmos.color = Color.yellow; Gizmos.matrix = transform.localToWorldMatrix; Gizmos.DrawWireSphere(center, radius); }
}

