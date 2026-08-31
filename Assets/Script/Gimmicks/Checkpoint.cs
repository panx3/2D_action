using System.Collections;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Playerが通過した復帰地点を登録するCheckpoint。
/// 既存のRespawn登録を維持しつつ、起動後はMonumentの発光を保持する。
/// </summary>
public class Checkpoint : MonoBehaviour
{
    [Header("Detect Settings")]
    [SerializeField] private string playerTag = "Player";

    [Header("References")]
    [FormerlySerializedAs("respawnController")]
    [SerializeField] private GimmickRespawnController gimmickRespawnController;
    [FormerlySerializedAs("respawnManager")]
    [SerializeField] private DeathRespawnManager deathRespawnManager;
    [SerializeField] private Transform respawnPoint;

    [Header("Visual Settings")]
    [SerializeField] private Color inactiveColor = new Color(0.52f, 0.47f, 0.56f, 1f);
    [SerializeField] private Color activeColor = Color.white;
    [SerializeField] private SpriteRenderer[] glowRenderers;
    [SerializeField, Min(0f)] private float glowTransitionDuration = 0.25f;
    [SerializeField, Range(0f, 1f)] private float glowPulseMinimum = 0.72f;
    [SerializeField, Min(0f)] private float glowPulseSpeed = 1.8f;

    private SpriteRenderer _spriteRenderer;
    private Color[] _glowBaseColors;
    private Coroutine _glowRoutine;
    private bool _isActivated;

    public bool IsActivated => _isActivated;

    private void Awake()
    {
        _spriteRenderer = GetComponent<SpriteRenderer>();

        if (respawnPoint == null)
            respawnPoint = transform;

        if (deathRespawnManager == null)
        {
            deathRespawnManager = FindAnyObjectByType<DeathRespawnManager>(FindObjectsInactive.Exclude);
            if (deathRespawnManager != null)
                Debug.LogWarning("[Checkpoint] DeathRespawnManager was auto-found. Assign it in Inspector to avoid wrong references.", this);
        }

        CacheGlowColors();
        SetVisualImmediate(false);
    }

    private void Update()
    {
        if (!_isActivated || _glowRoutine != null || glowRenderers == null)
            return;

        float pulse = Mathf.Lerp(glowPulseMinimum, 1f,
            (Mathf.Sin(Time.unscaledTime * glowPulseSpeed * Mathf.PI * 2f) + 1f) * 0.5f);
        SetGlowAlpha(pulse);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag(playerTag)) return;
        if (!PlayerColliderUtility.IsPlayerBody(other)) return;

        Activate();
    }

    private void Activate()
    {
        if (_isActivated)
            return;

        _isActivated = true;

        if (deathRespawnManager != null)
            deathRespawnManager.RegisterCheckpoint(respawnPoint.position);

        if (gimmickRespawnController != null)
            gimmickRespawnController.SetRespawnPoint(respawnPoint.position);

        if (_glowRoutine != null)
            StopCoroutine(_glowRoutine);
        _glowRoutine = StartCoroutine(GlowOnRoutine());
    }

    private IEnumerator GlowOnRoutine()
    {
        float duration = Mathf.Max(0f, glowTransitionDuration);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = duration > 0f ? Mathf.SmoothStep(0f, 1f, elapsed / duration) : 1f;
            if (_spriteRenderer != null)
                _spriteRenderer.color = Color.Lerp(inactiveColor, activeColor, t);
            SetGlowAlpha(t);
            yield return null;
        }

        SetVisualImmediate(true);
        _glowRoutine = null;
    }

    private void CacheGlowColors()
    {
        if (glowRenderers == null)
        {
            _glowBaseColors = System.Array.Empty<Color>();
            return;
        }

        _glowBaseColors = new Color[glowRenderers.Length];
        for (int i = 0; i < glowRenderers.Length; i++)
            _glowBaseColors[i] = glowRenderers[i] != null ? glowRenderers[i].color : Color.white;
    }

    private void SetVisualImmediate(bool active)
    {
        if (_spriteRenderer != null)
            _spriteRenderer.color = active ? activeColor : inactiveColor;
        SetGlowAlpha(active ? 1f : 0f);
    }

    private void SetGlowAlpha(float normalizedAlpha)
    {
        if (glowRenderers == null || _glowBaseColors == null)
            return;

        for (int i = 0; i < glowRenderers.Length && i < _glowBaseColors.Length; i++)
        {
            SpriteRenderer renderer = glowRenderers[i];
            if (renderer == null)
                continue;

            Color color = _glowBaseColors[i];
            color.a *= Mathf.Clamp01(normalizedAlpha);
            renderer.color = color;
        }
    }
}
