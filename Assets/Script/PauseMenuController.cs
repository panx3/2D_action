using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>Owns pause input/time and scene actions. CRTOverlayController owns the presentation.</summary>
[DisallowMultipleComponent]
public sealed class PauseMenuController : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private InputActionAsset inputActions;
    [SerializeField] private string pauseActionName = "Player/Pause";
    [Header("CRT UI")]
    [SerializeField] private CRTOverlayController overlay;
    [SerializeField] private GameObject menuPanel;
    [SerializeField] private ControlsPanelController controlsPanel;
    [SerializeField] private UnityEngine.UI.Button resumeButton;
    [SerializeField] private UnityEngine.UI.Button retryButton;
    [SerializeField] private UnityEngine.UI.Button controlsButton;
    [SerializeField] private UnityEngine.UI.Button titleButton;
    [Header("Scene Flow")]
    [SerializeField] private string titleSceneName = "TitleScene";

    private InputAction pauseAction;
    private bool enabledPauseAction;
    private float previousTimeScale = 1f;
    private bool externallyBlocked;
    private bool transitioning;
    private int controlsBackFrame = -1;
    private GameBgmController pausedBgm;
    private MorningStarLauncher hitStopOwner;
    public bool IsPaused { get; private set; }
    public bool IsShowingControls => controlsPanel != null && controlsPanel.IsVisible;
    public bool IsExternallyBlocked => externallyBlocked;
    public event System.Action<bool> PauseStateChanged;

    private void Awake()
    {
        hitStopOwner = FindAnyObjectByType<MorningStarLauncher>();
        resumeButton.onClick.AddListener(ClosePause);
        retryButton.onClick.AddListener(RetryStage);
        controlsButton.onClick.AddListener(ShowControls);
        titleButton.onClick.AddListener(ReturnToTitle);
        controlsPanel.BackRequested += HandleControlsBack;
        controlsPanel.Hide();
        CRTMenuNavigation.Configure(resumeButton, retryButton, controlsButton, titleButton);
    }
    private void OnEnable()
    {
        pauseAction = inputActions != null ? inputActions.FindAction(pauseActionName, false) : null;
        if (pauseAction == null) { Debug.LogError("[PauseMenu] Player/Pause is not assigned.", this); return; }
        enabledPauseAction = !pauseAction.enabled;
        pauseAction.performed += HandlePause;
        pauseAction.Enable();
    }
    private void OnDisable()
    {
        if (pauseAction != null)
        {
            pauseAction.performed -= HandlePause;
            if (enabledPauseAction) pauseAction.Disable();
        }
        if (IsPaused) ClosePauseImmediately();
    }
    private void Update()
    {
        if (IsPaused && !IsShowingControls && controlsBackFrame != Time.frameCount
            && Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame)
            ClosePause();
    }
    private void HandlePause(InputAction.CallbackContext context)
    {
        if (externallyBlocked || transitioning || overlay.IsClosing) return;
        if (IsShowingControls) HandleControlsBack();
        else if (IsPaused) ClosePause();
        else OpenPause();
    }
    public void OpenPause()
    {
        if (IsPaused || externallyBlocked || transitioning) return;
        if (Time.timeScale <= 0f && (hitStopOwner == null || !hitStopOwner.IsHitStopping)) return;
        controlsPanel.Hide();
        menuPanel.SetActive(true);
        overlay.SetMessage("PAUSE", "TAKE A BREATHER");
        if (!overlay.Show()) return;
        // 命中直後のPause入力も受け取り、停止コルーチンにPauseを解除させない。
        if (hitStopOwner != null) hitStopOwner.CancelHitStop();
        previousTimeScale = Time.timeScale;
        IsPaused = true;
        PauseStateChanged?.Invoke(true);
        Time.timeScale = 0f;
        pausedBgm = GameBgmController.Instance;
        if (pausedBgm != null) pausedBgm.SetPauseMuffled(true);
        StartCoroutine(SelectWhenReady(resumeButton));
    }
    public void ClosePause()
    {
        if (!IsPaused || overlay.IsClosing) return;
        CRTMenuNavigation.ClearSelection();
        if (pausedBgm != null) pausedBgm.SetPauseMuffled(false);
        overlay.HideAnimated(FinishPause);
    }
    private void ClosePauseImmediately()
    {
        overlay.Hide();
        FinishPause();
    }
    private void FinishPause()
    {
        controlsPanel.Hide();
        IsPaused = false;
        PauseStateChanged?.Invoke(false);
        Time.timeScale = previousTimeScale;
        if (pausedBgm != null) pausedBgm.SetPauseMuffled(false);
        pausedBgm = null;
        CRTMenuNavigation.ClearSelection();
    }
    public void ShowControls()
    {
        if (!IsPaused || !overlay.IsReady) return;
        menuPanel.SetActive(false);
        overlay.SetMessage("CONTROLS", "GAMEPAD / KEYBOARD");
        controlsPanel.Show();
    }
    private void HandleControlsBack()
    {
        if (!IsPaused || overlay.IsClosing) return;
        controlsBackFrame = Time.frameCount;
        controlsPanel.Hide();
        overlay.SetMessage("PAUSE", "TAKE A BREATHER");
        menuPanel.SetActive(true);
        EventSystem.current?.SetSelectedGameObject(controlsButton.gameObject);
    }
    public void RetryStage() => LoadScene(SceneManager.GetActiveScene().name);
    public void ReturnToTitle() => LoadScene(titleSceneName);
    private void LoadScene(string scene)
    {
        if (!IsPaused || !overlay.IsReady || transitioning || !Application.CanStreamedLevelBeLoaded(scene)) return;
        transitioning = true;
        ClosePauseImmediately();
        Time.timeScale = 1f;
        SceneManager.LoadScene(scene, LoadSceneMode.Single);
    }
    public void SetExternalPauseBlocked(bool blocked)
    {
        externallyBlocked = blocked;
        if (blocked && IsPaused) ClosePauseImmediately();
    }
    private IEnumerator SelectWhenReady(UnityEngine.UI.Button button)
    {
        while (IsPaused && !overlay.IsClosing && !overlay.IsReady) yield return null;
        if (IsPaused && overlay.IsReady) EventSystem.current?.SetSelectedGameObject(button.gameObject);
    }
}
