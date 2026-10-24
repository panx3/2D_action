#if UNITY_EDITOR_WIN
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;

/// <summary>A disposable native Game View; the docked Editor layout is never moved or maximized.</summary>
[InitializeOnLoad]
internal static class FullscreenGameView
{
    private const string MenuPath = "Tools/Tekkyu/Fullscreen Game View (F11)";
    private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly uint ProcessId = (uint)Process.GetCurrentProcess().Id;
    private static EditorWindow fullscreen;
    private static EditorWindow previousFocus;
    private static IntPtr nativeWindow;
    private static bool f11Held;
    private static bool topmost;
    private static VisualElement inputRoot;

    static FullscreenGameView()
    {
        EditorApplication.update += Update;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        AssemblyReloadEvents.beforeAssemblyReload += Cleanup;
        EditorApplication.wantsToQuit += OnWantsToQuit;
        EditorApplication.quitting += Cleanup;
    }

    [MenuItem(MenuPath)]
    private static void Toggle()
    {
        if (fullscreen != null)
        {
            Exit(true);
            return;
        }
        if (!CanEnter())
            return;

        try
        {
            var api = new GameViewApi(); // Resolve required internals before opening anything.
            var owner = Native.GetForegroundWindow();
            if (!IsEditorWindow(owner))
            {
                using (var process = Process.GetCurrentProcess())
                    owner = process.MainWindowHandle;
            }
            var monitor = Native.GetMonitorBounds(owner);
            previousFocus = EditorWindow.focusedWindow;
            var source = previousFocus != null && previousFocus.GetType() == api.Type ? previousFocus : null;
            if (source == null)
            {
                var views = Resources.FindObjectsOfTypeAll(api.Type);
                if (views.Length > 0)
                    source = views[0] as EditorWindow;
            }

            fullscreen = ScriptableObject.CreateInstance(api.Type) as EditorWindow;
            fullscreen.hideFlags = HideFlags.HideAndDontSave;
            var title = "Tekkyu Fullscreen " + Guid.NewGuid().ToString("N");
            fullscreen.titleContent = new GUIContent(title);
            fullscreen.minSize = Vector2.one;
            fullscreen.maxSize = new Vector2(32768, 32768);
            // Start on the owner's display in Editor points. Native placement below uses physical pixels.
            fullscreen.position = previousFocus != null ? previousFocus.position : new Rect(0, 0, 640, 480);
            api.Configure(fullscreen, source);
            // Utility windows close on native Esc before UI Toolkit can intercept it.
            // A normal HostView has no dock/tab strip and leaves Esc to the game.
            api.Show(fullscreen);
            api.ExcludeFromLayout(fullscreen);
            nativeWindow = Native.FindWindow(title, ProcessId);
            if (nativeWindow == IntPtr.Zero)
                throw new InvalidOperationException("Could not identify the temporary Game View window.");
            Native.MakeBorderless(nativeWindow, monitor);
            Native.InstallCursorGuard(nativeWindow);
            inputRoot = fullscreen.rootVisualElement.panel?.visualTree ?? fullscreen.rootVisualElement;
            inputRoot.RegisterCallback<KeyDownEvent>(OnEditorKeyDown, TrickleDown.TrickleDown);
            inputRoot.RegisterCallback<KeyUpEvent>(OnEditorKeyUp, TrickleDown.TrickleDown);
            topmost = true;
            fullscreen.Focus();
            api.Fit(fullscreen);
            // Let Unity process WM_SIZE / per-monitor DPI before fitting the final viewport again.
            EditorApplication.delayCall += FitAfterResize;
        }
        catch (Exception exception)
        {
            Exit(true);
            Debug.LogWarning("Fullscreen Game View could not open; the Editor layout was preserved. "
                + exception.GetBaseException().Message);
        }
    }

    [MenuItem(MenuPath, true)]
    private static bool ValidateToggle() => fullscreen != null || CanEnter();

    private static bool CanEnter() => EditorApplication.isPlaying && !EditorApplication.isCompiling
        && !EditorApplication.isUpdating;

    private static void Update()
    {
        // Poll in the Editor so F11 works with Game View focus, a paused game, and Input System only.
        HandleF11((Native.GetAsyncKeyState(0x7A) & 0x8000) != 0,
            IsEditorWindow(Native.GetForegroundWindow()));
        if (fullscreen == null)
        {
            if (nativeWindow != IntPtr.Zero) // Includes an externally closed utility window.
                Exit(true);
            return;
        }
        if (!CanEnter() || !Native.IsWindow(nativeWindow))
        {
            Exit(true);
            return;
        }

        // Do not cover other applications after Alt+Tab, or cover Unity's own dialogs.
        bool shouldBeTopmost = Native.GetForegroundWindow() == nativeWindow;
        if (topmost != shouldBeTopmost)
        {
            Native.SetTopmost(nativeWindow, shouldBeTopmost);
            topmost = shouldBeTopmost;
        }
        Native.HideCursorInClient(nativeWindow);
    }

    private static void OnEditorKeyDown(KeyDownEvent evt)
    {
        // Input System receives native device events independently. Block only the
        // Editor's IMGUI Escape handler, which would revoke cursor-hide permission.
        if (evt.keyCode == KeyCode.Escape)
            evt.StopImmediatePropagation();
    }

    private static void OnEditorKeyUp(KeyUpEvent evt)
    {
        if (evt.keyCode == KeyCode.Escape)
            evt.StopImmediatePropagation();
    }

    private static void HandleF11(bool pressed, bool editorInForeground)
    {
        bool down = pressed && !f11Held;
        f11Held = pressed;
        if (down && editorInForeground)
            Toggle();
    }

    private static bool IsEditorWindow(IntPtr window)
    {
        Native.GetWindowThreadProcessId(window, out uint processId);
        return window != IntPtr.Zero && processId == ProcessId;
    }

    private static void FitAfterResize()
    {
        if (fullscreen == null)
            return;
        try { new GameViewApi().Fit(fullscreen); }
        catch (Exception exception)
        {
            Exit(true);
            Debug.LogWarning("Fullscreen Game View closed after a viewport API failure. "
                + exception.GetBaseException().Message);
        }
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.EnteredEditMode)
            Exit(true);
    }

    private static bool OnWantsToQuit()
    {
        Cleanup(); // Before Unity saves its layout or asks about unsaved scenes.
        return true;
    }

    private static void Cleanup() => Exit(false);

    private static void Exit(bool restoreFocus)
    {
        EditorApplication.delayCall -= FitAfterResize;
        var window = fullscreen;
        var focus = previousFocus;
        if (inputRoot != null)
        {
            inputRoot.UnregisterCallback<KeyDownEvent>(OnEditorKeyDown, TrickleDown.TrickleDown);
            inputRoot.UnregisterCallback<KeyUpEvent>(OnEditorKeyUp, TrickleDown.TrickleDown);
            inputRoot = null;
        }
        Native.RemoveCursorGuard(nativeWindow);
        fullscreen = null;
        previousFocus = null;
        nativeWindow = IntPtr.Zero;
        topmost = false;
        if (window != null)
            window.Close();
        if (restoreFocus && focus != null)
            focus.Focus();
    }

    /// <summary>Version-sensitive Unity internals are confined here and checked on each entry.</summary>
    private sealed class GameViewApi
    {
        internal readonly Type Type = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView", true);
        private readonly PropertyInfo toolbar, sizeIndex, display, lowResolution, renderSize, viewRect;
        private readonly FieldInfo zoomArea, parent, dontSave;
        private readonly PropertyInfo container;
        private readonly MethodInfo updateZoom, fitScale, setTransform, constrain, showWindow;
        private readonly object normalWindowMode;

        internal GameViewApi()
        {
            toolbar = Property("showToolbar");
            sizeIndex = Property("selectedSizeIndex");
            display = Property("targetDisplay");
            lowResolution = Property("lowResolutionForAspectRatios");
            renderSize = Property("targetRenderSize");
            viewRect = Property("viewInWindow");
            zoomArea = Type.GetField("m_ZoomArea", InstanceMembers)
                ?? throw new MissingFieldException(Type.FullName, "m_ZoomArea");
            updateZoom = Method(Type, "UpdateZoomAreaAndParent", System.Type.EmptyTypes);
            fitScale = Method(Type, "ScaleThatFitsTargetInView", new[] { typeof(Vector2), typeof(Vector2) });
            setTransform = Method(zoomArea.FieldType, "SetTransform", new[] { typeof(Vector2), typeof(Vector2) });
            constrain = Method(Type, "EnforceZoomAreaConstraints", System.Type.EmptyTypes);
            parent = typeof(EditorWindow).GetField("m_Parent", InstanceMembers)
                ?? throw new MissingFieldException("EditorWindow", "m_Parent");
            container = parent.FieldType.GetProperty("window", InstanceMembers)
                ?? throw new MissingMemberException("GUIView", "window");
            dontSave = container.PropertyType.GetField("m_DontSaveToLayout", InstanceMembers)
                ?? throw new MissingFieldException("ContainerWindow", "m_DontSaveToLayout");
            var showModeType = typeof(EditorWindow).Assembly.GetType("UnityEditor.ShowMode", true);
            normalWindowMode = Enum.Parse(showModeType, "NormalWindow");
            showWindow = Method(typeof(EditorWindow), "ShowWithMode", new[] { showModeType });
        }

        private PropertyInfo Property(string name) => Type.GetProperty(name, InstanceMembers)
            ?? throw new MissingMemberException(Type.FullName, name);

        private static MethodInfo Method(Type type, string name, Type[] parameters) =>
            type.GetMethod(name, InstanceMembers, null, parameters, null)
            ?? throw new MissingMethodException(type.FullName, name);

        internal void Configure(EditorWindow window, EditorWindow source)
        {
            if (source != null)
            {
                sizeIndex.SetValue(window, sizeIndex.GetValue(source));
                display.SetValue(window, display.GetValue(source));
            }
            toolbar.SetValue(window, false);
            lowResolution.SetValue(window, false);
        }

        internal void ExcludeFromLayout(EditorWindow window) =>
            dontSave.SetValue(container.GetValue(parent.GetValue(window)), true);

        internal void Show(EditorWindow window) => showWindow.Invoke(window, new[] { normalWindowMode });

        internal void Fit(EditorWindow window)
        {
            updateZoom.Invoke(window, null);
            var size = (Vector2)renderSize.GetValue(window);
            var view = (Rect)viewRect.GetValue(window);
            float scale = (float)fitScale.Invoke(window, new object[] { size, view.size });
            setTransform.Invoke(zoomArea.GetValue(window), new object[] { Vector2.zero, Vector2.one * scale });
            constrain.Invoke(window, null);
            window.Repaint();
        }
    }

    private static class Native
    {
        private const int Style = -16;
        private const uint NoSize = 0x0001, NoMove = 0x0002, NoActivate = 0x0010;
        private const uint FrameChanged = 0x0020, ShowWindow = 0x0040;
        private static readonly IntPtr Topmost = new IntPtr(-1), NotTopmost = new IntPtr(-2);
        private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);
        private delegate IntPtr SubclassProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam,
            UIntPtr id, UIntPtr data);
        private static readonly SubclassProc CursorGuard = OnWindowMessage;
        private static readonly UIntPtr CursorGuardId = new UIntPtr(1);

        [StructLayout(LayoutKind.Sequential)]
        private struct Point { internal int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Rectangle { internal int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            internal int Size;
            internal Rectangle Monitor, Work;
            internal uint Flags;
        }

        internal static Rectangle GetMonitorBounds(IntPtr owner)
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(MonitorFromWindow(owner, 2), ref info))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return info.Monitor; // rcMonitor includes the taskbar; rcWork deliberately does not.
        }

        internal static IntPtr FindWindow(string title, uint processId)
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows((window, parameter) =>
            {
                GetWindowThreadProcessId(window, out uint id);
                if (id != processId)
                    return true;
                var text = new StringBuilder(256);
                GetWindowText(window, text, text.Capacity);
                if (text.ToString() != title)
                    return true;
                found = window;
                return false;
            }, IntPtr.Zero);
            return found;
        }

        internal static void MakeBorderless(IntPtr window, Rectangle monitor)
        {
            // Only the disposable utility's styles are touched. Never hide or modify the shell/taskbar.
            int oldStyle = GetWindowLong(window, Style);
            const int chrome = 0x00C00000 | 0x00040000 | 0x00080000 | 0x00020000 | 0x00010000;
            SetWindowLong(window, Style, (oldStyle & ~chrome) | unchecked((int)0x80000000));
            if (!SetWindowPos(window, Topmost, monitor.Left, monitor.Top,
                monitor.Right - monitor.Left, monitor.Bottom - monitor.Top, FrameChanged | ShowWindow))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        internal static void SetTopmost(IntPtr window, bool enabled) =>
            SetWindowPos(window, enabled ? Topmost : NotTopmost, 0, 0, 0, 0, NoMove | NoSize | NoActivate);

        internal static void InstallCursorGuard(IntPtr window)
        {
            if (!SetWindowSubclass(window, CursorGuard, CursorGuardId, UIntPtr.Zero))
                throw new InvalidOperationException("Could not install the fullscreen cursor guard.");
        }

        internal static void RemoveCursorGuard(IntPtr window)
        {
            if (window != IntPtr.Zero && IsWindow(window))
                RemoveWindowSubclass(window, CursorGuard, CursorGuardId);
        }

        internal static void HideCursorInClient(IntPtr window)
        {
            if (GetForegroundWindow() != window || !GetCursorPos(out var point))
                return;
            var underPointer = WindowFromPoint(point);
            if (underPointer == window || IsChild(window, underPointer))
                SetCursor(IntPtr.Zero);
        }

        private static IntPtr OnWindowMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam,
            UIntPtr id, UIntPtr data)
        {
            const uint SetCursorMessage = 0x0020, NonClientDestroy = 0x0082;
            if (message == SetCursorMessage && window == nativeWindow && GetForegroundWindow() == window)
            {
                SetCursor(IntPtr.Zero);
                return new IntPtr(1);
            }
            if (message == NonClientDestroy)
                RemoveWindowSubclass(window, CursorGuard, id);
            return DefSubclassProc(window, message, wParam, lParam);
        }

        // Window-local cursor handling leaves Unity's Cursor state and the OS ShowCursor counter alone.
        [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowSubclass(IntPtr window, SubclassProc callback, UIntPtr id, UIntPtr data);
        [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RemoveWindowSubclass(IntPtr window, SubclassProc callback, UIntPtr id);
        [DllImport("comctl32.dll")]
        private static extern IntPtr DefSubclassProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern IntPtr SetCursor(IntPtr cursor);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsChild(IntPtr parent, IntPtr child);

        [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr window, StringBuilder text, int count);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
        private static extern int SetWindowLong(IntPtr window, int index, int value);
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    }
}
#endif
