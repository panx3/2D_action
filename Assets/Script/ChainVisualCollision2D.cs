using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Chain visual 専用の非Alloc 2D collision補正。
/// Rigidbody2DやConstraintには触れず、描画用Pointだけを床・壁の表面へ沿わせる。
/// </summary>
public static class ChainVisualCollision2D
{
    private const float MinDistance = 0.0001f;

    public static int Resolve(
        Vector3[] points,
        int count,
        float radius,
        float skin,
        ContactFilter2D filter,
        RaycastHit2D[] hitBuffer)
    {
        if (points == null || count < 3 || radius <= 0f || hitBuffer == null || hitBuffer.Length == 0)
            return 0;

        int adjusted = 0;
        int lastIndex = Mathf.Min(count, points.Length) - 1;
        for (int i = 1; i < lastIndex; i++)
        {
            Vector3 candidate = points[i];
            if (!ResolveSegment(points[i - 1], candidate, radius, skin, filter, hitBuffer, out Vector2 safePoint))
                continue;

            points[i] = new Vector3(safePoint.x, safePoint.y, candidate.z);
            adjusted++;
        }

        return adjusted;
    }

    public static int Resolve(
        List<Vector3> points,
        float radius,
        float skin,
        ContactFilter2D filter,
        RaycastHit2D[] hitBuffer)
    {
        if (points == null || points.Count < 3 || radius <= 0f || hitBuffer == null || hitBuffer.Length == 0)
            return 0;

        int adjusted = 0;
        for (int i = 1; i < points.Count - 1; i++)
        {
            Vector3 candidate = points[i];
            if (!ResolveSegment(points[i - 1], candidate, radius, skin, filter, hitBuffer, out Vector2 safePoint))
                continue;

            points[i] = new Vector3(safePoint.x, safePoint.y, candidate.z);
            adjusted++;
        }

        return adjusted;
    }

    private static bool ResolveSegment(
        Vector2 previous,
        Vector2 candidate,
        float radius,
        float skin,
        ContactFilter2D filter,
        RaycastHit2D[] hitBuffer,
        out Vector2 resolved)
    {
        resolved = candidate;
        Vector2 movement = candidate - previous;
        float distance = movement.magnitude;
        if (distance <= MinDistance)
            return false;

        if (!TryCircleCast(previous, radius, movement / distance, distance, filter, hitBuffer, out RaycastHit2D hit))
            return false;

        Vector2 normal = hit.normal.sqrMagnitude > MinDistance ? hit.normal.normalized : Vector2.up;
        Vector2 surfaceCenter = hit.centroid + normal * Mathf.Max(0f, skin);

        // 残りの移動からCollider内部へ向かう成分だけを除き、床・壁の接線方向へ滑らせる。
        Vector2 remaining = candidate - surfaceCenter;
        float inward = Vector2.Dot(remaining, normal);
        if (inward < 0f)
            remaining -= normal * inward;

        float remainingDistance = remaining.magnitude;
        if (remainingDistance > MinDistance)
        {
            Vector2 direction = remaining / remainingDistance;
            if (TryCircleCast(surfaceCenter, radius, direction, remainingDistance, filter, hitBuffer, out RaycastHit2D slideHit))
            {
                Vector2 slideNormal = slideHit.normal.sqrMagnitude > MinDistance
                    ? slideHit.normal.normalized
                    : normal;
                surfaceCenter = slideHit.centroid + slideNormal * Mathf.Max(0f, skin);
            }
            else
            {
                surfaceCenter += remaining;
            }
        }

        resolved = surfaceCenter;
        return true;
    }

    private static bool TryCircleCast(
        Vector2 origin,
        float radius,
        Vector2 direction,
        float distance,
        ContactFilter2D filter,
        RaycastHit2D[] hitBuffer,
        out RaycastHit2D nearest)
    {
        int hitCount = Physics2D.CircleCast(origin, radius, direction, filter, hitBuffer, distance);
        nearest = default;
        float nearestDistance = float.PositiveInfinity;
        bool found = false;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit2D hit = hitBuffer[i];
            if (hit.collider == null || hit.collider.isTrigger)
                continue;

            if (hit.distance >= nearestDistance)
                continue;

            nearest = hit;
            nearestDistance = hit.distance;
            found = true;
        }

        return found;
    }
}
