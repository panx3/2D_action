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
    [Header("Demo Ending / 体験版終了画面")]
    [SerializeField, InspectorName("体験版終了画面を表示"), Tooltip("ON: クリア画面の後に体験版終了画面を表示。OFF: 通常のクリアメニューを維持します。")]
    private bool showDemoEnding = true;
    [SerializeField, Min(0f), InspectorName("クリア画面の表示時間（秒）")]
    private float resultDisplayDuration = 3.5f;
    [SerializeField, Min(0.1f), InspectorName("ノイズ切り替え時間（秒）")]
    private float demoTransitionDuration = 0.4f;
    [SerializeField, Min(0.1f), Tooltip("体験版終了画面から自動でタイトルへ戻るまでの秒数。")]
    private float demoAutoReturnSeconds = 10f;
    [SerializeField] private GameObject demoEndingContent;
    [SerializeField] private GameObject[] resultOnlyContent;
    private bool goalReached;
    private bool transitioning;
    private bool demoSequenceRunning;
    private bool demoEndingVisible;
    private float demoReturnAt = float.PositiveInfinity;
    private Player goalPlayer;
    private Player stagePlayer;
    private PlayerStartIntro startIntro;
    private bool timerStarted;
    private bool timerPaused;
    private bool gimmickPresentationPaused;
    private bool clearTimeFinalized;
    private double elapsedSeconds;
    private double resumedAt;
    public bool IsGoalReached => goalReached;
    public string NextSceneName => nextSceneName;
    public bool IsClearTimeFinalized => clearTimeFinalized;
    public bool IsGimmickPresentationPaused => gimmickPresentationPaused;
    public double ElapsedSeconds => elapsedSeconds + (timerStarted && !timerPaused && !gimmickPresentationPaused && !clearTimeFinalized
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
        if (demoEndingContent != null) demoEndingContent.SetActive(false);
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
        if (demoEndingVisible && !demoSequenceRunning && !transitioning && Time.unscaledTime >= demoReturnAt)
            ReturnToTitle();
        if (demoEndingVisible && !demoSequenceRunning && !transitioning && overlay.IsReady
            && EventSystem.current != null && EventSystem.current.currentSelectedGameObject == null)
            EventSystem.current.SetSelectedGameObject(titleButton.gameObject);
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
    public void SetGimmickPresentationPaused(bool paused)
    {
        elapsedSeconds = ElapsedSeconds;
        resumedAt = Time.unscaledTimeAsDouble;
        gimmickPresentationPaused = paused;
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
        CRTMenuNavigation.EnsureInput();
        Time.timeScale = 0f;
        demoSequenceRunning = showDemoEnding && demoEndingContent != null;
        if (demoSequenceRunning)
        {
            overlay.SetInputBlocked(true);
            CRTMenuNavigation.ClearSelection();
        }
        StartCoroutine(SelectWhenReady());
    }
    private IEnumerator SelectWhenReady()
    {
        while (!overlay.IsReady) yield return null;
        if (demoSequenceRunning)
        {
            yield return ShowDemoEnding();
            yield break;
        }
        EventSystem.current?.SetSelectedGameObject((nextButton.interactable ? nextButton : retryButton).gameObject);
    }
    private IEnumerator ShowDemoEnding()
    {
        // The result and both halves of the signal transition continue at timeScale = 0.
        float displayUntil = Time.unscaledTime + Mathf.Max(0f, resultDisplayDuration);
        while (Time.unscaledTime < displayUntil && showDemoEnding) yield return null;
        if (!showDemoEnding)
        {
            demoSequenceRunning = false;
            overlay.SetInputBlocked(false);
            EventSystem.current?.SetSelectedGameObject((nextButton.interactable ? nextButton : retryButton).gameObject);
            yield break;
        }

        float duration = Mathf.Max(0.1f, demoTransitionDuration);
        float startedAt = Time.unscaledTime;
        while (Time.unscaledTime - startedAt < duration)
        {
            float progress = (Time.unscaledTime - startedAt) / duration;
            overlay.SetSignalInterference(Mathf.Sin(progress * Mathf.PI));
            if (!demoEndingVisible && progress >= 0.5f)
                RevealDemoEnding();
            yield return null;
        }
        if (!demoEndingVisible) RevealDemoEnding();
        overlay.SetSignalInterference(0f);
        demoSequenceRunning = false;
        overlay.SetInputBlocked(false);
        titleButton.gameObject.SetActive(true);
        titleButton.interactable = true;
        CRTMenuNavigation.Configure(titleButton);
        // Do not carry a submit pressed during the transition into the new screen.
        yield return null;
        EventSystem.current?.SetSelectedGameObject(titleButton.gameObject);
        demoReturnAt = Time.unscaledTime + Mathf.Max(0.1f, demoAutoReturnSeconds);
    }
    private void RevealDemoEnding()
    {
        demoEndingVisible = true;
        foreach (GameObject item in resultOnlyContent)
            if (item != null) item.SetActive(false);
        // Result jewels can be scene-added children rather than part of the menu prefab.
        foreach (PinkJewelIconRow row in GetComponentsInChildren<PinkJewelIconRow>(true))
            row.gameObject.SetActive(false);
        nextButton.gameObject.SetActive(false);
        retryButton.gameObject.SetActive(false);
        ((RectTransform)titleButton.transform).anchoredPosition = new Vector2(0f, -254f);
        demoEndingContent.SetActive(true);
    }
    public void RetryStage() => LoadScene(SceneManager.GetActiveScene().name);
    public void ReturnToTitle() => LoadScene(titleSceneName);
    public void LoadNextScene() { if (CanLoadNextScene()) LoadScene(nextSceneName); }
    private bool CanLoadNextScene() => !string.IsNullOrWhiteSpace(nextSceneName) && Application.CanStreamedLevelBeLoaded(nextSceneName);
    private void LoadScene(string scene)
    {
        if (!goalReached || !overlay.IsReady || demoSequenceRunning || transitioning
            || (demoEndingVisible && scene != titleSceneName) || !Application.CanStreamedLevelBeLoaded(scene)) return;
        transitioning = true;
        overlay.Hide();
        CRTMenuNavigation.ClearSelection();
        Time.timeScale = 1f;
        SceneManager.LoadScene(scene, LoadSceneMode.Single);
    }
    private void OnDisable()
    {
        StopAllCoroutines();
        if (startIntro != null) startIntro.Completed -= TryStartTimer;
        if (pauseMenu != null) pauseMenu.PauseStateChanged -= SetTimerPaused;
        if (!goalReached) return;
        overlay.Hide();
        if (goalPlayer != null) goalPlayer.SetGoalInputBlocked(false);
        Time.timeScale = 1f;
        if (pauseMenu != null) pauseMenu.SetExternalPauseBlocked(false);
    }
}
