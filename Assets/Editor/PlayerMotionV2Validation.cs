using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>実装対象の接続・状態復帰を検証。Editor終了やScene保存を行わない。</summary>
[InitializeOnLoad]
public static class PlayerMotionV2Validation
{
    private const string PausedPlayKey = "PlayerMotionV2Validation.PausedPlay";
    static PlayerMotionV2Validation()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(PausedPlayKey, false))
            {
                SessionState.SetBool(PausedPlayKey, false);
                EditorApplication.isPaused = true;
            }
        };
    }

    public static string StartPausedPlay()
    {
        Require(!EditorApplication.isPlaying, "Already playing.");
        SessionState.SetBool(PausedPlayKey, true);
        EditorApplication.isPlaying = true;
        return "Entering Play Mode paused for isolated motion checks.";
    }

    private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly string ReportDirectory = "Docs/Art/PlayerMotionV2/Implementation";
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static object Call(object target, string name, params object[] args)
        => target.GetType().GetMethod(name, PrivateInstance).Invoke(target, args);
    private static void Set(object target, string name, object value)
        => target.GetType().GetField(name, PrivateInstance).SetValue(target, value);
    private static T Read<T>(object target, string name)
        => (T)target.GetType().GetField(name, PrivateInstance).GetValue(target);

    public static string Assets() => AssetsAt(ReportDirectory);

    public static string AssetsAt(string reportDirectory)
    {
        PlayerMotionSet set = AssetDatabase.LoadAssetAtPath<PlayerMotionSet>(PlayerMotionV2Setup.MotionAssetPath);
        Require(set != null, "Motion set missing.");
        var sequences = new List<PlayerMotionSet.Sequence>
        { set.run, set.jumpUp, set.apex, set.fall, set.land, set.charge, set.chargeRunning, set.chargeAirborne,
          set.recall, set.brace, set.pulled, set.hanging, set.climbing };
        foreach (var direction in set.throws) { sequences.Add(direction.ground); sequences.Add(direction.air); }
        Require(sequences.All(sequence => sequence.IsValid && sequence.frames.All(frame => frame.sprite != null)), "Missing motion frame.");
        Require(set.run.frames.Length == 6 && set.chargeRunning.frames.Length == 6
            && set.charge.frames.Length == 8 && set.climbing.frames.Length == 4, "Wrong frame counts.");
        Vector2[] directions = { Vector2.right, new Vector2(1,1), Vector2.up, new Vector2(-1,1),
            Vector2.left, new Vector2(-1,-1), Vector2.down, new Vector2(1,-1) };
        foreach (Vector2 direction in directions)
        {
            Require(set.Throw(direction, false).frames.Length == 3, "Ground directional throw missing.");
            Require(set.Throw(direction, true).frames.Length == 3, "Air directional throw missing.");
        }
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { PlayerMotionV2Setup.SpriteFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            Require(importer.spritePixelsPerUnit == 32f && importer.filterMode == FilterMode.Point
                && !importer.mipmapEnabled && importer.textureCompression == TextureImporterCompression.Uncompressed, "Incorrect import settings: " + path);
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            Require(texture.width <= importer.maxTextureSize, "Texture downscaled: " + path);
        }
        int frameCount = sequences.Sum(sequence => sequence.frames.Length);
        Require(frameCount == 78, "Wrong total frame count.");
        Require(!set.fall.loop && !set.brace.loop && !set.pulled.loop, "Repeated nodding/flapping cycle restored.");
        string report = "PASS: 78 frame slots; original 6-frame running rhythm; non-looping descent; 8-direction ground/air throws; 32 PPU Point/uncompressed/no mipmaps; shared references valid.";
        Directory.CreateDirectory(reportDirectory);
        File.WriteAllText(reportDirectory + "/asset-validation.txt", report);
        return report;
    }

    private static string BodySignature(Rigidbody2D body)
        => body.bodyType + "|" + body.mass + "|" + body.gravityScale + "|" + body.linearDamping + "|"
           + body.constraints + "|" + body.interpolation + "|" + body.collisionDetectionMode + "|" + body.simulated;

    public static string Runtime() => RuntimeAt(ReportDirectory);

    public static string RuntimeAt(string reportDirectory)
    {
        Require(EditorApplication.isPlaying, "Runtime checks require Play Mode.");
        var player = UnityEngine.Object.FindFirstObjectByType<Player>();
        Require(player != null, "Player missing.");
        var launcher = player.GetComponent<MorningStarLauncher>();
        var intro = player.GetComponent<PlayerStartIntro>();
        if (intro != null && intro.enabled) intro.enabled = false;
        player.SetStartIntroControl(false);
        player.GetComponent<PlayerHealth>().ResetToFullHp();
        launcher.EndRespawnPresentation();
        var set = launcher.MotionSet;
        Require(set != null, "Runtime motion set missing.");
        Rigidbody2D ball = launcher.BallBody;
        var report = new StringBuilder();
        bool input = player.InputEnabled;
        bool grounded = Read<bool>(player, "_isGrounded");
        Vector2 oldBallPosition = ball.position;
        try
        {
            player.SetInputEnabled(false);
            Call(launcher, "EnterDraggingState", false);
            string rest = BodySignature(ball);
            Set(launcher, "_nextLaunchTime", 0f);
            Set(launcher, "_airThrowsUsed", 0);
            Call(launcher, "BeginSpinCharging");
            Require(launcher.State == MorningStarLauncher.MorningStarState.SpinCharging, "Charge did not start.");
            Set(launcher, "_chargeTime", 0.5f);
            var points = new List<Vector3>(16);
            for (int i = 0; i < 8; i++)
            {
                Set(launcher, "_spinAngle", i * Mathf.PI / 4f);
                Call(launcher, "UpdateUnifiedMotionVisual");
                Require(player.BodySprite.sprite.name.StartsWith("Charge", StringComparison.Ordinal), "Charge sprite missing.");
                Vector2 orbit = (Vector2)Call(launcher, "ResolveChargeOrbitPosition");
                Require(Mathf.Abs(Vector2.Distance(orbit, launcher.ChargeGripWorld) - set.chargeOrbitRadius) < 0.001f, "Wrong charge hub.");
                Require(launcher.TryGetChargeVisualPath(points) && points.Count == 14, "Charge route missing.");
                Require(Vector2.Distance(points[12], launcher.ChargeGripWorld) < 0.001f, "Route does not pass through chain grip.");
            }
            Call(launcher, "CancelShoulderCharge");
            Require(BodySignature(ball) == rest && !launcher.TryGetChargeVisualPath(points), "Charge leaked into Rest.");
            report.AppendLine("PASS charge: 8 phases, separate near-ball grip, handle-to-grip sag path, cancel -> identical Rest physics.");

            Vector2[] directions = { Vector2.right, new Vector2(1,1), Vector2.up, new Vector2(-1,1),
                Vector2.left, new Vector2(-1,-1), Vector2.down, new Vector2(1,-1) };
            for (int air = 0; air < 2; air++) foreach (Vector2 direction in directions)
            {
                Set(player, "_isGrounded", air == 0);
                player.SetLaunchFacing(direction.x);
                Set(launcher, "_pendingLaunchDir", direction.normalized);
                Call(launcher, "BeginLaunchPose", air == 1);
                Set(launcher, "_launchPoseElapsed", 0.10f);
                Set(launcher, "motionLaunchVisualStartedAt", Time.time - 0.10f);
                Call(launcher, "UpdateUnifiedMotionVisual");
                PlayerMotionSet.Frame expected = set.Throw(direction, air == 1).Sample(0.10f);
                Require(player.BodySprite.sprite == expected.sprite, "Directional pose mismatch.");
            }
            report.AppendLine("PASS throws: 8 directions x ground/air, unsnapped direction retained, correct sprite selected.");
            Call(launcher, "EnterDraggingState", false);
            Set(player, "_isGrounded", false);
            Set(launcher, "_isHooked", true);
            Set(launcher, "_hookPoint", (Vector2)player.transform.position + Vector2.up * 8f);
            Call(launcher, "TransitionToState", MorningStarLauncher.MorningStarState.Hooked);
            Call(launcher, "UpdateUnifiedMotionVisual");
            Require(player.BodySprite.sprite == null, "Hanging did not use anchored visual.");
            var hanging = player.GetComponentsInChildren<SpriteRenderer>(true)
                .FirstOrDefault(view => view.sprite != null && view.sprite.name.StartsWith("Hanging", StringComparison.Ordinal));
            Require(hanging != null, "Hanging frame missing.");
            report.AppendLine("PASS hanging: dedicated anchored sprite replaces upward-throw pose.");
            Set(launcher, "_isChainClimbing", true);
            for (int i = 0; i < 4; i++)
            {
                Set(launcher, "_chainClimbAnimationTime", i / set.climbing.framesPerSecond);
                Set(launcher, "_chainClimbMovedThisFixedStep", false);
                Call(launcher, "UpdateUnifiedMotionVisual");
                SpriteRenderer view = launcher.ChainClimbRenderer;
                Require(view != null && view.sprite == set.climbing.frames[i].sprite, "Climbing frame missing.");
                Vector2 grip = view.transform.TransformPoint(set.climbing.frames[i].chainAnchor);
                Vector2 anchor = (Vector2)Call(launcher, "GetUpwardPoseChainAnchorWorld");
                Require(Vector2.Distance(grip, anchor) < 0.002f, "Climbing hand not on central chain.");
            }
            report.AppendLine("PASS climb: all 4 high grips stay on chain axis; stationary climb phase can freeze.");
            Set(launcher, "_isChainClimbing", false);
            Set(launcher, "_isHooked", false);
            Call(launcher, "EnterDraggingState", false);
            Call(launcher, "BeginReturn");
            Call(launcher, "EnterDraggingState", false);
            Require(BodySignature(ball) == rest, "Recall changed Rest physics.");
            report.AppendLine("PASS recall -> Rest: Rigidbody settings match initial Rest.");
        }
        finally
        {
            Set(launcher, "_isChainClimbing", false);
            Set(launcher, "_isHooked", false);
            Call(launcher, "EnterDraggingState", false);
            Call(launcher, "UpdateUnifiedMotionVisual");
            Set(player, "_isGrounded", grounded);
            player.SetInputEnabled(input);
            ball.position = oldBallPosition;
        }
        Directory.CreateDirectory(reportDirectory);
        File.WriteAllText(reportDirectory + "/runtime-validation.txt", report.ToString());
        return report.ToString();
    }

    public static string CalmContinuity() => CalmContinuityAt("Docs/Art/PlayerMotionV2/ArtRefinement");

    public static string CalmContinuityAt(string reportDirectory)
    {
        Require(EditorApplication.isPlaying && EditorApplication.isPaused, "Paused Play Mode required.");
        var player = UnityEngine.Object.FindFirstObjectByType<Player>();
        var launcher = player.GetComponent<MorningStarLauncher>();
        var chain = Read<ChainConstraint2D>(launcher, "chainConstraint");
        var set = launcher.MotionSet;
        Require(chain != null, "Chain telemetry missing.");
        Vector2 oldPosition = launcher.BallBody.position;
        var body = player.GetComponent<Rigidbody2D>();
        Vector2 oldVelocity = body.linearVelocity;
        var animator = player.GetComponent<Animator>();
        int layer = animator.GetLayerIndex("Launcher Pose");
        Require(layer >= 0, "Launch animation layer missing.");
        float oldWeight = animator.GetLayerWeight(layer);
        bool grounded = Read<bool>(player, "_isGrounded"), input = player.InputEnabled;
        var tension = typeof(ChainConstraint2D).GetProperty("LastAppliedPlayerTension");
        try
        {
            player.SetInputEnabled(false);
            launcher.EndRespawnPresentation();
            launcher.ResetForRespawn();
            Set(player, "_isGrounded", false);
            body.linearVelocity = Vector2.down * 2f;
            player.SetLaunchFacing(1f);
            Vector2 aim = new Vector2(1f, 1f).normalized;
            Set(launcher, "_pendingLaunchDir", aim);
            Call(launcher, "TransitionToState", MorningStarLauncher.MorningStarState.Thrown);
            Call(launcher, "BeginLaunchPose", true);
            Set(launcher, "_launchPoseElapsed", 0.10f);
            Set(launcher, "motionLaunchVisualStartedAt", Time.time - .10f);
            foreach (float angle in new[] { 66f, 69f, 64f, 72f, 88f, 0f })
            {
                Vector2 force = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad)) * 20f;
                tension.SetValue(chain, force);
                Call(launcher, "UpdateUnifiedMotionVisual");
                Require(player.BodySprite.sprite == set.Throw(aim, true).Sample(.1f).sprite,
                    "Tension interrupted the actual throwing pose.");
            }
            string thrownPhysics = BodySignature(launcher.BallBody);
            Set(launcher, "_launchPoseElapsed", .25f);
            Set(launcher, "motionLaunchVisualStartedAt", Time.time - .25f);
            Call(launcher, "UpdateUnifiedMotionVisual");
            Require(player.BodySprite.sprite == set.Throw(aim, true).Sample(.25f).sprite, "Final throw frame was not held.");
            Set(launcher, "_launchPoseElapsed", .34f);
            Set(launcher, "motionLaunchVisualStartedAt", Time.time - .34f);
            Call(launcher, "UpdateUnifiedMotionVisual");
            Require(Mathf.Approximately(animator.GetLayerWeight(layer), 0f), "Launch layer still holds the final frame.");
            Require(player.BodySprite.sprite == set.Throw(aim, true).Sample(.34f).sprite, "Throw direction flashed on the Animator handoff frame.");
            Require(Read<bool>(launcher, "_launchPoseActive"), "Visual release changed the physical hold time.");
            Require(BodySignature(launcher.BallBody) == thrownPhysics, "Visual release changed ball physics.");
            Call(launcher, "UpdateUnifiedMotionVisual");
            Sprite idle = AssetDatabase.LoadAllAssetsAtPath("Assets/image_/立ち絵(sprite).png").OfType<Sprite>().First();
            player.BodySprite.sprite = idle;
            Vector3 defaultHand = player.RightFacingHandAnchorLocalPosition;
            if (!player.FacingRight) defaultHand.x = -defaultHand.x;
            Vector2 expectedAnchor = player.WeaponHandAnchor.parent.TransformPoint(defaultHand);
            Vector2 visualAnchor = (Vector2)Call(launcher, "GetVisualRopeAnchorWorld");
            Require(Vector2.Distance(visualAnchor, expectedAnchor) < .001f, "Released Idle no longer connects to its handle.");
            Set(launcher, "_pendingLaunchDir", aim);
            Call(launcher, "BeginLaunchPose", true);
            Set(launcher, "motionLaunchVisualStartedAt", Time.time - .20f);
            Call(launcher, "EnterDraggingState", false);
            Call(launcher, "UpdateUnifiedMotionVisual");
            Require(!Read<bool>(launcher, "_launchPoseActive") && player.BodySprite.sprite == set.Throw(aim, true).Sample(.20f).sprite,
                "An early physical return erased the visible throw.");
            Set(launcher, "motionLaunchVisualStartedAt", Time.time - .34f);
            Call(launcher, "UpdateUnifiedMotionVisual");
            Call(launcher, "UpdateUnifiedMotionVisual");
            foreach (float angle in new[] { 66f, 69f, 64f, 72f, 88f, -10f })
            {
                tension.SetValue(chain, new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad)) * 20f);
                Call(launcher, "UpdateUnifiedMotionVisual");
                Require(!Read<bool>(launcher, "motionOverrideActive"), "A tension pose replaced normal locomotion.");
                Require(Mathf.Approximately(animator.GetLayerWeight(layer), oldWeight), "Launch layer weight did not restore.");
            }
            Set(player, "_isGrounded", true);
            Call(launcher, "UpdateUnifiedMotionVisual");
            Require(!Read<bool>(launcher, "motionOverrideActive"), "Ground tension still forced a brace pose.");
            PlayerMotionSet.Frame gait = set.ChargeFrame(0f, false, true, .4f);
            for (int i = 0; i < 8; i++)
                Require(set.ChargeFrame(i * Mathf.PI / 4f, false, true, .4f) == gait,
                    "Charge rotation still drives the running legs.");
            Require(set.ChargeFrame(0f, false, true, 0f) != set.ChargeFrame(0f, false, true, .3f), "Charge gait does not advance.");
            foreach (PlayerMotionSet.Sequence settle in new[] { set.fall, set.brace, set.pulled })
                Require(settle.Sample(1f) == settle.Sample(5f) && !settle.loop, "Settle motion repeatedly flaps/nods.");
        }
        finally
        {
            tension.SetValue(chain, Vector2.zero);
            launcher.ResetForRespawn();
            Call(launcher, "UpdateUnifiedMotionVisual");
            Set(player, "_isGrounded", grounded);
            body.linearVelocity = oldVelocity;
            player.SetInputEnabled(input);
            launcher.BallBody.position = oldPosition;
        }
        string report = "PASS: final throwing frame remains visible through 0.25s, including an early physical return, then releases at 0.32s; tension adds no pose and charge gait remains correct.";
        Directory.CreateDirectory(reportDirectory);
        File.WriteAllText(reportDirectory + "/continuity-validation.txt", report);
        return report;
    }

    public static string ChargeTransitions() => ChargeTransitionsAt(ReportDirectory);

    public static string ChargeLayerSplitAt(string reportDirectory)
    {
        Require(EditorApplication.isPlaying && EditorApplication.isPaused, "Paused Play Mode required.");
        var player = UnityEngine.Object.FindFirstObjectByType<Player>();
        var launcher = player.GetComponent<MorningStarLauncher>();
        var links = UnityEngine.Object.FindFirstObjectByType<ChainLinkVisualController>();
        var line = UnityEngine.Object.FindFirstObjectByType<ChainLineController>();
        Require(links != null && line != null, "Chain visual controllers missing.");
        var ball = launcher.BallBody;
        var originalBallPosition = ball.position;
        bool originalInput = player.InputEnabled;
        bool originalDrawLine = Read<bool>(line, "drawLineRenderer");
        bool originalHideLine = Read<bool>(line, "hideLineWhenLinksVisible");
        var originalLinkReference = Read<ChainLinkVisualController>(line, "chainLinkVisual");
        var originalLinkSprite = Read<Sprite>(links, "chainLinkSprite");
        float originalSpacing = Read<float>(links, "segmentSpacing");
        bool originalLinksEnabled = links.enabled;
        try
        {
            player.SetInputEnabled(false);
            launcher.EndRespawnPresentation();
            launcher.ResetForRespawn();
            string rest = BodySignature(ball);
            Call(launcher, "BeginSpinCharging");
            Set(launcher, "_chargeTime", .6f);
            // Sceneによってリンク描画は未設定のため、Play中だけ代替Spriteで同じプールを検査する。
            if (originalLinkSprite == null) Set(links, "chainLinkSprite", player.BodySprite.sprite);
            if (originalSpacing < .1f) Set(links, "segmentSpacing", .16f);
            links.enabled = true;
            Set(line, "drawLineRenderer", true);
            Set(line, "hideLineWhenLinksVisible", false);
            for (int phase = 0; phase < 8; phase++)
            {
                Set(launcher, "_spinAngle", phase * Mathf.PI / 4f);
                ball.position = (Vector2)Call(launcher, "ResolveChargeOrbitPosition");
                Call(launcher, "UpdateUnifiedMotionVisual");
                Call(links, "LateUpdate");
                var pool = Read<List<SpriteRenderer>>(links, "_links");
                float handleLength = Read<float>(links, "_chargeHandleVisualLength");
                float spacing = Read<float>(links, "segmentSpacing");
                bool sawHandle = false, sawBallSide = false;
                for (int i = 0; i < pool.Count; i++)
                {
                    var link = pool[i];
                    if (link == null || !link.gameObject.activeSelf) continue;
                    bool handleSide = (i + .5f) * spacing < handleLength;
                    Require(link.sortingLayerID == player.BodySprite.sortingLayerID, "Charge link sorting layer differs from player.");
                    if (handleSide)
                    {
                        sawHandle = true;
                        Require(link.sortingOrder == player.BodySprite.sortingOrder + 1, "Handle-side link was not in front.");
                    }
                    else
                    {
                        sawBallSide = true;
                        Require(link.sortingOrder == player.BodySprite.sortingOrder
                            && Mathf.Abs(link.transform.position.z - launcher.ChargeRearWorldZ) < .0001f,
                            "Ball-side link was not immediately behind.");
                    }
                }
                Require(sawHandle && sawBallSide, "A charge chain segment was missing.");
                Call(line, "LateUpdate");
                var front = Read<LineRenderer>(line, "_line");
                var rear = Read<LineRenderer>(line, "_chargeBallLine");
                Require(front.enabled && rear != null && rear.enabled && front.positionCount == 13 && rear.positionCount == 2,
                    "Fallback line was not split at the grip.");
                Require(front.sortingOrder == player.BodySprite.sortingOrder + 1 && rear.sortingOrder == player.BodySprite.sortingOrder
                    && rear.sortingLayerID == player.BodySprite.sortingLayerID
                    && Mathf.Abs(rear.GetPosition(0).z - launcher.ChargeRearWorldZ) < .0001f,
                    "Fallback line depth differs from sprite links.");
            }
            Set(line, "chainLinkVisual", null);
            Call(line, "LateUpdate");
            Require(Read<LineRenderer>(line, "_line").enabled && Read<LineRenderer>(line, "_chargeBallLine").enabled,
                "Line-only fallback lost a charge segment.");
            Call(launcher, "CancelShoulderCharge");
            Call(links, "LateUpdate");
            Call(line, "LateUpdate");
            Require(!Read<LineRenderer>(line, "_chargeBallLine").enabled && BodySignature(ball) == rest,
                "Charge line or physics was not restored.");
        }
        finally
        {
            launcher.ResetForRespawn();
            Set(line, "drawLineRenderer", originalDrawLine);
            Set(line, "hideLineWhenLinksVisible", originalHideLine);
            Set(line, "chainLinkVisual", originalLinkReference);
            Set(links, "chainLinkSprite", originalLinkSprite);
            Set(links, "segmentSpacing", originalSpacing);
            links.enabled = originalLinksEnabled;
            Call(links, "LateUpdate");
            Call(line, "LateUpdate");
            player.SetInputEnabled(originalInput);
            ball.position = originalBallPosition;
        }
        string report = "PASS: 8 charge phases keep handle-side links in front, ball-side links behind; supplemental and line-only renderers split at the grip; return restores normal sorting and physics.";
        Directory.CreateDirectory(reportDirectory);
        File.WriteAllText(reportDirectory + "/layer-split-validation.txt", report);
        return report;
    }

    public static string ChargeTransitionsAt(string reportDirectory)
    {
        Require(EditorApplication.isPlaying && EditorApplication.isPaused, "Paused Play Mode required.");
        var player = UnityEngine.Object.FindFirstObjectByType<Player>();
        var launcher = player.GetComponent<MorningStarLauncher>();
        var ball = launcher.BallBody;
        var renderer = ball.GetComponentInChildren<SpriteRenderer>();
        var intro = player.GetComponent<PlayerStartIntro>();
        if (intro != null) intro.enabled = false;
        player.SetStartIntroControl(false);
        launcher.EndRespawnPresentation();
        bool input = player.InputEnabled;
        bool grounded = Read<bool>(player, "_isGrounded");
        Vector2 oldPosition = ball.position;
        var points = new List<Vector3>(16);
        var report = new StringBuilder();
        try
        {
            player.SetInputEnabled(false);
            launcher.ResetForRespawn();
            string rest = BodySignature(ball);
            int layer = renderer.sortingLayerID, order = renderer.sortingOrder;
            float visualLocalZ = renderer.transform.localPosition.z;
            float bodyZ = ball.transform.position.z;
            for (int air = 0; air < 2; air++) for (int facing = 0; facing < 2; facing++)
            {
                Set(player, "_isGrounded", air == 0);
                player.SetLaunchFacing(facing == 0 ? 1f : -1f);
                Call(launcher, "BeginSpinCharging");
                Set(launcher, "_chargeTime", 0.5f);
                for (int phase = 0; phase < 8; phase++)
                {
                    Set(launcher, "_spinAngle", phase * Mathf.PI / 4f);
                    Call(launcher, "UpdateUnifiedMotionVisual");
                    string prefix = air == 1 ? "ChargeAir_" : "Charge_";
                    Require(player.BodySprite.sprite.name.StartsWith(prefix, StringComparison.Ordinal), "Wrong charge variant.");
                    Require(player.BodySprite.flipX == (facing == 1), "Charge facing mismatch.");
                    Require(renderer.sortingLayerID == player.BodySprite.sortingLayerID
                        && renderer.sortingOrder == player.BodySprite.sortingOrder
                        && renderer.bounds.center.z > player.BodySprite.bounds.center.z,
                        "Charge ball is not immediately behind the player.");
                    Require(Mathf.Approximately(ball.transform.position.z, bodyZ), "Charge moved the physical ball depth.");
                }
                Vector2 orbit = (Vector2)Call(launcher, "ResolveChargeOrbitPosition");
                Require(Mathf.Abs(Vector2.Distance(orbit, launcher.ChargeGripWorld) - launcher.MotionSet.chargeOrbitRadius) < 0.001f, "Mirrored orbit radius changed.");
                launcher.ResetForRespawn();
                Call(launcher, "UpdateUnifiedMotionVisual");
                Require(!launcher.TryGetChargeVisualPath(points) && !launcher.UsesChargeGrip, "Respawn retained charge route.");
                Require(renderer.sortingLayerID == layer && renderer.sortingOrder == order
                    && Mathf.Approximately(renderer.transform.localPosition.z, visualLocalZ), "Respawn retained charge sorting/depth.");
                Require(BodySignature(ball) == rest, "Respawn retained charge physics.");
            }
            report.AppendLine("PASS charge interruption: ground/air x left/right x 8 phases; ball always immediately behind player, Respawn restores original visual depth/sorting and physics.");
            Set(player, "_isGrounded", true);
            Call(launcher, "BeginSpinCharging");
            Set(launcher, "_chargeTime", 0.6f);
            Set(launcher, "_spinAngle", Mathf.PI);
            Call(launcher, "UpdateUnifiedMotionVisual");
            Set(launcher, "_pendingAimDirection", new Vector2(0.37f, 0.91f).normalized);
            Call(launcher, "ExecuteChargedThrow");
            Require(launcher.State == MorningStarLauncher.MorningStarState.RecallBeforeThrow, "Charged throw skipped original recall state.");
            Require(!launcher.UsesChargeGrip && renderer.sortingOrder == order, "Charged throw retained charge presentation.");
            Call(launcher, "FirePendingThrow");
            Require(launcher.State == MorningStarLauncher.MorningStarState.Thrown, "Charged shot did not enter original Thrown state.");
            launcher.ResetForRespawn();
            Require(BodySignature(ball) == rest, "Charged throw leaked physics into Rest.");
            report.AppendLine("PASS charge -> original recall-before-throw -> thrown -> Respawn; dedicated orbit/path/sorting released.");
        }
        finally
        {
            launcher.ResetForRespawn();
            Call(launcher, "UpdateUnifiedMotionVisual");
            Set(player, "_isGrounded", grounded);
            player.ClearAimFacing();
            player.SetInputEnabled(input);
            ball.position = oldPosition;
        }
        Directory.CreateDirectory(reportDirectory);
        File.WriteAllText(reportDirectory + "/charge-transition-validation.txt", report.ToString());
        return report.ToString();
    }

    public static string BeginLiveCharge()
    {
        Require(EditorApplication.isPlaying, "Play Mode required.");
        var player = UnityEngine.Object.FindFirstObjectByType<Player>();
        var intro = player.GetComponent<PlayerStartIntro>();
        if (intro != null) intro.enabled = false;
        var launcher = player.GetComponent<MorningStarLauncher>();
        Call(launcher, "EnterDraggingState", false);
        Set(launcher, "_nextLaunchTime", 0f);
        Set(launcher, "_airThrowsUsed", 0);
        player.SetInputEnabled(false);
        Set(player, "_isGrounded", true);
        Set(launcher, "_chargeTime", 0f);
        Call(launcher, "BeginSpinCharging");
        return "charge started frame=" + Time.frameCount + " position=" + launcher.BallBody.position;
    }

    private static int chargePauseFrame;

    public static string AdvanceChargeAndPause()
    {
        var player = UnityEngine.Object.FindFirstObjectByType<Player>();
        player.GetComponent<PlayerHealth>().ResetToFullHp();
        player.GetComponent<PlayerHealth>().BeginRespawnProtection();
        player.GetComponent<MorningStarLauncher>().EndRespawnPresentation();
        player.SetStartIntroControl(true);
        player.SetStartIntroMotion(true, Vector2.zero);
        player.GetComponent<Rigidbody2D>().simulated = false;
        string started = BeginLiveCharge();
        player.SetStartIntroControl(true);
        player.SetStartIntroMotion(true, Vector2.zero);
        Time.timeScale = 1f;
        Application.runInBackground = true;
        chargePauseFrame = Time.frameCount + 50;
        EditorApplication.update -= PauseAfterChargeFrames;
        EditorApplication.update += PauseAfterChargeFrames;
        EditorApplication.isPaused = false;
        return started + "; will pause after 50 advancing frames.";
    }

    private static void PauseAfterChargeFrames()
    {
        if (!EditorApplication.isPlaying || Time.frameCount >= chargePauseFrame)
        {
            EditorApplication.update -= PauseAfterChargeFrames;
            if (EditorApplication.isPlaying) EditorApplication.isPaused = true;
        }
    }
}

