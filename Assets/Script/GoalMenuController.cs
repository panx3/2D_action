using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>GoalPoint成立後のUI、祝福演出、Scene遷移を一元管理する。</summary>
[DisallowMultipleComponent]
public sealed class GoalMenuController : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject presentationRoot;
    [SerializeField] private RectTransform stonePanel;
    [SerializeField] private Graphic darkOverlay;
    [SerializeField] private RectTransform goalTitle;
    [SerializeField] private RectTransform[] sparkleVisuals;
    [SerializeField] private Button nextButton;
    [SerializeField] private Button retryButton;
    [SerializeField] private Button titleButton;

    [Header("Scene Flow")]
    [SerializeField] private string nextSceneName;
    [SerializeField] private string titleSceneName = "TitleScene";
    [SerializeField] private PauseMenuController pauseMenu;

    [Header("Presentation")]
    [SerializeField, Min(0f)] private float showDuration = 0.24f;
    [SerializeField, Min(0f)] private float titlePopDuration = 0.32f;
    [SerializeField, Min(0f)] private float buttonRevealDuration = 0.1f;
    [SerializeField, Min(0f)] private float buttonStagger = 0.055f;
    [SerializeField] private Vector2 panelScaleRange = new Vector2(0.94f, 1f);
    [SerializeField] private Vector3 titlePopScale = new Vector3(0.85f, 1.08f, 1f);
    [SerializeField, Min(0f)] private float sparkleRotationSpeed = 12f;
    [SerializeField, Min(0f)] private float sparklePulseSpeed = 1.15f;
    [SerializeField, Min(0f)] private float sparkleDriftPixels = 3f;

    private CanvasGroup _panelCanvasGroup;
    private readonly CanvasGroup[] _buttonGroups = new CanvasGroup[3];
    private RectTransform[] _sparkleRects = System.Array.Empty<RectTransform>();
    private Graphic[] _sparkleGraphics = System.Array.Empty<Graphic>();
    private Vector2[] _sparkleBasePositions = System.Array.Empty<Vector2>();
    private Vector3[] _sparkleBaseScales = System.Array.Empty<Vector3>();
    private Quaternion[] _sparkleBaseRotations = System.Array.Empty<Quaternion>();
    private Color[] _sparkleBaseColors = System.Array.Empty<Color>();
    private Vector3 _panelBaseScale = Vector3.one;
    private Vector3 _titleBaseScale = Vector3.one;
    private Color _darkOverlayBaseColor = Color.clear;
    private float _sparkleReveal;
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
            _panelBaseScale = stonePanel.localScale;
        }

        if (goalTitle != null)
            _titleBaseScale = goalTitle.localScale;
        if (darkOverlay != null)
            _darkOverlayBaseColor = darkOverlay.color;

        RefreshNextButton();
        CacheButtonGroups();
        CacheSparkles();

        if (presentationRoot != null)
            presentationRoot.SetActive(false);
    }

    private void Update()
    {
        if (!_goalReached || presentationRoot == null || !presentationRoot.activeInHierarchy)
            return;

        AnimateSparkles();
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
        _sparkleReveal = 0f;
        if (_panelCanvasGroup != null)
            _panelCanvasGroup.alpha = 0f;
        if (stonePanel != null)
            stonePanel.localScale = _panelBaseScale * panelScaleRange.x;
        if (goalTitle != null)
            goalTitle.localScale = _titleBaseScale * titlePopScale.x;
        SetDarkOverlayAlpha(0f);
        SetAllButtonGroups(0f, false);

        float presentationDuration = Mathf.Max(showDuration, titlePopDuration);
        float elapsed = 0f;
        while (elapsed < presentationDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float panelT = showDuration > 0f ? Mathf.Clamp01(elapsed / showDuration) : 1f;
            float panelEase = Mathf.SmoothStep(0f, 1f, panelT);
            if (_panelCanvasGroup != null)
                _panelCanvasGroup.alpha = panelEase;
            if (stonePanel != null)
                stonePanel.localScale = _panelBaseScale * Mathf.Lerp(panelScaleRange.x, panelScaleRange.y, panelEase);
            SetDarkOverlayAlpha(panelEase);

            float titleT = titlePopDuration > 0f ? Mathf.Clamp01(elapsed / titlePopDuration) : 1f;
            if (goalTitle != null)
            {
                float scale;
                if (titleT < 0.62f)
                {
                    float rise = Mathf.SmoothStep(0f, 1f, titleT / 0.62f);
                    scale = Mathf.Lerp(titlePopScale.x, titlePopScale.y, rise);
                }
                else
                {
                    float settle = Mathf.SmoothStep(0f, 1f, (titleT - 0.62f) / 0.38f);
                    scale = Mathf.Lerp(titlePopScale.y, titlePopScale.z, settle);
                }
                goalTitle.localScale = _titleBaseScale * scale;
            }

            _sparkleReveal = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.2f, 1f, panelT));
            yield return null;
        }

        if (_panelCanvasGroup != null)
            _panelCanvasGroup.alpha = 1f;
        if (stonePanel != null)
            stonePanel.localScale = _panelBaseScale * panelScaleRange.y;
        if (goalTitle != null)
            goalTitle.localScale = _titleBaseScale * titlePopScale.z;
        SetDarkOverlayAlpha(1f);
        _sparkleReveal = 1f;

        for (int i = 0; i < _buttonGroups.Length; i++)
        {
            float revealElapsed = 0f;
            while (revealElapsed < buttonRevealDuration)
            {
                revealElapsed += Time.unscaledDeltaTime;
                float t = buttonRevealDuration > 0f
                    ? Mathf.SmoothStep(0f, 1f, revealElapsed / buttonRevealDuration)
                    : 1f;
                SetButtonGroup(i, t, false);
                yield return null;
            }

            SetButtonGroup(i, 1f, true);
            if (buttonStagger > 0f)
            {
                float wait = 0f;
                while (wait < buttonStagger)
                {
                    wait += Time.unscaledDeltaTime;
                    yield return null;
                }
            }
        }

        _showRoutine = null;
        SelectButton();
    }

    private void CacheButtonGroups()
    {
        _buttonGroups[0] = GetOrAddCanvasGroup(nextButton);
        _buttonGroups[1] = GetOrAddCanvasGroup(retryButton);
        _buttonGroups[2] = GetOrAddCanvasGroup(titleButton);
    }

    private static CanvasGroup GetOrAddCanvasGroup(Button button)
    {
        if (button == null)
            return null;
        CanvasGroup group = button.GetComponent<CanvasGroup>();
        return group != null ? group : button.gameObject.AddComponent<CanvasGroup>();
    }

    private void SetAllButtonGroups(float alpha, bool blocksRaycasts)
    {
        for (int i = 0; i < _buttonGroups.Length; i++)
            SetButtonGroup(i, alpha, blocksRaycasts);
    }

    private void SetButtonGroup(int index, float alpha, bool blocksRaycasts)
    {
        if (index < 0 || index >= _buttonGroups.Length || _buttonGroups[index] == null)
            return;
        _buttonGroups[index].alpha = Mathf.Clamp01(alpha);
        _buttonGroups[index].blocksRaycasts = blocksRaycasts;
        _buttonGroups[index].interactable = blocksRaycasts;
    }

    private void CacheSparkles()
    {
        _sparkleRects = sparkleVisuals ?? System.Array.Empty<RectTransform>();
        int count = _sparkleRects.Length;
        _sparkleGraphics = new Graphic[count];
        _sparkleBasePositions = new Vector2[count];
        _sparkleBaseScales = new Vector3[count];
        _sparkleBaseRotations = new Quaternion[count];
        _sparkleBaseColors = new Color[count];

        for (int i = 0; i < count; i++)
        {
            RectTransform rect = _sparkleRects[i];
            if (rect == null)
                continue;
            _sparkleBasePositions[i] = rect.anchoredPosition;
            _sparkleBaseScales[i] = rect.localScale;
            _sparkleBaseRotations[i] = rect.localRotation;
            _sparkleGraphics[i] = rect.GetComponent<Graphic>();
            _sparkleBaseColors[i] = _sparkleGraphics[i] != null ? _sparkleGraphics[i].color : Color.white;
        }
    }

    private void AnimateSparkles()
    {
        float now = Time.unscaledTime;
        for (int i = 0; i < _sparkleRects.Length; i++)
        {
            RectTransform rect = _sparkleRects[i];
            if (rect == null)
                continue;

            float direction = i % 2 == 0 ? 1f : -1f;
            float phase = now * sparklePulseSpeed + i * 0.73f;
            float driftPhase = now * (0.45f + (i % 4) * 0.08f) + i * 1.17f;
            rect.anchoredPosition = _sparkleBasePositions[i] + new Vector2(
                Mathf.Sin(driftPhase) * sparkleDriftPixels,
                Mathf.Cos(driftPhase * 0.83f) * sparkleDriftPixels);
            rect.localRotation = _sparkleBaseRotations[i]
                * Quaternion.Euler(0f, 0f, direction * now * sparkleRotationSpeed);
            rect.localScale = _sparkleBaseScales[i] * Mathf.Lerp(0.88f, 1.08f,
                (Mathf.Sin(phase * Mathf.PI * 2f) + 1f) * 0.5f);

            Graphic graphic = _sparkleGraphics[i];
            if (graphic == null)
                continue;
            Color color = _sparkleBaseColors[i];
            color.a *= _sparkleReveal * Mathf.Lerp(0.52f, 1f,
                (Mathf.Sin(phase * Mathf.PI * 2f) + 1f) * 0.5f);
            graphic.color = color;
        }
    }

    private void SetDarkOverlayAlpha(float normalized)
    {
        if (darkOverlay == null)
            return;
        Color color = _darkOverlayBaseColor;
        color.a *= Mathf.Clamp01(normalized);
        darkOverlay.color = color;
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
