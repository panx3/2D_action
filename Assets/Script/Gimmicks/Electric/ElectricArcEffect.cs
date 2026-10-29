using UnityEngine;

/// <summary>軽量な表示専用の電撃。判定には使わない。</summary>
public sealed class ElectricArcEffect : MonoBehaviour
{
    private LineRenderer line;
    private Material material;
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
    }
    private void OnDestroy() { if (material != null) Destroy(material); }
    public void Hide() { if (line != null) line.enabled = false; }
    public void DrawBurst(Vector2 center, float radius, float intensity)
    {
        if (line == null) return;
        line.enabled = intensity > 0f;
        if (!line.enabled) return;
        line.startWidth = line.endWidth = 0.025f * Mathf.Clamp(intensity, 0.3f, 2f);
        line.startColor = new Color(1f, 0.9f, 0.35f, 0.9f); line.endColor = new Color(1f, 1f, 0.8f, 0.8f);
        int frame = Mathf.FloorToInt(Time.time * 9f);
        float angle = (frame * 2.39996f + GetInstanceID()) % (Mathf.PI * 2f);
        line.positionCount = 7;
        for (int i = 0; i < 7; i++)
        {
            float a = angle + i * 0.8f;
            float r = radius * ((i % 2 == 0) ? 0.75f : 1f);
            line.SetPosition(i, new Vector3(center.x + Mathf.Cos(a) * r, center.y + Mathf.Sin(a) * r, -0.1f));
        }
    }
    public void DrawChain(MorningStarLauncher launcher, float intensity)
    {
        if (line == null) return;
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
                Vector3 p = child.position; p.z -= 0.1f; line.SetPosition(index++, p);
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
    }
}



