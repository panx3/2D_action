using UnityEngine;

/// <summary>既存の磁石を、配置点を始点とするレールに沿って移動させる。</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-100)]
public sealed class MovementMagnet : MonoBehaviour
{
    [SerializeField, Tooltip("始点はこのオブジェクトの位置。以降の点をローカル座標で順に指定します。")]
    private Vector2[] waypoints = { new Vector2(4f, 0f) };
    [SerializeField, Min(0f), Tooltip("1秒あたりの移動距離。")]
    private float moveSpeed = 2f;
    [SerializeField, Tooltip("有効にすると終点から始点へレールを閉じて周回します。")]
    private bool loop;
    [SerializeField, Min(0f), Tooltip("往復時に始点と終点で待つ秒数。")]
    private float waitTimeAtEnds = 0.25f;

    [SerializeField, HideInInspector] private Rigidbody2D magnetBody;
    [SerializeField, HideInInspector] private LineRenderer rail;

    private int targetIndex = 1;
    private int direction = 1;
    private float waitRemaining;
    private Vector2 nextPosition;

    internal Rigidbody2D Body => magnetBody;
    internal Vector2 AnchorPosition => isActiveAndEnabled ? nextPosition : magnetBody.position;
    private int PointCount => 1 + (waypoints?.Length ?? 0);

    private void Awake()
    {
        RefreshRail();
        if (magnetBody != null)
            nextPosition = magnetBody.position;
    }

    private void FixedUpdate()
    {
        if (magnetBody == null)
            return;

        nextPosition = magnetBody.position;
        if (PointCount < 2 || moveSpeed <= 0f)
            return;

        if (waitRemaining > 0f)
        {
            waitRemaining -= Time.fixedDeltaTime;
            return;
        }

        Vector2 target = transform.TransformPoint(GetLocalPoint(targetIndex));
        float speed = Mathf.Min(moveSpeed, Physics2D.maxTranslationSpeed);
        nextPosition = Vector2.MoveTowards(magnetBody.position, target, speed * Time.fixedDeltaTime);
        magnetBody.MovePosition(nextPosition);
        if (nextPosition != target)
            return;

        if (loop)
        {
            targetIndex = (targetIndex + 1) % PointCount;
            return;
        }

        if (targetIndex == 0 || targetIndex == PointCount - 1)
        {
            direction = -direction;
            waitRemaining = waitTimeAtEnds;
        }
        targetIndex += direction;
    }

    public void RefreshRail()
    {
        if (rail == null)
            return;

        rail.loop = loop && PointCount > 2;
        rail.positionCount = PointCount >= 2 ? PointCount : 0;
        for (int i = 0; i < rail.positionCount; i++)
            rail.SetPosition(i, GetLocalPoint(i));
    }

    private Vector2 GetLocalPoint(int index)
    {
        return index == 0 ? Vector2.zero : waypoints[index - 1];
    }
}
