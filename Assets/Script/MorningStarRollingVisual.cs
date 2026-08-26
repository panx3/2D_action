using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 鉄球の物理本体を回さず、地面での水平移動距離に応じてVisualだけを段階回転させる。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
public sealed class MorningStarRollingVisual : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform visual;
    [SerializeField] private MorningStarLauncher launcher;

    [Header("Rolling Visual")]
    [SerializeField, Min(0.001f)] private float visualRadius = 0.395f;
    [SerializeField, Min(1f)] private float rotationStep = 30f;
    [SerializeField, Min(0f)] private float minimumRollSpeed = 0.1f;

    private readonly HashSet<Collider2D> floorContacts = new HashSet<Collider2D>();
    private readonly HashSet<Collider2D> magnetContacts = new HashSet<Collider2D>();
    private Rigidbody2D body;
    private Vector2 previousPosition;
    private Quaternion baseVisualRotation;
    private float accumulatedRollAngle;

    public Transform Visual => visual;
    public float VisualRadius => visualRadius;
    public float RotationStep => rotationStep;
    public float MinimumRollSpeed => minimumRollSpeed;
    public bool IsGrounded => floorContacts.Count > 0;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();

        if (launcher == null)
            launcher = FindAnyObjectByType<MorningStarLauncher>(FindObjectsInactive.Include);

        if (visual == null)
        {
            Transform candidate = transform.Find("Visual");
            if (candidate != null)
                visual = candidate;
        }

        previousPosition = body.position;
        baseVisualRotation = visual != null ? visual.localRotation : Quaternion.identity;
    }

    private void OnEnable()
    {
        if (body == null)
            body = GetComponent<Rigidbody2D>();

        previousPosition = body.position;
    }

    private void OnDisable()
    {
        floorContacts.Clear();
        magnetContacts.Clear();
    }

    private void FixedUpdate()
    {
        Vector2 currentPosition = body.position;
        float deltaX = currentPosition.x - previousPosition.x;
        previousPosition = currentPosition;

        if (visual == null || !CanUpdateRollingVisual())
            return;

        float horizontalSpeedFromDistance = Mathf.Abs(deltaX) / Mathf.Max(Time.fixedDeltaTime, 0.0001f);
        if (horizontalSpeedFromDistance < minimumRollSpeed)
            return;

        // 右移動は時計回り（Zマイナス）、左移動は反時計回り。
        accumulatedRollAngle -= deltaX / Mathf.Max(visualRadius, 0.001f) * Mathf.Rad2Deg;
        float steppedAngle = Mathf.Round(accumulatedRollAngle / rotationStep) * rotationStep;
        visual.localRotation = baseVisualRotation * Quaternion.Euler(0f, 0f, steppedAngle);
    }

    private bool CanUpdateRollingVisual()
    {
        if (floorContacts.Count == 0 || magnetContacts.Count > 0 || launcher == null)
            return false;

        MorningStarLauncher.MorningStarState state = launcher.CurrentState;
        return state == MorningStarLauncher.MorningStarState.Dragging
            || state == MorningStarLauncher.MorningStarState.Dropping;
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        RegisterFloorContact(collision);
    }

    private void OnCollisionStay2D(Collision2D collision)
    {
        RegisterFloorContact(collision);
    }

    private void OnCollisionExit2D(Collision2D collision)
    {
        if (collision.collider != null)
            floorContacts.Remove(collision.collider);
    }

    private void RegisterFloorContact(Collision2D collision)
    {
        if (collision.collider == null)
            return;

        if (launcher != null && launcher.HasFloorContact(collision))
            floorContacts.Add(collision.collider);
        else
            floorContacts.Remove(collision.collider);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other != null && other.GetComponentInParent<MagnetPoint>() != null)
            magnetContacts.Add(other);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other != null)
            magnetContacts.Remove(other);
    }

    private void OnValidate()
    {
        visualRadius = Mathf.Max(0.001f, visualRadius);
        rotationStep = Mathf.Max(1f, rotationStep);
        minimumRollSpeed = Mathf.Max(0f, minimumRollSpeed);
    }
}
