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
    [SerializeField] private RectTransform consoleBack;
    [SerializeField] private RectTransform consoleFront;
    [SerializeField] private UnityEngine.UI.Image focusVeil;
    [SerializeField] private UnityEngine.UI.Image energyHalo;
    [SerializeField] private UnityEngine.UI.RawImage reflectedScreen;
    [SerializeField] private UnityEngine.UI.Image[] arrowHalos;
    [SerializeField] private UnityEngine.UI.Image[] shardImages;
    [SerializeField] private Color crtColor = new Color(.24f, .5f, 1f);
    [SerializeField] private Color crystalColor = new Color(1f, .22f, .6f);
    [SerializeField, Range(0f, .5f)] private float crtSpill = .13f;
    [SerializeField, Range(0f, .5f)] private float pinkSpill = .12f;
    [SerializeField, Range(0f, .3f)] private float floorReflection = .07f;
    [SerializeField, Range(0f, 1f)] private float moteOpacity = .65f;
    [SerializeField, Min(.5f)] private float breathPeriod = 4.5f;
    private Vector2[] moteOrigins;
    private Material energyMaterial, foregroundMaterial;
    private Vector2[] moteSizes;

    private void Awake()
    {
        moteOrigins = new Vector2[motes.Length];
        moteSizes = new Vector2[motes.Length];
        for (int i = 0; i < motes.Length; i++)
        {
            moteOrigins[i] = motes[i].rectTransform.anchoredPosition;
            moteSizes[i] = motes[i].rectTransform.sizeDelta;
        }
        if (energyHalo != null)
        {
            energyMaterial = new Material(energyHalo.material);
            energyHalo.material = energyMaterial;
        }
        if (shardImages != null && shardImages.Length > 0)
        {
            foregroundMaterial = new Material(shardImages[0].material);
            for (int i = 0; i < shardImages.Length; i++)
                if (i < Mathf.Max(1, shardImages.Length / 4)) shardImages[i].material = foregroundMaterial;
        }
    }
    private void LateUpdate()
    {
        if (controller == null) return;
        if (consoleBack != null && consoleFront != null) consoleBack.anchoredPosition = consoleFront.anchoredPosition;
        float t = Time.unscaledTime;
        float breath = .9f + .1f * Mathf.Sin(t * Mathf.PI * 2 / Mathf.Max(.5f, breathPeriod));
        bool powered = controller.Phase == StageSelectController.SelectPhase.Zooming
            || controller.Phase == StageSelectController.SelectPhase.Loading
            || controller.Phase == StageSelectController.SelectPhase.Revealing;
        Color glow = crtColor;
        if (controller.SelectedStage != null) glow = Color.Lerp(crtColor, controller.SelectedStage.accent, .18f);
        bool signal = controller.SelectedStage != null && controller.SelectedStage.IsUnlocked && !controller.IsUnlockPresentation;
        Tint(screenGlow, signal ? glow : new Color(.35f,.4f,.5f), crtSpill * breath * (powered ? 1.3f : signal ? 1 : .35f));
        Tint(floorGlow, glow, floorReflection * breath * (signal ? 1 : .25f));
        Tint(cartridgeGlow, crystalColor, pinkSpill * breath);
        Tint(crystalGlow, crystalColor, pinkSpill * 1.5f * breath);
        Tint(powerGlow, crystalColor, powered ? .26f : .1f);
        if (focusVeil != null) focusVeil.color = new Color(.004f,.006f,.016f,controller.UnlockFocus*.36f);
        if (reflectedScreen != null) reflectedScreen.color = new Color(1,1,1,signal ? .65f : .12f);
        if (energyHalo != null)
        {
            energyMaterial.SetFloat("_Progress", controller.UnlockRelease);
            energyMaterial.SetColor("_Accent", Color.Lerp(crystalColor, new Color(1,.78f,.56f), .28f));
            Tint(energyHalo, Color.white, controller.UnlockLight*.8f);
        }
        if (foregroundMaterial != null)
            foregroundMaterial.SetFloat("_DepthBlur", controller.UnlockRelease>0 ? 1.5f*controller.UnlockRelease : 1.7f*(1-controller.UnlockAssemblyProgress));
        if (arrowHalos != null) foreach (var halo in arrowHalos) Tint(halo, crystalColor, .11f*breath);
        float visible = controller.Phase == StageSelectController.SelectPhase.Ready
            || controller.Phase == StageSelectController.SelectPhase.Switching ? 1 : .25f;
        for (int i = 0; i < motes.Length; i++)
        {
            var mote = motes[i];
            float phase = t * (.15f + i * .009f) + i * 2.4f;
            mote.rectTransform.anchoredPosition = moteOrigins[i]
                + new Vector2(Mathf.Round(Mathf.Sin(phase) * 7), Mathf.Round(Mathf.Cos(phase * .8f) * 13));
            mote.rectTransform.sizeDelta = moteSizes[i] * (1+.05f*Mathf.Sin(phase));
            Tint(mote, i%3==0 ? Color.Lerp(crystalColor,Color.white,.65f) : crystalColor,
                (.24f + .76f * Mathf.Pow(.5f + .5f * Mathf.Sin(phase * 1.8f), 2)) * moteOpacity * visible);
        }
    }
    private void OnDestroy()
    {
        if (energyMaterial != null) Destroy(energyMaterial);
        if (foregroundMaterial != null) Destroy(foregroundMaterial);
    }
    private static void Tint(UnityEngine.UI.Image image, Color color, float alpha)
    { if (image != null) { color.a = alpha; image.color = color; } }
}
