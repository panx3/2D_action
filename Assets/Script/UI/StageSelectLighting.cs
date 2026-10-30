using UnityEngine;

/// <summary>Presentation-only reflected light and drifting crystal motes for the cartridge theatre.</summary>
[DisallowMultipleComponent]
public sealed class StageSelectLighting : MonoBehaviour
{
    [SerializeField] private StageSelectController controller;
    [SerializeField] private UnityEngine.UI.Image screenGlow;
    [SerializeField] private UnityEngine.UI.Image floorGlow;
    [SerializeField] private UnityEngine.UI.Image cartridgeGlow;
    [SerializeField] private UnityEngine.UI.Image crystalGlow;
    [SerializeField] private UnityEngine.UI.Image powerGlow;
    [SerializeField] private UnityEngine.UI.Image[] motes;
    [SerializeField] private Color crtColor = new Color(.24f, .5f, 1f);
    [SerializeField] private Color crystalColor = new Color(1f, .22f, .6f);
    [SerializeField, Range(0f, .5f)] private float crtSpill = .13f;
    [SerializeField, Range(0f, .5f)] private float pinkSpill = .12f;
    [SerializeField, Range(0f, .3f)] private float floorReflection = .07f;
    [SerializeField, Range(0f, 1f)] private float moteOpacity = .65f;
    [SerializeField, Min(.5f)] private float breathPeriod = 4.5f;
    private Vector2[] moteOrigins;

    private void Awake()
    {
        moteOrigins = new Vector2[motes.Length];
        for (int i = 0; i < motes.Length; i++) moteOrigins[i] = motes[i].rectTransform.anchoredPosition;
    }
    private void LateUpdate()
    {
        if (controller == null) return;
        float t = Time.unscaledTime;
        float breath = .9f + .1f * Mathf.Sin(t * Mathf.PI * 2 / Mathf.Max(.5f, breathPeriod));
        bool powered = controller.Phase == StageSelectController.SelectPhase.Zooming
            || controller.Phase == StageSelectController.SelectPhase.Loading
            || controller.Phase == StageSelectController.SelectPhase.Revealing;
        Color glow = crtColor;
        if (controller.SelectedStage != null) glow = Color.Lerp(crtColor, controller.SelectedStage.accent, .18f);
        Tint(screenGlow, glow, crtSpill * breath * (powered ? 1.3f : 1));
        Tint(floorGlow, glow, floorReflection * breath);
        Tint(cartridgeGlow, crystalColor, pinkSpill * breath);
        Tint(crystalGlow, crystalColor, pinkSpill * 1.5f * breath);
        Tint(powerGlow, crystalColor, powered ? .26f : .1f);
        float visible = controller.Phase == StageSelectController.SelectPhase.Ready
            || controller.Phase == StageSelectController.SelectPhase.Switching ? 1 : .25f;
        for (int i = 0; i < motes.Length; i++)
        {
            var mote = motes[i];
            float phase = t * (.15f + i * .009f) + i * 2.4f;
            mote.rectTransform.anchoredPosition = moteOrigins[i]
                + new Vector2(Mathf.Round(Mathf.Sin(phase) * 7), Mathf.Round(Mathf.Cos(phase * .8f) * 13));
            Tint(mote, crystalColor, (.3f + .7f * Mathf.Pow(.5f + .5f * Mathf.Sin(phase * 1.8f), 2)) * moteOpacity * visible);
        }
    }
    private static void Tint(UnityEngine.UI.Image image, Color color, float alpha)
    { if (image != null) { color.a = alpha; image.color = color; } }
}
