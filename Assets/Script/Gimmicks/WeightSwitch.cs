using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class WeightSwitch : MonoBehaviour
{
    [Header("Detect Settings")]
    [SerializeField] private string targetTag = "morningstar";

    [Header("Visual Settings")]
    [SerializeField] private Color offColor = Color.gray;
    [SerializeField] private Color onColor = Color.green;

    [Header("Events")]
    [SerializeField] private UnityEvent onPressed;
    [SerializeField] private UnityEvent onReleased;

    private readonly HashSet<Collider2D> detectedObjects = new HashSet<Collider2D>();

    private SpriteRenderer spriteRenderer;
    private bool isPressed = false;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        UpdateVisual();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag(targetTag)) return;

        detectedObjects.Add(other);
        UpdateSwitchState();
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (!other.CompareTag(targetTag)) return;

        detectedObjects.Remove(other);
        UpdateSwitchState();
    }

    private void UpdateSwitchState()
    {
        bool shouldBePressed = detectedObjects.Count > 0;

        if (isPressed == shouldBePressed) return;

        isPressed = shouldBePressed;
        UpdateVisual();

        if (isPressed)
        {
            Debug.Log("WeightSwitch ON");
            onPressed?.Invoke();
        }
        else
        {
            Debug.Log("WeightSwitch OFF");
            onReleased?.Invoke();
        }
    }

    private void UpdateVisual()
    {
        if (spriteRenderer == null) return;

        spriteRenderer.color = isPressed ? onColor : offColor;
    }
}