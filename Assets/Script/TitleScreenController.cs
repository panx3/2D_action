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
    [SerializeField] private Button exitButton;
    [SerializeField] private TitleAtmosphere atmosphere;
    [SerializeField] private CanvasGroup titleCompositionGroup;
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
    [SerializeField] private Shader transitionShader;
    [SerializeField] private Texture2D loadingBackdrop;
    [SerializeField] private TitleStageTransition.Settings transitionSettings = new TitleStageTransition.Settings();

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
    private bool _inputReady;
    private bool _isStarting;
    private AudioClip _navigationClip;
    private float _lastNavigationSound;
    private TitleMenuItem[] _menuItems;
    private Vector3 _backgroundScale;

    public bool InputReady => _inputReady;
    public bool IsStarting => _isStarting;
    public string StageSceneName => stageSceneName;
    public float MinimumLoadingDuration => transitionSettings.minimumLoadingDuration;

    private void Awake()
    {
        Time.timeScale = 1f;
        ValidateReferences();
        RegisterButtonListeners();
        ConfigureButtonNavigation();
        CreateNavigationSound();
        _menuItems = startPanel != null ? startPanel.GetComponentsInChildren<TitleMenuItem>(true) : new TitleMenuItem[0];
        _backgroundScale = backgroundGroup != null ? backgroundGroup.transform.localScale : Vector3.one;
        if (titleCompositionGroup == null && logoGroup != null && logoGroup.transform.parent != null)
        {
            titleCompositionGroup = logoGroup.transform.parent.GetComponent<CanvasGroup>();
            if (titleCompositionGroup == null)
                titleCompositionGroup = logoGroup.transform.parent.gameObject.AddComponent<CanvasGroup>();
        }

        _sceneOrigin = sceneRoot != null ? sceneRoot.anchoredPosition : Vector2.zero;
        _heroOrigin = heroMotionRoot != null ? heroMotionRoot.anchoredPosition : Vector2.zero;
        _ballOrigin = ballRoot != null ? ballRoot.anchoredPosition : Vector2.zero;
        _ballRotationOrigin = ballRoot != null ? ballRoot.localEulerAngles : Vector3.zero;
        _startScaleOrigin = startPanel != null ? startPanel.localScale : Vector3.one;
        _startGlowScaleOrigin = startGlow != null ? startGlow.localScale : Vector3.one;
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
        if (exitButton != null)
            exitButton.interactable = false;
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
        if (_inputReady && !_isStarting && (controlsPanel == null || !controlsPanel.IsVisible)
            && EventSystem.current != null && EventSystem.current.currentSelectedGameObject == null)
            startButton.Select();
    }

    public void OnStartPressed()
    {
        if (!_inputReady || _isStarting || (controlsPanel != null && controlsPanel.IsVisible))
            return;

        _isStarting = true;
        _inputReady = false;
        if (startButton != null)
            startButton.interactable = false;
        if (startGroup != null) startGroup.interactable = false;
        startButton.GetComponent<TitleMenuItem>()?.Confirm();
        atmosphere?.Confirm();
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

    public void OnExitPressed()
    {
        if (!_inputReady || _isStarting || (controlsPanel != null && controlsPanel.IsVisible)) return;
#if UNITY_EDITOR
        // Never close the editor from a runtime menu.
        Debug.Log("[TitleScreen] EXIT: standalone builds quit here.");
#else
        Application.Quit();
#endif
    }

    internal void PlayTransitionConfirm(AudioSource destination)
    {
        if (sfxSource != null) destination.outputAudioMixerGroup = sfxSource.outputAudioMixerGroup;
        if (startConfirmClip != null) destination.PlayOneShot(startConfirmClip, startConfirmVolume);
    }

    internal void PresentStartTransition(float elapsed)
    {
        float zoom = SmoothRange(elapsed, transitionSettings.confirmDuration, transitionSettings.DepartureDuration);
        if (backgroundGroup != null)
            backgroundGroup.transform.localScale = _backgroundScale * Mathf.Lerp(1, transitionSettings.backgroundZoom, zoom);
        if (logoGroup != null) logoGroup.alpha = 1 - SmoothRange(elapsed, 0, .30f);
        if (titleCompositionGroup != null) titleCompositionGroup.alpha = 1 - SmoothRange(elapsed, .18f, transitionSettings.DepartureDuration);
        foreach (var item in _menuItems)
            item.SetTransitionAlpha(1 - SmoothRange(elapsed, item.gameObject == startButton.gameObject ? .18f : 0, .34f));
        // The backdrop is drawn by the background shader, outside the menu CanvasGroup.
        atmosphere?.SetMenuVisibility(1 - SmoothRange(elapsed, .18f, .34f));
        atmosphere?.SetDeparting(true);
    }

    internal void RestoreAfterTransitionFailure()
    {
        _isStarting = false;
        if (backgroundGroup != null) backgroundGroup.transform.localScale = _backgroundScale;
        SetCanvasAlpha(logoGroup, 1); SetCanvasAlpha(titleCompositionGroup, 1);
        foreach (var item in _menuItems) item.SetTransitionAlpha(1);
        atmosphere?.SetMenuVisibility(1);
        atmosphere?.SetDeparting(false);
        RestoreStartInput();
        GameBgmController.Instance?.PlayTitle();
    }

    public void PlayNavigationSound()
    {
        if (sfxSource == null || _navigationClip == null || Time.unscaledTime - _lastNavigationSound < 0.09f) return;
        _lastNavigationSound = Time.unscaledTime;
        sfxSource.PlayOneShot(_navigationClip, 0.1f);
    }

    private void CreateNavigationSound()
    {
        // Original, short bell-like tick; no external recording or asset dependency.
        const int rate = 22050;
        float[] samples = new float[2205];
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)rate;
            float envelope = Mathf.Exp(-t * 55f) * Mathf.Min(1f, t * 800f) * (1f - i / (float)samples.Length);
            samples[i] = (Mathf.Sin(t * 880f * Mathf.PI * 2f) + 0.2f * Mathf.Sin(t * 2376f * Mathf.PI * 2f)) * envelope * 0.3f;
        }
        _navigationClip = AudioClip.Create("Title navigation tick", samples.Length, 1, rate, false);
        _navigationClip.SetData(samples, 0);
    }

    private void OnDestroy()
    {
        if (startButton != null)
            startButton.onClick.RemoveListener(OnStartPressed);
        if (controlsButton != null)
            controlsButton.onClick.RemoveListener(OnControlsPressed);
        if (controlsPanel != null)
            controlsPanel.BackRequested -= HandleControlsBack;
        if (exitButton != null) exitButton.onClick.RemoveListener(OnExitPressed);
        if (_navigationClip != null) Destroy(_navigationClip);
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
        if (exitButton != null)
        {
            exitButton.onClick.RemoveListener(OnExitPressed);
            exitButton.onClick.AddListener(OnExitPressed);
        }
    }

    private void ConfigureButtonNavigation()
    {
        if (startButton == null || controlsButton == null)
            return;

        CRTMenuNavigation.Configure(startButton, controlsButton, exitButton);
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

        if (controlsButton != null && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(controlsButton.gameObject);
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
        if (exitButton != null)
            exitButton.interactable = true;
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
                transitionShader,
                loadingBackdrop,
                transitionSettings,
                this))
        {
            Debug.LogError("[TitleScreen] Stage Loading Transitionを開始できませんでした。", this);
            _isStarting = false;
            RestoreStartInput();
            return;
        }

        GameBgmController.Instance?.FadeOut(transitionSettings.DepartureDuration + transitionSettings.coverDuration);
    }

    private void RestoreStartInput()
    {
        _inputReady = true;
        if (startButton != null)
        {
            startButton.interactable = true;
            startButton.Select();
        }
        if (startGroup != null) startGroup.interactable = true;
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
