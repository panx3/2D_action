using System.Collections.Generic;
using UnityEngine;

/// <summary>輪郭からの短いランダム放電と、鉄球→少女への疎な電流。表示専用。</summary>
public sealed class ElectricArcEffect : MonoBehaviour
{
    private static Sprite[] frames;
    private static readonly Dictionary<Sprite, Vector2[][]> outlines = new Dictionary<Sprite, Vector2[][]>();
    private readonly List<SpriteRenderer> pool = new List<SpriteRenderer>();
    private readonly List<Vector2> path = new List<Vector2>();
    private readonly List<Edge> edges = new List<Edge>();
    private readonly List<SurfaceSpark> surfaceSparks = new List<SurfaceSpark>(2);
    private readonly List<FlowSpark> flowSparks = new List<FlowSpark>(3);
    private System.Random random;
    private int used, bridgeSeed;
    private float nextSurface = -1f, nextFlow = -1f, nextBridge = -1f, bridgeEnd, bridgeShape;
    private float opacity = 1f;
    private struct Edge { public Vector2 a, b, normal; public float length; }
    private struct SurfaceSpark
    {
        public Vector2 localPoint, localNormal;
        public float born, life, length, tilt, shapeAt;
        public int seed;
        public bool fork;
    }
    private struct FlowSpark
    {
        public float born, speed, span, side, shapeAt;
        public int seed;
        public bool fork;
    }

    private void Awake()
    {
        // Keep visual randomness separate from gameplay's UnityEngine.Random state.
        random = new System.Random(unchecked(GetInstanceID() * 397 ^ System.Environment.TickCount));
        if (frames == null || frames.Length == 0)
        {
            frames = Resources.LoadAll<Sprite>("Feedback/ElectricPixelArcs");
            System.Array.Sort(frames, (a, b) => string.CompareOrdinal(a.name, b.name));
        }
    }

    private float Range(float min, float max) => Mathf.Lerp(min, max, (float)random.NextDouble());
    private int Seed() => random.Next(1 << 20);
    private static Vector3 Pixel(Vector2 p) => new Vector3(Mathf.Round(p.x * 32f) / 32f, Mathf.Round(p.y * 32f) / 32f, -0.1f);

    private bool Begin(float intensity)
    {
        used = 0;
        opacity = 1f;
        if (intensity <= 0f || frames == null || frames.Length < 12) { Hide(); return false; }
        return true;
    }

    private void Place(int frame, Vector2 position, Vector2 scale, float angle)
    {
        if (used >= 128) return;
        if (used == pool.Count)
        {
            var visual = new GameObject("ElectricPixel");
            visual.transform.SetParent(transform, false);
            var renderer = visual.AddComponent<SpriteRenderer>();
            renderer.sortingOrder = 30;
            pool.Add(renderer);
        }
        var sprite = pool[used++];
        sprite.enabled = true;
        sprite.sprite = frames[frame % frames.Length];
        sprite.color = new Color(1f, 1f, 1f, opacity);
        sprite.transform.SetPositionAndRotation(Pixel(position), Quaternion.Euler(0f, 0f, angle));
        Vector3 parentScale = transform.lossyScale;
        sprite.transform.localScale = new Vector3(scale.x / Mathf.Max(0.001f, Mathf.Abs(parentScale.x)),
            scale.y / Mathf.Max(0.001f, Mathf.Abs(parentScale.y)), 1f);
    }

    private void End()
    {
        for (int i = used; i < pool.Count; i++) pool[i].enabled = false;
    }

    public void Hide()
    {
        foreach (var sprite in pool) if (sprite != null) sprite.enabled = false;
        used = 0;
        surfaceSparks.Clear();
        flowSparks.Clear();
        nextSurface = nextFlow = nextBridge = -1f;
    }

    private void Stroke(Vector2 from, Vector2 to, int seed, float thickness)
    {
        Vector2 delta = to - from;
        float length = delta.magnitude;
        if (length < 0.025f) return;
        int count = Mathf.Clamp(Mathf.CeilToInt(length / 1.5f), 1, 32);
        float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
        for (int i = 0; i < count; i++)
            Place(seed + i * 17, Vector2.Lerp(from, to, (i + 0.5f) / count),
                new Vector2(length / count * 1.15f, thickness), angle);
    }

    private bool BridgeVisible()
    {
        float now = Time.time;
        if (nextBridge < 0f) nextBridge = now + Range(0.01f, 0.08f);
        if (now >= nextBridge)
        {
            bridgeSeed = Seed();
            bridgeEnd = now + Range(0.09f, 0.16f);
            bridgeShape = now + Range(0.035f, 0.07f);
            nextBridge = bridgeEnd + Range(0.04f, 0.18f);
        }
        if (now >= bridgeEnd) return false;
        if (now >= bridgeShape) { bridgeSeed = Seed(); bridgeShape = now + Range(0.035f, 0.07f); }
        return true;
    }

    public void DrawBolt(Vector2 from, Vector2 to, float intensity)
    {
        if (!Begin(intensity)) return;
        if (BridgeVisible()) Stroke(from, to, bridgeSeed, Mathf.Clamp(intensity * 0.6f, 0.5f, 0.9f));
        End();
    }

    // Compatibility for existing callers. No expanding rings or radial star textures.
    public void DrawBurst(Vector2 center, float radius, float intensity)
    {
        if (!Begin(intensity)) return;
        if (BridgeVisible())
        {
            float angle = (bridgeSeed % 360) * Mathf.Deg2Rad;
            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Stroke(center + direction * radius, center + direction * (radius + 0.8f), bridgeSeed, 0.5f);
        }
        End();
    }

    private static Vector2[][] Outline(Sprite sprite)
    {
        if (outlines.TryGetValue(sprite, out var cached)) return cached;
        int count = sprite.GetPhysicsShapeCount();
        var loops = new Vector2[Mathf.Max(1, count)][];
        var points = new List<Vector2>();
        for (int i = 0; i < count; i++) { sprite.GetPhysicsShape(i, points); loops[i] = points.ToArray(); }
        if (count == 0)
        {
            var min = sprite.bounds.min; var max = sprite.bounds.max;
            loops[0] = new[] { new Vector2(min.x, min.y), new Vector2(max.x, min.y), new Vector2(max.x, max.y), new Vector2(min.x, max.y) };
        }
        outlines[sprite] = loops;
        return loops;
    }

    private static Vector2 World(SpriteRenderer renderer, Vector2 point)
    {
        return renderer.transform.TransformPoint(Flip(renderer, point));
    }

    private static Vector2 Flip(SpriteRenderer renderer, Vector2 point)
    {
        if (renderer.flipX) point.x = -point.x;
        if (renderer.flipY) point.y = -point.y;
        return point;
    }

    public static SpriteRenderer BallVisual(Rigidbody2D ball)
    {
        if (ball == null) return null;
        var rolling = ball.GetComponent<MorningStarRollingVisual>();
        var renderer = rolling != null && rolling.Visual != null ? rolling.Visual.GetComponent<SpriteRenderer>() : null;
        return renderer != null ? renderer : ball.GetComponentInChildren<SpriteRenderer>();
    }

    private float BuildEdges(SpriteRenderer renderer)
    {
        edges.Clear();
        float perimeter = 0f;
        foreach (var loop in Outline(renderer.sprite))
        {
            float area = 0f;
            for (int i = 0; i < loop.Length; i++)
            {
                Vector2 a = World(renderer, loop[i]), b = World(renderer, loop[(i + 1) % loop.Length]);
                area += a.x * b.y - b.x * a.y;
            }
            for (int i = 0; i < loop.Length; i++)
            {
                Vector2 a = World(renderer, loop[i]), b = World(renderer, loop[(i + 1) % loop.Length]);
                Vector2 delta = b - a; float length = delta.magnitude;
                if (length < 0.001f) continue;
                Vector2 normal = (area >= 0f ? new Vector2(delta.y, -delta.x) : new Vector2(-delta.y, delta.x)) / length;
                edges.Add(new Edge { a = a, b = b, normal = normal, length = length });
                perimeter += length;
            }
        }
        return perimeter;
    }

    private Edge EdgeAt(float distance, out Vector2 surface)
    {
        Edge edge = edges[edges.Count - 1];
        foreach (var candidate in edges)
        {
            edge = candidate;
            if (distance <= edge.length) break;
            distance -= edge.length;
        }
        surface = Vector2.Lerp(edge.a, edge.b, Mathf.Clamp01(distance / edge.length));
        return edge;
    }

    private float PickSurface(SpriteRenderer renderer, float perimeter, Vector2? focus, float radius)
    {
        float bestRatio = 0f, bestWeight = -1f;
        for (int attempt = 0; attempt < 24; attempt++)
        {
            float ratio = Range(0f, 1f);
            Edge candidate = EdgeAt(ratio * perimeter, out var point);
            float weight;
            if (focus.HasValue)
                weight = Mathf.Clamp01(1f - Vector2.Distance(point, focus.Value) / Mathf.Max(0.05f, radius));
            else
            {
                Vector2 delta = point - (Vector2)renderer.bounds.center;
                Vector3 extents = renderer.bounds.extents;
                float extremity = new Vector2(delta.x / Mathf.Max(0.01f, extents.x), delta.y / Mathf.Max(0.01f, extents.y)).magnitude;
                weight = 0.08f + 0.92f * Mathf.Pow(Mathf.Clamp01(extremity / 1.1f), 3f);
            }
            Vector2 outward = (candidate.normal + (point - (Vector2)renderer.bounds.center).normalized).normalized;
            if (outward.sqrMagnitude < 0.1f) outward = candidate.normal;
            Vector2 airEnd = point + outward * 0.7f;
            if (!ClipToAir(point + candidate.normal * 0.04f, ref airEnd)) weight = 0f;
            if (weight > bestWeight) { bestWeight = weight; bestRatio = ratio; }
            if (Range(0f, 1f) < weight) return ratio;
        }
        return bestRatio;
    }

    private static bool ClipToAir(Vector2 from, ref Vector2 to)
    {
        // Visual only: ignore actors/triggers, and never draw a discharge through solid scenery.
        foreach (var hit in Physics2D.OverlapPointAll(from))
            if (!hit.isTrigger && (hit.attachedRigidbody == null || hit.attachedRigidbody.bodyType == RigidbodyType2D.Static)) return false;
        Vector2 delta = to - from;
        float length = delta.magnitude;
        if (length < 0.01f) return false;
        Vector2 direction = delta / length;
        foreach (var hit in Physics2D.RaycastAll(from, direction, length))
            if (hit.collider != null && !hit.collider.isTrigger && (hit.rigidbody == null || hit.rigidbody.bodyType == RigidbodyType2D.Static))
                length = Mathf.Min(length, Mathf.Max(0f, hit.distance - 0.03f));
        to = from + direction * length;
        return length >= 0.12f;
    }

    public void DrawOutline(SpriteRenderer renderer, float intensity, Vector2? focus = null, Vector2? secondaryFocus = null, float focusRadius = 0.6f)
    {
        if (!Begin(renderer != null && renderer.sprite != null ? intensity : 0f)) return;
        float perimeter = BuildEdges(renderer);
        if (edges.Count == 0) { End(); return; }
        float now = Time.time;
        for (int i = surfaceSparks.Count - 1; i >= 0; i--)
            if (now - surfaceSparks[i].born >= surfaceSparks[i].life) surfaceSparks.RemoveAt(i);
        if (nextSurface < 0f) nextSurface = now + Range(0.02f, 0.12f);
        if (now >= nextSurface)
        {
            const int limit = 2;
            if (surfaceSparks.Count < limit)
            {
                Vector2? selected = secondaryFocus.HasValue && Range(0f, 1f) < 0.35f ? secondaryFocus : focus;
                float ratio = PickSurface(renderer, perimeter, selected, focusRadius);
                Edge origin = EdgeAt(ratio * perimeter, out var point);
                surfaceSparks.Add(new SurfaceSpark {
                    localPoint = Flip(renderer, renderer.transform.InverseTransformPoint(point)),
                    localNormal = Flip(renderer, renderer.transform.InverseTransformVector(origin.normal)),
                    born = now, life = Range(0.14f, 0.24f), length = Range(0.7f, 1.1f) * Mathf.Sqrt(intensity),
                    tilt = Range(-0.22f, 0.22f), seed = Seed(), shapeAt = now + Range(0.035f, 0.075f), fork = Range(0f, 1f) < 0.4f
                });
            }
            // Different gaps per object, including occasional quiet beats. Never catch up missed bursts.
            nextSurface = now + Range(0.1f, 0.25f) + (Range(0f, 1f) < 0.12f ? Range(0.15f, 0.28f) : 0f);
        }
        for (int i = 0; i < surfaceSparks.Count; i++)
        {
            var spark = surfaceSparks[i];
            if (now >= spark.shapeAt)
            {
                spark.seed = Seed(); spark.tilt = Range(-0.22f, 0.22f);
                spark.shapeAt = now + Range(0.035f, 0.075f); surfaceSparks[i] = spark;
            }
            // Preserve the root through normal/bone sprite changes; follow the object's transform only.
            Vector2 surface = World(renderer, spark.localPoint);
            Vector2 normal = renderer.transform.TransformVector(Flip(renderer, spark.localNormal)).normalized;
            Vector2 tangent = new Vector2(-normal.y, normal.x);
            Vector2 radial = (surface - (Vector2)renderer.bounds.center).normalized;
            Vector2 outward = (normal + radial + tangent * spark.tilt).normalized;
            if (outward.sqrMagnitude < 0.1f) outward = normal;
            Vector2 start = surface + normal * 0.04f, end = start + outward * spark.length;
            if (!ClipToAir(start, ref end)) continue;
            float age = (now - spark.born) / spark.life;
            opacity = age < 0.7f ? 1f : Mathf.Clamp01((1f - age) / 0.3f);
            Stroke(start, end, spark.seed, Mathf.Clamp(0.75f * intensity, 0.65f, 1.05f));
            if (spark.fork)
            {
                Vector2 fork = Vector2.Lerp(start, end, 0.48f);
                Vector2 forkEnd = fork + (outward + tangent * 0.8f).normalized * spark.length * 0.45f;
                if (ClipToAir(fork, ref forkEnd)) Stroke(fork, forkEnd, spark.seed + 7, 0.45f);
            }
        }
        End();
    }

    public static Vector2 ClosestOutlinePoint(SpriteRenderer renderer, Vector2 target)
    {
        if (renderer == null || renderer.sprite == null) return target;
        Vector2 closest = renderer.bounds.center; float best = float.PositiveInfinity;
        foreach (var loop in Outline(renderer.sprite)) for (int i = 0; i < loop.Length; i++)
        {
            Vector2 a = World(renderer, loop[i]), delta = World(renderer, loop[(i + 1) % loop.Length]) - a;
            Vector2 point = a + delta * Mathf.Clamp01(Vector2.Dot(target - a, delta) / Mathf.Max(0.00001f, delta.sqrMagnitude));
            float distance = (point - target).sqrMagnitude;
            if (distance < best) { best = distance; closest = point; }
        }
        return closest;
    }

    public void DrawBetweenOutlines(SpriteRenderer from, SpriteRenderer to, float intensity)
    {
        if (from == null || to == null) { Hide(); return; }
        Vector2 a = ClosestOutlinePoint(from, to.bounds.center), b = ClosestOutlinePoint(to, a);
        a = ClosestOutlinePoint(from, b);
        DrawBolt(a, b, intensity);
    }

    public void DrawChain(MorningStarLauncher launcher, float intensity)
    {
        if (!Begin(launcher != null ? intensity : 0f)) return;
        path.Clear();
        path.Add(launcher.VisualRopeAnchorWorld);
        Transform root = launcher.ChainVisualRoot;
        var links = root != null ? root.GetComponent<ChainLinkVisualController>() : null;
        var line = root != null ? root.GetComponent<LineRenderer>() : null;
        if (links != null && links.IsDisplaying)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.gameObject.activeSelf && child.name.StartsWith("ChainLink_")) path.Add(child.position);
            }
        }
        else if (line != null && line.enabled && line.positionCount >= 2)
        {
            for (int i = 0; i < line.positionCount; i++)
            {
                Vector3 p = line.GetPosition(i);
                path.Add(line.useWorldSpace ? p : line.transform.TransformPoint(p));
            }
        }
        path.Add(launcher.BallBody.position);
        float length = 0f, now = Time.time;
        for (int i = 1; i < path.Count; i++) length += Vector2.Distance(path[i - 1], path[i]);
        if (nextFlow < 0f) nextFlow = now + Range(0.03f, 0.17f);
        if (now >= nextFlow)
        {
            if (flowSparks.Count < 3 && length > 0.1f)
                flowSparks.Add(new FlowSpark { born = now, speed = Range(3.1f, 5.2f), span = Range(0.55f, 0.85f),
                    side = Range(0.07f, 0.12f) * (Range(0f, 1f) < 0.5f ? -1f : 1f), seed = Seed(),
                    shapeAt = now + Range(0.04f, 0.09f), fork = Range(0f, 1f) < 0.35f });
            nextFlow = now + Range(0.28f, 0.72f) + (Range(0f, 1f) < 0.2f ? 0.25f : 0f);
        }
        for (int i = flowSparks.Count - 1; i >= 0; i--)
        {
            var spark = flowSparks[i];
            float traveled = (now - spark.born) * spark.speed;
            if (traveled > length + spark.span) { flowSparks.RemoveAt(i); continue; }
            if (now >= spark.shapeAt) { spark.seed = Seed(); spark.shapeAt = now + Range(0.04f, 0.09f); flowSparks[i] = spark; }
            // Decreasing distance along this player-first path means ball -> girl.
            float distance = length - traveled;
            Vector2 from = PathPoint(Mathf.Clamp(distance + spark.span * 0.5f, 0f, length));
            Vector2 to = PathPoint(Mathf.Clamp(distance - spark.span * 0.5f, 0f, length));
            Vector2 tangent = (to - from).normalized, side = new Vector2(-tangent.y, tangent.x) * spark.side;
            opacity = 1f;
            Stroke(from + side, to + side, spark.seed, 0.7f);
            if (spark.fork)
            {
                Vector2 fork = (from + to) * 0.5f + side;
                Vector2 forkEnd = fork + side.normalized * 0.35f - tangent * 0.12f;
                if (ClipToAir(fork, ref forkEnd)) Stroke(fork, forkEnd, spark.seed + 7, 0.45f);
            }
        }
        End();
    }

    private Vector2 PathPoint(float distance)
    {
        for (int i = 1; i < path.Count; i++)
        {
            float segment = Vector2.Distance(path[i - 1], path[i]);
            if (distance <= segment) return Vector2.Lerp(path[i - 1], path[i], distance / Mathf.Max(0.00001f, segment));
            distance -= segment;
        }
        return path[path.Count - 1];
    }

    private void OnDisable() => Hide();
}
