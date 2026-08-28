using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// TitleとPauseで共有する操作説明Panel。
/// 内容と見た目はPrefab側へ置き、戻る通知だけをScene側へ渡す。
/// </summary>
[DisallowMultipleComponent]
public sealed class ControlsPanelController : MonoBehaviour
{
    [SerializeField] private Button backButton;

    private CanvasGroup _canvasGroup;

    public event Action BackRequested;

    public bool IsVisible => gameObject.activeSelf;

    private void Awake()
    {
        _canvasGroup = GetComponent<CanvasGroup>();
        if (backButton == null)
        {
            Debug.LogError("[ControlsPanel] 必須参照が未設定です: backButton", this);
            return;
        }

        backButton.onClick.RemoveListener(RequestBack);
        backButton.onClick.AddListener(RequestBack);
    }

    private void OnDestroy()
    {
        if (backButton != null)
            backButton.onClick.RemoveListener(RequestBack);
    }

    private void Update()
    {
        if (!IsVisible)
            return;

        Keyboard keyboard = Keyboard.current;
        Gamepad gamepad = Gamepad.current;
        bool cancelPressed = (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            || (gamepad != null && gamepad.buttonEast.wasPressedThisFrame);

        if (cancelPressed)
            RequestBack();
    }

    public void Show()
    {
        gameObject.SetActive(true);
        // Shared panel must render above whichever menu opened it after activation.
        transform.SetAsLastSibling();
        SetCanvasGroupState(true);
        if (backButton != null)
            StartCoroutine(SelectBackButtonNextFrame());
    }

    public void Hide()
    {
        SetCanvasGroupState(false);
        gameObject.SetActive(false);
    }

    private void RequestBack()
    {
        Hide();
        BackRequested?.Invoke();
    }

    private void SetCanvasGroupState(bool visible)
    {
        if (_canvasGroup == null)
            return;

        _canvasGroup.alpha = visible ? 1f : 0f;
        _canvasGroup.interactable = visible;
        _canvasGroup.blocksRaycasts = visible;
    }

    private IEnumerator SelectBackButtonNextFrame()
    {
        yield return null;
        if (backButton != null && backButton.isActiveAndEnabled && backButton.interactable && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(backButton.gameObject);
    }
}
