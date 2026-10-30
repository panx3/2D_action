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
    [SerializeField] private TMP_Text stageNumber;
    [SerializeField] private TMP_Text unlockLabel;
    [SerializeField] private CanvasGroup unlockAnnouncement;
    [SerializeField] private UnityEngine.UI.Image labelSheen;
    [SerializeField] private CanvasGroup controlsGroup;
    [SerializeField] private UnityEngine.UI.Image powerLamp;
    [SerializeField] private UnityEngine.UI.Image flash;
    [SerializeField] private UnityEngine.UI.Image fusionLight;
    [SerializeField] private RectTransform console;
    [SerializeField] private RectTransform crystal;
    [SerializeField] private CanvasGroup crystalGroup;
    [SerializeField] private CanvasGroup cracks;
    [SerializeField] private RectTransform[] shards;
    [SerializeField, HideInInspector] private Vector2[] assemblyPositions;
    [SerializeField, HideInInspector] private float[] assemblyAngles;
    [SerializeField] private CanvasGroup crystalPileGroup;
    [SerializeField] private RectTransform[] piledShards;

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
    [SerializeField, Min(0f)] private float chargeHoldDuration = .28f;
    [SerializeField, Min(.1f)] private float unlockSettleDuration = 1.15f;
    [Header("Motion (1280 × 720 reference pixels)")]
    [SerializeField] private Vector2 insertPosition = new Vector2(2, -220);
    [SerializeField, Range(.3f, 1f)] private float insertScale = .46f;
    [SerializeField, Min(0f)] private float floatHeight = 6f;
    [SerializeField, Min(.5f)] private float floatPeriod = 3.4f;
    [SerializeField, Min(0f)] private float switchTravel = 460f;
    [SerializeField, Range(0f, 16f)] private float impactShake = 5f;
    [Header("Unlock assembly")]
    [SerializeField, Range(1f,5f)] private float foregroundShardScale = 2.8f;
    [SerializeField, Range(1f,8f), Tooltip("集結先での破片倍率。配置も同じ比率で変え、輪郭同士の重なりを保つ")]
    private float assembledShardScale = 5.8f;
    [SerializeField, Range(0f,1f)] private float fusionWhiteStrength = .7f;

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
    private UnityEngine.UI.Image[] shardImages;
    private StageSelectCrystalFragment[] shardFragments;
    private UnityEngine.UI.Image[] piledShardImages;
    private Material sheenMaterial;
    public float UnlockAssemblyProgress { get; private set; }
    public float UnlockLight { get; private set; }
    public float UnlockRelease { get; private set; }
    public float UnlockFocus { get; private set; }
    public float EntryProgress { get; private set; }
    public SelectPhase Phase { get; private set; } = SelectPhase.Opening;
    public StageSelectStage SelectedStage => stages != null && selected < stages.Length ? stages[selected] : null;
    public int SelectedIndex => selected;
    public bool InputReady => Phase == SelectPhase.Ready;
    public bool IsUnlockPresentation => Phase == SelectPhase.Opening && SelectedStage != null && SelectedStage.NeedsReveal;
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
        generatedCues = new AudioClip[8];
        for (int i = 0; i < generatedCues.Length; i++) generatedCues[i] = ChipCue(i);
        cartridgeGroup.alpha = controlsGroup.alpha = crystalGroup.alpha = cracks.alpha = 0;
        loadingLabel.alpha = 0;
        flash.color = new Color(1, .8f, .94f, 0);
        foreach (var shard in shards) shard.gameObject.SetActive(false);
        shardImages=Array.ConvertAll(shards, shard => shard.GetComponent<UnityEngine.UI.Image>());
        shardFragments=Array.ConvertAll(shards, shard => shard.GetComponent<StageSelectCrystalFragment>());
        if(HasCrystalPile)
        {
            piledShardImages=Array.ConvertAll(piledShards, shard=>shard.GetComponent<UnityEngine.UI.Image>());
            crystalPileGroup.gameObject.SetActive(false);
            foreach(var shard in piledShards) shard.gameObject.SetActive(false);
        }
        SetFusionLight(0,0);
        if (unlockAnnouncement != null) unlockAnnouncement.alpha = 0;
        if (labelSheen != null)
        {
            sheenMaterial = new Material(labelSheen.material);
            labelSheen.material = sheenMaterial;
            labelSheen.color = Color.clear;
        }
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
            RectTransform floating = cartridge;
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
        UnlockRelease = 0;
        if(HasCrystalPile) { crystalPileGroup.gameObject.SetActive(true); crystalPileGroup.alpha=1; }
        if (unlockLabel != null) unlockLabel.text = "STAGE " + (selected + 1).ToString("00") + "  /  NEW CARTRIDGE";
        Play(gatherClip, 0, .65f);
        yield return Animate(gatherDuration, t =>
        {
            UnlockAssemblyProgress=t;
            UnlockFocus = Mathf.SmoothStep(0, 1, t * 2.5f);
            UnlockLight = 0;
            preview.FocusDepth = UnlockFocus;
            if(HasCrystalPile) DrawCrystalPile(t);
            else for (int i = 0; i < shards.Length; i++)
            {
                float angle = i * 2.39996f;
                // All fragments rise from below the frame, shrinking as they leave the foreground.
                float spread = ((i*11)%shards.Length)/(float)Mathf.Max(1,shards.Length-1)*2-1;
                Vector2 from = home + new Vector2(spread*(440+i%3*40),-425-i%5*30);
                Vector2 to = home + AssemblyCell(i);
                float flight = AssemblyFlight(t, i);
                float arrival=flight*flight*(3-2*flight);
                if(shardFragments[i]!=null) shardFragments[i].SetAssemblyProgress(flight);
                // Every chip has a fixed destination; the chips alone suggest the rectangular silhouette.
                shards[i].gameObject.SetActive(flight>0);
                shards[i].anchoredPosition = Vector2.Lerp(from, to, arrival)
                    + Vector2.right*Mathf.Sin(angle)*Mathf.Sin(arrival*Mathf.PI)*42;
                float overlap=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.68f,1,flight));
                float travellingScale=Mathf.Lerp(foregroundShardScale,1.05f,arrival);
                shards[i].localScale=Vector3.one*Mathf.Lerp(travellingScale,assembledShardScale,overlap);
                float spin=i*47+(i%2==0?1:-1)*(38+i%5*17)*arrival;
                shards[i].localEulerAngles=new Vector3(0,0,HasFittedAssembly ? Mathf.LerpAngle(spin,0,arrival) : spin);
                if(shardImages[i]!=null)
                {
                    Color color=Color.Lerp(new Color(.72f,.54f,.78f,0),Color.white,Mathf.SmoothStep(0,1,flight/.14f));
                    shardImages[i].color=color;
                }
            }
            crystalGroup.alpha=0;
            crystal.localScale=Vector3.one;
            SetFusionLight(0,0);
        });
        yield return Animate(chargeHoldDuration, t =>
        {
            float peak=Mathf.Max(0,Mathf.Sin(t*Mathf.PI));
            bool replaced=t>=.5f;
            // Swap once beneath the local flash, rather than morphing a rectangular placeholder image.
            crystalGroup.alpha=replaced ? 1 : 0;
            crystal.localScale=Vector3.one;
            crystal.anchoredPosition=home;
            foreach(var shard in shards) shard.gameObject.SetActive(!HasCrystalPile&&!replaced);
            if(HasCrystalPile) crystalPileGroup.gameObject.SetActive(!replaced);
            UnlockLight=peak*.95f;
            SetFusionLight(fusionWhiteStrength*peak*peak*1.36f,1);
            if(fusionLight!=null) fusionLight.rectTransform.localScale*=1+peak*.45f;
            Flash(Mathf.Pow(peak,12)*.22f,true);
        });
        Flash(0,true);
        foreach(var shard in shards) shard.gameObject.SetActive(false);
        Play(crackClip, 1, .7f);
        yield return Animate(crackDuration, t =>
        {
            cracks.alpha = Mathf.SmoothStep(0, 1, t);
            crystal.anchoredPosition = home + new Vector2(Mathf.Round(Mathf.Sin(t * 54) * t * 1.8f), 0);
            crystal.localScale = Vector3.one * (1 + t * t * .035f);
            UnlockLight = .72f + t * .28f;
            SetFusionLight(fusionWhiteStrength*(.25f+t*t*.28f),1);
        });
        Play(burstClip, 2, .8f);
        crystalGroup.alpha = 0; cracks.alpha = 0;
        cartridgeGroup.alpha = 1;
        preview.SignalLost = false;
        preview.SignalRecovery = 0;
        preview.Disturbance = .16f;
        for(int i=0;i<shards.Length;i++) { shards[i].gameObject.SetActive(true); if(shardImages[i]!=null) shardImages[i].color=Color.white; }
        yield return Animate(burstDuration, t =>
        {
            float eased = EaseOut(t);
            UnlockRelease = t;
            UnlockLight = Mathf.Pow(1-t, 1.5f);
            preview.SignalRecovery = Mathf.SmoothStep(0, 1, t);
            for (int i = 0; i < shards.Length; i++)
            {
                float angle = i * 2.39996f;
                Vector2 origin = AssemblyCell(i)*(HasFittedAssembly ? 1 : 1.15f);
                Vector2 direction = (origin.normalized + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle))*.25f).normalized;
                shards[i].anchoredPosition = home + origin + direction * eased * (180+i%6*35) + Vector2.down*t*t*110;
                float depth = .65f + i % 6 * .13f;
                shards[i].localScale = new Vector3(1, HasFittedAssembly || i%2==0 ? 1 : -1, 1)
                    * Mathf.Lerp(HasFittedAssembly ? assembledShardScale : .8f, depth, eased);
                shards[i].localEulerAngles = new Vector3(0, 0, (HasCrystalPile ? assemblyAngles[i] : HasFittedAssembly ? 0 : i * 47)
                    + t * (i%2==0?1:-1) * (85+i%7*29));
                if (shardImages[i] != null) shardImages[i].color = new Color(1, 1, 1, 1-Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.22f, 1, t)));
            }
            cartridge.localScale = Vector3.one * Mathf.Lerp(.94f, 1, eased);
            cartridge.anchoredPosition = home + Vector2.down * Mathf.Pow(1-t, 2) * 14;
            Flash(Mathf.Pow(1 - t, 6) * .28f,true);
            SetFusionLight(fusionWhiteStrength*Mathf.Pow(1-t,4)*.6f,1+t*.5f);
            world.anchoredPosition = Vector2.right * Mathf.Round(Mathf.Sin(t * 56) * Mathf.Pow(1-t,3) * impactShake*.5f);
        });
        foreach (var shard in shards) shard.gameObject.SetActive(false);
        world.anchoredPosition = Vector2.zero;
        cartridge.localScale = Vector3.one;
        crystal.anchoredPosition = home; crystal.localRotation = Quaternion.identity;
        crystal.localScale = Vector3.one;
        SetFusionLight(0,1);
        // Let the player register the new world before handing navigation back.
        Play(null, 7, .55f);
        yield return Animate(unlockSettleDuration, t =>
        {
            UnlockFocus = 1 - Mathf.SmoothStep(0, 1, t);
            preview.FocusDepth = UnlockFocus;
            controlsGroup.alpha = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.55f, 1, t));
            if (unlockAnnouncement != null) unlockAnnouncement.alpha = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0, .18f, t)) * (1-Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.72f, 1, t)));
            if (labelSheen != null)
            {
                labelSheen.color = new Color(1, 1, 1, Mathf.Sin(t * Mathf.PI));
                sheenMaterial.SetFloat("_Progress", t);
            }
        });
        UnlockFocus = UnlockLight = 0;
        preview.FocusDepth = 0;
        if (unlockAnnouncement != null) unlockAnnouncement.alpha = 0;
        if (labelSheen != null) labelSheen.color = Color.clear;
        StageSelectProgress.CompleteReveal(SelectedStage.sceneName);
        Phase = SelectPhase.Ready;
    }

    private float AssemblyFlight(float progress, int index)
    {
        if(HasCrystalPile) return Mathf.Clamp01((progress-index/(float)Mathf.Max(1,shards.Length-1)*.72f)/.25f);
        float delay=((index*7)%shards.Length)/(float)Mathf.Max(1,shards.Length-1)*.4f;
        return Mathf.Clamp01((progress-delay)/.56f);
    }
    private bool HasCrystalPile => crystalPileGroup!=null && piledShards!=null
        && piledShards.Length==shards.Length && assemblyAngles!=null && assemblyAngles.Length==shards.Length;

    private void DrawCrystalPile(float progress)
    {
        ((RectTransform)crystalPileGroup.transform).anchoredPosition=home;
        for(int i=0;i<shards.Length;i++)
        {
            float flight=AssemblyFlight(progress,i);
            float arrival=flight*flight*(3-2*flight);
            float seated=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.88f,1,flight));
            float spread=Mathf.Repeat(i*.618034f,1)*2-1;
            Vector2 from=home+new Vector2(spread*(440+i%3*40),-425-i%5*30);
            Vector2 target=AssemblyCell(i);
            shards[i].gameObject.SetActive(flight>0&&seated<1);
            shards[i].anchoredPosition=Vector2.Lerp(from,home+target,arrival)
                +Vector2.right*Mathf.Sin(i*2.39996f)*Mathf.Sin(arrival*Mathf.PI)*32;
            shards[i].localScale=Vector3.one*Mathf.Lerp(foregroundShardScale,assembledShardScale,arrival);
            shards[i].localEulerAngles=new Vector3(0,0,Mathf.LerpAngle(i*61,assemblyAngles[i],arrival));
            shardImages[i].color=new Color(1,1,1,Mathf.SmoothStep(0,1,flight/.12f)*(1-seated));
            // Once embedded, fragments stay put. The expanding pile is made of shaded sprites, not slices of a flat image.
            piledShards[i].gameObject.SetActive(seated>0);
            piledShards[i].anchoredPosition=target;
            piledShards[i].localScale=Vector3.one*assembledShardScale;
            piledShards[i].localEulerAngles=new Vector3(0,0,assemblyAngles[i]);
            piledShardImages[i].color=new Color(1,1,1,seated);
        }
    }
    private bool HasFittedAssembly => assemblyPositions != null && assemblyPositions.Length == shards.Length;

    private Vector2 AssemblyCell(int index)
    {
        if(HasFittedAssembly) return assemblyPositions[index]*assembledShardScale;
        int columns=Mathf.Max(1,Mathf.CeilToInt(Mathf.Sqrt(shards.Length*4f/3f)));
        int rows=Mathf.CeilToInt(shards.Length/(float)columns), slot=(index*17)%shards.Length;
        return new Vector2((slot%columns-(columns-1)*.5f)*56+Mathf.Sin(index*2.4f)*6,
            (slot/columns-(rows-1)*.5f)*23+Mathf.Cos(index*1.7f)*5)*(assembledShardScale/6.4f);
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
            if (!SelectedStage.IsUnlocked) cartridgeGroup.alpha = 0;
        });
        selected = (selected + direction + stages.Length) % stages.Length;
        ApplySelection();
        crystal.anchoredPosition = home;
        preview.Disturbance = .16f;
        yield return Animate(half, t =>
        {
            cartridge.anchoredPosition = Vector2.Lerp(home + Vector2.right * direction * switchTravel, home, EaseOut(t));
            cartridgeGroup.alpha = SelectedStage.IsUnlocked ? t : 0;
            crystalGroup.alpha = 0;
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
        Flash(0);
        cartridge.localRotation = Quaternion.identity;
        Vector2 from = cartridge.anchoredPosition;
        // Align width above the slot, then keep that width while sliding physically down behind its front lip.
        Vector2 aligned = new Vector2(insertPosition.x, home.y - 6);
        yield return Animate(alignDuration, t =>
        {
            float eased = Mathf.SmoothStep(0, 1, t);
            cartridge.anchoredPosition = Vector2.Lerp(from, aligned, eased);
            cartridge.localScale = Vector3.one * Mathf.Lerp(1, insertScale, eased);
            cartridge.localEulerAngles = new Vector3(0, 0, Mathf.Sin(t * Mathf.PI) * -1.4f);
        });
        yield return Animate(insertDuration, t =>
        {
            cartridge.anchoredPosition = Vector2.Lerp(aligned, insertPosition, t*t*(3-2*t));
        });
        // The cartridge remains seated and visible above the lip, like a top-loading console.
        Play(insertClip, 4, .9f);
        powerLamp.color = new Color(.65f, 1, .78f);
        preview.Disturbance = .24f;
        yield return Animate(bootDuration, t =>
        {
            console.anchoredPosition = consoleHome + Vector2.down * Mathf.Round(Mathf.Sin(t * Mathf.PI * 3) * (1 - t) * impactShake);
            cartridge.anchoredPosition = insertPosition + Vector2.down * Mathf.Sin(t * Mathf.PI * 2) * (1 - t) * 1.5f;
            preview.EntryProgress = t;
            EntryProgress = t * .18f;
        });
        Play(bootClip, 5, .7f);
        Phase = SelectPhase.Zooming;
        Canvas.ForceUpdateCanvases();
        Vector2 screenCenter = screen.rectTransform.anchoredPosition;
        Vector2 canvasSize = ((RectTransform)canvas.transform).rect.size;
        Vector2 screenSize = screen.rectTransform.rect.size;
        float targetScale = Mathf.Max(canvasSize.x / screenSize.x, canvasSize.y / screenSize.y) * 1.14f;
        yield return Animate(zoomDuration, t =>
        {
            float eased = t * t * t * (t * (t * 6 - 15) + 10);
            float scale = Mathf.Lerp(1, targetScale, eased * eased);
            world.localScale = Vector3.one * scale;
            world.anchoredPosition = -screenCenter * scale * eased;
            preview.ClearBlend = Mathf.SmoothStep(0, .6f, t);
            EntryProgress = Mathf.Lerp(.18f, 1, t);
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
        preview.Disturbance = .08f;
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
        if (stageNumber != null) stageNumber.text = (selected+1).ToString("00") + "  /  " + stages.Length.ToString("00");
        status.text = normalStatus; status.color = stage.accent;
        deniedUntil = 0;
        preview.Show(stage);
    }

    private void PresentRest()
    {
        cartridgeGroup.alpha = SelectedStage.IsUnlocked ? 1 : 0;
        crystalGroup.alpha = 0;
        controlsGroup.alpha = 1; cracks.alpha = 0;
        cartridge.localScale = crystal.localScale = Vector3.one;
        crystal.anchoredPosition = home; crystal.localRotation = Quaternion.identity;
    }

#if UNITY_EDITOR
    /// <summary>Refresh the live debug cartridge through this presentation's own state/visual writers.</summary>
    public bool RefreshDebugSelection(string sceneName)
    {
        if (!isActiveAndEnabled || !didStart || ownedActions == null
            || (Phase != SelectPhase.Ready && Phase != SelectPhase.Opening && Phase != SelectPhase.Switching)) return false;
        int index=Array.FindIndex(stages,stage=>stage.sceneName==sceneName);
        if(index<0) return false;
        StopAllCoroutines();
        if(audioSource!=null) audioSource.Stop();
        Phase=SelectPhase.Opening;
        selected=index; queuedDirection=heldDirection=0; idleClock=0;
        world.anchoredPosition=Vector2.zero; world.localScale=Vector3.one; worldGroup.alpha=1;
        cartridge.anchoredPosition=home; cartridge.localRotation=Quaternion.identity;
        console.anchoredPosition=consoleHome;
        UnlockAssemblyProgress=UnlockLight=UnlockRelease=UnlockFocus=EntryProgress=0;
        preview.ClearBlend=preview.FocusDepth=preview.EntryProgress=preview.Disturbance=0;
        preview.SignalRecovery=1;
        Flash(0); SetFusionLight(0,0);
        foreach(var shard in shards) shard.gameObject.SetActive(false);
        if(HasCrystalPile)
        {
            crystalPileGroup.gameObject.SetActive(false);
            foreach(var shard in piledShards) shard.gameObject.SetActive(false);
        }
        if(unlockAnnouncement!=null) unlockAnnouncement.alpha=0;
        if(labelSheen!=null) labelSheen.color=Color.clear;
        ApplySelection();
        PresentRest();
        if(SelectedStage.NeedsReveal) StartCoroutine(Opening());
        else Phase=SelectPhase.Ready;
        return true;
    }
#endif

    private static IEnumerator Animate(float duration, Action<float> draw)
    {
        duration = Mathf.Max(.01f, duration);
        float elapsed = 0;
        draw(0);
        while (elapsed < duration)
        {
            yield return null;
            // Scene activation / first shader use can stall one frame. Keep the visible assembly and
            // insertion from jumping straight to their endpoint when that delayed frame arrives.
            elapsed = Mathf.Min(duration, elapsed + Mathf.Min(Time.unscaledDeltaTime, .05f));
            draw(elapsed / duration);
        }
    }
    private static float EaseOut(float t) => 1 - Mathf.Pow(1 - t, 3);
    private void Flash(float alpha,bool white=false) => flash.color = white ? new Color(1,1,1,alpha) : new Color(1,.85f,.96f,alpha);
    private void SetFusionLight(float strength,float growth)
    {
        if(fusionLight==null) return;
        fusionLight.color=new Color(1,.98f,1,strength);
        fusionLight.rectTransform.anchoredPosition=home;
        fusionLight.rectTransform.localScale=Vector3.one*Mathf.Lerp(.25f,1,Mathf.Clamp01(growth));
    }
    private void Play(AudioClip clip, int fallback, float gain)
    {
        if (audioSource != null) audioSource.PlayOneShot(clip != null ? clip : generatedCues[fallback], effectVolume * gain);
    }

    private static AudioClip ChipCue(int kind)
    {
        const int rate = 44100;
        float length = kind == 0 ? 1.25f : kind == 5 ? .75f : kind == 7 ? 1.6f : kind == 3 ? .18f : .22f;
        var samples = new float[Mathf.CeilToInt(length * rate)];
        float[] notes = { 523.25f, 659.25f, 783.99f, 1046.5f };
        uint seed = (uint)(139 + kind);
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)rate, p = t / length;
            seed = seed * 1664525u + 1013904223u;
            float noise = ((seed >> 8) / 16777215f) * 2 - 1;
            float attack = Mathf.Min(t * 130, 1), v = 0;
            if (kind == 7 || kind == 5)
            {
                // Soft, inharmonic glass partials and delayed notes give the reveal a long tail.
                for (int n = 0; n < notes.Length; n++)
                {
                    float age = t - n * (kind == 7 ? .11f : .075f);
                    if (age < 0) continue;
                    float env = Mathf.Min(age * 100, 1) * Mathf.Exp(-age * (kind == 7 ? 3.6f : 7));
                    v += (Mathf.Sin(age * notes[n] * Mathf.PI * 2) + .22f*Mathf.Sin(age * notes[n] * 2.013f * Mathf.PI * 2)) * env * .075f;
                }
            }
            else if (kind == 0)
                v = (Mathf.Sin(Mathf.PI*2*(180*t+260*t*t))*.11f+noise*.035f)*Mathf.Sin(p*Mathf.PI)*attack;
            else if (kind == 3)
                v = (noise*.13f+Mathf.Sin(t*280*Mathf.PI*2)*.1f)*Mathf.Exp(-t*32)*attack;
            else
                v = (Mathf.Sin(t*(kind==6?130:kind==4?85:440)*Mathf.PI*2)*.16f+noise*(kind==4?.23f:.018f))*Mathf.Pow(1-p,3)*attack;
            samples[i] = v;
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
        if (sheenMaterial != null) Destroy(sheenMaterial);
        if (generatedCues != null) foreach (var clip in generatedCues) if (clip != null) Destroy(clip);
    }
}
