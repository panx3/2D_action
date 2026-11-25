using UnityEngine;

/// <summary>A gear that transfers tangential motion through physical contacts.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
public sealed class GearPlatform : MonoBehaviour
{
    [SerializeField, Min(0.01f), Tooltip("歯の噛み合いに使う半径。中心間隔は両方の歯車のピッチ半径の合計に合わせます。")]
    private float pitchRadius = 2.35f;

    private Rigidbody2D body;
    private float initialRotation;

    internal float WorldPitchRadius => Mathf.Max(0.01f, pitchRadius * Mathf.Abs(transform.lossyScale.x));

    private void Awake()
    {
        // The wheel is mounted to the level. Older instances may not have a body yet.
        if (!TryGetComponent(out body))
            body = gameObject.AddComponent<Rigidbody2D>();

        body.bodyType = RigidbodyType2D.Kinematic;
        body.gravityScale = 0f;
        body.useFullKinematicContacts = false;
        body.constraints = RigidbodyConstraints2D.FreezePosition;
        body.linearVelocity = Vector2.zero;
        initialRotation = body.rotation;
    }

    internal void RotateTo(double angle)
    {
        if (body != null && isActiveAndEnabled)
            body.MoveRotation(initialRotation + (float)(angle % 360d));
    }

    internal void StopRotation()
    {
        if (body != null)
            body.angularVelocity = 0f;
    }

    private void OnDisable()
    {
        StopRotation();
    }
}
