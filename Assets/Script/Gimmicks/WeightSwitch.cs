using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class WeightSwitch : MonoBehaviour
{
    [Header("Detect Settings")]
    [SerializeField] private string targetTag = "morningstar";

    [Header("Visual Settings")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Transform visualRoot;
    [SerializeField] private Sprite offSprite;
    [SerializeField] private Sprite onSprite;
    [SerializeField] private Color offColor = Color.white;
    [SerializeField] private Color onColor = Color.white;
    [SerializeField] private Vector3 pressedLocalOffset = new Vector3(0f, -0.05f, 0f);
    [SerializeField, Min(0f)] private float visualMoveSpeed = 0.5f;

    [Header("Events")]
    [SerializeField] private UnityEvent onPressed;
    [SerializeField] private UnityEvent onReleased;

    private readonly HashSet<Collider2D> detectedObjects = new HashSet<Collider2D>();

    private bool isPressed = false;
    private Vector3 visualRestLocalPosition;

    public bool IsPressed => isPressed;

    private void Awake()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (visualRoot == null && spriteRenderer != null)
            visualRoot = spriteRenderer.transform;
        if (visualRoot != null)
            visualRestLocalPosition = visualRoot.localPosition;
        UpdateVisual();
    }

    private void Update()
    {
        if (visualRoot == null)
            return;

        Vector3 targetPosition = visualRestLocalPosition + (isPressed ? pressedLocalOffset : Vector3.zero);
        visualRoot.localPosition = Vector3.MoveTowards(
            visualRoot.localPosition,
            targetPosition,
            visualMoveSpeed * Time.deltaTime);
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

        Sprite stateSprite = isPressed ? onSprite : offSprite;
        if (stateSprite != null)
            spriteRenderer.sprite = stateSprite;
        spriteRenderer.color = isPressed ? onColor : offColor;
    }
}
