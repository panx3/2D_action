using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// TekkyuEnemy の配置・既存接触処理・MorningStar Damage API・死亡処理を
/// CompletScene の Play Mode で確認するバッチ向けスモークテスト。
/// </summary>
[InitializeOnLoad]
public static class TekkyuEnemyPlayModeTest
{
    private const string RunningKey = "TekkyuEnemyPlayModeTest.Running";
    private const string ScenePath = "Assets/Scenes/CompletScene.unity";
    private const string EnemyName = "TekkyuEnemy_MidStage";

    private static double enteredAt;
    private static double phaseStartedAt;
    private static double actualHitAt;
    private static int phase;
    private static int warningCount;
    private static int errorCount;
    private static bool passed;

    private static GameObject sceneEnemy;
    private static EnemyHealth sceneHealth;
    private static Enemy sceneEnemyAi;
    private static Rigidbody2D sceneBody;
    private static Collider2D sceneCollider;
    private static float initialEnemyY;

    private static GameObject actualHitTarget;
    private static GameObject actualCollisionBall;
    private static EnemyHealth actualHitHealth;
    private static MorningStarLauncher actualHitLauncher;
    private static Rigidbody2D actualMorningStarBody;
    private static int hpAfterActualHit;
    private static int actualMorningStarDamage;
    private static int fragmentBaseline;

    static TekkyuEnemyPlayModeTest()
    {
        if (SessionState.GetBool(RunningKey, false))
            Subscribe();
    }

    public static void Run()
    {
        RatEnemyVisualSetup.Validate();
        SessionState.SetBool(RunningKey, true);
        EditorSceneManager.OpenScene(ScenePath);
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
            enteredAt = EditorApplication.timeSinceStartup;
            phaseStartedAt = enteredAt;
            phase = 0;
            warningCount = 0;
            errorCount = 0;
            passed = false;
            Application.logMessageReceived -= CountConsoleMessage;
            Application.logMessageReceived += CountConsoleMessage;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }
        else if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(RunningKey, false))
        {
            Application.logMessageReceived -= CountConsoleMessage;
            EditorApplication.update -= Tick;
            SessionState.SetBool(RunningKey, false);
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;

            Debug.Log($"[TekkyuEnemyPlayTest] RESULT passed={passed}, " +
                      $"actualMorningStarDamage={actualMorningStarDamage}, warnings={warningCount}, errors={errorCount}");
            EditorApplication.Exit(passed && warningCount == 0 && errorCount == 0 ? 0 : 1);
        }
    }

    private static void CountConsoleMessage(string condition, string stackTrace, LogType type)
    {
        if (type == LogType.Warning)
            warningCount++;
        else if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            errorCount++;
    }

    private static void Tick()
    {
        try
        {
            double elapsed = EditorApplication.timeSinceStartup - enteredAt;
            switch (phase)
            {
                case 0 when elapsed >= 0.25d:
                    BeginSceneChecks();
                    phaseStartedAt = EditorApplication.timeSinceStartup;
                    phase = 1;
                    break;

                case 1 when EditorApplication.timeSinceStartup - phaseStartedAt >= 0.45d:
                    CheckFloorAndContactDamage();
                    BeginActualMorningStarCollision();
                    phaseStartedAt = EditorApplication.timeSinceStartup;
                    phase = 2;
                    break;

                case 2:
                    if (actualHitHealth != null && actualHitHealth.CurrentHp < 3)
                    {
                        hpAfterActualHit = actualHitHealth.CurrentHp;
                        actualMorningStarDamage = 3 - hpAfterActualHit;
                        actualHitAt = EditorApplication.timeSinceStartup;
                        Check(actualMorningStarDamage >= 1, "Actual MorningStar collision damages EnemyHealth");
                        UnityEngine.Object.Destroy(actualCollisionBall);
                        SpawnReporterCollisionBall(false);
                        phase = 3;
                    }
                    else if (EditorApplication.timeSinceStartup - phaseStartedAt >= 2.0d)
                    {
                        throw new InvalidOperationException("Actual MorningStar collision did not damage TekkyuEnemy.");
                    }
                    break;

                case 3 when EditorApplication.timeSinceStartup - actualHitAt >= 0.12d:
                    Check(actualHitHealth != null && actualHitHealth.CurrentHp == hpAfterActualHit,
                        "A repeated collision does not apply damage during the 0.2 second target cooldown");
                    UnityEngine.Object.Destroy(actualCollisionBall);
                    UnityEngine.Object.Destroy(actualHitTarget);
                    Time.timeScale = 1f;
                    CheckThreeOneDamageHits();
                    phase = 4;
                    break;

                case 4 when EditorApplication.timeSinceStartup - actualHitAt >= 0.30d:
                    Check(sceneEnemy == null, "Enemy is destroyed when HP reaches 0");
                    CheckEnemyFragments();
                    phase = 5;
                    break;

                case 5 when EditorApplication.timeSinceStartup - actualHitAt >= 1.80d:
                    Check(UnityEngine.Object.FindObjectsByType<EnemyFragment>(FindObjectsInactive.Exclude)
                            .Select(fragment => fragment.GetComponent<SpriteRenderer>())
                            .Any(renderer => renderer != null && renderer.color.a > 0f && renderer.color.a < 0.95f),
                        "EnemyFragment did not fade before expiration");
                    phase = 6;
                    break;

                case 6 when EditorApplication.timeSinceStartup - actualHitAt >= 2.25d:
                    Check(CountEnemyFragments() <= fragmentBaseline,
                        "EnemyFragment instances did not expire after their configured lifetime");
                    passed = true;
                    Debug.Log("[TekkyuEnemyPlayTest] PASS visible=True, floorContact=True, " +
                              "walk4=True, colliderFixed=True, flipX=True, contactDamage=True, " +
                              "hp=3->2->1->0, knockback=True, hitStun=True, death=True, " +
                              "fragments=10, fragmentSprites=multiple, fragmentCollider=False, duplicateDeath=False");
                    StopPlayMode();
                    phase = 7;
                    break;
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            passed = false;
            StopPlayMode();
            phase = 99;
        }
    }

    private static void BeginSceneChecks()
    {
        sceneEnemy = GameObject.Find(EnemyName);
        Check(sceneEnemy != null, "TekkyuEnemy_MidStage exists in Play Mode");

        SpriteRenderer renderer = sceneEnemy.GetComponentInChildren<SpriteRenderer>(true);
        Animator animator = sceneEnemy.GetComponentInChildren<Animator>(true);
        sceneHealth = sceneEnemy.GetComponent<EnemyHealth>();
        sceneEnemyAi = sceneEnemy.GetComponent<Enemy>();
        sceneBody = sceneEnemy.GetComponent<Rigidbody2D>();
        sceneCollider = sceneEnemy.GetComponent<Collider2D>();

        Check(sceneEnemy.GetComponent<SpriteRenderer>() == null && sceneEnemy.GetComponent<Animator>() == null,
            "SpriteRenderer/Animator must not remain on the physics Root");
        Check(renderer != null && animator != null && renderer.enabled && renderer.sprite != null,
            "Visual child does not contain a visible SpriteRenderer and Animator");
        Check(renderer.sprite.texture.width == 281 && renderer.sprite.texture.height == 94,
            "Enemy does not use the attached 281 x 94 four-frame walk sheet");
        Check(sceneHealth != null && sceneHealth.MaxHp == 3 && sceneHealth.CurrentHp == 3,
            "Enemy starts with Current/Max HP 3/3");
        Check(sceneEnemyAi != null, "Enemy.cs is active");
        Check(sceneBody != null && sceneBody.bodyType == RigidbodyType2D.Dynamic, "Existing dynamic Rigidbody2D is active");
        Check(sceneCollider is BoxCollider2D && sceneCollider.enabled && !sceneCollider.isTrigger,
            "Body-sized BoxCollider2D is active");
        Check(sceneCollider.transform == sceneEnemy.transform, "Collider2D is not on Enemy Root");
        Check(Vector3.Distance(sceneEnemy.transform.localScale, Vector3.one) < 0.001f,
            "CompletScene Enemy Root scale is not (1,1,1)");
        BoxCollider2D bodyCollider = (BoxCollider2D)sceneCollider;
        Check(Vector2.Distance(bodyCollider.size, new Vector2(1.52f, 1.24f)) < 0.001f,
            "TekkyuEnemy fixed collider size changed");
        Check(Vector2.Distance(bodyCollider.offset, new Vector2(-0.1f, -0.19f)) < 0.001f,
            "TekkyuEnemy fixed collider offset changed");
        VerifyWalkFramesAndFacing(animator, renderer, bodyCollider);

        initialEnemyY = sceneBody.position.y;
        fragmentBaseline = CountEnemyFragments();
    }

    private static void CheckFloorAndContactDamage()
    {
        Collider2D[] floors = UnityEngine.Object.FindObjectsByType<Collider2D>(FindObjectsInactive.Exclude)
            .Where(c => c.gameObject.CompareTag("Floor"))
            .ToArray();
        Check(floors.Any(floor => sceneCollider.IsTouching(floor)), "Enemy is touching a Floor collider");
        Check(Mathf.Abs(sceneBody.position.y - initialEnemyY) < 0.12f, "Enemy remains on the middle floor");

        PlayerHealth playerHealth = UnityEngine.Object.FindAnyObjectByType<PlayerHealth>();
        Player player = UnityEngine.Object.FindAnyObjectByType<Player>();
        Collider2D playerCollider = player != null
            ? player.GetComponentsInChildren<Collider2D>(true).FirstOrDefault(PlayerColliderUtility.IsPlayerBody)
            : null;
        Check(playerHealth != null && playerCollider != null, "Player contact target exists");

        int playerHpBefore = playerHealth.CurrentHp;
        int enemyHpBefore = sceneHealth.CurrentHp;
        MethodInfo contactMethod = typeof(Enemy).GetMethod(
            "TryContactDamage", BindingFlags.Instance | BindingFlags.NonPublic);
        Check(contactMethod != null, "Existing Enemy contact damage method exists");
        contactMethod.Invoke(sceneEnemyAi, new object[] { playerCollider });
        Check(playerHealth.CurrentHp == playerHpBefore - 1, "Existing Enemy contact damage is 1");
        Check(sceneHealth.CurrentHp == enemyHpBefore, "Player contact does not reduce Enemy HP");
    }

    private static void BeginActualMorningStarCollision()
    {
        MorningStarLauncher launcher = UnityEngine.Object.FindAnyObjectByType<MorningStarLauncher>();
        GameObject ball = GameObject.FindGameObjectWithTag("morningstar");
        Rigidbody2D ballBody = ball != null ? ball.GetComponent<Rigidbody2D>() : null;
        Collider2D ballCollider = ball != null ? ball.GetComponent<Collider2D>() : null;
        Check(launcher != null && ballBody != null && ballCollider != null,
            "Existing MorningStar launcher, Rigidbody2D, and Collider2D exist");
        Check(IsDamageState(launcher.CurrentState),
            $"MorningStar is in a damage-capable state ({launcher.CurrentState})");

        actualHitTarget = UnityEngine.Object.Instantiate(sceneEnemy, new Vector3(1000f, 1000f, 0f), Quaternion.identity);
        actualHitTarget.name = "TekkyuEnemy_ActualMorningStarTest";
        actualHitHealth = actualHitTarget.GetComponent<EnemyHealth>();
        Enemy targetAi = actualHitTarget.GetComponent<Enemy>();
        Rigidbody2D targetBody = actualHitTarget.GetComponent<Rigidbody2D>();
        Collider2D targetCollider = actualHitTarget.GetComponent<Collider2D>();
        Check(actualHitHealth != null && targetBody != null && targetCollider != null,
            "Actual collision target reuses TekkyuEnemy components");

        targetAi.enabled = false;
        targetBody.bodyType = RigidbodyType2D.Dynamic;
        targetBody.gravityScale = 0f;
        targetBody.constraints = RigidbodyConstraints2D.FreezeAll;
        targetBody.linearVelocity = Vector2.zero;
        targetBody.angularVelocity = 0f;

        launcher.enabled = false;
        MorningStarCollisionReporter reporter = ball.GetComponent<MorningStarCollisionReporter>();
        Check(reporter != null, "Existing MorningStarCollisionReporter exists");
        reporter.Initialize(launcher);
        foreach (Joint2D joint in ball.GetComponents<Joint2D>())
            joint.enabled = false;

        actualHitLauncher = launcher;
        actualMorningStarBody = ballBody;
        ballBody.simulated = true;
        ballBody.bodyType = RigidbodyType2D.Dynamic;
        ballBody.gravityScale = 0f;
        ballBody.constraints = RigidbodyConstraints2D.None;
        ballBody.linearDamping = 0f;
        ballBody.angularDamping = 0f;
        ballBody.linearVelocity = Vector2.zero;
        ballBody.angularVelocity = 0f;
        ballCollider.enabled = true;
        ballCollider.isTrigger = false;
        targetCollider.enabled = true;
        targetCollider.isTrigger = false;
        Check(!Physics2D.GetIgnoreLayerCollision(ball.layer, actualHitTarget.layer),
            "MorningStar and Enemy layers are configured to collide");

        // Launcherは参照中の本体速度からDamageを計算する。最低有効速度付近を使い、
        // 既存の baseDamage + speed scaling をそのまま検証する。
        ballBody.position = new Vector2(1200f, 1200f);
        ballBody.linearVelocity = new Vector2(3.1f, 0f);
        Physics2D.SyncTransforms();
        SpawnReporterCollisionBall(true);
        Debug.Log($"[TekkyuEnemyPlayTest] Actual collision armed state={launcher.CurrentState}, " +
                  $"ballLayer={ball.layer}, enemyLayer={actualHitTarget.layer}, " +
                  $"reporterBall={actualCollisionBall.transform.position}, target={targetCollider.bounds.center}");
    }

    private static void SpawnReporterCollisionBall(bool fromLeft)
    {
        Collider2D targetCollider = actualHitTarget.GetComponent<Collider2D>();
        int morningStarLayer = actualMorningStarBody.gameObject.layer;

        actualCollisionBall = new GameObject(fromLeft
            ? "__TekkyuEnemyMorningStarHit"
            : "__TekkyuEnemyMorningStarCooldownProbe");
        actualCollisionBall.layer = morningStarLayer;
        Rigidbody2D reporterBody = actualCollisionBall.AddComponent<Rigidbody2D>();
        reporterBody.gravityScale = 0f;
        reporterBody.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        CircleCollider2D reporterCollider = actualCollisionBall.AddComponent<CircleCollider2D>();
        reporterCollider.radius = 0.35f;
        MorningStarCollisionReporter reporter = actualCollisionBall.AddComponent<MorningStarCollisionReporter>();
        reporter.Initialize(actualHitLauncher);

        Physics2D.SyncTransforms();
        float x = fromLeft
            ? targetCollider.bounds.min.x - reporterCollider.radius + 0.08f
            : targetCollider.bounds.max.x + reporterCollider.radius - 0.08f;
        actualCollisionBall.transform.position = new Vector3(x, targetCollider.bounds.center.y, 0f);
        reporterBody.linearVelocity = new Vector2(fromLeft ? 3.1f : -3.1f, 0f);
        Physics2D.SyncTransforms();

        // Batch ModeではEditor更新とPhysics更新の間隔が一定でないため、
        // この隔離座標のテスト衝突だけを1 Fixed Step進めてCallbackを確定させる。
        SimulationMode2D originalMode = Physics2D.simulationMode;
        try
        {
            Physics2D.simulationMode = SimulationMode2D.Script;
            Physics2D.Simulate(Time.fixedDeltaTime);
        }
        finally
        {
            Physics2D.simulationMode = originalMode;
        }
    }

    private static void CheckThreeOneDamageHits()
    {
        sceneEnemyAi.enabled = false;
        sceneBody.linearVelocity = Vector2.zero;
        sceneBody.angularVelocity = 0f;

        MorningStarHitContext context = new MorningStarHitContext(
            1,
            new Vector2(20f, 0f),
            0f,
            sceneCollider.bounds.center,
            Vector2.right,
            8f,
            1f);

        sceneHealth.OnMorningStarHit(context);
        Check(sceneHealth.CurrentHp == 2, "First 1-damage hit changes HP 3 -> 2");
        Check(sceneHealth.IsHitStunned, "Hit Stun starts on hit");
        Check(sceneBody.linearVelocity.magnitude > 0.1f && sceneBody.linearVelocity.magnitude <= 8.01f,
            "Knockback is applied and clamped to Max Knockback Speed 8");

        sceneHealth.OnMorningStarHit(context);
        Check(sceneHealth.CurrentHp == 1, "Second 1-damage hit changes HP 2 -> 1");

        sceneHealth.OnMorningStarHit(context);
        Check(sceneHealth.CurrentHp == 0, "Third 1-damage hit changes HP 1 -> 0");
        int fragmentCountAfterDeath = CountEnemyFragments();
        sceneHealth.OnMorningStarHit(context);
        Check(CountEnemyFragments() == fragmentCountAfterDeath,
            "A repeated death hit generated a second fragment burst");
        actualHitAt = EditorApplication.timeSinceStartup;
    }

    private static void VerifyWalkFramesAndFacing(
        Animator animator,
        SpriteRenderer renderer,
        BoxCollider2D bodyCollider)
    {
        Vector2 originalSize = bodyCollider.size;
        Vector2 originalOffset = bodyCollider.offset;
        Vector3 originalRootScale = sceneEnemy.transform.localScale;
        HashSet<Sprite> frames = new HashSet<Sprite>();

        for (int i = 0; i < 4; i++)
        {
            animator.Play("Walk", 0, i / 4f);
            animator.Update(0f);
            frames.Add(renderer.sprite);
            Check(bodyCollider.size == originalSize && bodyCollider.offset == originalOffset,
                "Collider changed while sampling walk sprites");
            Check(sceneEnemy.transform.localScale == originalRootScale,
                "Animation changed Enemy Root scale");
        }
        Check(frames.Count == 4, "Walk state did not display four distinct sprites");

        MethodInfo moveToward = typeof(Enemy).GetMethod(
            "MoveToward", BindingFlags.Instance | BindingFlags.NonPublic);
        Check(moveToward != null, "Enemy MoveToward method is missing");
        moveToward.Invoke(sceneEnemyAi, new object[] { sceneEnemy.transform.position.x + 5f });
        Check(renderer.flipX, "Visual did not face right through SpriteRenderer.flipX");
        moveToward.Invoke(sceneEnemyAi, new object[] { sceneEnemy.transform.position.x - 5f });
        Check(!renderer.flipX, "Visual did not face left through SpriteRenderer.flipX");
        Check(sceneEnemy.transform.localScale == originalRootScale,
            "Facing changed Enemy Root scale");
        sceneBody.linearVelocity = Vector2.zero;
        animator.SetBool("IsMoving", false);
    }

    private static void CheckEnemyFragments()
    {
        EnemyFragment[] fragments = UnityEngine.Object.FindObjectsByType<EnemyFragment>(FindObjectsInactive.Exclude);
        Check(fragments.Length >= fragmentBaseline + 10, "Ten EnemyFragment instances were not generated");
        EnemyFragment[] spawned = fragments.Skip(fragmentBaseline).ToArray();
        Check(spawned.Select(fragment => fragment.CurrentSprite).Where(sprite => sprite != null).Distinct().Count() >= 4,
            "EnemyFragment did not mix multiple fragment sprites");
        Check(spawned.All(fragment => fragment.GetComponent<Collider2D>() == null),
            "EnemyFragment has a Collider2D and can obstruct Player");
        Check(spawned.All(fragment => fragment.GetComponent<Rigidbody2D>() != null),
            "EnemyFragment Rigidbody2D is missing");
        Check(spawned.Any(fragment => Mathf.Abs(fragment.GetComponent<Rigidbody2D>().angularVelocity) > 1f),
            "EnemyFragment rotation was not applied");
    }

    private static int CountEnemyFragments()
    {
        return UnityEngine.Object.FindObjectsByType<EnemyFragment>(FindObjectsInactive.Exclude).Length;
    }

    private static void StopPlayMode()
    {
        EditorApplication.update -= Tick;
        if (EditorApplication.isPlaying)
            EditorApplication.isPlaying = false;
    }

    private static bool IsDamageState(MorningStarLauncher.MorningStarState state)
    {
        return state == MorningStarLauncher.MorningStarState.Dragging ||
               state == MorningStarLauncher.MorningStarState.SpinCharging ||
               state == MorningStarLauncher.MorningStarState.Thrown ||
               state == MorningStarLauncher.MorningStarState.Dropping ||
               state == MorningStarLauncher.MorningStarState.Hooked ||
               state == MorningStarLauncher.MorningStarState.Swinging;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException("[TekkyuEnemyPlayTest] Failed: " + message);
    }
}
