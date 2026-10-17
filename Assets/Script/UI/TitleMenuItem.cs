using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>Quiet title-only selection. The title controller remains the action owner.</summary>
[RequireComponent(typeof(UnityEngine.UI.Button))]
public sealed class TitleMenuItem : MonoBehaviour, IPointerEnterHandler
{
    [SerializeField] private TMP_Text label;
    [SerializeField] private UnityEngine.UI.Graphic cursor;
    [SerializeField] private UnityEngine.UI.Graphic line;
    [SerializeField] private UnityEngine.UI.Graphic highlight;
    [SerializeField] private TMP_Text description;
    [SerializeField] private TMP_Text number;
    [SerializeField] private TitleScreenController owner;
    private UnityEngine.UI.Button button;
    private Vector2 origin;
    private float selection;
    private float response;
    private bool wasSelected;
    private CanvasGroup transitionGroup;

    private void Awake()
    {
        button = GetComponent<UnityEngine.UI.Button>();
        origin = label.rectTransform.anchoredPosition;
        transitionGroup = GetComponent<CanvasGroup>();
        if (transitionGroup == null) transitionGroup = gameObject.AddComponent<CanvasGroup>();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (button.IsInteractable() && owner.InputReady)
            button.Select();
    }

    public void Confirm() => response = 1f;
    public void SetTransitionAlpha(float value)
    {
        if (transitionGroup != null) transitionGroup.alpha = Mathf.Clamp01(value);
    }

    private void Update()
    {
        bool selected = EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject;
        if (selected && !wasSelected && owner.InputReady) owner.PlayNavigationSound();
        wasSelected = selected;
        selection = Mathf.Lerp(selection, selected ? 1f : 0f, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 9f));
        response = Mathf.MoveTowards(response, 0f, Time.unscaledDeltaTime * 2.4f);
        label.color = Color.Lerp(new Color(.70f, .75f, .79f, .88f), new Color(1f, .98f, .93f), selection);
        label.rectTransform.anchoredPosition = origin;
        cursor.color = new Color(.94f, .93f, .88f, Mathf.Max(selection * (.72f + .12f * Mathf.Sin(Time.unscaledTime * 1.4f)), response));
        cursor.rectTransform.localScale = Vector3.one;
        if (line != null) line.color = Color.clear;
        if (highlight != null) highlight.color = Color.clear;
        if (description != null) description.color = Color.Lerp(new Color(.55f, .67f, .78f), new Color(.89f, .91f, .95f), selection);
        if (number != null) number.color = Color.Lerp(new Color(.47f, .6f, .72f), new Color(1f, .7f, .82f), selection);
    }
}
