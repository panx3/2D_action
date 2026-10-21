using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>Shared phosphor selection treatment. Input and scene flow belong to the menu owner.</summary>
[DisallowMultipleComponent]
public sealed class CRTMenuItem : MonoBehaviour
{
    [SerializeField] private UnityEngine.UI.Button button;
    [SerializeField] private TMP_Text label;
    [SerializeField] private UnityEngine.UI.Graphic cursor;
    [SerializeField] private UnityEngine.UI.Graphic highlight;
    [SerializeField] private Color accent = new Color(1f, 0.32f, 0.63f);

    public UnityEngine.UI.Button Button => button;

    private void Update()
    {
        bool selected = button.interactable && EventSystem.current != null
            && EventSystem.current.currentSelectedGameObject == button.gameObject;
        label.color = !button.interactable ? new Color(0.43f, 0.39f, 0.48f)
            : selected ? accent : new Color(0.92f, 0.91f, 0.96f);
        cursor.enabled = selected;
        if (selected)
        {
            Color color = accent;
            color.a = Mathf.Repeat(Time.unscaledTime, 0.85f) < 0.66f ? 1f : 0.3f;
            cursor.color = color;
        }
        highlight.color = selected ? new Color(accent.r, accent.g, accent.b, 0.1f) : Color.clear;
    }
}
