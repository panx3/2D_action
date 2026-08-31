using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>GoalPoint成立後のUIとScene遷移を一元管理する。</summary>
[DisallowMultipleComponent]
public sealed class GoalMenuController : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject presentationRoot;
    [SerializeField] private RectTransform stonePanel;
    [SerializeField] private Button nextButton;
    [SerializeField] private Button retryButton;
    [SerializeField] private Button titleButton;

    [Header("Scene Flow")]
    [SerializeField] private string nextSceneName;
    [SerializeField] private string titleSceneName = "TitleScene";
    [SerializeField] private PauseMenuController pauseMenu;

    [Header("Presentation")]
    [SerializeField, Min(0f)] private float showDuration = 0.24f;

    private CanvasGroup _panelCanvasGroup;
    private Coroutine _showRoutine;
    private bool _goalReached;

    public bool IsGoalReached => _goalReached;
    public string NextSceneName => nextSceneName;

    private void Awake()
    {
        RegisterButtons();
        ConfigureNavigation();

        if (pauseMenu == null)
            pauseMenu = FindAnyObjectByType<PauseMenuController>(FindObjectsInactive.Include);

        if (stonePanel != null)
        {
            _panelCanvasGroup = stonePanel.GetComponent<CanvasGroup>();
            if (_panelCanvasGroup == null)
                _panelCanvasGroup = stonePanel.gameObject.AddComponent<CanvasGroup>();
        }

        if (presentationRoot != null)
            presentationRoot.SetActive(false);

        RefreshNextButton();
    }

    private void OnDestroy()
    {
        if (nextButton != null)
            nextButton.onClick.RemoveListener(LoadNextScene);
        if (retryButton != null)
            retryButton.onClick.RemoveListener(RetryStage);
        if (titleButton != null)
            titleButton.onClick.RemoveListener(ReturnToTitle);

        if (_goalReached)
            Time.timeScale = 1f;
    }

    public void ShowGoal()
    {
        if (_goalReached)
            return;

        _goalReached = true;
        if (pauseMenu != null)
            pauseMenu.SetExternalPauseBlocked(true);

        Time.timeScale = 0f;
        if (presentationRoot != null)
            presentationRoot.SetActive(true);

        if (_showRoutine != null)
            StopCoroutine(_showRoutine);
        _showRoutine = StartCoroutine(ShowRoutine());
    }

    public void RetryStage()
    {
        if (!_goalReached)
            return;

        RestoreTimeAndSelection();
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex, LoadSceneMode.Single);
    }

    public void ReturnToTitle()
    {
        if (!_goalReached || string.IsNullOrWhiteSpace(titleSceneName))
            return;

        RestoreTimeAndSelection();
        SceneManager.LoadScene(titleSceneName, LoadSceneMode.Single);
    }

    public void LoadNextScene()
    {
        if (!_goalReached || !CanLoadNextScene())
            return;

        RestoreTimeAndSelection();
        SceneManager.LoadScene(nextSceneName, LoadSceneMode.Single);
    }

    private IEnumerator ShowRoutine()
    {
        if (stonePanel == null)
        {
            SelectButton();
            yield break;
        }

        float duration = Mathf.Max(0f, showDuration);
        if (_panelCanvasGroup != null)
            _panelCanvasGroup.alpha = duration > 0f ? 0f : 1f;
        stonePanel.localScale = duration > 0f ? Vector3.one * 0.94f : Vector3.one;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = duration > 0f ? Mathf.Clamp01(elapsed / duration) : 1f;
            float eased = Mathf.SmoothStep(0f, 1f, t);
            if (_panelCanvasGroup != null)
                _panelCanvasGroup.alpha = eased;
            stonePanel.localScale = Vector3.one * Mathf.Lerp(0.94f, 1f, eased);
            yield return null;
        }

        if (_panelCanvasGroup != null)
            _panelCanvasGroup.alpha = 1f;
        stonePanel.localScale = Vector3.one;
        _showRoutine = null;
        SelectButton();
    }

    private void RegisterButtons()
    {
        if (nextButton != null)
        {
            nextButton.onClick.RemoveListener(LoadNextScene);
            nextButton.onClick.AddListener(LoadNextScene);
        }
        if (retryButton != null)
        {
            retryButton.onClick.RemoveListener(RetryStage);
            retryButton.onClick.AddListener(RetryStage);
        }
        if (titleButton != null)
        {
            titleButton.onClick.RemoveListener(ReturnToTitle);
            titleButton.onClick.AddListener(ReturnToTitle);
        }
    }

    private void ConfigureNavigation()
    {
        if (nextButton == null || retryButton == null || titleButton == null)
            return;

        nextButton.navigation = CreateNavigation(titleButton, retryButton);
        retryButton.navigation = CreateNavigation(nextButton, titleButton);
        titleButton.navigation = CreateNavigation(retryButton, nextButton);
    }

    private static Navigation CreateNavigation(Selectable up, Selectable down)
    {
        return new Navigation
        {
            mode = Navigation.Mode.Explicit,
            selectOnUp = up,
            selectOnDown = down
        };
    }

    private void RefreshNextButton()
    {
        if (nextButton != null)
            nextButton.interactable = CanLoadNextScene();
    }

    private bool CanLoadNextScene()
    {
        return !string.IsNullOrWhiteSpace(nextSceneName)
            && Application.CanStreamedLevelBeLoaded(nextSceneName);
    }

    private void SelectButton()
    {
        Button target = nextButton != null && nextButton.interactable ? nextButton : retryButton;
        if (target != null && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(target.gameObject);
    }

    private static void RestoreTimeAndSelection()
    {
        Time.timeScale = 1f;
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }
}
