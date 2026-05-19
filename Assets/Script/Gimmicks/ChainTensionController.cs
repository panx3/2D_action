using UnityEngine;

public class ChainTensionController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform startPoint;          // Playerの手元
    [SerializeField] private Rigidbody2D playerRb;          // Player
    [SerializeField] private Rigidbody2D morningStarRb;     // 鉄球

    [Header("Chain Length")]
    [SerializeField] private float maxChainLength = 5.0f;

    [Header("Ground / Air Tension")]
    [SerializeField] private float groundSlackDistance = 0.25f;
    [SerializeField] private float airSlackDistance = 0.05f;
    [SerializeField] private float groundStretchForMaxPull = 0.8f;
    [SerializeField] private float airStretchForMaxPull = 0.35f;

    [Header("Player Pull")]
    [SerializeField] private float playerPullForce = 180f;
    [SerializeField] private float maxGroundPullSpeed = 5f;
    [SerializeField] private float maxAirPullSpeed = 22f;

    [Header("Ground Horizontal Assist")]
    [SerializeField] private float minGroundPullRate = 0.25f;
    [SerializeField] private float minGroundHorizontalDistance = 0.15f;

    [Header("Morning Star Limit")]
    [SerializeField] private bool limitMorningStarDistance = true;

    [Range(0f, 1f)]
    [SerializeField] private float positionCorrectionRate = 0.05f;

    [Header("Debug")]
    [SerializeField] private bool showDebugLog = false;

    private Player player;

    private void Awake()
    {
        if (playerRb != null)
        {
            player = playerRb.GetComponent<Player>();
        }
    }

    private void FixedUpdate()
    {
        if (startPoint == null || playerRb == null || morningStarRb == null)
        {
            return;
        }

        Vector2 startPos = startPoint.position;
        Vector2 ballPos = morningStarRb.position;

        Vector2 toBall = ballPos - startPos;
        float distance = toBall.magnitude;

        if (distance <= 0.001f)
        {
            return;
        }

        bool isGrounded = IsPlayerGrounded();

        float currentSlackDistance = isGrounded ? groundSlackDistance : airSlackDistance;
        float currentStretchForMaxPull = isGrounded ? groundStretchForMaxPull : airStretchForMaxPull;

        // 鎖がまだ張っていないなら何もしない
        if (distance <= maxChainLength + currentSlackDistance)
        {
            if (showDebugLog)
            {
                Debug.Log($"Chain slack | Grounded: {isGrounded}, Distance: {distance:F2}");
            }

            return;
        }

        Vector2 dirToBall = toBall.normalized;

        float stretch = distance - maxChainLength;
        float effectiveStretch = Mathf.Max(0f, stretch - currentSlackDistance);

        float pullRate = Mathf.Clamp01(
            effectiveStretch / Mathf.Max(0.01f, currentStretchForMaxPull)
        );

        // 地上では、少しでも鎖が張っていて横方向に差があるなら最低限の横引っ張りを出す
        if (isGrounded && Mathf.Abs(toBall.x) >= minGroundHorizontalDistance)
        {
            pullRate = Mathf.Max(pullRate, minGroundPullRate);
        }

        if (showDebugLog)
        {
            Debug.Log(
                $"Chain taut | Grounded: {isGrounded}, Distance: {distance:F2}, Stretch: {stretch:F2}, PullRate: {pullRate:F2}, ToBallX: {toBall.x:F2}"
            );
        }

        if (pullRate > 0f)
        {
            PullPlayer(dirToBall, pullRate, isGrounded);
        }

        if (limitMorningStarDistance)
        {
            LimitMorningStarDistance(startPos, dirToBall);
        }

        RemoveOutwardMorningStarVelocity(dirToBall);
    }

    private bool IsPlayerGrounded()
    {
        if (player == null)
        {
            return false;
        }

        return player.IsGrounded;
    }

    private void PullPlayer(Vector2 dirToBall, float pullRate, bool isGrounded)
    {
        Vector2 pullDirection = dirToBall;

        // 地上では横方向だけ引っ張る
        if (isGrounded)
        {
            pullDirection = new Vector2(dirToBall.x, 0f);

            if (pullDirection.sqrMagnitude < 0.001f)
            {
                return;
            }

            pullDirection.Normalize();
        }

        Vector2 force = pullDirection * playerPullForce * pullRate * playerRb.mass;
        playerRb.AddForce(force, ForceMode2D.Force);

        float maxSpeed = isGrounded ? maxGroundPullSpeed : maxAirPullSpeed;

        Vector2 velocity = playerRb.linearVelocity;
        float speedTowardPull = Vector2.Dot(velocity, pullDirection);

        if (speedTowardPull > maxSpeed)
        {
            velocity -= pullDirection * (speedTowardPull - maxSpeed);
            playerRb.linearVelocity = velocity;
        }
    }

    private void LimitMorningStarDistance(Vector2 startPos, Vector2 dirToBall)
    {
        Vector2 targetBallPos = startPos + dirToBall * maxChainLength;

        morningStarRb.position = Vector2.Lerp(
            morningStarRb.position,
            targetBallPos,
            positionCorrectionRate
        );
    }

    private void RemoveOutwardMorningStarVelocity(Vector2 dirToBall)
    {
        Vector2 velocity = morningStarRb.linearVelocity;
        float outwardSpeed = Vector2.Dot(velocity, dirToBall);

        if (outwardSpeed > 0f)
        {
            velocity -= dirToBall * outwardSpeed;
            morningStarRb.linearVelocity = velocity;
        }
    }
}