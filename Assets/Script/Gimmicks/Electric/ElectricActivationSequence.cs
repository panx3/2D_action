using UnityEngine;

/// <summary>受電の接続から扉全開までの感電演出。受電判定や扉の進捗は既存の所有者に委ねる。</summary>
[DefaultExecutionOrder(50), DisallowMultipleComponent]
public sealed class ElectricActivationSequence : MonoBehaviour
{
    [SerializeField] private ElectricReceiverCable receiver;
    [SerializeField] private ElectricDoor door;
    [SerializeField] private Player player;
    [SerializeField] private Sprite skeletonSprite;
    [SerializeField, Min(0.02f)] private float blinkInterval = 0.08f;
    [Header("Grand gate reveal (Stage2 only)")]
    [SerializeField] private bool showDoorReveal;
    [SerializeField] private CameraFollow revealCamera;
    [SerializeField, Min(0.01f)] private float focusDuration = 0.5f;
    [SerializeField, Min(0f)] private float fullOpenHold = 0.2f;
    [SerializeField, Min(0.01f)] private float returnDuration = 0.45f;
    [SerializeField, Min(0f)] private float framingMargin = 1f;
    private enum RevealPhase { Focus, Opening, Hold, Return }
    private RevealPhase phase;
    private bool revealing, revealedOnce, finishing;
    private bool previousPauseBlocked, previousTimerPaused;
    private float phaseTime, previousTimeScale, returnLens, focusLens;
    private Vector3 returnPosition, focusPosition;
    private Camera lens;
    private PauseMenuController pause;
    private GoalMenuController timer;
    private GameOverMenuController gameOver;
    private PlayerHealth health;
    private SpriteRenderer body, skeleton;
    private bool active, committed, previousForceRenderingOff;
    private float elapsed;
    public bool IsPresenting => active;
    public bool IsRevealingDoor => revealing;
    public SpriteRenderer EffectSprite => skeleton != null && skeleton.enabled ? skeleton : player != null ? player.BodySprite : null;

    private void OnEnable()
    {
        if (receiver == null) receiver = GetComponent<ElectricReceiverCable>();
        if (player == null && receiver != null && receiver.Launcher != null) player = receiver.Launcher.GetComponent<Player>();
        if (receiver != null)
        {
            receiver.ConductionStarted += Begin;
            receiver.ConductionEnded += OnConductionEnded;
            receiver.PowerStarted += OnPowerStarted;
        }
        if (door != null) door.FullyOpened += OnDoorFullyOpened;
        if (player != null)
        {
            player.ElectricStunEnded += Finish;
            health = player.GetComponent<PlayerHealth>();
            if (health != null) health.OnDead += Finish;
        }
    }

    private void Begin()
    {
        if (active || receiver == null || receiver.IsPowered || door == null || !door.isActiveAndEnabled
            || door.IsFullyOpen || player == null || skeletonSprite == null || !player.TryBeginElectricStun(this)) return;
        body = player.BodySprite;
        if (body == null) { player.EndElectricStun(this); return; }
        if (skeleton == null)
        {
            var visual = new GameObject("ElectricShockSkeleton");
            // The body owns the stage filter; this runtime copy follows its material directly.
            visual.hideFlags = HideFlags.DontSave;
            visual.layer = body.gameObject.layer;
            visual.transform.SetParent(body.transform, false);
            skeleton = visual.AddComponent<SpriteRenderer>();
            skeleton.sprite = skeletonSprite;
        }
        previousForceRenderingOff = body.forceRenderingOff;
        elapsed = 0f;
        committed = false;
        active = true;
    }

    private void Update()
    {
        if (!active)
        {
            if (receiver != null && receiver.IsConducting && !receiver.IsPowered) Begin();
            return;
        }
        if (player == null || !player.IsElectricStunned || !player.isActiveAndEnabled || receiver == null || !receiver.isActiveAndEnabled
            || door == null || !door.isActiveAndEnabled || (health != null && (health.IsDead || health.IsRespawning)))
        { Finish(); return; }
        elapsed += revealing ? Time.unscaledDeltaTime : Time.deltaTime;
        if (revealing)
        {
            if (revealCamera == null || !revealCamera.isActiveAndEnabled || !revealCamera.IsEventControlledBy(this)
                || !Mathf.Approximately(Time.timeScale, 0f) || (pause != null && pause.IsPaused)
                || player.IsGoalInputBlocked || (timer != null && timer.IsGoalReached))
            { Finish(); return; }
            TickDoorReveal(Time.unscaledDeltaTime);
        }
        else if (committed && door.IsFullyOpen) Finish();
    }

    private void LateUpdate()
    {
        if (!active || body == null || skeleton == null) return;
        bool showBones = ((int)(elapsed / blinkInterval) & 1) == 0;
        body.forceRenderingOff = previousForceRenderingOff || showBones;
        skeleton.enabled = showBones && !previousForceRenderingOff;
        skeleton.flipX = body.flipX;
        skeleton.flipY = body.flipY;
        skeleton.sharedMaterial = body.sharedMaterial;
        skeleton.sortingLayerID = body.sortingLayerID;
        skeleton.sortingOrder = body.sortingOrder + 2;
        skeleton.color = body.color;
        float pixel = 1f / skeletonSprite.pixelsPerUnit;
        skeleton.transform.localPosition = new Vector3(((int)(elapsed / blinkInterval) & 2) == 0 ? -pixel : pixel, 0f, -0.01f);
    }

    private void OnPowerStarted()
    {
        if (!active) return;
        committed = true;
        TryBeginDoorReveal();
    }
    private void OnConductionEnded() { if (!committed) Finish(); }

    private void OnDoorFullyOpened()
    {
        if (revealing)
        {
            phase = RevealPhase.Hold;
            phaseTime = 0f;
        }
        else Finish();
    }

    private void TryBeginDoorReveal()
    {
        if (!showDoorReveal || revealedOnce || revealing || door == null || !door.IsGrandGate || door.IsFullyOpen
            || player == null || player.IsGoalInputBlocked || Time.timeScale <= 0f) return;
        if (revealCamera == null && Camera.main != null) revealCamera = Camera.main.GetComponent<CameraFollow>();
        if (revealCamera == null || !revealCamera.isActiveAndEnabled) return;
        lens = revealCamera.GetComponent<Camera>();
        if (lens == null || !lens.orthographic) return;
        pause = FindInScene<PauseMenuController>();
        timer = FindInScene<GoalMenuController>();
        gameOver = FindInScene<GameOverMenuController>();
        if ((pause != null && pause.IsPaused) || (timer != null && timer.IsGoalReached)) return;
        if (!revealCamera.BeginEventControl(this)) return;
        if (!door.BeginPresentation(this)) { revealCamera.EndEventControl(this); return; }
        returnPosition = revealCamera.transform.position;
        returnLens = lens.orthographicSize;
        Bounds bounds = door.RevealBounds;
        var playerCollider = player.GetComponent<Collider2D>();
        if (playerCollider != null) bounds.Encapsulate(playerCollider.bounds);
        focusPosition = bounds.center;
        focusPosition.z = returnPosition.z;
        focusLens = Mathf.Max(returnLens, bounds.extents.y + framingMargin,
            (bounds.extents.x + framingMargin) / Mathf.Max(0.1f, lens.aspect));
        previousTimeScale = Time.timeScale;
        previousPauseBlocked = pause != null && pause.IsExternallyBlocked;
        previousTimerPaused = timer != null && timer.IsGimmickPresentationPaused;
        if (pause != null) pause.SetExternalPauseBlocked(true);
        if (timer != null) timer.SetGimmickPresentationPaused(true);
        phase = RevealPhase.Focus;
        phaseTime = 0f;
        revealing = revealedOnce = true;
        Time.timeScale = 0f;
    }

    private void TickDoorReveal(float deltaTime)
    {
        phaseTime += Mathf.Max(0f, deltaTime);
        if (phase == RevealPhase.Focus)
        {
            float t = Ease(phaseTime / Mathf.Max(0.01f, focusDuration));
            revealCamera.SetEventFraming(this, Vector3.LerpUnclamped(returnPosition, focusPosition, t), Mathf.Lerp(returnLens, focusLens, t));
            if (phaseTime >= focusDuration) { phase = RevealPhase.Opening; phaseTime = 0f; }
        }
        else if (phase == RevealPhase.Opening)
        {
            revealCamera.SetEventFraming(this, focusPosition, focusLens);
            door.AdvancePresentation(this, deltaTime);
        }
        else if (phase == RevealPhase.Hold)
        {
            revealCamera.SetEventFraming(this, focusPosition, focusLens);
            if (phaseTime >= fullOpenHold) { phase = RevealPhase.Return; phaseTime = 0f; }
        }
        else
        {
            float t = Ease(phaseTime / Mathf.Max(0.01f, returnDuration));
            revealCamera.SetEventFraming(this, Vector3.LerpUnclamped(focusPosition, returnPosition, t), Mathf.Lerp(focusLens, returnLens, t));
            if (phaseTime >= returnDuration) Finish();
        }
    }

    private static float Ease(float value)
    {
        float t = Mathf.Clamp01(value);
        return t * t * t * (t * (t * 6f - 15f) + 10f);
    }

    private T FindInScene<T>() where T : Component
    {
        foreach (GameObject root in gameObject.scene.GetRootGameObjects())
        {
            var component = root.GetComponentInChildren<T>(true);
            if (component != null) return component;
        }
        return null;
    }

    private void EndDoorReveal()
    {
        if (!revealing) return;
        bool terminalPresentation = (player != null && player.IsGoalInputBlocked)
            || (timer != null && timer.IsGoalReached)
            || (gameOver != null && gameOver.IsGameOver);
        bool keepClockStopped = terminalPresentation || (pause != null && pause.IsPaused);
        bool keepFraming = keepClockStopped
            || (health != null && (health.IsDead || health.IsRespawning))
            || (revealCamera != null && revealCamera.IsRespawnCameraHeld);
        if (door != null) door.EndPresentation(this);
        if (revealCamera != null && revealCamera.IsEventControlledBy(this))
        {
            if (!keepFraming) revealCamera.SetEventFraming(this, returnPosition, returnLens);
            revealCamera.EndEventControl(this);
        }
        if (timer != null) timer.SetGimmickPresentationPaused(previousTimerPaused);
        if (pause != null && !terminalPresentation) pause.SetExternalPauseBlocked(previousPauseBlocked);
        // Death/fall dissolve runs on scaled time; only an actual menu/goal owns a stopped clock.
        if (!keepClockStopped && Mathf.Approximately(Time.timeScale, 0f)) Time.timeScale = previousTimeScale;
        revealing = false;
    }

    private void Finish()
    {
        if (finishing || (!active && !revealing)) return;
        finishing = true;
        try
        {
            EndDoorReveal();
            active = false;
            if (body != null) body.forceRenderingOff = previousForceRenderingOff;
            if (skeleton != null) skeleton.enabled = false;
            if (player != null) player.EndElectricStun(this);
        }
        finally { finishing = false; }
    }

    private void OnDisable()
    {
        Finish();
        if (receiver != null)
        {
            receiver.ConductionStarted -= Begin;
            receiver.ConductionEnded -= OnConductionEnded;
            receiver.PowerStarted -= OnPowerStarted;
        }
        if (door != null) door.FullyOpened -= OnDoorFullyOpened;
        if (player != null) player.ElectricStunEnded -= Finish;
        if (health != null) health.OnDead -= Finish;
    }

    private void OnDestroy() { if (skeleton != null) Destroy(skeleton.gameObject); }
}
