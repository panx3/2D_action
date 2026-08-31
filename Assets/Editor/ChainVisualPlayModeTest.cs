using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

[InitializeOnLoad]
public static class ChainVisualPlayModeTest
{
    private const string RunningKey = "ChainVisualPlayModeTest.Running";
    private const string ResultKey = "ChainVisualPlayModeTest.Result";

    private static double enteredAt;
    private static int warnings;
    private static int errors;
    private static bool finished;

    static ChainVisualPlayModeTest()
    {
        if (SessionState.GetBool(RunningKey, false))
            Subscribe();
    }

    public static void Run()
    {
        SessionState.SetBool(RunningKey, true);
        SessionState.EraseString(ResultKey);
        EditorSceneManager.OpenScene("Assets/Scenes/CompletScene.unity");
        Subscribe();
        EditorApplication.isPlaying = true;
    }

    private static void Subscribe()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            Time.timeScale = 1f;
            Application.runInBackground = true;
            enteredAt = EditorApplication.timeSinceStartup;
            warnings = 0;
            errors = 0;
            finished = false;
            Application.logMessageReceived -= CountLog;
            Application.logMessageReceived += CountLog;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }
        else if (state == PlayModeStateChange.EnteredEditMode
                 && SessionState.GetBool(RunningKey, false))
        {
            SessionState.SetBool(RunningKey, false);
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            string result = SessionState.GetString(ResultKey, "FAILED: Play Mode ended early");
            Debug.Log("[ChainVisualPlayModeTest] " + result);
            EditorApplication.Exit(result.StartsWith("PASS", StringComparison.Ordinal) ? 0 : 1);
        }
    }

    private static void CountLog(string message, string stack, LogType type)
    {
        if (stack != null && stack.Contains("UnityEditor.Search.SearchDatabase"))
            return;
        if (message != null && message.StartsWith("No graphic device is available", StringComparison.Ordinal))
            return;

        if (type == LogType.Warning)
            warnings++;
        else if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            errors++;
    }

    private static void Tick()
    {
        if (finished || EditorApplication.timeSinceStartup - enteredAt < 1d)
            return;

        try
        {
            RunChecks();
            Require(errors == 0, $"runtime errors={errors}");
            Complete();
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private static void RunChecks()
    {
        ChainLineController chain = UnityEngine.Object.FindAnyObjectByType<ChainLineController>();
        MorningStarLauncher launcher = UnityEngine.Object.FindAnyObjectByType<MorningStarLauncher>();
        ChainConstraint2D constraint = UnityEngine.Object.FindAnyObjectByType<ChainConstraint2D>();
        GameObject ball = GameObject.FindGameObjectWithTag("morningstar");
        Rigidbody2D ballBody = ball != null ? ball.GetComponent<Rigidbody2D>() : null;
        SpriteRenderer ballRenderer = ball != null ? ball.GetComponentInChildren<SpriteRenderer>(true) : null;

        Require(chain != null && launcher != null && constraint != null && ballBody != null && ballRenderer != null,
            "required Chain/MorningStar references are missing");

        LineRenderer line = chain.GetComponent<LineRenderer>();
        Require(line != null && line.sharedMaterial != null, "Chain LineRenderer/material is missing");
        Require(line.sortingLayerName == "Default" && line.sortingOrder == 8,
            "Chain sorting is not Default/8");
        Require(ballRenderer.sortingLayerName == line.sortingLayerName
                && ballRenderer.sortingOrder > line.sortingOrder,
            "MorningStar is not rendered in front of Chain");
        Require(ballRenderer.sortingOrder == 9, "MorningStar sorting order is not 9");
        Require(line.textureMode == LineTextureMode.Tile, "Chain texture mode changed from Tile");
        Require(Mathf.Abs(line.startWidth - 0.3125f) < 0.001f
                && Mathf.Abs(line.endWidth - 0.3125f) < 0.001f,
            "Chain width changed");
        Require(line.positionCount == 16, "Chain visual does not use 16 points");
        Require(chain.GroundLayerMask.value == 65, "Ground LayerMask is not Default + Walls");
        Require(Mathf.Abs(chain.ChainCollisionRadius - 0.14f) < 0.001f,
            "Chain collision radius is not 0.14");

        float mass = ballBody.mass;
        float gravity = ballBody.gravityScale;
        float linearDamping = ballBody.linearDamping;
        float baseLength = launcher.BaseMaxRopeLength;
        float multiplier = launcher.LaunchRopeLengthMultiplier;
        float constraintLength = constraint.MaxRopeLength;

        int tilemapAdjusted = CheckSceneTilemapResolution(chain);
        int floorAdjusted = CheckFloorSurfaceResolution(chain.ChainCollisionRadius);
        int wallAdjusted = CheckWallResolution(chain.ChainCollisionRadius);

        Require(Mathf.Approximately(ballBody.mass, mass)
                && Mathf.Approximately(ballBody.gravityScale, gravity)
                && Mathf.Approximately(ballBody.linearDamping, linearDamping),
            "Visual collision changed MorningStar Rigidbody2D settings");
        Require(Mathf.Approximately(launcher.BaseMaxRopeLength, baseLength)
                && Mathf.Approximately(launcher.LaunchRopeLengthMultiplier, multiplier)
                && Mathf.Approximately(constraint.MaxRopeLength, constraintLength),
            "Visual collision changed rope physics length");
        Require(Mathf.Abs(multiplier - 1.4f) < 0.001f,
            "launchRopeLengthMultiplier changed from 1.4");

        SessionState.SetString(ResultKey,
            $"PASS sorting+tile+width+tilemapFloor+wall+physicsIsolation; tilemapAdjusted={tilemapAdjusted}; "
            + $"floorAdjusted={floorAdjusted}; "
            + $"wallAdjusted={wallAdjusted}; warnings={warnings}; errors={errors}");
    }

    private static int CheckSceneTilemapResolution(ChainLineController chain)
    {
        TilemapCollider2D tilemapCollider = UnityEngine.Object.FindAnyObjectByType<TilemapCollider2D>();
        Require(tilemapCollider != null && tilemapCollider.enabled,
            "CompletScene TilemapCollider2D is missing or disabled");
        Require((chain.GroundLayerMask.value & (1 << tilemapCollider.gameObject.layer)) != 0,
            "Tilemap floor layer is outside Chain Ground LayerMask");

        GameObject gridObject = new GameObject("__ChainVisualTestGrid");
        gridObject.AddComponent<Grid>();
        gridObject.transform.position = new Vector3(1000f, 1000f, 0f);
        GameObject tilemapObject = new GameObject("__ChainVisualTestTilemap");
        tilemapObject.layer = 0;
        tilemapObject.transform.SetParent(gridObject.transform, false);
        Tilemap testTilemap = tilemapObject.AddComponent<Tilemap>();
        tilemapObject.AddComponent<TilemapRenderer>();
        TilemapCollider2D testCollider = tilemapObject.AddComponent<TilemapCollider2D>();
        Tile tile = ScriptableObject.CreateInstance<Tile>();
        tile.colliderType = Tile.ColliderType.Grid;
        testTilemap.SetTile(Vector3Int.zero, tile);
        testTilemap.RefreshAllTiles();
        testCollider.ProcessTilemapChanges();
        Physics2D.SyncTransforms();

        Vector3[] points = BuildCurve(
            new Vector3(1000.1f, 1001.6f),
            new Vector3(1000.9f, 1001.6f),
            1.2f,
            16);
        int adjusted = ChainVisualCollision2D.Resolve(
            points,
            points.Length,
            chain.ChainCollisionRadius,
            0.01f,
            CreateGroundFilter(),
            new RaycastHit2D[8]);
        Require(adjusted > 0, "CompletScene Tilemap floor did not adjust the test Sag");

        float probeRadius = chain.ChainCollisionRadius * 0.9f;
        for (int i = 1; i < points.Length - 1; i++)
        {
            Collider2D overlap = Physics2D.OverlapCircle(
                points[i],
                probeRadius,
                chain.GroundLayerMask.value);
            Require(overlap == null || overlap.isTrigger,
                $"Chain point {i} remained inside {(overlap != null ? overlap.name : "none")}");
        }

        UnityEngine.Object.DestroyImmediate(gridObject);
        UnityEngine.Object.DestroyImmediate(tile);
        return adjusted;
    }

    private static int CheckFloorSurfaceResolution(float radius)
    {
        GameObject floor = new GameObject("__ChainVisualTestFloor");
        floor.layer = 0;
        floor.transform.position = new Vector3(0f, -0.5f, 0f);
        BoxCollider2D collider = floor.AddComponent<BoxCollider2D>();
        collider.size = new Vector2(6f, 1f);
        Physics2D.SyncTransforms();

        Vector3[] points = BuildCurve(new Vector3(-2f, 1f), new Vector3(2f, 1f), 2f, 16);
        Vector3 first = points[0];
        Vector3 last = points[points.Length - 1];
        ContactFilter2D filter = CreateGroundFilter();
        int adjusted = ChainVisualCollision2D.Resolve(points, points.Length, radius, 0.01f, filter, new RaycastHit2D[8]);

        Require(adjusted > 0, "Floor collision did not adjust any sag point");
        Require(points[0] == first && points[points.Length - 1] == last,
            "Floor visual correction moved a Chain endpoint");
        for (int i = 1; i < points.Length - 1; i++)
            Require(points[i].y >= radius - 0.002f, $"Floor point {i} remained inside the surface");

        UnityEngine.Object.DestroyImmediate(floor);
        return adjusted;
    }

    private static int CheckWallResolution(float radius)
    {
        GameObject wall = new GameObject("__ChainVisualTestWall");
        wall.layer = 6;
        wall.transform.position = new Vector3(0f, 2f, 0f);
        BoxCollider2D collider = wall.AddComponent<BoxCollider2D>();
        collider.size = new Vector2(1f, 4f);
        Physics2D.SyncTransforms();

        Vector3[] points = BuildCurve(new Vector3(-2f, 2f), new Vector3(2f, 2f), 0f, 16);
        int adjusted = ChainVisualCollision2D.Resolve(
            points,
            points.Length,
            radius,
            0.01f,
            CreateGroundFilter(),
            new RaycastHit2D[8]);

        Require(adjusted > 0, "Wall collision did not adjust the straight Chain path");
        Require(points[points.Length / 2].x <= -0.5f - radius + 0.002f,
            "Wall correction did not keep intermediate points outside the wall");

        UnityEngine.Object.DestroyImmediate(wall);
        return adjusted;
    }

    private static Vector3[] BuildCurve(Vector3 start, Vector3 end, float sag, int count)
    {
        Vector3[] points = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)(count - 1);
            points[i] = Vector3.Lerp(start, end, t);
            points[i].y -= sag * (4f * t * (1f - t));
        }
        return points;
    }

    private static ContactFilter2D CreateGroundFilter()
    {
        ContactFilter2D filter = new ContactFilter2D();
        filter.SetLayerMask((1 << 0) | (1 << 6));
        filter.useTriggers = false;
        return filter;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void Complete()
    {
        finished = true;
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= CountLog;
        EditorApplication.isPlaying = false;
    }

    private static void Fail(Exception exception)
    {
        if (finished)
            return;
        finished = true;
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= CountLog;
        errors++;
        SessionState.SetString(ResultKey,
            $"FAILED: {exception.Message}; warnings={warnings}; errors={errors}");
        Debug.LogException(exception);
        EditorApplication.isPlaying = false;
    }
}
