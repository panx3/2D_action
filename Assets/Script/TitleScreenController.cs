using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 起動タイトル専用Controller。表示演出とStage遷移のみを担当し、本編Playerには依存しない。
/// </summary>
[DisallowMultipleComponent]
public sealed class TitleScreenController : MonoBehaviour
{
    [Header("Scene Flow")]
    [SerializeField] private string stageSceneName;

    [Header("Intro Groups")]
    [SerializeField] private CanvasGroup backgroundGroup;
    [SerializeField] private CanvasGroup logoGroup;
    [SerializeField] private CanvasGroup characterGroup;
    [SerializeField] private CanvasGroup startGroup;
    [SerializeField] private CanvasGroup flashGroup;
    [SerializeField] private CanvasGroup fadeGroup;
    [SerializeField, Min(0.1f)] private float introDuration = 1.3f;

    [Header("Title Display")]
    [SerializeField] private RectTransform sceneRoot;
    [SerializeField] private RectTransform heroMotionRoot;
    [SerializeField] private RectTransform ballRoot;
    [SerializeField] private RectTransform startPanel;
    [SerializeField] private RectTransform startGlow;
    [SerializeField] private Button startButton;
    [SerializeField] private Button controlsButton;
    [SerializeField] private ControlsPanelController controlsPanel;
    [SerializeField] private TitleChainDisplay chainDisplay;
    [SerializeField] private RectTransform[] backgroundLayers;
    [SerializeField] private Graphic[] ambientGlowGraphics;

    [Header("Idle")]
    [SerializeField, Min(0.2f)] private float startPulsePeriod = 2f;
    [SerializeField, Range(0f, 0.2f)] private float startPulseScale = 0.035f;
    [SerializeField, Min(0f)] private float backgroundDrift = 8f;

    [Header("Stage Loading Transition")]
    [SerializeField] private Sprite loadingBallSprite;
    [SerializeField] private TMP_FontAsset loadingFont;
    [SerializeField, Min(0f)] private float fadeOutDuration = 0.25f;
    [SerializeField, Min(0f)] private float minimumLoadingDuration = 0.6f;
    [SerializeField, Min(0f)] private float loadingFadeOutDuration = 0.18f;
    [SerializeField, Min(0f)] private float stageFadeInDuration = 0.3f;
    [SerializeField, Min(0f)] private float loadingRotationSpeed = 220f;

    [Header("Audio")]
    [SerializeField] private AudioSource bgmSource;
    [SerializeField] private AudioSource sfxSource;
    [SerializeField] private AudioClip startConfirmClip;
    [SerializeField, Range(0f, 1f)] private float startConfirmVolume = 0.55f;

    private Vector2 _sceneOrigin;
    private Vector2 _heroOrigin;
    private Vector2 _ballOrigin;
    private Vector3 _ballRotationOrigin;
    private Vector3 _startScaleOrigin;
    private Vector3 _startGlowScaleOrigin;
    private Vector2[] _backgroundOrigins;
    private Color[] _ambientBaseColors;
    private float _bgmStartVolume;
    private bool _inputReady;
    private bool _isStarting;

    public bool InputReady => _inputReady;
    public bool IsStarting => _isStarting;
    public string StageSceneName => stageSceneName;
    public float MinimumLoadingDuration => minimumLoadingDuration;

    private void Awake()
    {
        Time.timeScale = 1f;
        ValidateReferences();
        RegisterButtonListeners();
        ConfigureButtonNavigation();

        _sceneOrigin = sceneRoot != null ? sceneRoot.anchoredPosition : Vector2.zero;
        _heroOrigin = heroMotionRoot != null ? heroMotionRoot.anchoredPosition : Vector2.zero;
        _ballOrigin = ballRoot != null ? ballRoot.anchoredPosition : Vector2.zero;
        _ballRotationOrigin = ballRoot != null ? ballRoot.localEulerAngles : Vector3.zero;
        _startScaleOrigin = startPanel != null ? startPanel.localScale : Vector3.one;
        _startGlowScaleOrigin = startGlow != null ? startGlow.localScale : Vector3.one;
        _bgmStartVolume = bgmSource != null ? bgmSource.volume : 0f;

        CacheAmbientState();
        SetCanvasAlpha(backgroundGroup, 0f);
        SetCanvasAlpha(logoGroup, 0f);
        SetCanvasAlpha(characterGroup, 0f);
        SetCanvasAlpha(startGroup, 0f);
        SetCanvasAlpha(flashGroup, 0f);
        SetCanvasAlpha(fadeGroup, 1f);
        if (startButton != null)
            startButton.interactable = false;
        if (controlsButton != null)
            controlsButton.interactable = false;
        if (controlsPanel != null)
        {
            controlsPanel.BackRequested += HandleControlsBack;
            controlsPanel.Hide();
        }
        if (chainDisplay != null)
        {
            chainDisplay.SetTension(0f);
            chainDisplay.SetAlpha(0f);
        }
    }

    private void Start()
    {
        ValidateEventSystem();
        StartCoroutine(IntroRoutine());
    }

    private void Update()
    {
        if (!_isStarting)
            UpdateIdlePresentation();
    }

    public void OnStartPressed()
    {
        if (!_inputReady || _isStarting || (controlsPanel != null && controlsPanel.IsVisible))
            return;

        _isStarting = true;
        _inputReady = false;
        if (startButton != null)
            startButton.interactable = false;
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);

        StartSequence();
    }

    public void OnControlsPressed()
    {
        if (!_inputReady || _isStarting || controlsPanel == null)
            return;

        if (startGroup != null)
        {
            startGroup.interactable = false;
            startGroup.blocksRaycasts = false;
        }

        controlsPanel.Show();
    }

    private void OnDestroy()
    {
        if (startButton != null)
            startButton.onClick.RemoveListener(OnStartPressed);
        if (controlsButton != null)
            controlsButton.onClick.RemoveListener(OnControlsPressed);
        if (controlsPanel != null)
            controlsPanel.BackRequested -= HandleControlsBack;
    }

    private void RegisterButtonListeners()
    {
        if (startButton != null)
        {
            startButton.onClick.RemoveListener(OnStartPressed);
            startButton.onClick.AddListener(OnStartPressed);
        }

        if (controlsButton != null)
        {
            controlsButton.onClick.RemoveListener(OnControlsPressed);
            controlsButton.onClick.AddListener(OnControlsPressed);
        }
    }

    private void ConfigureButtonNavigation()
    {
        if (startButton == null || controlsButton == null)
            return;

        Navigation startNavigation = new Navigation
        {
            mode = Navigation.Mode.Explicit,
            selectOnUp = controlsButton,
            selectOnDown = controlsButton
        };
        startButton.navigation = startNavigation;

        Navigation controlsNavigation = new Navigation
        {
            mode = Navigation.Mode.Explicit,
            selectOnUp = startButton,
            selectOnDown = startButton
        };
        controlsButton.navigation = controlsNavigation;
    }

    private void ValidateReferences()
    {
        ValidateReference(sceneRoot, nameof(sceneRoot));
        ValidateReference(startGroup, nameof(startGroup));
        ValidateReference(startButton, nameof(startButton));
        ValidateReference(controlsButton, nameof(controlsButton));
        ValidateReference(controlsPanel, nameof(controlsPanel));
    }

    private void ValidateEventSystem()
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null)
        {
            Debug.LogError("[TitleScreen] EventSystemがSceneに存在しません。", this);
            return;
        }

        InputSystemUIInputModule inputModule = eventSystem.GetComponent<InputSystemUIInputModule>();
        if (inputModule == null || !inputModule.enabled || inputModule.actionsAsset == null)
            Debug.LogError("[TitleScreen] 有効なInputSystemUIInputModuleまたはUI Input Actionsが設定されていません。", eventSystem);
    }

    private void ValidateReference(Object value, string fieldName)
    {
        if (value == null)
            Debug.LogError($"[TitleScreen] 必須参照が未設定です: {fieldName}", this);
    }

    private void HandleControlsBack()
    {
        if (startGroup != null)
        {
            startGroup.interactable = true;
            startGroup.blocksRaycasts = true;
        }

        if (startButton != null && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(startButton.gameObject);
    }

    private IEnumerator IntroRoutine()
    {
        float duration = Mathf.Max(0.1f, introDuration);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            SetCanvasAlpha(fadeGroup, 1f - SmoothRange(t, 0f, 0.42f));
            SetCanvasAlpha(backgroundGroup, SmoothRange(t, 0f, 0.42f));
            SetCanvasAlpha(logoGroup, SmoothRange(t, 0.14f, 0.64f));
            SetCanvasAlpha(characterGroup, SmoothRange(t, 0.3f, 0.78f));
            SetCanvasAlpha(startGroup, SmoothRange(t, 0.62f, 1f));
            if (chainDisplay != null)
                chainDisplay.SetAlpha(characterGroup != null ? characterGroup.alpha : 1f);
            yield return null;
        }

        SetCanvasAlpha(fadeGroup, 0f);
        SetCanvasAlpha(backgroundGroup, 1f);
        SetCanvasAlpha(logoGroup, 1f);
        SetCanvasAlpha(characterGroup, 1f);
        SetCanvasAlpha(startGroup, 1f);
        if (chainDisplay != null)
            chainDisplay.SetAlpha(1f);

        _inputReady = true;
        if (startButton != null)
        {
            startButton.interactable = true;
            startButton.Select();
        }
        if (controlsButton != null)
            controlsButton.interactable = true;
    }

    private void UpdateIdlePresentation()
    {
        float time = Time.unscaledTime;
        float pulsePhase = (Mathf.Sin(time * Mathf.PI * 2f / Mathf.Max(0.2f, startPulsePeriod)) + 1f) * 0.5f;

        if (startPanel != null)
            startPanel.localScale = _startScaleOrigin * (1f + pulsePhase * startPulseScale);
        if (startGlow != null)
            startGlow.localScale = _startGlowScaleOrigin * Mathf.Lerp(0.98f, 1.06f, pulsePhase);

        if (ambientGlowGraphics != null)
        {
            for (int i = 0; i < ambientGlowGraphics.Length; i++)
            {
                Graphic graphic = ambientGlowGraphics[i];
                if (graphic == null || i >= _ambientBaseColors.Length)
                    continue;
                Color color = _ambientBaseColors[i];
                color.a *= Mathf.Lerp(0.55f, 1f, pulsePhase);
                graphic.color = color;
            }
        }

        if (backgroundLayers != null)
        {
            for (int i = 0; i < backgroundLayers.Length; i++)
            {
                RectTransform layer = backgroundLayers[i];
                if (layer == null || i >= _backgroundOrigins.Length)
                    continue;
                float factor = (i + 1f) / backgroundLayers.Length;
                float x = Mathf.Sin(time * (0.035f + i * 0.012f)) * backgroundDrift * factor;
                float y = Mathf.Cos(time * (0.025f + i * 0.01f)) * backgroundDrift * factor * 0.2f;
                layer.anchoredPosition = _backgroundOrigins[i] + new Vector2(x, y);
            }
        }
    }

    private void StartSequence()
    {
        if (sfxSource != null && startConfirmClip != null)
            sfxSource.PlayOneShot(startConfirmClip, startConfirmVolume);

        if (string.IsNullOrWhiteSpace(stageSceneName))
        {
            Debug.LogError("[TitleScreen] Stage Sceneが設定されていません。", this);
            _isStarting = false;
            RestoreStartInput();
            return;
        }

        if (!TitleStageTransition.Begin(
                stageSceneName,
                loadingBallSprite,
                loadingFont,
                fadeOutDuration,
                minimumLoadingDuration,
                loadingFadeOutDuration,
                stageFadeInDuration,
                loadingRotationSpeed))
        {
            Debug.LogError("[TitleScreen] Stage Loading Transitionを開始できませんでした。", this);
            _isStarting = false;
            RestoreStartInput();
        }
    }

    private void RestoreStartInput()
    {
        _inputReady = true;
        if (startButton != null)
            startButton.interactable = true;
    }

    private void CacheAmbientState()
    {
        if (backgroundLayers == null)
            backgroundLayers = new RectTransform[0];
        _backgroundOrigins = new Vector2[backgroundLayers.Length];
        for (int i = 0; i < backgroundLayers.Length; i++)
            _backgroundOrigins[i] = backgroundLayers[i] != null ? backgroundLayers[i].anchoredPosition : Vector2.zero;

        if (ambientGlowGraphics == null)
            ambientGlowGraphics = new Graphic[0];
        _ambientBaseColors = new Color[ambientGlowGraphics.Length];
        for (int i = 0; i < ambientGlowGraphics.Length; i++)
            _ambientBaseColors[i] = ambientGlowGraphics[i] != null ? ambientGlowGraphics[i].color : Color.white;
    }

    private static float SmoothRange(float value, float start, float end)
    {
        if (end <= start)
            return value >= end ? 1f : 0f;
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start, end, value));
    }

    private static void SetCanvasAlpha(CanvasGroup group, float alpha)
    {
        if (group != null)
            group.alpha = Mathf.Clamp01(alpha);
    }
}
