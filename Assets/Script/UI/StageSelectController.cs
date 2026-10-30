using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>Scene-local cartridge theatre; survives only the final CRT entry/reveal.</summary>
[DisallowMultipleComponent]
public sealed class StageSelectController : MonoBehaviour
{
    public enum SelectPhase { Opening, Ready, Switching, Inserting, Zooming, Loading, Revealing, Returning }

    [Header("Cartridges / Input")]
    [SerializeField] private StageSelectStage[] stages;
    [SerializeField] private InputActionAsset inputActions;
    [SerializeField] private string titleScene = "TitleScene";
    [SerializeField, Min(.1f)] private float repeatDelay = .34f;
    [SerializeField, Min(.08f)] private float repeatInterval = .18f;

    [Header("Scene View")]
    [SerializeField] private RectTransform world;
    [SerializeField] private Canvas canvas;
    [SerializeField] private CanvasGroup worldGroup;
    [SerializeField] private StageSelectPreview preview;
    [SerializeField] private UnityEngine.UI.RawImage screen;
    [SerializeField] private RectTransform cartridge;
    [SerializeField] private CanvasGroup cartridgeGroup;
    [SerializeField] private UnityEngine.UI.Image cartridgeImage;
    [SerializeField] private Sprite defaultCartridge;
    [SerializeField] private UnityEngine.UI.Image stageTitle;
    [SerializeField] private TMP_Text fallbackTitle;
    [SerializeField] private TMP_Text status;
    [SerializeField] private TMP_Text hints;
    [SerializeField] private TMP_Text loadingLabel;
    [SerializeField] private CanvasGroup controlsGroup;
    [SerializeField] private UnityEngine.UI.Image powerLamp;
    [SerializeField] private UnityEngine.UI.Image flash;
    [SerializeField] private RectTransform console;
    [SerializeField] private RectTransform crystal;
    [SerializeField] private CanvasGroup crystalGroup;
    [SerializeField] private CanvasGroup cracks;
    [SerializeField] private RectTransform[] shards;

    [Header("Timing (unscaled seconds)")]
    [SerializeField, Min(.1f)] private float gatherDuration = .55f;
    [SerializeField, Min(.1f)] private float crackDuration = .34f;
    [SerializeField, Min(.1f)] private float burstDuration = .38f;
    [SerializeField, Min(.1f)] private float switchDuration = .24f;
    [SerializeField, Min(.1f)] private float insertDuration = .52f;
    [SerializeField, Min(.01f)] private float alignDuration = .16f;
    [SerializeField, Min(.1f)] private float bootDuration = .26f;
    [SerializeField, Min(.1f)] private float zoomDuration = 1.0f;
    [SerializeField, Min(.1f)] private float revealDuration = .34f;
    [Header("Motion (1280 × 720 reference pixels)")]
    [SerializeField] private Vector2 insertPosition = new Vector2(2, -220);
    [SerializeField, Range(.3f, 1f)] private float insertScale = .46f;
    [SerializeField, Min(0f)] private float floatHeight = 6f;
    [SerializeField, Min(.5f)] private float floatPeriod = 3.4f;
    [SerializeField, Min(0f)] private float switchTravel = 460f;
    [SerializeField, Range(0f, 16f)] private float impactShake = 5f;

    [Header("Sound (replaceable; original chip cues are used when empty)")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip gatherClip, crackClip, burstClip, switchClip, insertClip, bootClip, deniedClip;
    [SerializeField, Range(0f, 1f)] private float effectVolume = .42f;

    private static StageSelectController entryOwner;
    private static string lastCartridge;
    private InputActionAsset ownedActions;
    private InputAction navigate, submit, cancel;
    private Vector2 home, consoleHome;
    private int selected, queuedDirection, heldDirection;
    private float nextRepeat, idleClock, deniedUntil;
    private AsyncOperation pendingLoad;
    private bool ownsTime, stageActivated;
    private float previousTimeScale;
    private AudioClip[] generatedCues;
    private string normalStatus;
    private bool showingGamepad;
    public SelectPhase Phase { get; private set; } = SelectPhase.Opening;
    public StageSelectStage SelectedStage => stages != null && selected < stages.Length ? stages[selected] : null;
    public int SelectedIndex => selected;
    public bool InputReady => Phase == SelectPhase.Ready;
    public static bool IsEnteringStage => entryOwner != null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { entryOwner = null; lastCartridge = null; }

    private void Awake()
    {
        if (stages == null || stages.Length == 0 || Array.Exists(stages, value => value == null)
            || world == null || canvas == null || preview == null || cartridge == null || inputActions == null)
        {
            Debug.LogError("[StageSelect] Assign cartridges, input actions and scene view references.", this);
            enabled = false;
            return;
        }
        ownedActions = Instantiate(inputActions);
        navigate = ownedActions.FindAction("UI/Navigate", true);
        submit = ownedActions.FindAction("UI/Submit", true);
        cancel = ownedActions.FindAction("UI/Cancel", true);
        ownedActions.FindActionMap("UI", true).Enable();
        home = cartridge.anchoredPosition;
        consoleHome = console.anchoredPosition;
        selected = Mathf.Max(0, Array.FindIndex(stages, value => value.name == lastCartridge));
        int pending = Array.FindIndex(stages, value => value.NeedsReveal);
        if (pending >= 0) selected = pending;
        generatedCues = new AudioClip[7];
        for (int i = 0; i < generatedCues.Length; i++) generatedCues[i] = ChipCue(i);
        cartridgeGroup.alpha = controlsGroup.alpha = crystalGroup.alpha = cracks.alpha = 0;
        loadingLabel.alpha = 0;
        flash.color = new Color(1, .8f, .94f, 0);
        foreach (var shard in shards) shard.gameObject.SetActive(false);
        ApplySelection();
        if (!SelectedStage.NeedsReveal) PresentRest();
    }

    private IEnumerator Start()
    {
        // Do not spend the crystal animation underneath the title's existing loading veil.
        while (TitleStageTransition.IsTransitioning) yield return null;
        if (SelectedStage.NeedsReveal) yield return Opening();
        else { PresentRest(); Phase = SelectPhase.Ready; }
    }

    private void Update()
    {
        if (ownedActions == null) return;
        if (cancel.WasPressedThisFrame()) Back();
        if (Phase == SelectPhase.Ready || Phase == SelectPhase.Switching)
        {
            float x = navigate.ReadValue<Vector2>().x;
            int direction = Mathf.Abs(x) > .55f ? (int)Mathf.Sign(x) : 0;
            if (direction == 0) heldDirection = 0;
            else if (direction != heldDirection)
            {
                heldDirection = direction; nextRepeat = Time.unscaledTime + repeatDelay;
                Move(direction);
            }
            else if (Time.unscaledTime >= nextRepeat)
            {
                nextRepeat = Time.unscaledTime + repeatInterval;
                Move(direction);
            }
            if (submit.WasPressedThisFrame()) Confirm();
        }
        else heldDirection = 0;
        if (Phase == SelectPhase.Ready)
        {
            idleClock += Time.unscaledDeltaTime;
            RectTransform floating = SelectedStage.IsUnlocked ? cartridge : crystal;
            floating.anchoredPosition = home + Vector2.up * Mathf.Round(Mathf.Sin(idleClock * Mathf.PI * 2 / Mathf.Max(.5f, floatPeriod)) * floatHeight);
            floating.localEulerAngles = new Vector3(0, 0, Mathf.Sin(idleClock * 1.2f) * .65f);
            if (deniedUntil > 0 && Time.unscaledTime >= deniedUntil)
            { deniedUntil = 0; status.text = normalStatus; status.color = SelectedStage.accent; }
        }
        bool pad = (navigate.activeControl?.device is Gamepad || submit.activeControl?.device is Gamepad)
            || (Gamepad.current != null && Keyboard.current == null);
        if (pad != showingGamepad || string.IsNullOrEmpty(hints.text))
        {
            showingGamepad = pad;
            hints.text = pad ? "L / 十字キー  えらぶ      A  いれる      B  もどる"
                : "← → / A D  えらぶ      ENTER  いれる      ESC  もどる";
        }
    }

    public void Move(int direction)
    {
        if (direction == 0 || stages.Length < 2) return;
        direction = Math.Sign(direction);
        if (Phase == SelectPhase.Switching) { queuedDirection = direction; return; }
        if (Phase != SelectPhase.Ready) return;
        // The coroutine changes Phase before its first yield; button/key bursts cannot start a second writer.
        StartCoroutine(Switch(direction));
    }

    public void Confirm()
    {
        if (Phase != SelectPhase.Ready) return;
        if (!SelectedStage.CanLaunch)
        {
            Play(deniedClip, 6, .5f);
            status.text = !SelectedStage.IsUnlocked ? SelectedStage.lockedMessage : SelectedStage.playable ? "このカセットはまだ起動できません" : SelectedStage.unavailableMessage;
            status.color = new Color(1, .6f, .71f); deniedUntil = Time.unscaledTime + 1.6f;
            preview.Disturbance = .22f;
            return;
        }
        try
        {
            pendingLoad = SceneManager.LoadSceneAsync(SelectedStage.sceneName, LoadSceneMode.Single);
            if (pendingLoad != null) pendingLoad.allowSceneActivation = false;
        }
        catch (Exception exception) { Debug.LogException(exception, this); }
        if (pendingLoad == null)
        {
            status.text = "よみこみに失敗しました。もういちどお試しください";
            return;
        }
        Phase = SelectPhase.Inserting;
        entryOwner = this;
        previousTimeScale = Time.timeScale;
        ownsTime = true;
        DontDestroyOnLoad(gameObject);
        canvas.sortingOrder = short.MaxValue - 1;
        GameBgmController.Instance?.FadeOut(insertDuration + bootDuration + zoomDuration);
        StartCoroutine(EnterStage());
    }

    public void Back()
    {
        if (Phase != SelectPhase.Opening && Phase != SelectPhase.Ready && Phase != SelectPhase.Switching) return;
        if (!Application.CanStreamedLevelBeLoaded(titleScene))
        { status.text = "タイトルSceneが登録されていません"; return; }
        StopAllCoroutines();
        Phase = SelectPhase.Returning;
        StartCoroutine(ReturnToTitle());
    }

    private IEnumerator Opening()
    {
        controlsGroup.alpha = cartridgeGroup.alpha = 0;
        Play(gatherClip, 0, .65f);
        for (int i = 0; i < shards.Length; i++) shards[i].gameObject.SetActive(true);
        yield return Animate(gatherDuration, t =>
        {
            for (int i = 0; i < shards.Length; i++)
            {
                float angle = i * 2.39996f;
                Vector2 from = home + new Vector2(Mathf.Cos(angle) * (670 + i % 3 * 60), Mathf.Sin(angle) * (420 + i % 4 * 30));
                Vector2 to = home + new Vector2(Mathf.Cos(angle) * (32 + i % 3 * 16), Mathf.Sin(angle) * (42 + i % 2 * 13));
                float arrival = Mathf.SmoothStep(0, 1, Mathf.Clamp01((t - i % 4 * .045f) / .865f));
                shards[i].anchoredPosition = Vector2.Lerp(from, to, arrival)
                    + new Vector2(-Mathf.Sin(angle), Mathf.Cos(angle)) * Mathf.Sin(arrival * Mathf.PI) * 65;
                // Each splinter is absorbed into the solid mass before the crack phase.
                shards[i].localScale = Vector3.one * Mathf.Lerp(.8f, 0, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.72f, 1, arrival)));
                shards[i].localEulerAngles = new Vector3(0, 0, i * 47 + 130 * t);
            }
            crystalGroup.alpha = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.45f, 1, t));
            crystal.localScale = Vector3.one * Mathf.Lerp(.2f, 1f, EaseOut(t));
        });
        Play(crackClip, 1, .7f);
        yield return Animate(crackDuration, t =>
        {
            cracks.alpha = t;
            crystal.anchoredPosition = home + new Vector2(Mathf.Round(Mathf.Sin(t * 80) * t * 3), 0);
            crystal.localScale = Vector3.one * (1 + t * .12f);
        });
        Play(burstClip, 2, .8f);
        crystalGroup.alpha = 0; cracks.alpha = 0;
        cartridgeGroup.alpha = 1;
        preview.Disturbance = .35f;
        yield return Animate(burstDuration, t =>
        {
            float eased = EaseOut(t);
            for (int i = 0; i < shards.Length; i++)
            {
                float angle = i * 2.39996f;
                Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                shards[i].anchoredPosition = home + direction * (40 + eased * (220 + i % 4 * 48)) + Vector2.down * t * t * 110;
                shards[i].localScale = Vector3.one * (1 - t);
                shards[i].localEulerAngles = new Vector3(0, 0, i * 47 + t * 240);
            }
            cartridge.localScale = Vector3.one * (1 + Mathf.Sin(t * Mathf.PI) * .08f);
            controlsGroup.alpha = Mathf.SmoothStep(0, 1, t);
            Flash(Mathf.Pow(1 - t, 4) * .7f);
            world.anchoredPosition = Vector2.right * Mathf.Round(Mathf.Sin(t * 70) * (1 - t) * impactShake);
        });
        foreach (var shard in shards) shard.gameObject.SetActive(false);
        world.anchoredPosition = Vector2.zero;
        cartridge.localScale = Vector3.one;
        crystal.anchoredPosition = home; crystal.localRotation = Quaternion.identity;
        crystal.localScale = Vector3.one;
        StageSelectProgress.CompleteReveal(SelectedStage.sceneName);
        Phase = SelectPhase.Ready;
    }

    private IEnumerator Switch(int direction)
    {
        Phase = SelectPhase.Switching;
        Play(switchClip, 3, .5f);
        Vector2 from = cartridge.anchoredPosition;
        cartridge.localRotation = Quaternion.identity;
        float half = Mathf.Max(.05f, switchDuration * .5f);
        yield return Animate(half, t =>
        {
            cartridge.anchoredPosition = Vector2.Lerp(from, home - Vector2.right * direction * switchTravel, t * t);
            cartridgeGroup.alpha = 1 - t;
            if (!SelectedStage.IsUnlocked) { cartridgeGroup.alpha = 0; crystalGroup.alpha = 1 - t; }
        });
        selected = (selected + direction + stages.Length) % stages.Length;
        ApplySelection();
        crystal.anchoredPosition = home;
        preview.Disturbance = .85f;
        yield return Animate(half, t =>
        {
            cartridge.anchoredPosition = Vector2.Lerp(home + Vector2.right * direction * switchTravel, home, EaseOut(t));
            cartridgeGroup.alpha = SelectedStage.IsUnlocked ? t : 0;
            crystalGroup.alpha = SelectedStage.IsUnlocked ? 0 : t;
        });
        Phase = SelectPhase.Ready;
        if (queuedDirection != 0)
        {
            int next = queuedDirection; queuedDirection = 0;
            Move(next);
        }
    }

    private IEnumerator EnterStage()
    {
        controlsGroup.alpha = 0;
        cartridge.localRotation = Quaternion.identity;
        Vector2 from = cartridge.anchoredPosition;
        // Align width above the slot, then keep that width while sliding physically down behind its front lip.
        Vector2 aligned = new Vector2(insertPosition.x, home.y - 6);
        yield return Animate(alignDuration, t =>
        {
            float eased = Mathf.SmoothStep(0, 1, t);
            cartridge.anchoredPosition = Vector2.Lerp(from, aligned, eased);
            cartridge.localScale = Vector3.one * Mathf.Lerp(1, insertScale, eased);
        });
        yield return Animate(insertDuration, t =>
        {
            cartridge.anchoredPosition = Vector2.Lerp(aligned, insertPosition, t * t);
        });
        cartridgeGroup.alpha = 0;
        Play(insertClip, 4, .9f);
        powerLamp.color = new Color(.65f, 1, .78f);
        preview.Disturbance = 1;
        yield return Animate(bootDuration, t =>
        {
            console.anchoredPosition = consoleHome + Vector2.down * Mathf.Round(Mathf.Sin(t * Mathf.PI * 3) * (1 - t) * impactShake);
            Flash(Mathf.Sin(t * Mathf.PI) * .32f);
        });
        Play(bootClip, 5, .7f);
        Phase = SelectPhase.Zooming;
        Canvas.ForceUpdateCanvases();
        Vector2 screenCenter = screen.rectTransform.anchoredPosition;
        Vector2 canvasSize = ((RectTransform)canvas.transform).rect.size;
        Vector2 screenSize = screen.rectTransform.rect.size;
        float targetScale = Mathf.Max(canvasSize.x / screenSize.x, canvasSize.y / screenSize.y) * 1.035f;
        yield return Animate(zoomDuration, t =>
        {
            float eased = t * t * (3 - 2 * t);
            float scale = Mathf.Lerp(1, targetScale, eased * eased);
            world.localScale = Vector3.one * scale;
            world.anchoredPosition = -screenCenter * scale * eased;
            preview.ClearBlend = Mathf.SmoothStep(0, .6f, t);
            Flash(Mathf.Pow(1 - t, 6) * .18f);
        });
        // The CRT fills the entire viewport now. Keep rendering it during asset/scene activation.
        Phase = SelectPhase.Loading;
        float waitStarted = Time.unscaledTime;
        while (pendingLoad.progress < .9f)
        {
            loadingLabel.alpha = Mathf.Clamp01(Time.unscaledTime - waitStarted - .5f);
            yield return null;
        }
        loadingLabel.alpha = 0;
        Time.timeScale = 0;
        pendingLoad.allowSceneActivation = true;
        while (!pendingLoad.isDone) yield return null;
        stageActivated = true;
        // Let destination cameras and its existing entrance setup produce a real frame.
        yield return null; yield return new WaitForEndOfFrame();
        Time.timeScale = 0;
        Phase = SelectPhase.Revealing;
        preview.Disturbance = .6f;
        yield return Animate(revealDuration, t =>
        {
            preview.ClearBlend = Mathf.Lerp(.6f, 1, t);
            worldGroup.alpha = 1 - Mathf.SmoothStep(0, 1, t);
        });
        FinishEntry();
        Destroy(gameObject);
    }

    private IEnumerator ReturnToTitle()
    {
        Play(switchClip, 3, .4f);
        // Async activation keeps this scene visible until the title is ready.
        AsyncOperation operation = SceneManager.LoadSceneAsync(titleScene);
        while (operation != null && !operation.isDone) yield return null;
    }

    private void ApplySelection()
    {
        StageSelectStage stage = SelectedStage;
        lastCartridge = stage.name;
        cartridgeImage.sprite = stage.cartridgeSprite != null ? stage.cartridgeSprite : defaultCartridge;
        cartridgeImage.color = stage.cartridgeTint;
        stageTitle.sprite = stage.titleSprite;
        stageTitle.enabled = stage.titleSprite != null;
        fallbackTitle.text = stage.stageName;
        fallbackTitle.gameObject.SetActive(stage.titleSprite == null);
        normalStatus = !stage.IsUnlocked ? stage.lockedMessage : stage.CanLaunch ? "このカセットで あそぶ" : stage.unavailableMessage;
        status.text = normalStatus; status.color = stage.accent;
        deniedUntil = 0;
        preview.Show(stage);
    }

    private void PresentRest()
    {
        cartridgeGroup.alpha = SelectedStage.IsUnlocked ? 1 : 0;
        crystalGroup.alpha = SelectedStage.IsUnlocked ? 0 : .85f;
        controlsGroup.alpha = 1; cracks.alpha = 0;
        cartridge.localScale = crystal.localScale = Vector3.one;
        crystal.anchoredPosition = home; crystal.localRotation = Quaternion.identity;
    }

    private static IEnumerator Animate(float duration, Action<float> draw)
    {
        duration = Mathf.Max(.01f, duration);
        float elapsed = 0;
        draw(0);
        while (elapsed < duration)
        {
            yield return null;
            elapsed = Mathf.Min(duration, elapsed + Time.unscaledDeltaTime);
            draw(elapsed / duration);
        }
    }
    private static float EaseOut(float t) => 1 - Mathf.Pow(1 - t, 3);
    private void Flash(float alpha) => flash.color = new Color(1, .85f, .96f, alpha);
    private void Play(AudioClip clip, int fallback, float gain)
    {
        if (audioSource != null) audioSource.PlayOneShot(clip != null ? clip : generatedCues[fallback], effectVolume * gain);
    }

    private static AudioClip ChipCue(int kind)
    {
        const int rate = 22050;
        float length = kind == 0 ? .55f : kind == 5 ? .3f : .12f;
        var samples = new float[Mathf.CeilToInt(length * rate)];
        uint seed = (uint)(139 + kind);
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)rate, p = t / length;
            float frequency = kind == 0 ? Mathf.Lerp(330, 1320, p) : kind == 5 ? (p < .35f ? 523 : p < .65f ? 659 : 1047)
                : kind == 6 ? 145 : kind == 4 ? Mathf.Lerp(240, 65, p) : kind == 3 ? 880 : 440;
            seed = seed * 1664525u + 1013904223u;
            float noise = ((seed >> 8) / 16777215f) * 2 - 1;
            float envelope = Mathf.Min(t * 500, 1) * Mathf.Pow(1 - p, kind == 0 ? .6f : 2);
            samples[i] = (Mathf.Sin(t * frequency * Mathf.PI * 2) * .2f + noise * (kind == 4 ? .25f : .025f)) * envelope;
        }
        var clip = AudioClip.Create("Cartridge chip cue " + kind, samples.Length, 1, rate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private void FinishEntry()
    {
        if (entryOwner == this) entryOwner = null;
        if (ownsTime) { Time.timeScale = stageActivated ? 1 : previousTimeScale; ownsTime = false; }
    }
    private void OnDestroy()
    {
        // An externally interrupted async load must not leave Unity's activation queue blocked.
        if (pendingLoad != null && !pendingLoad.isDone) pendingLoad.allowSceneActivation = true;
        FinishEntry();
        if (ownedActions != null) { ownedActions.Disable(); Destroy(ownedActions); }
        if (generatedCues != null) foreach (var clip in generatedCues) if (clip != null) Destroy(clip);
    }
}
