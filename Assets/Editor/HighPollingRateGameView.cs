using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>Skip redundant Editor IMGUI motion dispatch; runtime input remains owned by Input System.</summary>
[InitializeOnLoad]
internal static class HighPollingRateGameView
{
    private static EditorWindow gameView;
    private static VisualElement panelRoot;
    private static GameplayPixelCursor cursor;
    private static Rect contentRect;
    private static PropertyInfo viewRectProperty;

    static HighPollingRateGameView()
    {
        if (Application.platform != RuntimePlatform.WindowsEditor)
            return;
        EditorApplication.update += Refresh;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        AssemblyReloadEvents.beforeAssemblyReload += Detach;
        EditorApplication.quitting += Detach;
    }

    private static void Refresh()
    {
        var window = EditorWindow.focusedWindow;
        if (!EditorApplication.isPlaying || EditorApplication.isPaused
            || window == null || window.GetType().FullName != "UnityEditor.GameView"
            || InputSystem.settings.updateMode != InputSettings.UpdateMode.ProcessEventsInDynamicUpdate)
        {
            Detach();
            return;
        }

        if (cursor == null)
            cursor = Object.FindAnyObjectByType<GameplayPixelCursor>();
        if (cursor == null)
        {
            Detach();
            return;
        }

        var root = window.rootVisualElement.panel?.visualTree;
        if (root == null)
        {
            Detach();
            return;
        }

        if (window != gameView)
            viewRectProperty = window.GetType().GetProperty("viewInWindow", BindingFlags.Instance | BindingFlags.NonPublic);
        // Fail open if Unity changes its Game View geometry API. Never intercept
        // toolbar/tab events using an estimated content rectangle.
        if (viewRectProperty == null)
        {
            Detach();
            return;
        }

        var localRect = (Rect)viewRectProperty.GetValue(window);
        contentRect = window.rootVisualElement.LocalToWorld(localRect);
        if (root == panelRoot && window == gameView)
            return;

        Detach();
        gameView = window;
        panelRoot = root;
        panelRoot.RegisterCallback<PointerMoveEvent>(OnPointerMove, TrickleDown.TrickleDown);
        panelRoot.RegisterCallback<MouseMoveEvent>(OnMouseMove, TrickleDown.TrickleDown);
    }

    private static bool IsGameContent(Vector2 position) => EditorApplication.isPlaying
        && !EditorApplication.isPaused && EditorWindow.focusedWindow == gameView
        && cursor != null && cursor.IsShowing && contentRect.Contains(position);

    private static void OnPointerMove(PointerMoveEvent evt)
    {
        if (evt.pointerType == UnityEngine.UIElements.PointerType.mouse && IsGameContent(evt.position))
            evt.StopImmediatePropagation();
    }

    private static void OnMouseMove(MouseMoveEvent evt)
    {
        if (IsGameContent(evt.mousePosition))
            evt.StopImmediatePropagation();
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode)
            Detach();
    }

    private static void Detach()
    {
        if (panelRoot != null)
        {
            panelRoot.UnregisterCallback<PointerMoveEvent>(OnPointerMove, TrickleDown.TrickleDown);
            panelRoot.UnregisterCallback<MouseMoveEvent>(OnMouseMove, TrickleDown.TrickleDown);
        }
        panelRoot = null;
        gameView = null;
    }
}
