using UnityEngine;
using UnityEngine.Events;

public class HitSwitch : MonoBehaviour
{
    [Header("Detect Settings")]
    [SerializeField] private string targetTag = "morningstar";

    [Header("Visual Settings")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Sprite offSprite;
    [SerializeField] private Sprite onSprite;
    [SerializeField] private Color offColor = Color.white;
    [SerializeField] private Color onColor = Color.white;

    [Header("Events")]
    [SerializeField] private UnityEvent onHit;
    [SerializeField] private UnityEvent onOff;

    [Header("Debug")]
    [SerializeField] private bool showDebugLog = false;

    private bool isOn = false;

    public bool IsOn => isOn;

    private void Awake()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        UpdateVisual(offColor);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (!collision.gameObject.CompareTag(targetTag)) return;

        Activate();
    }

    private void Activate()
    {
        if (isOn)
        {
            Deactivate();
            return;
        }

        isOn = true;
        UpdateVisual(onColor);

        if (showDebugLog)
        {
            Debug.Log("HitSwitch ON");
        }

        onHit?.Invoke();
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
    }

    private void UpdateVisual(Color color)
    {
        if (spriteRenderer == null) return;

        Sprite stateSprite = isOn ? onSprite : offSprite;
        if (stateSprite != null)
            spriteRenderer.sprite = stateSprite;
        spriteRenderer.color = color;
    }
}
