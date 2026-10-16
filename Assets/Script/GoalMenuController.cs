using System.Collections;
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
    [Header("Scene Flow")]
    [SerializeField] private string nextSceneName;
    [SerializeField] private string titleSceneName = "TitleScene";
    [SerializeField] private PauseMenuController pauseMenu;
    private bool goalReached;
    private bool transitioning;
    private Player goalPlayer;
    public bool IsGoalReached => goalReached;
    public string NextSceneName => nextSceneName;

    private void Awake()
    {
        nextButton.onClick.AddListener(LoadNextScene);
        retryButton.onClick.AddListener(RetryStage);
        titleButton.onClick.AddListener(ReturnToTitle);
        if (pauseMenu == null) pauseMenu = FindAnyObjectByType<PauseMenuController>();
        nextButton.interactable = CanLoadNextScene();
        CRTMenuNavigation.Configure(nextButton, retryButton, titleButton);
    }
    public void ShowGoal()
    {
        if (goalReached || transitioning) return;
        if (pauseMenu != null) pauseMenu.SetExternalPauseBlocked(true);
        overlay.SetMessage("STAGE CLEAR", "CRYSTAL ACQUIRED");
        if (!overlay.Show())
        {
            if (pauseMenu != null) pauseMenu.SetExternalPauseBlocked(false);
            return;
        }
        goalReached = true;
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
        if (!goalReached) return;
        overlay.Hide();
        if (goalPlayer != null) goalPlayer.SetGoalInputBlocked(false);
        Time.timeScale = 1f;
        if (pauseMenu != null) pauseMenu.SetExternalPauseBlocked(false);
    }
}
