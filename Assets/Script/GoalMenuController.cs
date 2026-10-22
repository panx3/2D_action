using System.Collections;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

/// <summary>Existing GoalPoint entry point; result flow uses the shared CRT presentation.</summary>
[DisallowMultipleComponent]
public sealed class GoalMenuController : MonoBehaviour
{
    [Header("CRT UI")]
    [SerializeField] private CRTOverlayController overlay;
    [SerializeField] private UnityEngine.UI.Button nextButton;
    [SerializeField] private UnityEngine.UI.Button retryButton;
    [SerializeField] private UnityEngine.UI.Button titleButton;
    [SerializeField] private TMP_Text clearTimeLabel;
    [Header("Scene Flow")]
    [SerializeField] private string nextSceneName;
    [SerializeField] private string titleSceneName = "TitleScene";
    [SerializeField] private PauseMenuController pauseMenu;
    private bool goalReached;
    private bool transitioning;
    private Player goalPlayer;
    private Player stagePlayer;
    private PlayerStartIntro startIntro;
    private bool timerStarted;
    private bool timerPaused;
    private bool clearTimeFinalized;
    private double elapsedSeconds;
    private double resumedAt;
    public bool IsGoalReached => goalReached;
    public string NextSceneName => nextSceneName;
    public bool IsClearTimeFinalized => clearTimeFinalized;
    public double ElapsedSeconds => elapsedSeconds + (timerStarted && !timerPaused && !clearTimeFinalized
        ? System.Math.Max(0d, Time.unscaledTimeAsDouble - resumedAt) : 0d);

    private void Awake()
    {
        nextButton.onClick.AddListener(LoadNextScene);
        retryButton.onClick.AddListener(RetryStage);
        titleButton.onClick.AddListener(ReturnToTitle);
        if (pauseMenu == null) pauseMenu = FindAnyObjectByType<PauseMenuController>();
        stagePlayer = FindAnyObjectByType<Player>(FindObjectsInactive.Include);
        if (stagePlayer != null) startIntro = stagePlayer.GetComponent<PlayerStartIntro>();
        if (clearTimeLabel != null) clearTimeLabel.gameObject.SetActive(false);
        nextButton.interactable = CanLoadNextScene();
        CRTMenuNavigation.Configure(nextButton, retryButton, titleButton);
    }
    private void OnEnable()
    {
        if (startIntro != null) startIntro.Completed += TryStartTimer;
        if (pauseMenu != null) pauseMenu.PauseStateChanged += SetTimerPaused;
        SetTimerPaused(pauseMenu != null && pauseMenu.IsPaused);
    }
    private void Update()
    {
        // Also handles stages without an intro and an intro with Play On Start disabled.
        if (!timerStarted && !clearTimeFinalized) TryStartTimer();
    }
    private void TryStartTimer()
    {
        if (timerStarted || clearTimeFinalized || stagePlayer == null || !stagePlayer.InputEnabled
            || stagePlayer.IsGoalInputBlocked || (startIntro != null && startIntro.IsPlaying)) return;
        timerStarted = true;
        elapsedSeconds = 0d;
        resumedAt = Time.unscaledTimeAsDouble;
    }
    private void SetTimerPaused(bool paused)
    {
        elapsedSeconds = ElapsedSeconds;
        resumedAt = Time.unscaledTimeAsDouble;
        timerPaused = paused;
    }
    /// <summary>リトライを選ぶまで死亡後の待機時間をプレイ時間へ加算しない。</summary>
    public void StopTimerForGameOver() => SetTimerPaused(true);
    /// <summary>最終結晶破壊時に一度だけ確定し、獲得演出の時間は加算しない。</summary>
    public void CaptureClearTime()
    {
        if (clearTimeFinalized) return;
        elapsedSeconds = ElapsedSeconds;
        clearTimeFinalized = true;
    }
    public static string FormatClearTime(double seconds)
    {
        long hundredths = (long)System.Math.Floor(System.Math.Max(0d, seconds) * 100d);
        return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}.{2:00}",
            hundredths / 6000, hundredths / 100 % 60, hundredths % 100);
    }
    public void ShowGoal()
    {
        if (goalReached || transitioning) return;
        if (pauseMenu != null) pauseMenu.SetExternalPauseBlocked(true);
        // Capture the centered camera even when entered without the celebration.
        Camera.main?.GetComponent<CameraShake2D>()?.ResetShake();
        overlay.SetMessage("STAGE CLEAR", string.Empty);
        if (!overlay.Show())
        {
            if (pauseMenu != null) pauseMenu.SetExternalPauseBlocked(false);
            return;
        }
        goalReached = true;
        CaptureClearTime();
        if (clearTimeLabel != null)
        {
            clearTimeLabel.text = "CLEAR TIME  <color=#FFE066><size=38>" + FormatClearTime(elapsedSeconds) + "</size></color>";
            clearTimeLabel.gameObject.SetActive(true);
        }
        goalPlayer = FindAnyObjectByType<Player>(FindObjectsInactive.Include);
        if (goalPlayer != null) goalPlayer.SetGoalInputBlocked(true);
        Time.timeScale = 0f;
        StartCoroutine(SelectWhenReady());
    }
    private IEnumerator SelectWhenReady()
    {
        while (!overlay.IsReady) yield return null;
        EventSystem.current?.SetSelectedGameObject((nextButton.interactable ? nextButton : retryButton).gameObject);
    }
    public void RetryStage() => LoadScene(SceneManager.GetActiveScene().name);
    public void ReturnToTitle() => LoadScene(titleSceneName);
    public void LoadNextScene() { if (CanLoadNextScene()) LoadScene(nextSceneName); }
    private bool CanLoadNextScene() => !string.IsNullOrWhiteSpace(nextSceneName) && Application.CanStreamedLevelBeLoaded(nextSceneName);
    private void LoadScene(string scene)
    {
        if (!goalReached || !overlay.IsReady || transitioning || !Application.CanStreamedLevelBeLoaded(scene)) return;
        transitioning = true;
        overlay.Hide();
        CRTMenuNavigation.ClearSelection();
        Time.timeScale = 1f;
        SceneManager.LoadScene(scene, LoadSceneMode.Single);
    }
    private void OnDisable()
    {
        if (startIntro != null) startIntro.Completed -= TryStartTimer;
        if (pauseMenu != null) pauseMenu.PauseStateChanged -= SetTimerPaused;
        if (!goalReached) return;
        overlay.Hide();
        if (goalPlayer != null) goalPlayer.SetGoalInputBlocked(false);
        Time.timeScale = 1f;
        if (pauseMenu != null) pauseMenu.SetExternalPauseBlocked(false);
    }
}
