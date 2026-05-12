using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class ChainLineController : MonoBehaviour
{
    [Header("Chain Points")]
    [SerializeField] private Transform startPoint; // Playerの手元
    [SerializeField] private Transform endPoint;   // MorningStar

    [Header("Visual")]
    [SerializeField] private bool useSag = false;
    [SerializeField] private int sagPointCount = 8;
    [SerializeField] private float sagAmount = 0.25f;

    private LineRenderer line;

    private void Awake()
    {
        line = GetComponent<LineRenderer>();

        if (!useSag)
        {
            line.positionCount = 2;
        }
        else
        {
            line.positionCount = Mathf.Max(2, sagPointCount);
        }
    }

    private void LateUpdate()
    {
        if (startPoint == null || endPoint == null)
        {
            line.enabled = false;
            return;
        }

        line.enabled = true;

        if (!useSag)
        {
            DrawStraightChain();
        }
        else
        {
            DrawSagChain();
        }
    }

    private void DrawStraightChain()
    {
        line.positionCount = 2;
        line.SetPosition(0, startPoint.position);
        line.SetPosition(1, endPoint.position);
    }

    private void DrawSagChain()
    {
        int count = Mathf.Max(2, sagPointCount);
        line.positionCount = count;

        Vector3 start = startPoint.position;
        Vector3 end = endPoint.position;

        for (int i = 0; i < count; i++)
        {
            float t = i / (float)(count - 1);
            Vector3 pos = Vector3.Lerp(start, end, t);

            // 真ん中ほど下にたるませる
            float sag = Mathf.Sin(t * Mathf.PI) * sagAmount;
            pos.y -= sag;

            line.SetPosition(i, pos);
        }
    }

    public void SetPoints(Transform newStart, Transform newEnd)
    {
        startPoint = newStart;
        endPoint = newEnd;
    }
}
