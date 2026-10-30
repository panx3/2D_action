using System;
using UnityEngine;
using UnityEditor;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

[InitializeOnLoad]
public static class FallingPressPlayerChecks
{
    private const string Key = "FallingPressPlayerChecks";
    private static readonly string Preview = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "IronBallGirlPressChecks");
    private static FallingPressController press;
    private static PressBallStandController stand;
    private static Player player;
    private static MorningStarLauncher launcher;
    private static PlayerHealth hp;
    private static Rigidbody2D rb, ball;
    private static CapsuleCollider2D collider;
    private static Gamepad pad;
    private static Vector2 originalSize, originalOffset;
    private static Vector3 originalScale;
    private static Material originalMaterial;
    private static float start, minFeet;
    private static bool background, squashSeen, previewSaved;
    private static int phase, errors, hpBefore;
    private static double deadline;
    static FallingPressPlayerChecks() { EditorApplication.playModeStateChanged += State; }
    public static string Result() => SessionState.GetString(Key + ".Result", "Not run");
    public static void RunBaseline() => Run(true);
    [MenuItem("Tools/Falling Press/Run Player And Stone Checks")]
    public static void RunChecks() => Run();
    public static void Run(bool baseline = false)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Stop Play Mode first");
        SessionState.SetBool(Key, true); SessionState.SetBool(Key + ".Baseline", baseline);
        SessionState.SetString(Key + ".Result", "Running"); EditorApplication.isPlaying = true;
    }
    private static void State(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            background = Application.runInBackground; Application.runInBackground = true;
            start = Time.time; minFeet = 100; phase = errors = 0; squashSeen = previewSaved = false;
            deadline = EditorApplication.timeSinceStartup + 100;
            EditorApplication.update += Tick; Application.logMessageReceived += Log;
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
            SessionState.SetBool(Key, false); Debug.Log("[PressPlayerChecks] " + Result());
        }
    }
    private static void Log(string message, string stack, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors++; }
    private static void Tick()
    {
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Timeout phase=" + phase);
            float elapsed = Time.time - start;
            if (phase == 0)
            {
                if (elapsed < 6) return;
                Setup(); Next(); return;
            }
            if (SessionState.GetBool(Key + ".Baseline", false))
            {
                minFeet = Mathf.Min(minFeet, collider.bounds.min.y);
                if (elapsed < 3) return;
                Finish("BASELINE minFeet=" + minFeet + " hp=" + hp.CurrentHp + " errors=" + errors); return;
            }
            switch (phase)
            {
                case 1:
                    if (elapsed < 0.55f) return;
                    Require(rb.position.x < 997.75f, "Player crossed solid stand side: " + rb.position);
                    Require(stand.StoneColor.r > stand.StoneColor.b, "unoccupied stones are not red");
                    Capture("Stand_Empty.png", new Vector2(1000, 0.95f), 1.35f);
                    InputSystem.QueueStateEvent(pad, new GamepadState());
                    Move(rb, new Vector2(1000, 4.1f)); Next(); break;
                case 2:
                    if (elapsed < 0.5f) return;
                    Require(Mathf.Abs(collider.bounds.min.y - 1.9f) < 0.08f && player.IsGrounded,
                        "Player did not land/ground on stand: feet=" + collider.bounds.min.y + " grounded=" + player.IsGrounded);
                    Move(rb, new Vector2(1004.2f, 2.06f)); launcher.ResetForRespawn(); Move(ball, new Vector2(1000, 2.76f));
                    Next(); break;
                case 3:
                    if (!stand.IsOccupied) { Require(elapsed < 2, "ball not supported by stand"); return; }
                    Require(stand.StoneColor.b > stand.StoneColor.r, "occupied stones are not cyan");
                    Capture("Stand_Occupied.png", new Vector2(1000, 0.95f), 1.35f);
                    Move(rb, new Vector2(997, 2.06f)); launcher.ResetForRespawn(); Move(ball, new Vector2(995, 0.86f));
                    Next(); break;
                case 4:
                    if (elapsed < 0.2f) return;
                    Require(!stand.IsOccupied && stand.StoneColor.r > stand.StoneColor.b, "stones did not return to red");
                    stand.gameObject.SetActive(false); Move(rb, new Vector2(1000, 2.06f));
                    launcher.ResetForRespawn(); Move(ball, new Vector2(995, 0.86f)); hp.ResetToFullHp(); hpBefore = hp.CurrentHp;
                    originalMaterial = player.BodySprite.sharedMaterial; press.enabled = true; Next(); break;
                case 5:
                    minFeet = Mathf.Min(minFeet, collider.bounds.min.y);
                    Require(minFeet > -0.08f, "Player embedded in floor: " + minFeet);
                    var squash = player.GetComponent<PressPlayerSquash>();
                    if (squash != null && squash.VerticalScale < 0.7f)
                    {
                        squashSeen = true;
                        Require(squash.HorizontalScale > 1f && squash.HorizontalScale <= 1.19f, "incorrect horizontal stretch");
                        Require(player.BodySprite.sharedMaterial.shader.name == "IronBallGirl/Press Player Squash", "squash shader not active");
                        if (!previewSaved) { Capture("Player_PressSquash.png", new Vector2(1000, 1.8f), 2.7f); previewSaved = true; }
                    }
                    Require(player.transform.localScale == originalScale && collider.size == originalSize && collider.offset == originalOffset,
                        "Player root/collider changed");
                    if (elapsed < 3) return;
                    Require(squashSeen && hp.CurrentHp < hpBefore, "no visible compression or damage");
                    Move(rb, new Vector2(1006, 2.06f)); launcher.ResetForRespawn(); Move(ball, new Vector2(1005, 0.86f)); Next(); break;
                case 6:
                    if (elapsed < 0.5f) return;
                    var restored = player.GetComponent<PressPlayerSquash>();
                    Require(restored != null && restored.VerticalScale == 1f && restored.HorizontalScale == 1f, "squash did not recover");
                    Require(player.BodySprite.sharedMaterial == originalMaterial && player.BodySprite.sortingOrder == 2, "material/order not restored");
                    Require(errors == 0, "runtime errors=" + errors);
                    Finish("PASS: stand side collision + top grounding; red/cyan/red stones; squash and width stretch; feet min=" + minFeet +
                        "; Player root/collider unchanged; damage + visual recovery; runtime errors=0"); break;
            }
        }
        catch (Exception e) { Finish("FAIL phase=" + phase + ": " + e.Message); }
    }
    private static void Setup()
    {
        launcher = UnityEngine.Object.FindAnyObjectByType<MorningStarLauncher>(); player = launcher.GetComponent<Player>();
        hp = player.GetComponent<PlayerHealth>(); rb = player.GetComponent<Rigidbody2D>(); ball = launcher.BallBody;
        collider = player.GetComponent<CapsuleCollider2D>(); originalScale = player.transform.localScale;
        originalSize = collider.size; originalOffset = collider.offset;
        foreach (var p in UnityEngine.Object.FindObjectsByType<FallingPressController>(FindObjectsSortMode.None)) p.gameObject.SetActive(false);
        var floor = new GameObject("__PressPlayerFloor"); floor.transform.position = new Vector2(1000, -0.5f);
        floor.AddComponent<BoxCollider2D>().size = new Vector2(30, 1);
        var visual = new GameObject("FloorVisual").AddComponent<SpriteRenderer>(); visual.transform.SetParent(floor.transform, false);
        visual.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(FallingPressSetup.Art + "LightPixel.png");
        visual.transform.localPosition = new Vector2(0, 0.45f); visual.transform.localScale = new Vector3(30, 0.1f, 1); visual.color = new Color(0.2f, 0.24f, 0.3f);
        press = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(FallingPressSetup.PressPath), new Vector3(1000, 0), Quaternion.identity).GetComponent<FallingPressController>();
        stand = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(FallingPressSetup.StandPath), new Vector3(1000, 0), Quaternion.identity).GetComponent<PressBallStandController>();
        Set(press, "topWait", 0.1f); Set(press, "warningTime", 0.1f); Set(press, "bottomWait", 1); Set(press, "ascentSpeed", 6);
        var so = new SerializedObject(press); so.FindProperty("knockback").vector2Value = Vector2.zero; so.ApplyModifiedPropertiesWithoutUndo();
        bool baseline = SessionState.GetBool(Key + ".Baseline", false); stand.gameObject.SetActive(!baseline); press.enabled = baseline;
        Move(rb, new Vector2(baseline ? 1000 : 997.3f, 2.06f)); launcher.ResetForRespawn(); Move(ball, new Vector2(995, 0.86f)); hp.ResetToFullHp();
        if (!baseline) { pad = InputSystem.AddDevice<Gamepad>(); InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = Vector2.right }); }
        Physics2D.SyncTransforms();
    }
    private static void Capture(string name, Vector2 position, float size)
    {
        // 表示検証のみのCamera。Player/追従Cameraや保存Sceneを変更しない。
        var go = new GameObject("__PressPreviewCamera"); var camera = go.AddComponent<Camera>();
        camera.orthographic = true; camera.orthographicSize = size; camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.055f, 0.065f, 0.08f); go.transform.position = new Vector3(position.x, position.y, -10);
        var rt = new RenderTexture(720, 432, 24); var old = RenderTexture.active;
        try
        {
            camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
            var image = new Texture2D(720, 432, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0, 0, 720, 432), 0, 0); image.Apply();
            System.IO.Directory.CreateDirectory(Preview);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(Preview, name), image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
        }
        finally { RenderTexture.active = old; camera.targetTexture = null; UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(go); }
    }
    private static void Move(Rigidbody2D body, Vector2 position)
    { body.position = position; body.transform.position = new Vector3(position.x, position.y, body.transform.position.z); body.linearVelocity = Vector2.zero; Physics2D.SyncTransforms(); }
    private static void Set(UnityEngine.Object obj, string name, float value)
    { var so = new SerializedObject(obj); so.FindProperty(name).floatValue = value; so.ApplyModifiedPropertiesWithoutUndo(); }
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Next() { phase++; start = Time.time; }
    private static void Finish(string result)
    {
        EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
        if (pad != null && pad.added) InputSystem.RemoveDevice(pad);
        Application.runInBackground = background; SessionState.SetString(Key + ".Result", result); EditorApplication.isPlaying = false;
    }
}
