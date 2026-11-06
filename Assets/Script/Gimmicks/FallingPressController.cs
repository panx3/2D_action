using System.Collections.Generic;
using UnityEngine;

/// <summary>Kinematicのプレスのみを動かす。鉄球・Player・鎖のStateと物理設定は変更しない。</summary>
[DisallowMultipleComponent]
public sealed class FallingPressController : MonoBehaviour
{
    public enum CycleState { TopWait, Warning, Descending, BottomWait, Rising, BallBlocked, StandBlocked, PlayerBlocked, FloorBlocked }
    [Header("Pair / geometry (root = floor, head pivot = underside)")]
    [SerializeField, Tooltip("配置確認用の任意参照。停止判定はこの参照に依存せず、接触した台座・鉄球を使います。")]
    private PressBallStandController ballStand;
    [SerializeField] private Rigidbody2D head;
    [SerializeField] private BoxCollider2D pressCollider;
    [SerializeField] private BoxCollider2D damageDetector;
    [SerializeField] private BoxCollider2D playerTopSurface;
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
    [SerializeField, Tooltip("同じ周期・開始位相のプレスを共通時間で同期します")]
    private bool synchronize = true;
    [Header("Physical support")]
    [SerializeField, Min(0.1f), Tooltip("床からプレス下面までに必要な高さ。Player実寸3.3 + 余裕")]
    private float requiredPlayerClearance = 3.5f;
    [SerializeField, Min(0f)] private float contactSkin = 0.04f;
    [SerializeField, Tooltip("プレスを止める床・地形のレイヤー")]
    private LayerMask floorLayers = (1 << 0) | (1 << 6);
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
    [SerializeField, HideInInspector] private ParticleSystem sparks; // 旧Prefab参照を維持する。
    [SerializeField] private MorningStarImpactSparks impactSparks;
    [SerializeField, Range(0, 256), Tooltip("プレスの衝突時に発生する鉄球と同じ火花の本数。0で無効")]
    private int impactSparkCount = 24;
    private readonly List<RaycastHit2D> supportHits = new List<RaycastHit2D>(16);
    private readonly List<Collider2D> damageHits = new List<Collider2D>(8);
    private readonly List<RaycastHit2D> groundHits = new List<RaycastHit2D>(16);
    private readonly List<IgnoredContact> ignoredContacts = new List<IgnoredContact>(4);
    private sealed class IgnoredContact
    {
        public Collider2D collider;
        public bool bodyWasIgnored, topWasIgnored;
        public float feedbackUntil = -1f, visualUnderside;
    }
    private readonly ContactPoint2D[] playerSupportContacts = new ContactPoint2D[16];
    private ContactFilter2D damageFilter;
    private MorningStarLauncher launcher;
    private float elapsed;
    private CameraShake2D cameraShake;
    public CycleState State { get; private set; }
    public bool IsBallBlocked => State == CycleState.BallBlocked;
    public bool IsStandBlocked => State == CycleState.StandBlocked;
    public bool IsPlayerBlocked => State == CycleState.PlayerBlocked;
    public bool IsFloorBlocked => State == CycleState.FloorBlocked;
    public bool IsBlocked => IsBallBlocked || IsStandBlocked || IsFloorBlocked;
    public float UndersideY => head != null ? head.position.y : transform.position.y;
    public float CurrentClearance => UndersideY - transform.position.y;
    public bool HasSafeClearance => (IsBallBlocked || IsStandBlocked) && CurrentClearance >= requiredPlayerClearance;
    public PressBallStandController BallStand => ballStand;

    private void Awake()
    {
        ApplyGeometry();
        if (pressCollider != null)
        {
            // Inspectorで追加した床レイヤーにも実Colliderが接触するようにする。
            pressCollider.includeLayers = pressCollider.includeLayers.value | floorLayers.value;
            pressCollider.excludeLayers = pressCollider.excludeLayers.value & ~floorLayers.value;
        }
        damageFilter = new ContactFilter2D { useTriggers = false };
        launcher = FindAnyObjectByType<MorningStarLauncher>();
        cameraShake = Camera.main != null ? Camera.main.GetComponent<CameraShake2D>() : null;
        InitializePhase();
    }

    private void OnDisable()
    {
        if (head != null) head.linearVelocity = Vector2.zero;
        RestoreContacts(true);
    }

    public void ApplyGeometry()
    {
        if (body != null)
            body.transform.localScale = new Vector3(width / body.sprite.bounds.size.x,
                bodyHeight / body.sprite.bounds.size.y, 1f);
        if (pressCollider != null) { pressCollider.size = new Vector2(width, bodyHeight); pressCollider.offset = new Vector2(0f, bodyHeight * 0.5f); }
        if (damageDetector != null) { damageDetector.size = new Vector2(width + 0.04f, bodyHeight + 0.04f); damageDetector.offset = new Vector2(0f, bodyHeight * 0.5f); }
        if (playerTopSurface != null) { playerTopSurface.size = new Vector2(width, 0.12f); playerTopSurface.offset = new Vector2(0f, bodyHeight - 0.06f); }
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
        if (LimitDescent(top - target, out float allowed, out bool onBall, out bool onFloor, out _))
        { target = top - allowed; Enter(onBall ? CycleState.BallBlocked : onFloor ? CycleState.FloorBlocked : CycleState.StandBlocked); }
        head.position = new Vector2(transform.position.x, target);
    }

    private bool LimitDescent(float distance, out float allowed, out bool onBall, out bool onFloor, out Vector2 point)
    {
        allowed = distance; onBall = onFloor = false; point = default;
        if (pressCollider == null) return false;
        // 実際に当たった鉄球・台座・床を支点とし、Playerでは停止しない。
        pressCollider.Cast(Vector2.down, damageFilter, supportHits, distance + contactSkin, true);
        bool found = false;
        float nearest = float.PositiveInfinity;
        foreach (var hit in supportHits)
        {
            Collider2D c = hit.collider;
            if (c == null || hit.normal.y <= 0.001f) continue;
            bool ball = launcher != null && c.attachedRigidbody == launcher.BallBody;
            bool stand = c.GetComponentInParent<PressBallStandController>() != null;
            bool floor = !ball && !stand && (floorLayers.value & (1 << c.gameObject.layer)) != 0 &&
                c.GetComponentInParent<Player>() == null && c.GetComponentInParent<FallingPressController>() == null;
            if ((!ball && !stand && !floor) || (floor ? hit.point.y : c.bounds.max.y) > UndersideY + contactSkin) continue;
            if (hit.distance >= nearest) continue;
            found = true; nearest = hit.distance; point = hit.point; onBall = ball; onFloor = floor;
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
        RestoreContacts(false);
        elapsed += dt;
        if (synchronize)
        {
            UpdateSynchronized(y, bottom, top, dt);
            return;
        }
        switch (State)
        {
            case CycleState.TopWait: if (elapsed >= topWait) Enter(CycleState.Warning); break;
            case CycleState.Warning: if (elapsed >= warningTime) Enter(CycleState.Descending); break;
            case CycleState.Descending:
            case CycleState.BallBlocked:
            case CycleState.StandBlocked:
            case CycleState.FloorBlocked:
                next = Mathf.MoveTowards(y, bottom, descentSpeed * dt);
                if (LimitDescent(y - next, out float allowed, out bool onBall, out bool onFloor, out Vector2 point))
                {
                    next = y - allowed;
                    CycleState blocked = onBall ? CycleState.BallBlocked : onFloor ? (next <= bottom + Physics2D.defaultContactOffset ? CycleState.BottomWait : CycleState.FloorBlocked) : CycleState.StandBlocked;
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
        ApplyCrushDamage(y, next);
        head.MovePosition(new Vector2(transform.position.x, next));
    }

    private void Enter(CycleState state) { State = state; elapsed = 0f; }

    private void UpdateSynchronized(float y, float bottom, float top, float dt)
    {
        float downTime = travelDistance / descentSpeed, upTime = travelDistance / ascentSpeed;
        float cycle = topWait + warningTime + downTime + bottomWait + upTime;
        float time = Mathf.Repeat(Time.fixedTime + startPhase * cycle, cycle);
        float warningStart = topWait, downStart = warningStart + warningTime;
        float downEnd = downStart + downTime, upStart = downEnd + bottomWait;
        float next = y;
        if (time < downStart)
        {
            CycleState phase = time < warningStart ? CycleState.TopWait : CycleState.Warning;
            if (State != phase) Enter(phase);
            next = Mathf.MoveTowards(y, top, ascentSpeed * dt);
        }
        else if (time < upStart)
        {
            float scheduled = time < downEnd ? Mathf.Max(bottom, top - descentSpeed * (time - downStart)) : bottom;
            next = Mathf.MoveTowards(y, scheduled, descentSpeed * dt);
            if (LimitDescent(y - next, out float allowed, out bool onBall, out bool onFloor, out Vector2 point))
            {
                next = y - allowed;
                CycleState blocked = onBall ? CycleState.BallBlocked : onFloor ? (next <= bottom + Physics2D.defaultContactOffset ? CycleState.BottomWait : CycleState.FloorBlocked) : CycleState.StandBlocked;
                if (State != blocked) { Enter(blocked); Impact(onBall, point); }
            }
            else if (next <= bottom)
            {
                if (State != CycleState.BottomWait) { Enter(CycleState.BottomWait); Impact(false, new Vector2(head.position.x, bottom)); }
            }
            else if (State != CycleState.Descending) Enter(CycleState.Descending);
        }
        else
        {
            if (State != CycleState.Rising) Enter(CycleState.Rising);
            next = Mathf.MoveTowards(y, top, ascentSpeed * dt);
        }
        ApplyCrushDamage(y, next);
        head.MovePosition(new Vector2(transform.position.x, next));
    }

    private float PlayerDownSpace(Collider2D c, float requested)
    {
        if (HasFootSupport(c)) return 0f;
        Player player = c.GetComponentInParent<Player>();
        ContactFilter2D filter = damageFilter; filter.SetLayerMask(player.GroundLayers);
        c.Cast(Vector2.down, filter, groundHits, requested + contactSkin, true);
        float available = requested;
        foreach (var hit in groundHits)
        {
            if (hit.collider == null || hit.normal.y <= 0.5f || hit.collider.attachedRigidbody == head ||
                hit.collider.transform.IsChildOf(player.transform) || Physics2D.GetIgnoreCollision(c, hit.collider)) continue;
            var oneWay = hit.collider.GetComponentInParent<OneWayPlatform>();
            if (oneWay != null && !oneWay.BlocksBodyCorrection(c.attachedRigidbody, Vector2.down, hit.normal)) continue;
            available = Mathf.Min(available, Mathf.Max(0f, hit.distance - contactSkin));
        }
        return available;
    }

    private void ApplyCrushDamage(float from, float to)
    {
        if (State == CycleState.TopWait || State == CycleState.Warning || State == CycleState.Rising) return;
        // 移動とは独立したダメージ判定。解除した実体衝突に依存せず、移動区間も含めて検出する。
        Vector2 center = new Vector2(head.position.x, (from + to + bodyHeight) * 0.5f);
        Vector2 size = new Vector2(width + 0.04f, bodyHeight + Mathf.Abs(to - from) + 0.04f);
        Physics2D.OverlapBox(center, size, 0f, damageFilter, damageHits);
        foreach (var c in damageHits)
        {
            PlayerHealth hp = c.GetComponentInParent<PlayerHealth>();
            if (hp == null || hp.IsDead || hp.IsRespawning || hp.GetComponent<Player>() == null) continue;
            Bounds player = c.bounds, press = pressCollider.bounds;
            float overlap = Mathf.Min(player.max.x, press.max.x) - Mathf.Max(player.min.x, press.min.x);
            IgnoredContact contact = FindIgnoredContact(c);
            if (contact == null)
            {
                if (to >= from || overlap <= 0f || player.center.y >= from || player.max.y < to - contactSkin) continue;
                float needed = Mathf.Max(0f, player.max.y + contactSkin - to);
                if (PlayerDownSpace(c, needed) >= needed - 0.001f) continue;
                // 挟む直前に、このプレスとの押し込みだけを解除。Playerの床・台座・他プレスとの衝突は維持。
                contact = IgnoreContact(c);
            }
            if (overlap < player.size.x * 0.5f || !HasFootSupport(c)) continue;
            if (contact.feedbackUntil < 0f)
            {
                // 旧Player停止時と同じ接触面・保持期間で既存の演出を呼ぶ。下降位置には使わない。
                contact.visualUnderside = player.max.y + contactSkin;
                contact.feedbackUntil = Time.fixedTime + bottomWait;
            }
            if (Time.fixedTime >= contact.feedbackUntil) continue;
            var squash = hp.GetComponent<PressPlayerSquash>();
            if (squash == null) squash = hp.gameObject.AddComponent<PressPlayerSquash>();
            Collider2D ballCollider = launcher != null && launcher.BallBody != null
                ? launcher.BallBody.GetComponent<Collider2D>() : null;
            float ballHeight = ballCollider != null ? ballCollider.bounds.size.y : 0f;
            squash.Contact(contact.visualUnderside, minimumSquashHeight, maximumSquashWidth,
                squashHoldTime, squashRecoveryTime, body != null ? body.sortingOrder : 3, ballHeight);
            float side = player.center.x < head.position.x ? -1f : 1f;
            hp.TakeDamage(damage, new Vector2(knockback.x * side, knockback.y));
        }
    }

    private IgnoredContact IgnoreContact(Collider2D c)
    {
        var contact = FindIgnoredContact(c);
        if (contact == null)
        {
            contact = new IgnoredContact { collider = c, bodyWasIgnored = Physics2D.GetIgnoreCollision(pressCollider, c),
                topWasIgnored = playerTopSurface != null && Physics2D.GetIgnoreCollision(playerTopSurface, c) };
            ignoredContacts.Add(contact);
        }
        Physics2D.IgnoreCollision(pressCollider, c, true);
        if (playerTopSurface != null) Physics2D.IgnoreCollision(playerTopSurface, c, true);
        return contact;
    }

    private IgnoredContact FindIgnoredContact(Collider2D c)
    {
        foreach (var contact in ignoredContacts)
            if (contact.collider == c) return contact;
        return null;
    }

    private void RestoreContacts(bool all)
    {
        for (int i = ignoredContacts.Count - 1; i >= 0; i--)
        {
            var contact = ignoredContacts[i]; Collider2D c = contact.collider;
            if (!all && c != null && c.enabled && c.gameObject.activeInHierarchy)
            {
                ColliderDistance2D separation = pressCollider.Distance(c);
                if (separation.isValid && separation.distance <= contactSkin + 0.01f) continue;
            }
            // 重なっている間は戻さない。離脱・復帰後は閉じた隙間への侵入も通常Colliderが防ぐ。
            if (c != null && c.gameObject.activeInHierarchy)
            {
                if (pressCollider != null && pressCollider.gameObject.activeInHierarchy)
                    Physics2D.IgnoreCollision(pressCollider, c, contact.bodyWasIgnored);
                if (playerTopSurface != null && playerTopSurface.gameObject.activeInHierarchy)
                    Physics2D.IgnoreCollision(playerTopSurface, c, contact.topWasIgnored);
            }
            ignoredContacts.RemoveAt(i);
        }
    }

    private bool HasFootSupport(Collider2D playerCollider)
    {
        Bounds player = playerCollider.bounds;
        int count = playerCollider.GetContacts(playerSupportContacts);
        for (int i = 0; i < count; i++)
        {
            ContactPoint2D contact = playerSupportContacts[i];
            if (contact.normal.y <= 0.5f || contact.point.y > player.min.y + 0.12f) continue;
            Collider2D support = contact.collider == playerCollider ? contact.otherCollider : contact.collider;
            if (support == null || support.isTrigger || support.transform.IsChildOf(playerCollider.transform) ||
                support.attachedRigidbody == head) continue;
            // 接地猶予フラグを使わず実接触を見る。跳び立った直後の古い接触も除く。
            float supportSpeed = support.attachedRigidbody != null
                ? support.attachedRigidbody.GetPointVelocity(contact.point).y : 0f;
            float playerSpeed = playerCollider.attachedRigidbody != null
                ? playerCollider.attachedRigidbody.GetPointVelocity(contact.point).y : 0f;
            if (playerSpeed - supportSpeed <= 0.1f) return true;
        }
        return false;
    }

    private void Impact(bool onBall, Vector2 point)
    {
        AudioClip clip = onBall ? ballImpact : floorImpact;
        if (audioSource != null && clip != null) audioSource.PlayOneShot(clip);
        var ballSparks = impactSparks != null ? impactSparks : launcher != null && launcher.BallBody != null
            ? launcher.BallBody.GetComponent<MorningStarImpactSparks>() : null;
        if (ballSparks != null) ballSparks.PlayPressImpact(point, impactSparkCount);
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
