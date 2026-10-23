using UnityEngine;

/// <summary>A detached one-second flash, expanding light rings, and sparkling tail; never changes gameplay time.</summary>
[DisallowMultipleComponent]
public sealed class PinkJewelPickupVfx : MonoBehaviour
{
    [SerializeField] private SpriteRenderer echo;
    [SerializeField] private SpriteRenderer flash;
    [SerializeField] private SpriteRenderer halo;
    [SerializeField] private SpriteRenderer[] rings;
    [SerializeField] private ParticleSystem[] bursts;
    private float duration = 1f, intensity = 0.85f, elapsed;
    private Vector3 echoScale;
    private bool started;

    public void Play(Sprite jewel, Vector3 scale, int sortingLayer, int order,
        float seconds, int count, float speed, float brightness)
    {
        started = true;
        duration = Mathf.Clamp(seconds, 0.8f, 1.2f);
        intensity = brightness;
        echo.sprite = jewel;
        echoScale = scale;
        echo.transform.localScale = echoScale;
        foreach (SpriteRenderer sr in GetComponentsInChildren<SpriteRenderer>())
        {
            sr.sortingLayerID = sortingLayer;
            sr.sortingOrder = order + (sr == echo ? 1 : 2);
        }
        foreach (ParticleSystem ps in bursts)
        {
            var main = ps.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.45f, speed);
            main.startLifetime = new ParticleSystem.MinMaxCurve(duration * 0.55f, duration * 0.92f);
            var emission = ps.emission;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)(count / bursts.Length)) });
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.sortingLayerID = sortingLayer;
            renderer.sortingOrder = order + 3;
            ps.Play(true);
        }
        Animate();
    }

    private void Update()
    {
        if (!started) return;
        elapsed += Time.deltaTime;
        Animate();
        if (elapsed >= duration) Destroy(gameObject);
    }

    private void Animate()
    {
        float t = Mathf.Clamp01(elapsed / duration);
        float contact = Mathf.Clamp01(t / 0.16f);
        echo.transform.localScale = echoScale * Mathf.Lerp(1f, 1.12f, contact);
        echo.color = new Color(1f, 1f, 1f, 1f - contact);
        flash.transform.localScale = Vector3.one * Mathf.Lerp(1.4f, 2.8f, contact);
        flash.color = new Color(1f, 0.88f, 0.95f, intensity * (1f - contact));
        halo.transform.localScale = Vector3.one * Mathf.Lerp(2.6f, 4.4f, Mathf.Sqrt(t));
        halo.color = new Color(1f, 0.22f, 0.56f, intensity * 0.45f * Mathf.Pow(1f - t, 2f));
        for (int i = 0; i < rings.Length; i++)
        {
            float progress = Mathf.Clamp01((t - i * 0.1f) / 0.65f);
            rings[i].transform.localScale = Vector3.one * Mathf.Lerp(0.7f, 4.5f - i * 0.5f, 1f - Mathf.Pow(1f - progress, 2f));
            float alpha = t < i * 0.1f ? 0f : intensity * Mathf.Pow(1f - progress, 2f);
            rings[i].color = i == 0 ? new Color(1f, 0.55f, 0.8f, alpha) : new Color(1f, 0.95f, 1f, alpha * 0.8f);
        }
    }
}
