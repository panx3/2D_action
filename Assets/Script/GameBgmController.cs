using System.Collections;
using UnityEngine;

/// <summary>
/// シーン内のBGM用AudioSourceを一元管理し、同じSource上で曲を切り替える。
/// Sceneロード時に作り直すため、DontDestroyOnLoadによる重複を持ち越さない。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public sealed class GameBgmController : MonoBehaviour
{
    public enum BgmState
    {
        None,
        Title,
        Stage,
        Goal
    }

    [Header("Source")]
    [SerializeField] private AudioSource bgmSource;
    [SerializeField] private BgmState initialState = BgmState.Stage;

    [Header("Clips")]
    [SerializeField] private AudioClip titleClip;
    [SerializeField] private AudioClip stageClip;
    [SerializeField] private AudioClip goalClip;

    [Header("Volumes")]
    [SerializeField, Range(0f, 1f)] private float titleVolume = 0.2f;
    [SerializeField, Range(0f, 1f)] private float stageVolume = 0.15f;
    [SerializeField, Range(0f, 1f)] private float goalVolume = 0.15f;
    [SerializeField, Min(0f)] private float switchFadeDuration = 0.5f;

    [Header("Pause Tone")]
    [SerializeField, Range(10f, 22000f)] private float pauseCutoffFrequency = 2200f;
    [SerializeField, Min(0f)] private float pauseToneFadeDuration = 0.18f;

    private Coroutine _transitionRoutine;
    private AudioLowPassFilter pauseFilter;
    private bool ownsPauseTone;
    private bool pauseToneRequested;
    private bool previousFilterEnabled;
    private float previousCutoff;
    private float pauseToneBlend;

    public static GameBgmController Instance { get; private set; }
    public BgmState CurrentState { get; private set; } = BgmState.None;
    public AudioSource Source => bgmSource;

    private void Awake()
    {
        // 同一Sceneに誤って複数置かれた場合は、後から起動したSourceを確実に止める。
        if (Instance != null && Instance != this && Instance.gameObject.scene == gameObject.scene)
        {
            AudioSource duplicateSource = bgmSource != null ? bgmSource : GetComponent<AudioSource>();
            duplicateSource.playOnAwake = false;
            duplicateSource.Stop();
            enabled = false;
            return;
        }

        Instance = this;
        if (bgmSource == null)
            bgmSource = GetComponent<AudioSource>();

        bgmSource.playOnAwake = false;
        bgmSource.loop = true;
        Play(initialState, true);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>Pause alone owns the muffled tone. Clip playback, pitch and volume stay with the existing BGM flow.</summary>
    public void SetPauseMuffled(bool muffled)
    {
        if (!muffled)
        {
            pauseToneRequested = false;
            return;
        }
        if (CurrentState == BgmState.Goal || bgmSource == null)
            return;
        if (!ownsPauseTone)
        {
            if (pauseFilter == null)
            {
                pauseFilter = bgmSource.GetComponent<AudioLowPassFilter>();
                if (pauseFilter == null)
                {
                    pauseFilter = bgmSource.gameObject.AddComponent<AudioLowPassFilter>();
                    pauseFilter.enabled = false;
                }
            }
            previousFilterEnabled = pauseFilter.enabled;
            previousCutoff = pauseFilter.cutoffFrequency;
            pauseToneBlend = 0f;
            ownsPauseTone = true;
        }
        pauseToneRequested = true;
    }

    private void Update()
    {
        if (!ownsPauseTone || pauseFilter == null)
            return;
        float target = pauseToneRequested ? 1f : 0f;
        pauseToneBlend = pauseToneFadeDuration > 0f
            ? Mathf.MoveTowards(pauseToneBlend, target, Time.unscaledDeltaTime / pauseToneFadeDuration)
            : target;
        float openCutoff = previousFilterEnabled ? previousCutoff : 22000f;
        float closedCutoff = Mathf.Min(openCutoff, pauseCutoffFrequency);
        pauseFilter.enabled = true;
        pauseFilter.cutoffFrequency = Mathf.Exp(Mathf.Lerp(Mathf.Log(openCutoff), Mathf.Log(closedCutoff), pauseToneBlend));
        if (!pauseToneRequested && pauseToneBlend <= 0f)
            RestorePauseTone();
    }

    private void RestorePauseTone()
    {
        if (ownsPauseTone && pauseFilter != null)
        {
            pauseFilter.cutoffFrequency = previousCutoff;
            pauseFilter.enabled = previousFilterEnabled;
        }
        ownsPauseTone = false;
        pauseToneRequested = false;
        pauseToneBlend = 0f;
    }

    private void OnDisable() => RestorePauseTone();

    public void PlayTitle() => Play(BgmState.Title);
    public void PlayStage() => Play(BgmState.Stage);
    public void PlayGoal() => Play(BgmState.Goal);

    /// <summary>同じ状態への要求では曲を先頭から再生し直さない。</summary>
    public void Play(BgmState state, bool immediate = false)
    {
        if (state == BgmState.None || bgmSource == null)
            return;

        AudioClip nextClip = GetClip(state);
        if (nextClip == null)
        {
            Debug.LogWarning($"[GameBgm] {state} BGMが未設定です。", this);
            return;
        }

        if (state == BgmState.Goal)
            RestorePauseTone();
        if (CurrentState == state)
            return;

        CurrentState = state;
        if (_transitionRoutine != null)
        {
            StopCoroutine(_transitionRoutine);
            _transitionRoutine = null;
        }

        float targetVolume = GetVolume(state);
        if (state == BgmState.Stage && TitleStageTransition.IsTransitioning)
        {
            _transitionRoutine = StartCoroutine(EnterStageAfterTitle(nextClip, targetVolume));
            return;
        }
        if (immediate || !bgmSource.isPlaying || switchFadeDuration <= 0f)
        {
            StartClip(nextClip, targetVolume);
            return;
        }

        _transitionRoutine = StartCoroutine(SwitchRoutine(nextClip, targetVolume));
    }

    public void FadeOut(float duration)
    {
        if (bgmSource == null || CurrentState == BgmState.None)
            return;

        CurrentState = BgmState.None;
        if (_transitionRoutine != null)
            StopCoroutine(_transitionRoutine);
        _transitionRoutine = StartCoroutine(FadeOutRoutine(Mathf.Max(0f, duration)));
    }

    private IEnumerator EnterStageAfterTitle(AudioClip clip, float volume)
    {
        // The existing intro starts when the veil clears. Bring its music in on that same gate.
        while (TitleStageTransition.IsTransitioning) yield return null;
        StartClip(clip, 0);
        yield return FadeVolume(0, volume, Mathf.Max(.1f, switchFadeDuration));
        _transitionRoutine = null;
    }

    private IEnumerator SwitchRoutine(AudioClip nextClip, float targetVolume)
    {
        float halfDuration = switchFadeDuration * 0.5f;
        yield return FadeVolume(bgmSource.volume, 0f, halfDuration);
        StartClip(nextClip, 0f);
        yield return FadeVolume(0f, targetVolume, halfDuration);
        bgmSource.volume = targetVolume;
        _transitionRoutine = null;
    }

    private IEnumerator FadeOutRoutine(float duration)
    {
        yield return FadeVolume(bgmSource.volume, 0f, duration);
        bgmSource.Stop();
        _transitionRoutine = null;
    }

    private IEnumerator FadeVolume(float from, float to, float duration)
    {
        if (duration <= 0f)
        {
            bgmSource.volume = to;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            bgmSource.volume = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }

        bgmSource.volume = to;
    }

    private void StartClip(AudioClip clip, float volume)
    {
        bgmSource.Stop();
        bgmSource.clip = clip;
        bgmSource.loop = true;
        bgmSource.volume = volume;
        bgmSource.Play();
    }

    private AudioClip GetClip(BgmState state)
    {
        return state switch
        {
            BgmState.Title => titleClip,
            BgmState.Stage => stageClip,
            BgmState.Goal => goalClip,
            _ => null
        };
    }

    private float GetVolume(BgmState state)
    {
        return state switch
        {
            BgmState.Title => titleVolume,
            BgmState.Stage => stageVolume,
            BgmState.Goal => goalVolume,
            _ => 0f
        };
    }
}
