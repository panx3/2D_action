using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 現在画面をキャプチャしてSceneを覆ったまま次Sceneをロードし、
/// キャプチャを左へ押し出して次Sceneを露出させる。
/// </summary>
public sealed class ScreenPushTransition : MonoBehaviour
{
    private static ScreenPushTransition _active;

    private RectTransform _captureRect;
    private Texture2D _captureTexture;

    public static bool IsTransitioning => _active != null;

    public static bool Begin(string sceneName, float slideDuration)
    {
        if (_active != null || string.IsNullOrWhiteSpace(sceneName))
            return false;
        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError($"[ScreenPushTransition] Sceneをロードできません: {sceneName}");
            return false;
        }

        GameObject root = new GameObject(nameof(ScreenPushTransition));
        Object.DontDestroyOnLoad(root);
        _active = root.AddComponent<ScreenPushTransition>();
        _active.StartCoroutine(_active.TransitionRoutine(sceneName, slideDuration));
        return true;
    }

    private IEnumerator TransitionRoutine(string sceneName, float slideDuration)
    {
        // TitleSceneの描画が完了したフレームをそのまま保持する。
        if (Application.isBatchMode)
        {
            yield return null;
            _captureTexture = CreateBatchModeTexture();
        }
        else
        {
            yield return new WaitForEndOfFrame();
            _captureTexture = ScreenCapture.CaptureScreenshotAsTexture();
        }
        BuildOverlay(_captureTexture);

        Time.timeScale = 0f;
        AsyncOperation load = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
        if (load == null)
        {
            FinishTransition();
            yield break;
        }

        while (!load.isDone)
            yield return null;

        // 新Sceneを少なくとも1回描画してから、覆っているTitle画像を動かす。
        yield return null;
        if (!Application.isBatchMode)
            yield return new WaitForEndOfFrame();

        float duration = Mathf.Max(0.05f, slideDuration);
        float width = Mathf.Max(1f, Screen.width);
        double slideStartedAt = Time.realtimeSinceStartupAsDouble;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed = (float)(Time.realtimeSinceStartupAsDouble - slideStartedAt);
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = Mathf.SmoothStep(0f, 1f, t);
            if (_captureRect != null)
                _captureRect.anchoredPosition = Vector2.left * (width * eased);
            yield return null;
        }

        FinishTransition();
    }

    private static Texture2D CreateBatchModeTexture()
    {
        Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        texture.SetPixels(new[] { Color.black, Color.black, Color.black, Color.black });
        texture.Apply(false, false);
        return texture;
    }

    private void BuildOverlay(Texture texture)
    {
        GameObject canvasObject = new GameObject(
            "TransitionCanvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;

        GameObject imageObject = new GameObject("TitleCapture", typeof(RectTransform), typeof(RawImage));
        imageObject.transform.SetParent(canvasObject.transform, false);
        _captureRect = imageObject.GetComponent<RectTransform>();
        _captureRect.anchorMin = Vector2.zero;
        _captureRect.anchorMax = Vector2.one;
        _captureRect.pivot = new Vector2(0.5f, 0.5f);
        _captureRect.offsetMin = Vector2.zero;
        _captureRect.offsetMax = Vector2.zero;

        RawImage image = imageObject.GetComponent<RawImage>();
        image.texture = texture;
        image.color = Color.white;
        image.raycastTarget = true;
    }

    private void FinishTransition()
    {
        Time.timeScale = 1f;
        if (_captureTexture != null)
            Destroy(_captureTexture);
        _active = null;
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (_active == this)
            _active = null;
        Time.timeScale = 1f;
    }
}
