using System.Collections;
using UnityEngine;
using UnityEngine.Events;

public class HitSwitch : MonoBehaviour
{
    [Header("Detect Settings")]
    [SerializeField] private string targetTag = "morningstar";

    [Header("Switch Settings")]
    [SerializeField] private float activeDuration = 3f;
    [SerializeField] private bool resetTimerOnHit = true;

    [Header("Warning Settings")]
    [SerializeField] private bool flashBeforeOff = true;
    [SerializeField] private float warningTime = 1f;
    [SerializeField] private float flashInterval = 0.15f;

    [Header("Visual Settings")]
    [SerializeField] private Color offColor = Color.gray;
    [SerializeField] private Color onColor = Color.yellow;
    [SerializeField] private Color warningColor = Color.red;

    [Header("Events")]
    [SerializeField] private UnityEvent onHit;
    [SerializeField] private UnityEvent onOff;

    [Header("Debug")]
    [SerializeField] private bool showDebugLog = false;

    private SpriteRenderer spriteRenderer;
    private bool isOn = false;
    private Coroutine activeCoroutine;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        UpdateVisual(offColor);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (!collision.gameObject.CompareTag(targetTag)) return;

        Activate();
    }

    private void Activate()
    {
        if (isOn && !resetTimerOnHit)
        {
            return;
        }

        isOn = true;
        UpdateVisual(onColor);

        if (showDebugLog)
        {
            Debug.Log("HitSwitch ON");
        }

        onHit?.Invoke();

        if (activeCoroutine != null)
        {
            StopCoroutine(activeCoroutine);
        }

        activeCoroutine = StartCoroutine(ActiveTimerRoutine());
    }

    private IEnumerator ActiveTimerRoutine()
    {
        float normalTime = Mathf.Max(0f, activeDuration - warningTime);

        if (normalTime > 0f)
        {
            yield return new WaitForSeconds(normalTime);
        }

        if (flashBeforeOff && warningTime > 0f)
        {
            float elapsed = 0f;
            bool warningColorEnabled = false;

            while (elapsed < warningTime)
            {
                warningColorEnabled = !warningColorEnabled;
                UpdateVisual(warningColorEnabled ? warningColor : onColor);

                yield return new WaitForSeconds(flashInterval);
                elapsed += flashInterval;
            }
        }
        else
        {
            yield return new WaitForSeconds(warningTime);
        }

        Deactivate();
    }

    private void Deactivate()
    {
        if (!isOn) return;

        isOn = false;
        UpdateVisual(offColor);

        if (showDebugLog)
        {
            Debug.Log("HitSwitch OFF");
        }

        onOff?.Invoke();

        activeCoroutine = null;
    }

    private void UpdateVisual(Color color)
    {
        if (spriteRenderer == null) return;

        spriteRenderer.color = color;
    }
}