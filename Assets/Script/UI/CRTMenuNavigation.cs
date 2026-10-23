using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Explicit wraparound navigation skips disabled result actions.</summary>
public static class CRTMenuNavigation
{
    public static void Configure(params UnityEngine.UI.Button[] buttons)
    {
        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] == null) continue;
            UnityEngine.UI.Button up = null, down = null;
            for (int j = 1; j <= buttons.Length; j++)
            {
                var candidate = buttons[(i - j + buttons.Length) % buttons.Length];
                if (candidate != null && candidate.interactable) { up = candidate; break; }
            }
            for (int j = 1; j <= buttons.Length; j++)
            {
                var candidate = buttons[(i + j) % buttons.Length];
                if (candidate != null && candidate.interactable) { down = candidate; break; }
            }
            buttons[i].navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = up, selectOnDown = down };
        }
    }
    public static void ClearSelection() => EventSystem.current?.SetSelectedGameObject(null);
}
