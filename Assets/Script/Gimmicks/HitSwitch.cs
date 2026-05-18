using System.Collections;
using UnityEngine;
using UnityEngine.Events;

public class HitSwitch : MonoBehaviour
{
    [Header("Detect Settings")]
    [SerializeField] private string targetTag = "morningstar";

    [Header("Visual Settings")]
    [SerializeField] private Color offColor = Color.gray;
    [SerializeField] private Color onColor = Color.yellow;

    [Header("Switch Settings")]
    [SerializeField] private bool autoOff = true;
    [SerializeField] private float autoOffDelay = 3f;

    [Header("Events")]
    [SerializeField] private UnityEvent onHit;
    [SerializeField] private UnityEvent onOff;

    private SpriteRenderer spriteRenderer;
    private bool isOn = false;
    private Coroutine autoOffCoroutine;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        UpdateVisual();
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (!collision.gameObject.CompareTag(targetTag)) return;

        Activate();
    }

    private void Activate()
    {
        isOn = true;
        UpdateVisual();

        Debug.Log("HitSwitch ON");
        onHit?.Invoke();

        if (autoOff)
        {
            if (autoOffCoroutine != null)
            {
                StopCoroutine(autoOffCoroutine);
            }

            autoOffCoroutine = StartCoroutine(AutoOffRoutine());
        }
    }

    private IEnumerator AutoOffRoutine()
    {
        yield return new WaitForSeconds(autoOffDelay);
        Deactivate();
    }

    private void Deactivate()
    {
        if (!isOn) return;

        isOn = false;
        UpdateVisual();

        Debug.Log("HitSwitch OFF");
        onOff?.Invoke();

        autoOffCoroutine = null;
    }

    private void UpdateVisual()
    {
        if (spriteRenderer == null) return;

        spriteRenderer.color = isOn ? onColor : offColor;
    }
}