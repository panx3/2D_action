using UnityEngine;

/// <summary>軽量な表示専用の電撃。判定には使わない。</summary>
public sealed class ElectricArcEffect : MonoBehaviour
{
    private LineRenderer line;
    private Material material;
    private LineRenderer branchA, branchB;
    private readonly Gradient flowGradient = new Gradient();
    private readonly GradientColorKey[] flowColors = new GradientColorKey[3];
    private readonly GradientAlphaKey[] flowAlpha = { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0.8f, 1f) };
    private void Awake()
    {
        GameObject child = new GameObject("ElectricArcVisual");
        child.transform.SetParent(transform, false);
        line = child.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.sortingOrder = 30;
        line.numCapVertices = 0;
        material = new Material(Shader.Find("Sprites/Default"));
        line.sharedMaterial = material;
        branchA = MakeBranch(child.transform, "BranchA");
        branchB = MakeBranch(child.transform, "BranchB");
    }
    private LineRenderer MakeBranch(Transform parent, string name)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false);
        var result = go.AddComponent<LineRenderer>(); result.useWorldSpace = true;
        result.sortingOrder = 30; result.sharedMaterial = material; return result;
    }
    private void OnDestroy() { if (material != null) Destroy(material); }
    public void Hide() { if (line != null) line.enabled = false; if (branchA != null) branchA.enabled = false; if (branchB != null) branchB.enabled = false; }
    private static float Hash(int seed) { float v = Mathf.Sin(seed * 12.9898f) * 43758.5453f; return v - Mathf.Floor(v); }
    private static Vector3 Pixel(Vector2 p) { return new Vector3(Mathf.Round(p.x * 32f) / 32f, Mathf.Round(p.y * 32f) / 32f, -0.1f); }
    private static void Configure(LineRenderer renderer, float intensity)
    {
        renderer.enabled = true; renderer.startWidth = renderer.endWidth = 0.035f * Mathf.Clamp(intensity, 0.4f, 2f);
        renderer.startColor = new Color(1f, 0.76f, 0.12f, 0.95f); renderer.endColor = new Color(1f, 1f, 0.82f, 0.95f);
    }
    private static void Lightning(LineRenderer renderer, Vector2 from, Vector2 to, int seed, float intensity)
    {
        Configure(renderer, intensity); renderer.positionCount = 6;
        Vector2 dir = to - from; Vector2 side = new Vector2(-dir.y, dir.x).normalized;
        for (int i = 0; i < 6; i++)
        {
            float t = i / 5f;
            float offset = i == 0 || i == 5 ? 0f : (Hash(seed + i * 7) - 0.5f) * Mathf.Min(0.26f, dir.magnitude * 0.4f);
            renderer.SetPosition(i, Pixel(Vector2.Lerp(from, to, t) + side * offset));
        }
    }
    public void DrawBurst(Vector2 center, float radius, float intensity)
    {
        if (line == null) return;
        int frame = Mathf.FloorToInt(Time.time * 12f);
        if (intensity <= 0f || frame % 5 == 0) { Hide(); return; }
        int seed = frame * 31 + GetInstanceID(); float angle = Hash(seed) * Mathf.PI * 2f;
        Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        Vector2 side = new Vector2(-dir.y, dir.x);
        Vector2 from = center + dir * radius * 0.38f;
        Vector2 to = center + dir * radius * (0.75f + Hash(seed + 3) * 0.25f);
        Lightning(line, from, to, seed, intensity);
        Lightning(branchA, Vector2.Lerp(from, to, 0.48f), to + side * radius * 0.35f, seed + 19, intensity * 0.65f);
        Vector2 dir2 = -dir * 0.5f + side * 0.86f;
        Lightning(branchB, center + dir2 * radius * 0.35f, center + dir2 * radius * 0.85f, seed + 37, intensity * 0.7f);
    }
    public void DrawBolt(Vector2 from, Vector2 to, float intensity)
    {
        if (line == null) return;
        int frame = Mathf.FloorToInt(Time.time * 12f);
        if (intensity <= 0f || frame % 4 == 0) { Hide(); return; }
        int seed = frame * 31 + GetInstanceID(); Lightning(line, from, to, seed, intensity);
        Vector2 delta = to - from; Vector2 side = new Vector2(-delta.y, delta.x).normalized;
        Lightning(branchA, Vector2.Lerp(from, to, 0.55f), Vector2.Lerp(from, to, 0.8f) + side * 0.18f, seed + 7, intensity * 0.6f);
        branchB.enabled = false;
    }
    public void DrawChain(MorningStarLauncher launcher, float intensity)
    {
        if (line == null) return;
        if (branchA != null) branchA.enabled = false; if (branchB != null) branchB.enabled = false;
        line.enabled = launcher != null && intensity > 0f;
        if (!line.enabled) return;
        line.startWidth = line.endWidth = 0.025f * Mathf.Clamp(intensity, 0.3f, 2f);
        float pulse = Mathf.PingPong(Time.time * 0.8f, 0.96f) + 0.02f;
        Color baseColor = new Color(1f, 0.65f, 0.12f);
        flowColors[0] = new GradientColorKey(baseColor, 0f);
        flowColors[1] = new GradientColorKey(new Color(1f, 1f, 0.8f), pulse);
        flowColors[2] = new GradientColorKey(baseColor, 1f);
        flowGradient.SetKeys(flowColors, flowAlpha); line.colorGradient = flowGradient;
        LineRenderer existing = launcher.ChainVisualRoot != null ? launcher.ChainVisualRoot.GetComponent<LineRenderer>() : null;
        ChainLinkVisualController links = launcher.ChainVisualRoot != null ? launcher.ChainVisualRoot.GetComponent<ChainLinkVisualController>() : null;
        if (links != null && links.IsDisplaying)
        {
            int count = 0;
            for (int i = 0; i < links.transform.childCount; i++)
            {
                Transform child = links.transform.GetChild(i);
                if (child.gameObject.activeSelf && child.name.StartsWith("ChainLink_")) count++;
            }
            line.positionCount = count + 2;
            line.SetPosition(0, launcher.VisualRopeAnchorWorld);
            int index = 1;
            for (int i = 0; i < links.transform.childCount; i++)
            {
                Transform child = links.transform.GetChild(i);
                if (!child.gameObject.activeSelf || !child.name.StartsWith("ChainLink_")) continue;
                Vector3 p = child.position;
                Vector2 delta = launcher.BallBody.position - launcher.VisualRopeAnchorWorld;
                Vector2 side = new Vector2(-delta.y, delta.x).normalized;
                p += (Vector3)(side * ((Hash(Mathf.FloorToInt(Time.time * 12f) * 29 + index * 7) - 0.5f) * 0.07f));
                p.z -= 0.1f; line.SetPosition(index++, p);
            }
            line.SetPosition(index, launcher.BallBody.position);
        }
        else if (existing != null && existing.enabled && existing.positionCount >= 2)
        {
            line.positionCount = existing.positionCount;
            for (int i = 0; i < line.positionCount; i++)
            {
                Vector3 p = existing.GetPosition(i);
                if (!existing.useWorldSpace) p = existing.transform.TransformPoint(p);
                p.z -= 0.1f;
                line.SetPosition(i, p);
            }
        }
        else
        {
            line.positionCount = 2;
            line.SetPosition(0, launcher.VisualRopeAnchorWorld);
            line.SetPosition(1, launcher.BallBody.position);
        }
        if (line.positionCount > 2 && Mathf.FloorToInt(Time.time * 12f) % 4 != 0)
        {
            int segment = 1 + Mathf.FloorToInt(Time.time * 12f) % (line.positionCount - 2);
            Vector2 start = line.GetPosition(segment);
            Vector2 delta = launcher.BallBody.position - launcher.VisualRopeAnchorWorld;
            Vector2 side = new Vector2(-delta.y, delta.x).normalized;
            Lightning(branchA, start, start + side * 0.16f, segment + Mathf.FloorToInt(Time.time * 12f), intensity * 0.65f);
        }
    }
}



