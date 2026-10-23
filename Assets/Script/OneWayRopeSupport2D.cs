using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Per-rope top-surface history. The existing constraint remains the sole force
/// owner; this only supplies its shared polyline, including slack on supported rope.
/// </summary>
public sealed class OneWayRopeSupport2D
{
    private const int Samples = 16;
    private const float Epsilon = 0.001f;
    private struct Support
    {
        public OneWayPlatform Platform;
        public int StartExit;
        public int EndExit;
    }

    private readonly List<Support> supports = new List<Support>(4);
    private readonly Vector2[] previous = new Vector2[Samples + 1];
    private readonly Vector2[] probes = new Vector2[Samples + 1];
    private readonly List<Vector3> points = new List<Vector3>(48);
    private readonly List<Vector3> scratch = new List<Vector3>(48);
    private readonly RaycastHit2D[] hits = new RaycastHit2D[16];
    private readonly List<ContactPoint2D> playerContacts = new List<ContactPoint2D>(8);
    private readonly List<ContactPoint2D> ballContacts = new List<ContactPoint2D>(8);
    private readonly HashSet<OneWayPlatform> eligible = new HashSet<OneWayPlatform>();
    private readonly HashSet<OneWayPlatform> previouslyEligible = new HashSet<OneWayPlatform>();
    private bool hasPrevious;
    public bool IsSupporting => supports.Count > 0;

    public void Clear()
    {
        supports.Clear();
        hasPrevious = false;
        points.Clear();
        scratch.Clear();
        eligible.Clear();
        previouslyEligible.Clear();
        playerContacts.Clear();
        ballContacts.Clear();
    }

    public void Rebuild(List<Vector2> path, float maxLength, float sagStrength, float maxSag,
        float radius, float skin, ContactFilter2D filter, Rigidbody2D player, Rigidbody2D ball)
    {
        // Read the live physics contacts together, never Player's cached grounded
        // flag. Each endpoint must rest on a OneWayPlatform, but their floor
        // owners may differ. The existing approach test still decides which
        // surfaces the rope can actually acquire.
        playerContacts.Clear();
        ballContacts.Clear();
        if (player != null && player.simulated) player.GetContacts(playerContacts);
        if (ball != null && ball.simulated) ball.GetContacts(ballContacts);
        eligible.Clear();
        IReadOnlyList<OneWayPlatform> platforms = OneWayPlatform.RopeSurfaces;
        bool playerOnOneWay = false;
        bool ballOnOneWay = false;
        for (int p = 0; p < platforms.Count; p++)
        {
            OneWayPlatform platform = platforms[p];
            if (platform == null || !platform.TryGetRopeSurface(out Vector2 left, out _, out Vector2 up))
                continue;
            playerOnOneWay |= HasTopContact(player, playerContacts, platform, left, up);
            ballOnOneWay |= HasTopContact(ball, ballContacts, platform, left, up);
        }
        if (playerOnOneWay && ballOnOneWay)
            for (int p = 0; p < platforms.Count; p++)
                eligible.Add(platforms[p]);
        float baseLength = Length(path);
        float sag = Mathf.Min(maxSag, Mathf.Max(0f, maxLength - baseLength) * sagStrength);
        for (int i = 0; i <= Samples; i++)
            probes[i] = Sample(path, baseLength, i / (float)Samples, sag);

        // Inactive/removed platforms, a completely clear curve, or leaving either
        // edge releases support. History belongs to this rope, never to the floor.
        for (int i = supports.Count - 1; i >= 0; i--)
        {
            OneWayPlatform platform = supports[i].Platform;
            if (platform == null || !eligible.Contains(platform)
                || !platform.TryGetRopeSurface(out Vector2 left, out Vector2 right, out Vector2 up)
                || IsClear(left, right, up, radius + skin))
                supports.RemoveAt(i);
        }

        for (int p = 0; p < platforms.Count; p++)
        {
            OneWayPlatform platform = platforms[p];
            if (platform == null || !eligible.Contains(platform) || Contains(platform)
                || !platform.TryGetRopeSurface(out Vector2 left, out Vector2 right, out Vector2 up))
                continue;
            // The prefab's Default-layer terrain proxy must be in this rope's mask.
            Collider2D proxy = platform.transform.Find("MorningStarSolid")?.GetComponent<Collider2D>();
            if (proxy == null || !proxy.enabled || (filter.layerMask.value & (1 << proxy.gameObject.layer)) == 0)
                continue;

            bool initialAbove = true;
            bool usePrevious = hasPrevious && previouslyEligible.Contains(platform);
            if (!usePrevious)
                for (int i = 0; i < path.Count; i++)
                    initialAbove &= Vector2.Dot(path[i] - left, up) >= radius + skin - Epsilon;
            if (!usePrevious && !initialAbove)
                continue;

            Vector2 tangent = (right - left).normalized;
            float width = Vector2.Distance(left, right);
            bool acquired = false;
            for (int i = 1; i < Samples; i++)
            {
                Vector2 before = usePrevious ? previous[i] : Sample(path, baseLength, i / (float)Samples, 0f);
                float oldHeight = Vector2.Dot(before - left, up);
                float newHeight = Vector2.Dot(probes[i] - left, up);
                float height = radius + skin;
                if (oldHeight < height - Epsilon || newHeight > height || newHeight >= oldHeight - Epsilon)
                    continue;
                Vector2 crossing = Vector2.Lerp(before, probes[i], Mathf.Clamp01((oldHeight - height) / (oldHeight - newHeight)));
                float x = Vector2.Dot(crossing - left, tangent);
                if (x >= -radius && x <= width + radius)
                {
                    acquired = true;
                    break;
                }
            }
            if (acquired)
                supports.Add(new Support { Platform = platform });
        }

        for (int i = 0; i <= Samples; i++)
            previous[i] = probes[i];
        hasPrevious = true;
        previouslyEligible.Clear();
        previouslyEligible.UnionWith(eligible);
        if (!IsSupporting)
            return;

        // Preserve every ordinary-terrain corner. Sag is apportioned by segment
        // length and vanishes at those corners rather than cutting through them.
        points.Clear();
        points.Add(path[0]);
        for (int segment = 1; segment < path.Count; segment++)
        {
            float length = Vector2.Distance(path[segment - 1], path[segment]);
            int steps = Mathf.Max(1, Mathf.CeilToInt(Samples * length / Mathf.Max(baseLength, Epsilon)));
            float localSag = sag * length / Mathf.Max(baseLength, Epsilon);
            for (int i = 1; i <= steps; i++)
            {
                float t = i / (float)steps;
                Vector2 point = Vector2.Lerp(path[segment - 1], path[segment], t);
                point.y -= localSag * 4f * t * (1f - t);
                points.Add(point);
            }
        }
        ChainVisualCollision2D.Resolve(points, radius, skin, filter, hits);
        for (int i = 0; i < supports.Count; i++)
        {
            Support support = supports[i];
            support.Platform.TryGetRopeSurface(out Vector2 left, out Vector2 right, out Vector2 up);
            ClampToTop(ref support, left, right, up, radius + skin);
            supports[i] = support;
        }
        path.Clear();
        for (int i = 0; i < points.Count; i++)
            path.Add(points[i]);
    }

    private static bool HasTopContact(Rigidbody2D body, List<ContactPoint2D> contacts,
        OneWayPlatform platform, Vector2 left, Vector2 up)
    {
        if (body == null || !body.simulated) return false;
        for (int i = 0; i < contacts.Count; i++)
        {
            ContactPoint2D contact = contacts[i];
            if (!contact.enabled) continue; // Effector-disabled underside contacts are not grounding.
            Collider2D actor = contact.collider;
            Collider2D floor = contact.otherCollider;
            if (actor == null || actor.attachedRigidbody != body)
            {
                actor = contact.otherCollider;
                floor = contact.collider;
            }
            if (actor == null || floor == null || actor.attachedRigidbody != body
                || !actor.enabled || actor.isTrigger || !floor.enabled || floor.isTrigger
                || floor.GetComponentInParent<OneWayPlatform>() != platform)
                continue;
            Bounds bounds = actor.bounds;
            Vector2 center = (Vector2)bounds.center + body.position - (Vector2)body.transform.position;
            Vector2 normal = contact.normal;
            if (Vector2.Dot(normal, center - contact.point) < 0f) normal = -normal;
            float extent = Mathf.Abs(up.x) * bounds.extents.x + Mathf.Abs(up.y) * bounds.extents.y;
            Vector2 surfaceVelocity = floor.attachedRigidbody != null
                ? floor.attachedRigidbody.GetPointVelocity(contact.point) : Vector2.zero;
            // Reject a jump/launch immediately, even before the next simulation
            // removes its old contact manifold. No grace period keeps support alive.
            if (Vector2.Dot(normal, up) >= 0.5f
                && Vector2.Dot(center - left, up) - extent >= -0.04f
                && actor.Distance(floor).distance <= 0.04f
                && Vector2.Dot(body.GetPointVelocity(contact.point) - surfaceVelocity, up) <= 0.05f)
                return true;
        }
        return false;
    }

    private void ClampToTop(ref Support support, Vector2 left, Vector2 right, Vector2 up, float clearance)
    {
        Vector2 tangent = (right - left).normalized;
        float width = Vector2.Distance(left, right);
        float minX = -clearance;
        float maxX = width + clearance;
        Vector2 start = points[0];
        Vector2 end = points[points.Count - 1];
        float startX = Vector2.Dot(start - left, tangent);
        float endX = Vector2.Dot(end - left, tangent);
        float startHeight = Vector2.Dot(start - left, up);
        float endHeight = Vector2.Dot(end - left, up);
        bool startBelow = startHeight < clearance - Epsilon;
        bool endBelow = endHeight < clearance - Epsilon;
        if (!startBelow) support.StartExit = 0;
        if (!endBelow) support.EndExit = 0;
        if (startBelow && support.StartExit == 0) support.StartExit = startX < width * 0.5f ? -1 : 1;
        if (endBelow && support.EndExit == 0) support.EndExit = endX < width * 0.5f ? -1 : 1;
        bool startInside = startBelow && startX > minX && startX < maxX;
        bool endInside = endBelow && endX > minX && endX < maxX;
        scratch.Clear();
        scratch.Add(start);
        if (startInside)
        {
            float exit = support.StartExit < 0 ? minX : maxX;
            scratch.Add(left + tangent * exit + up * Mathf.Min(startHeight, -clearance));
            scratch.Add(left + tangent * exit + up * clearance);
        }
        for (int i = 1; i < points.Count; i++)
        {
            Vector2 a = points[i - 1];
            Vector2 b = points[i];
            float ax = Vector2.Dot(a - left, tangent);
            float bx = Vector2.Dot(b - left, tangent);
            // Insert edge crossings before lifting points. This prevents the line
            // between a supported sample and an outside sample cutting the edge.
            if (Mathf.Abs(bx - ax) > Epsilon)
            {
                float first = bx > ax ? minX : maxX;
                float second = bx > ax ? maxX : minX;
                AddCrossing(a, b, ax, bx, first, left, up, clearance);
                AddCrossing(a, b, ax, bx, second, left, up, clearance);
            }
            if (i == points.Count - 1 && endInside)
            {
                float exit = support.EndExit < 0 ? minX : maxX;
                scratch.Add(left + tangent * exit + up * clearance);
                scratch.Add(left + tangent * exit + up * Mathf.Min(endHeight, -clearance));
                scratch.Add(end);
                continue;
            }
            if (i < points.Count - 1 && bx >= minX && bx <= maxX)
            {
                float height = Vector2.Dot(b - left, up);
                if (height < clearance) b += up * (clearance - height);
            }
            scratch.Add(b);
        }
        points.Clear();
        points.AddRange(scratch);
    }

    private void AddCrossing(Vector2 a, Vector2 b, float ax, float bx, float edge,
        Vector2 left, Vector2 up, float clearance)
    {
        float t = (edge - ax) / (bx - ax);
        if (t <= Epsilon || t >= 1f - Epsilon) return;
        Vector2 point = Vector2.Lerp(a, b, t);
        float height = Vector2.Dot(point - left, up);
        if (height < clearance) point += up * (clearance - height);
        scratch.Add(point);
    }

    private bool IsClear(Vector2 left, Vector2 right, Vector2 up, float clearance)
    {
        Vector2 tangent = (right - left).normalized;
        float width = Vector2.Distance(left, right);
        bool beforeLeft = true, afterRight = true, above = true;
        for (int i = 0; i <= Samples; i++)
        {
            float x = Vector2.Dot(probes[i] - left, tangent);
            beforeLeft &= x < -clearance;
            afterRight &= x > width + clearance;
            above &= Vector2.Dot(probes[i] - left, up) > clearance + Epsilon;
        }
        return beforeLeft || afterRight || above;
    }

    private bool Contains(OneWayPlatform platform)
    {
        for (int i = 0; i < supports.Count; i++)
            if (supports[i].Platform == platform) return true;
        return false;
    }

    private static float Length(List<Vector2> path)
    {
        float length = 0f;
        for (int i = 1; i < path.Count; i++) length += Vector2.Distance(path[i - 1], path[i]);
        return length;
    }

    private static Vector2 Sample(List<Vector2> path, float length, float t, float sag)
    {
        float remaining = length * t;
        for (int i = 1; i < path.Count; i++)
        {
            float segment = Vector2.Distance(path[i - 1], path[i]);
            if (remaining <= segment || i == path.Count - 1)
            {
                float localT = segment > Epsilon ? Mathf.Clamp01(remaining / segment) : 0f;
                Vector2 point = Vector2.Lerp(path[i - 1], path[i], localT);
                point.y -= sag * segment / Mathf.Max(length, Epsilon) * 4f * localT * (1f - localT);
                return point;
            }
            remaining -= segment;
        }
        return path[0];
    }
}
