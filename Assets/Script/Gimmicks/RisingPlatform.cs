using System.Collections.Generic;
using UnityEngine;

/// <summary>A directional lift whose placed position is its starting endpoint.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)]
[RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D))]
public sealed class RisingPlatform : MonoBehaviour
{
    private enum MoveDirection { Up, Down, Left, Right }

    [Header("Move Settings")]
    [SerializeField, InspectorName("移動方向"), Tooltip("配置位置から最初に進む方向。指定距離を移動した後は配置位置まで往復します。")]
    private MoveDirection moveDirection = MoveDirection.Up;
    [SerializeField, InspectorName("乗ったら動き出す"), Tooltip("最初にプレイヤーが乗るまで待機し、一度動き出したら降りても自動で往復し続けます。Auto Move より優先されます。")]
    private bool startOnPlayerRide = false;
    [SerializeField, Tooltip("ON: repeat automatically. OFF: wait for a player standing on top. Ignored when Start On Player Ride is enabled.")]
    private bool autoMove = true;
    [SerializeField, Min(0.01f), Tooltip("World-space travel distance from the placed starting endpoint, in Unity units.")]
    private float moveDistance = 3f;
    [SerializeField, Min(0.01f)] private float moveSpeed = 2f;
    [SerializeField, Min(0f), Tooltip("Automatic movement: wait at the far endpoint.")]
    private float topWaitTime = 1f;
    [SerializeField, Min(0f), Tooltip("Automatic movement: wait at the placed starting endpoint (also used at game start).")]
    private float bottomWaitTime = 1f;

    [Header("Player Detection Settings")]
    [SerializeField, Min(0f), Tooltip("Player must remain on top for this long before movement starts.")]
    private float activationDelay = 0.5f;
    [SerializeField, Min(0f), Tooltip("Legacy rider mode: time without a rider at the far endpoint before returning. Reboarding cancels the wait.")]
    private float returnDelay = 1f;

    private enum LiftState { Bottom, Rising, Top, Descending }

    private Rigidbody2D body;
    private BoxCollider2D platformCollider;
    private PhysicsMaterial2D originalMaterial;
    private PhysicsMaterial2D horizontalCarryMaterial;
    private Vector2 bottomPosition;
    private LiftState state;
    private float waitTimer;
    private float previousVerticalSpeed;
    private bool previousAutoMove;
    private bool hasStartedMoving;
    private readonly List<ContactPoint2D> contacts = new List<ContactPoint2D>(16);
    private readonly List<Player> riders = new List<Player>(2);

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        platformCollider = GetComponent<BoxCollider2D>();
        originalMaterial = platformCollider.sharedMaterial;
        moveDistance = Mathf.Max(0.01f, moveDistance);
        body.bodyType = RigidbodyType2D.Kinematic;
        body.gravityScale = 0f;
        body.constraints = RigidbodyConstraints2D.FreezeRotation;
        body.interpolation = RigidbodyInterpolation2D.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        body.linearVelocity = Vector2.zero;
        body.angularVelocity = 0f;
        platformCollider.isTrigger = false;
        bottomPosition = body.position;
        previousAutoMove = autoMove;
    }

    private void FixedUpdate()
    {
        RefreshRiders();
        UpdateCarryMaterial();
        if (previousAutoMove != autoMove)
        {
            waitTimer = 0f;
            previousAutoMove = autoMove;
        }

        float dt = Time.fixedDeltaTime;
        bool automatic = startOnPlayerRide ? hasStartedMoving : autoMove;
        Vector2 topPosition = bottomPosition + GetMoveDirection() * moveDistance;
        switch (state)
        {
            case LiftState.Bottom:
                if (WaitUntil(automatic || riders.Count > 0,
                    automatic ? bottomWaitTime : activationDelay, dt))
                {
                    hasStartedMoving = true;
                    EnterState(LiftState.Rising);
                }
                break;
            case LiftState.Top:
                if (WaitUntil(automatic || riders.Count == 0,
                    automatic ? topWaitTime : returnDelay, dt))
                    EnterState(LiftState.Descending);
                break;
        }

        Vector2 nextPosition = body.position;
        if (state == LiftState.Rising || state == LiftState.Descending)
        {
            bool rising = state == LiftState.Rising;
            Vector2 target = rising ? topPosition : bottomPosition;
            nextPosition = Vector2.MoveTowards(body.position, target, moveSpeed * dt);
            if ((nextPosition - target).sqrMagnitude < 0.0000001f)
                EnterState(rising ? LiftState.Top : LiftState.Bottom);
        }

        float verticalSpeed = (nextPosition.y - body.position.y) / dt;
        CarryHorizontalMotion(nextPosition.x - body.position.x);
        CarrySupportDeceleration(verticalSpeed);
        body.MovePosition(nextPosition);
        previousVerticalSpeed = verticalSpeed;
    }

    private Vector2 GetMoveDirection()
    {
        switch (moveDirection)
        {
            case MoveDirection.Down: return Vector2.down;
            case MoveDirection.Left: return Vector2.left;
            case MoveDirection.Right: return Vector2.right;
            default: return Vector2.up;
        }
    }

    private bool WaitUntil(bool condition, float duration, float dt)
    {
        waitTimer = condition ? waitTimer + dt : 0f;
        return condition && waitTimer >= duration;
    }

    private void EnterState(LiftState nextState)
    {
        state = nextState;
        waitTimer = 0f;
    }

    private void RefreshRiders()
    {
        riders.Clear();
        contacts.Clear();
        var filter = new ContactFilter2D { useTriggers = false };
        platformCollider.GetContacts(filter, contacts);
        float surfaceY = platformCollider.bounds.max.y;
        foreach (ContactPoint2D contact in contacts)
        {
            // Query fresh physical contacts, so disabled/destroyed colliders and side
            // contacts cannot leave stale riders behind. Handle either contact ordering.
            Collider2D other = contact.collider.attachedRigidbody == body
                ? contact.otherCollider : contact.collider;
            if (other == null || !other.enabled || other.isTrigger
                || Mathf.Abs(contact.normal.y) < 0.5f
                || contact.point.y < surfaceY - 0.05f
                || other.bounds.min.y < surfaceY - 0.1f
                || !PlayerColliderUtility.IsPlayerBody(other))
                continue;

            Player player = other.GetComponentInParent<Player>();
            Rigidbody2D riderBody = other.attachedRigidbody;
            if (riderBody == null || riderBody.bodyType != RigidbodyType2D.Dynamic
                || riderBody != player.GetComponent<Rigidbody2D>())
                continue;

            // A jump may have been queued earlier in this physics tick. Never attach
            // or transport a departing player, including a pull upward from the chain.
            if (!player.IsGrounded || riderBody.linearVelocity.y > previousVerticalSpeed + 0.05f)
                continue;
            if (!riders.Contains(player))
                riders.Add(player);
        }
    }

    private void UpdateCarryMaterial()
    {
        bool horizontal = moveDirection == MoveDirection.Left || moveDirection == MoveDirection.Right;
        if (!horizontal)
        {
            if (horizontalCarryMaterial != null && platformCollider.sharedMaterial == horizontalCarryMaterial)
                platformCollider.sharedMaterial = originalMaterial;
            return;
        }

        if (horizontalCarryMaterial == null)
        {
            horizontalCarryMaterial = originalMaterial != null
                ? Instantiate(originalMaterial) : new PhysicsMaterial2D("RisingPlatform Horizontal Carry");
            // Horizontal transport is explicit below. Friction would carry a second
            // time and introduce drift; keep the source material asset unchanged.
            horizontalCarryMaterial.friction = 0f;
        }
        if (platformCollider.sharedMaterial != horizontalCarryMaterial)
            platformCollider.sharedMaterial = horizontalCarryMaterial;
    }

    private void CarryHorizontalMotion(float deltaX)
    {
        if (Mathf.Abs(deltaX) < 0.000001f)
            return;

        // Apply only support displacement; Player still owns walking, jumps and forces.
        foreach (Player rider in riders)
        {
            Rigidbody2D riderBody = rider.GetComponent<Rigidbody2D>();
            riderBody.position += Vector2.right * deltaX;
        }
    }

    private void CarrySupportDeceleration(float verticalSpeed)
    {
        float speedChange = verticalSpeed - previousVerticalSpeed;
        if (speedChange >= -0.0001f)
            return;

        // Contact resolution pushes riders upward, but cannot pull them down when
        // the support decelerates (ascent stopping or descent starting). Inherit only
        // that change in support speed, avoiding a gap or an unintended launch at the
        // top. Leave horizontal movement, jumps and chain forces to their owners.
        // No parenting, position writes or recurring velocity clamp.
        foreach (Player rider in riders)
        {
            if (!rider.IsGrounded)
                continue;
            Rigidbody2D riderBody = rider.GetComponent<Rigidbody2D>();
            if (riderBody.linearVelocity.y <= previousVerticalSpeed + 0.05f)
                riderBody.AddForce(Vector2.up * (speedChange * riderBody.mass), ForceMode2D.Impulse);
        }
    }

    private void OnDisable()
    {
        if (body != null)
            body.linearVelocity = Vector2.zero;
        if (platformCollider != null && horizontalCarryMaterial != null
            && platformCollider.sharedMaterial == horizontalCarryMaterial)
            platformCollider.sharedMaterial = originalMaterial;
        previousVerticalSpeed = 0f;
        riders.Clear();
    }

    private void OnDestroy()
    {
        if (horizontalCarryMaterial != null)
            Destroy(horizontalCarryMaterial);
    }

    private void OnValidate()
    {
        moveDistance = Mathf.Max(0.01f, moveDistance);
        moveSpeed = Mathf.Max(0.01f, moveSpeed);
        topWaitTime = Mathf.Max(0f, topWaitTime);
        bottomWaitTime = Mathf.Max(0f, bottomWaitTime);
        activationDelay = Mathf.Max(0f, activationDelay);
        returnDelay = Mathf.Max(0f, returnDelay);
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 bottom = Application.isPlaying && body != null
            ? new Vector3(bottomPosition.x, bottomPosition.y, transform.position.z)
            : transform.position;
        Vector3 top = bottom + (Vector3)GetMoveDirection() * moveDistance;
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(bottom, top);
        Gizmos.DrawWireSphere(bottom, 0.15f);
        Gizmos.DrawWireSphere(top, 0.15f);
    }
}
