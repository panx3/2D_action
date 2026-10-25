using UnityEngine;

/// <summary>自然な剛体運動を保ち、圧殺・接地中の転がり音・投擲命中音を担当する。</summary>
[DisallowMultipleComponent, SelectionBase]
[RequireComponent(typeof(Rigidbody2D), typeof(CircleCollider2D))]
public sealed class RollingIronBall : MonoBehaviour
{
    [SerializeField, Min(0.05f), Tooltip("鉄球自身が敵へ向かう速さ。この値以上なら共通の撃破処理を呼ぶ。")]
    private float minimumCrushSpeed = 0.7f;

    [Header("転がりSE")]
    [SerializeField] private AudioClip rollingClip;
    [SerializeField, Range(0f, 1f)] private float rollingVolume = 0.6f;
    [SerializeField, Min(0.01f)] private float minimumRollingSpeed = 0.15f;
    [SerializeField, Min(0.1f)] private float fullRollingSpeed = 3.5f;
    [SerializeField, Min(0.1f), Tooltip("Playerからこの距離で転がり音が聞こえなくなる。")]
    private float rollingMaxDistance = 18f;
    [Header("投擲命中SE（火花は投擲側の既存演出を使用）")]
    [SerializeField] private AudioClip morningStarHitClip;
    [SerializeField, Range(0f, 1f)] private float morningStarHitVolume = 0.9f;
    [SerializeField, Min(0f)] private float minimumHitSpeed = 3.5f;
    [SerializeField, Min(0f)] private float hitSoundCooldown = 0.09f;

    private Rigidbody2D body;
    private CircleCollider2D ballCollider;
    private Vector2 velocityBeforePhysics;
    private AudioSource rollingSource;
    private AudioSource impactSource;
    private Transform listener;
    private float nextHitSoundTime;
    private readonly ContactPoint2D[] rollingContacts = new ContactPoint2D[16];
    public float MinimumCrushSpeed => minimumCrushSpeed;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        ballCollider = GetComponent<CircleCollider2D>();
        impactSource = OneShotAudioUtility.FindWorldImpactSource();
        var player = FindAnyObjectByType<Player>();
        listener = player != null ? player.transform : Camera.main != null ? Camera.main.transform : null;
        if (rollingClip == null) return;
        var audioObject = new GameObject("Rolling Audio");
        audioObject.transform.SetParent(transform, false);
        rollingSource = audioObject.AddComponent<AudioSource>();
        rollingSource.playOnAwake = false;
        rollingSource.loop = true;
        rollingSource.spatialBlend = 0f;
        rollingSource.clip = rollingClip;
        rollingSource.volume = 0f;
        if (impactSource != null)
            rollingSource.outputAudioMixerGroup = impactSource.outputAudioMixerGroup;
    }
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
    private void OnDisable()
    {
        velocityBeforePhysics = Vector2.zero;
        nextHitSoundTime = 0f;
        StopRollingSound();
    }

    private void Update()
    {
        if (rollingSource == null) return;
        float speed = Time.timeScale > 0f && body.simulated && ballCollider.enabled
            ? GetRollingSpeed() : 0f;
        float distance = listener != null ? Vector2.Distance(listener.position, body.position) : 0f;
        float proximity = Mathf.Clamp01(1f - distance / Mathf.Max(0.1f, rollingMaxDistance));
        if (speed < minimumRollingSpeed || proximity <= 0f)
        {
            StopRollingSound();
            return;
        }
        float strength = Mathf.InverseLerp(minimumRollingSpeed,
            Mathf.Max(minimumRollingSpeed + 0.01f, fullRollingSpeed), speed);
        float volume = rollingVolume * Mathf.Lerp(0.2f, 1f, strength) * proximity * proximity;
        rollingSource.volume = Mathf.MoveTowards(rollingSource.volume, volume, Time.unscaledDeltaTime * 4f);
        rollingSource.pitch = Mathf.Lerp(0.85f, 1.2f, strength);
        if (!rollingSource.isPlaying) rollingSource.Play();
    }

    private float GetRollingSpeed()
    {
        float radius = Mathf.Max(ballCollider.bounds.extents.x, ballCollider.bounds.extents.y);
        float rotationSpeed = Mathf.Abs(body.angularVelocity) * Mathf.Deg2Rad * radius;
        if (rotationSpeed < minimumRollingSpeed) return 0f;
        float speed = 0f;
        int count = ballCollider.GetContacts(rollingContacts);
        for (int i = 0; i < count; i++)
        {
            var contact = rollingContacts[i];
            var other = contact.collider == ballCollider ? contact.otherCollider : contact.collider;
            if (other == null || other.isTrigger || other.GetComponentInParent<Player>() != null
                || other.GetComponentInParent<EnemyHealth>() != null
                || other.GetComponentInParent<MorningStarCollisionReporter>() != null) continue;
            Vector2 normal = contact.normal;
            Vector2 towardBall = (Vector2)ballCollider.bounds.center - contact.point;
            if (Vector2.Dot(normal, towardBall) < 0f) normal = -normal;
            if (normal.y <= 0.35f) continue;
            Vector2 floorVelocity = other.attachedRigidbody != null
                ? other.attachedRigidbody.GetPointVelocity(contact.point) : Vector2.zero;
            Vector2 tangent = new Vector2(normal.y, -normal.x);
            float travelSpeed = Mathf.Abs(Vector2.Dot(body.linearVelocity - floorVelocity, tangent));
            // 空中の回転・その場の空回り・回転しない滑りでは鳴らさない。
            speed = Mathf.Max(speed, Mathf.Min(travelSpeed, rotationSpeed));
        }
        return speed;
    }

    private void StopRollingSound()
    {
        if (rollingSource == null) return;
        rollingSource.Stop();
        rollingSource.volume = 0f;
    }

    public void TryPlayMorningStarImpact(Collision2D collision, MorningStarLauncher launcher)
    {
        if (!isActiveAndEnabled || launcher == null || morningStarHitClip == null
            || collision.contactCount == 0 || Time.time < nextHitSoundTime) return;
        if (launcher.State != MorningStarLauncher.MorningStarState.Thrown
            && launcher.State != MorningStarLauncher.MorningStarState.Dropping) return;
        float speed = 0f;
        for (int i = 0; i < collision.contactCount; i++)
            speed = Mathf.Max(speed, Mathf.Abs(Vector2.Dot(collision.relativeVelocity, collision.GetContact(i).normal)));
        if (speed < minimumHitSpeed) return;
        if (impactSource == null) impactSource = OneShotAudioUtility.FindWorldImpactSource();
        if (OneShotAudioUtility.Play2D(impactSource, morningStarHitClip, morningStarHitVolume, collision.GetContact(0).point))
            nextHitSoundTime = Time.time + hitSoundCooldown;
    }

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
