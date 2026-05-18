using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 鉄球だけを maxRopeLength 内に収める。プレイヤー速度は変更しない。
/// </summary>
public class ChainConstraint2D : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform handAnchor;
    [SerializeField] private Rigidbody2D morningStarRb;

    [Header("Rope Length")]
    [FormerlySerializedAs("maxChainLength")]
    [SerializeField] private float maxRopeLength = 4.5f;
    [SerializeField] private float correctionThreshold = 0.01f;

    [Header("Ball Only")]
    [Range(0f, 1f)]
    [SerializeField] private float positionCorrectionRate = 1f;
    [SerializeField] private float maxBallSpeed = 22f;

    public float MaxRopeLength => maxRopeLength;

    public float MaxBallSpeed
    {
        get => maxBallSpeed;
        set => maxBallSpeed = Mathf.Max(0f, value);
    }

    public void SetMaxRopeLength(float length)
    {
        maxRopeLength = Mathf.Max(0.1f, length);
    }

    private void Reset()
    {
        morningStarRb = GetComponent<Rigidbody2D>();
    }

    private void FixedUpdate()
    {
        if (!enabled || handAnchor == null || morningStarRb == null)
            return;

        Vector2 handPos = handAnchor.position;
        Vector2 ballPos = morningStarRb.position;
        Vector2 handToBall = ballPos - handPos;
        float distance = handToBall.magnitude;

        if (distance > maxRopeLength + correctionThreshold)
        {
            Vector2 dir = handToBall / distance;
            Vector2 targetBallPos = handPos + dir * maxRopeLength;
            morningStarRb.position = Vector2.Lerp(
                ballPos,
                targetBallPos,
                positionCorrectionRate);

            Vector2 ballVelocity = morningStarRb.linearVelocity;
            float outwardSpeed = Vector2.Dot(ballVelocity, dir);
            if (outwardSpeed > 0f)
                morningStarRb.linearVelocity = ballVelocity - dir * outwardSpeed;
        }

        if (maxBallSpeed > 0f)
        {
            Vector2 v = morningStarRb.linearVelocity;
            float mag = v.magnitude;
            if (mag > maxBallSpeed)
                morningStarRb.linearVelocity = v * (maxBallSpeed / mag);
        }
    }
}
