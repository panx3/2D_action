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

    private void Awake()
    {
        button = GetComponent<UnityEngine.UI.Button>();
        origin = label.rectTransform.anchoredPosition;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (button.IsInteractable() && owner.InputReady)
            button.Select();
    }

    public void Confirm() => response = 1f;

    private void Update()
    {
        bool selected = EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject;
        if (selected && !wasSelected && owner.InputReady) owner.PlayNavigationSound();
        wasSelected = selected;
        selection = Mathf.MoveTowards(selection, selected ? 1f : 0f, Time.unscaledDeltaTime * 7f);
        response = Mathf.MoveTowards(response, 0f, Time.unscaledDeltaTime * 2.4f);
        label.color = Color.Lerp(new Color(0.72f, 0.80f, 0.88f), new Color(1f, 0.97f, 0.90f), selection);
        label.rectTransform.anchoredPosition = origin + Vector2.right * (selection * 6f + response * 3f);
        cursor.color = new Color(1f, 0.79f, 0.86f, Mathf.Max(selection, response));
        cursor.rectTransform.localScale = Vector3.one * (1f + selection * Mathf.Sin(Time.unscaledTime * 2.2f) * .12f);
        line.color = new Color(0.89f, 0.91f, 1f, Mathf.Max(selection * 0.55f, response));
        line.rectTransform.localScale = new Vector3(0.22f + selection * 0.48f + response * 0.3f, 1f, 1f);
        if (highlight != null) highlight.color = new Color(.65f, .75f, .98f, selection * .13f + response * .09f);
        if (description != null) description.color = Color.Lerp(new Color(.55f, .67f, .78f), new Color(.89f, .91f, .95f), selection);
        if (number != null) number.color = Color.Lerp(new Color(.47f, .6f, .72f), new Color(1f, .7f, .82f), selection);
    }
}
