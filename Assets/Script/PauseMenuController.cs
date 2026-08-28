using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// CompletSceneのPause状態とUI遷移を一元管理する。
/// Pause Actionは既存InputActionAsset内のPlayer/Pauseを使用する。
/// </summary>
[DisallowMultipleComponent]
public sealed class PauseMenuController : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private InputActionAsset inputActions;
    [SerializeField] private string pauseActionName = "Player/Pause";

    [Header("UI")]
    [SerializeField] private GameObject presentationRoot;
    [SerializeField] private GameObject menuPanel;
    [SerializeField] private ControlsPanelController controlsPanel;
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button controlsButton;
    [SerializeField] private Button titleButton;

    [Header("Scene Flow")]
    [SerializeField] private string titleSceneName = "TitleScene";

    private InputAction _pauseAction;

    public bool IsPaused { get; private set; }
    public bool IsShowingControls => controlsPanel != null && controlsPanel.IsVisible;

    private void Awake()
    {
        ValidateReferences();
        RegisterButtonListeners();
        ConfigureButtonNavigation();

        if (controlsPanel != null)
            controlsPanel.BackRequested += HandleControlsBack;

        if (presentationRoot != null)
            presentationRoot.SetActive(false);
        if (controlsPanel != null)
            controlsPanel.Hide();
    }

    private void Start()
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null)
        {
            Debug.LogError("[PauseMenu] EventSystemがSceneに存在しません。", this);
            return;
        }

        InputSystemUIInputModule inputModule = eventSystem.GetComponent<InputSystemUIInputModule>();
        if (inputModule == null || !inputModule.enabled || inputModule.actionsAsset == null)
            Debug.LogError("[PauseMenu] 有効なInputSystemUIInputModuleまたはUI Input Actionsが設定されていません。", eventSystem);
    }

    private void OnEnable()
    {
        _pauseAction = inputActions != null
            ? inputActions.FindAction(pauseActionName, false)
            : null;

        if (_pauseAction != null)
        {
            _pauseAction.performed += HandlePausePerformed;
            _pauseAction.Enable();
        }
        else
        {
            Debug.LogError($"[PauseMenu] Pause Actionが見つかりません: {pauseActionName}", this);
        }
    }

    private void OnDisable()
    {
        if (_pauseAction != null)
        {
            _pauseAction.performed -= HandlePausePerformed;
            _pauseAction.Disable();
        }
    }

    private void OnDestroy()
    {
        if (resumeButton != null)
            resumeButton.onClick.RemoveListener(ClosePause);
        if (controlsButton != null)
            controlsButton.onClick.RemoveListener(ShowControls);
        if (titleButton != null)
            titleButton.onClick.RemoveListener(ReturnToTitle);
        if (controlsPanel != null)
            controlsPanel.BackRequested -= HandleControlsBack;

        if (IsPaused)
            Time.timeScale = 1f;
    }

    private void RegisterButtonListeners()
    {
        if (resumeButton != null)
        {
            resumeButton.onClick.RemoveListener(ClosePause);
            resumeButton.onClick.AddListener(ClosePause);
        }

        if (controlsButton != null)
        {
            controlsButton.onClick.RemoveListener(ShowControls);
            controlsButton.onClick.AddListener(ShowControls);
        }

        if (titleButton != null)
        {
            titleButton.onClick.RemoveListener(ReturnToTitle);
            titleButton.onClick.AddListener(ReturnToTitle);
        }
    }

    private void ConfigureButtonNavigation()
    {
        if (resumeButton == null || controlsButton == null || titleButton == null)
            return;

        resumeButton.navigation = CreateNavigation(titleButton, controlsButton);
        controlsButton.navigation = CreateNavigation(resumeButton, titleButton);
        titleButton.navigation = CreateNavigation(controlsButton, resumeButton);
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

    private void ValidateReferences()
    {
        ValidateReference(inputActions, nameof(inputActions));
        ValidateReference(presentationRoot, nameof(presentationRoot));
        ValidateReference(menuPanel, nameof(menuPanel));
        ValidateReference(controlsPanel, nameof(controlsPanel));
        ValidateReference(resumeButton, nameof(resumeButton));
        ValidateReference(controlsButton, nameof(controlsButton));
        ValidateReference(titleButton, nameof(titleButton));
    }

    private void ValidateReference(Object value, string fieldName)
    {
        if (value == null)
            Debug.LogError($"[PauseMenu] 必須参照が未設定です: {fieldName}", this);
    }

    private void HandlePausePerformed(InputAction.CallbackContext context)
    {
        if (IsShowingControls)
        {
            HandleControlsBack();
            return;
        }

        if (IsPaused)
            ClosePause();
        else
            OpenPause();
    }

    public void OpenPause()
    {
        if (IsPaused)
            return;

        IsPaused = true;
        Time.timeScale = 0f;

        if (presentationRoot != null)
            presentationRoot.SetActive(true);
        if (menuPanel != null)
            menuPanel.SetActive(true);
        if (controlsPanel != null)
            controlsPanel.Hide();

        SelectButton(resumeButton);
    }

    public void ClosePause()
    {
        if (controlsPanel != null)
            controlsPanel.Hide();
        if (presentationRoot != null)
            presentationRoot.SetActive(false);

        IsPaused = false;
        Time.timeScale = 1f;
        ClearSelection();
    }

    public void ShowControls()
    {
        if (!IsPaused || controlsPanel == null)
            return;

        if (menuPanel != null)
            menuPanel.SetActive(false);
        controlsPanel.Show();
    }

    public void ReturnToTitle()
    {
        IsPaused = false;
        Time.timeScale = 1f;

        if (presentationRoot != null)
            presentationRoot.SetActive(false);

        ClearSelection();

        SceneManager.LoadScene(titleSceneName, LoadSceneMode.Single);
    }

    private void HandleControlsBack()
    {
        if (!IsPaused)
            return;

        if (controlsPanel != null)
            controlsPanel.Hide();
        if (menuPanel != null)
            menuPanel.SetActive(true);
        SelectButton(controlsButton);
    }

    private void SelectButton(Button button)
    {
        if (button != null)
            StartCoroutine(SelectButtonNextFrame(button));
    }

    private static void ClearSelection()
    {
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    private static IEnumerator SelectButtonNextFrame(Button button)
    {
        yield return null;
        if (button != null && button.isActiveAndEnabled && button.interactable && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(button.gameObject);
    }
}
