using UnityEngine;

public class ChainConstraint2D : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform handAnchor;
    [SerializeField] private Rigidbody2D morningStarRb;
    [SerializeField] private Rigidbody2D playerRb;

    [Header("Chain Length")]
    [SerializeField] private float maxChainLength = 3.0f;
    [SerializeField] private float correctionThreshold = 0.01f;

    [Header("Hardness")]
    [Range(0f, 1f)]
    [SerializeField] private float positionCorrectionRate = 1.0f;

    [Header("Weight Feel")]
    [SerializeField] private bool slowPlayerWhenTaut = true;

    [Range(0f, 1f)]
    [SerializeField] private float playerDragWhenTaut = 0.25f;

    private void Reset()
    {
        morningStarRb = GetComponent<Rigidbody2D>();
    }

    private void FixedUpdate()
    {
        if (handAnchor == null || morningStarRb == null) return;

        Vector2 handPos = handAnchor.position;
        Vector2 ballPos = morningStarRb.position;

        Vector2 handToBall = ballPos - handPos;
        float distance = handToBall.magnitude;

        if (distance <= maxChainLength + correctionThreshold)
        {
            return;
        }

        Vector2 dir = handToBall.normalized;

        // 1. 鎖が最大長以上に伸びないよう、MorningStarの位置を補正
        Vector2 targetBallPos = handPos + dir * maxChainLength;
        Vector2 correctedPos = Vector2.Lerp(
            ballPos,
            targetBallPos,
            positionCorrectionRate
        );

        morningStarRb.position = correctedPos;

        // 2. さらに外側へ伸びようとする速度を消す
        Vector2 ballVelocity = morningStarRb.linearVelocity;
        float outwardSpeed = Vector2.Dot(ballVelocity, dir);

        if (outwardSpeed > 0f)
        {
            ballVelocity -= dir * outwardSpeed;
            morningStarRb.linearVelocity = ballVelocity;
        }

        // 3. Playerが鎖を引っ張っているとき、少し重さを返す
        if (slowPlayerWhenTaut && playerRb != null)
        {
            Vector2 playerVelocity = playerRb.linearVelocity;

            // PlayerがMorningStarから離れる方向へ動くほど、少し速度を削る
            Vector2 awayFromBallDir = -dir;
            float awaySpeed = Vector2.Dot(playerVelocity, awayFromBallDir);

            if (awaySpeed > 0f)
            {
                playerVelocity -= awayFromBallDir * awaySpeed * playerDragWhenTaut;
                playerRb.linearVelocity = playerVelocity;
            }
        }
    }
}
