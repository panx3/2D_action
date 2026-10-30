using System.Collections.Generic;
using UnityEngine;

/// <summary>Kinematicのプレスのみを動かす。鉄球・Player・鎖のStateと物理設定は変更しない。</summary>
[DisallowMultipleComponent]
public sealed class FallingPressController : MonoBehaviour
{
    public enum CycleState { TopWait, Warning, Descending, BottomWait, Rising, BallBlocked, StandBlocked }
    [Header("Pair / geometry (root = floor, head pivot = underside)")]
    [SerializeField, Tooltip("配置確認用の任意参照。停止判定はこの参照に依存せず、接触した台座・鉄球を使います。")]
    private PressBallStandController ballStand;
    [SerializeField] private Rigidbody2D head;
    [SerializeField] private BoxCollider2D pressCollider;
    [SerializeField] private BoxCollider2D damageDetector;
    [SerializeField] private SpriteRenderer body;
    [SerializeField] private SpriteRenderer warningLight;
    [SerializeField] private Transform[] pistons;
    [SerializeField, Min(2f)] private float width = 7f;
    [SerializeField, Min(0.2f)] private float bodyHeight = 2f;
    [SerializeField, Min(0.5f)] private float travelDistance = 6f;
    [Header("Cycle")]
    [SerializeField, Min(0.1f)] private float descentSpeed = 12f;
    [SerializeField, Min(0.1f)] private float ascentSpeed = 2.5f;
    [SerializeField, Min(0f)] private float topWait = 2f;
    [SerializeField, Min(0f)] private float bottomWait = 1.8f;
    [SerializeField, Min(0.01f)] private float warningTime = 0.7f;
    [SerializeField, Range(0f, 1f), Tooltip("通常サイクルの開始位置。複数台の時差用")]
    private float startPhase;
    [Header("Physical support")]
    [SerializeField, Min(0.1f), Tooltip("床からプレス下面までに必要な高さ。Player実寸3.3 + 余裕")]
    private float requiredPlayerClearance = 3.5f;
    [SerializeField, Min(0f)] private float contactSkin = 0.04f;
    [Header("Damage / feedback")]
    [SerializeField, Min(1)] private int damage = 1;
    [SerializeField] private Vector2 knockback = new Vector2(7f, 0f);
    [SerializeField, Range(0.08f, 0.5f)] private float minimumSquashHeight = 0.15f;
    [SerializeField, Range(1f, 1.4f)] private float maximumSquashWidth = 1.18f;
    [SerializeField, Min(0f)] private float squashHoldTime = 0.12f;
    [SerializeField, Min(0.01f)] private float squashRecoveryTime = 0.18f;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip floorImpact;
    [SerializeField] private AudioClip ballImpact;
    [SerializeField] private ParticleSystem sparks;
    private readonly List<Collider2D> damageHits = new List<Collider2D>(16);
    private readonly List<RaycastHit2D> supportHits = new List<RaycastHit2D>(16);
    private ContactFilter2D damageFilter;
    private MorningStarLauncher launcher;
    private float elapsed;
    private CameraShake2D cameraShake;
    public CycleState State { get; private set; }
    public bool IsBallBlocked => State == CycleState.BallBlocked;
    public bool IsStandBlocked => State == CycleState.StandBlocked;
    public bool IsBlocked => IsBallBlocked || IsStandBlocked;
    public float UndersideY => head != null ? head.position.y : transform.position.y;
    public float CurrentClearance => UndersideY - transform.position.y;
    public bool HasSafeClearance => IsBlocked && CurrentClearance >= requiredPlayerClearance;
    public PressBallStandController BallStand => ballStand;

    private void Awake()
    {
        ApplyGeometry();
        damageFilter = new ContactFilter2D { useTriggers = false };
        launcher = FindAnyObjectByType<MorningStarLauncher>();
        cameraShake = Camera.main != null ? Camera.main.GetComponent<CameraShake2D>() : null;
        InitializePhase();
    }

    private void OnDisable()
    {
        if (head != null) head.linearVelocity = Vector2.zero;
    }

    public void ApplyGeometry()
    {
        if (body != null)
            body.transform.localScale = new Vector3(width / body.sprite.bounds.size.x,
                bodyHeight / body.sprite.bounds.size.y, 1f);
        if (pressCollider != null) { pressCollider.size = new Vector2(width, bodyHeight); pressCollider.offset = new Vector2(0f, bodyHeight * 0.5f); }
        if (damageDetector != null) { damageDetector.size = new Vector2(width + 0.04f, bodyHeight + 0.04f); damageDetector.offset = new Vector2(0f, bodyHeight * 0.5f); }
        if (warningLight != null) warningLight.transform.localScale = new Vector3(width * 0.43f, 0.08f, 1f);
    }

    private void InitializePhase()
    {
        float down = travelDistance / descentSpeed, up = travelDistance / ascentSpeed;
        float time = Mathf.Repeat(startPhase, 1f) * (topWait + warningTime + down + bottomWait + up);
        float y = travelDistance;
        if (time < topWait) { State = CycleState.TopWait; elapsed = time; }
        else if ((time -= topWait) < warningTime) { State = CycleState.Warning; elapsed = time; }
        else if ((time -= warningTime) < down) { State = CycleState.Descending; y -= time * descentSpeed; }
        else if ((time -= down) < bottomWait) { State = CycleState.BottomWait; elapsed = time; y = 0f; }
        else { time -= bottomWait; State = CycleState.Rising; y = time * ascentSpeed; }
        // 開始位相が下降途中でも、実Colliderを突き抜けた位置から始めない。
        float top = transform.position.y + contactSkin + travelDistance;
        head.position = new Vector2(transform.position.x, top);
        float target = transform.position.y + contactSkin + y;
        if (LimitDescent(top - target, out float allowed, out bool onBall, out _))
        { target = top - allowed; Enter(onBall ? CycleState.BallBlocked : CycleState.StandBlocked); }
        head.position = new Vector2(transform.position.x, target);
    }

    private bool LimitDescent(float distance, out float allowed, out bool onBall, out Vector2 point)
    {
        allowed = distance; onBall = false; point = default;
        if (pressCollider == null) return false;
        // 実形状をスイープする。中心範囲・台座へのセット・通過高さで衝突を無効化しない。
        pressCollider.Cast(Vector2.down, damageFilter, supportHits, distance + contactSkin, true);
        bool found = false;
        float nearest = float.PositiveInfinity;
        foreach (var hit in supportHits)
        {
            if (hit.collider == null || hit.normal.y <= 0.001f) continue;
            bool ball = launcher != null && hit.collider.attachedRigidbody == launcher.BallBody;
            bool stand = hit.collider.GetComponentInParent<PressBallStandController>() != null;
            if ((!ball && !stand) || hit.distance >= nearest) continue;
            found = true; nearest = hit.distance; onBall = ball; point = hit.point;
        }
        if (found) allowed = Mathf.Clamp(nearest - contactSkin, 0f, distance);
        return found;
    }

    private void FixedUpdate()
    {
        if (head == null) return;
        float dt = Time.fixedDeltaTime;
        float bottom = transform.position.y + contactSkin, top = bottom + travelDistance;
        float y = head.position.y, next = y;
        elapsed += dt;
        switch (State)
        {
            case CycleState.TopWait: if (elapsed >= topWait) Enter(CycleState.Warning); break;
            case CycleState.Warning: if (elapsed >= warningTime) Enter(CycleState.Descending); break;
            case CycleState.Descending:
            case CycleState.BallBlocked:
            case CycleState.StandBlocked:
                next = Mathf.MoveTowards(y, bottom, descentSpeed * dt);
                if (LimitDescent(y - next, out float allowed, out bool onBall, out Vector2 point))
                {
                    next = y - allowed;
                    CycleState blocked = onBall ? CycleState.BallBlocked : CycleState.StandBlocked;
                    if (State != blocked) { Enter(blocked); Impact(onBall, point); }
                    if (elapsed >= bottomWait) Enter(CycleState.Rising);
                }
                else
                {
                    if (IsBlocked) Enter(CycleState.Descending);
                    if (next <= bottom) { Enter(CycleState.BottomWait); Impact(false, new Vector2(head.position.x, bottom)); }
                }
                break;
            case CycleState.BottomWait: if (elapsed >= bottomWait) Enter(CycleState.Rising); break;
            case CycleState.Rising:
                next = Mathf.MoveTowards(y, top, ascentSpeed * dt);
                if (next >= top) Enter(CycleState.TopWait);
                break;
        }
        DamageSweep(y, next);
        head.MovePosition(new Vector2(transform.position.x, next));
    }

    private void Enter(CycleState state) { State = state; elapsed = 0f; }

    private void DamageSweep(float from, float to)
    {
        // 下降1tick分を含む領域を先に検査。高速下降のすり抜けでも既存HP処理に届く。
        Vector2 center = new Vector2(head.position.x, (from + to + bodyHeight) * 0.5f);
        Vector2 size = new Vector2(width + 0.04f, bodyHeight + Mathf.Abs(to - from) + 0.04f);
        Physics2D.OverlapBox(center, size, 0f, damageFilter, damageHits);
        foreach (var c in damageHits)
        {
            PlayerHealth hp = c.GetComponentInParent<PlayerHealth>();
            if (hp == null) continue;
            if (!hp.IsDead && !hp.IsRespawning && hp.GetComponent<Player>() != null)
            {
                var squash = hp.GetComponent<PressPlayerSquash>();
                if (squash == null) squash = hp.gameObject.AddComponent<PressPlayerSquash>();
                squash.Contact(Mathf.Min(from, to), minimumSquashHeight, maximumSquashWidth,
                    squashHoldTime, squashRecoveryTime, body != null ? body.sortingOrder : 3);
            }
            float side = c.bounds.center.x < head.position.x ? -1f : 1f;
            hp.TakeDamage(damage, new Vector2(knockback.x * side, knockback.y));
        }
    }

    private void Impact(bool onBall, Vector2 point)
    {
        AudioClip clip = onBall ? ballImpact : floorImpact;
        if (audioSource != null && clip != null) audioSource.PlayOneShot(clip);
        if (sparks != null) { sparks.transform.position = point; sparks.Emit(onBall ? 16 : 24); }
        if (cameraShake != null && Camera.main != null &&
            Mathf.Abs(Camera.main.transform.position.x - point.x) < 18f)
            cameraShake.Shake(0.12f, onBall ? 0.05f : 0.09f);
    }

    private void LateUpdate()
    {
        if (warningLight != null)
            warningLight.color = State == CycleState.Warning
                ? Color.Lerp(new Color(1f, 0.15f, 0.08f), Color.yellow, Mathf.PingPong(elapsed * 10f, 1f))
                : IsBlocked ? (HasSafeClearance ? new Color(0.3f, 1f, 0.75f) : new Color(1f, 0.55f, 0.08f)) : Color.cyan;
        if (pistons == null || head == null) return;
        foreach (Transform piston in pistons)
        {
            if (piston == null) continue;
            float length = Mathf.Max(0.3f, transform.position.y + travelDistance + bodyHeight + 1f - head.position.y - bodyHeight);
            Vector3 p = piston.position; p.y = head.position.y + bodyHeight + length * 0.5f; piston.position = p;
            Vector3 scale = piston.localScale; scale.y = length / 3f; piston.localScale = scale;
        }
    }

    private void OnValidate()
    {
        width = Mathf.Max(2f, width); bodyHeight = Mathf.Max(0.2f, bodyHeight);
        travelDistance = Mathf.Max(0.5f, travelDistance);
        descentSpeed = Mathf.Max(0.1f, descentSpeed); ascentSpeed = Mathf.Max(0.1f, ascentSpeed);
        if (body != null && body.sprite != null) ApplyGeometry();
    }
}
