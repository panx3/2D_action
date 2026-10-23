using UnityEngine;

/// <summary>Prefab内の灯りを点灯し、灯りごとに間隔をあけて短く点滅させる。</summary>
[DisallowMultipleComponent]
public sealed class StreetlampFlicker : MonoBehaviour
{
    [SerializeField] private ScreenLocalLight2D[] lights;
    [SerializeField] private bool lit = true;
    [SerializeField] private bool occasionalFlicker = true;
    [SerializeField] private Vector2 flickerInterval = new Vector2(8f, 18f);
    [SerializeField, Min(0.05f)] private float flickerDuration = 0.28f;

    private float[] nextFlicker;
    private float[] remaining;

    private void OnEnable()
    {
        if (lights == null || lights.Length == 0)
            lights = GetComponentsInChildren<ScreenLocalLight2D>(true);
        nextFlicker = new float[lights.Length];
        remaining = new float[lights.Length];
        for (int i = 0; i < lights.Length; i++)
        {
            Schedule(i);
            if (lights[i] != null) lights[i].lightEnabled = lit;
        }
    }

    private void Update()
    {
        for (int i = 0; i < lights.Length; i++)
        {
            if (lights[i] == null) continue;
            if (!lit || !occasionalFlicker)
            {
                lights[i].lightEnabled = lit;
                remaining[i] = 0f;
                continue;
            }
            nextFlicker[i] -= Time.deltaTime;
            if (nextFlicker[i] <= 0f && remaining[i] <= 0f)
                remaining[i] = Mathf.Max(0.05f, flickerDuration);
            if (remaining[i] > 0f)
            {
                remaining[i] -= Time.deltaTime;
                lights[i].lightEnabled = remaining[i] <= 0f ||
                    (Mathf.FloorToInt(remaining[i] / 0.045f) & 1) == 0;
                if (remaining[i] <= 0f) Schedule(i);
            }
        }
    }

    private void Schedule(int index) => nextFlicker[index] = Random.Range(
        Mathf.Max(0.5f, flickerInterval.x), Mathf.Max(0.5f, flickerInterval.x, flickerInterval.y));

    private void OnDisable()
    {
        if (lights == null) return;
        foreach (var light in lights) if (light != null) light.lightEnabled = lit;
    }
}
