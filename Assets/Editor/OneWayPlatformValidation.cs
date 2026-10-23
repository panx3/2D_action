using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>Called by the disposable Platforms Play Mode validation scene.</summary>
public static class OneWayPlatformValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const float Dt = 0.02f;

    public static void Validate()
    {
        var fixture = new GameObject("OneWayValidationFixture");
        try
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Gimmicks/OneWayPlatform.prefab");
            Require(prefab != null, "OneWay prefab missing");
            var platform = UnityEngine.Object.Instantiate(prefab, Vector3.zero, Quaternion.identity, fixture.transform);
            var surface = platform.GetComponent<BoxCollider2D>();
            var solid = platform.transform.Find("MorningStarSolid").GetComponent<BoxCollider2D>();
            var ballEffector = solid.GetComponent<PlatformEffector2D>();
            var effector = platform.GetComponent<PlatformEffector2D>();
            Require(surface.usedByEffector && solid.usedByEffector && effector.useOneWay
                && effector.useOneWayGrouping && effector.surfaceArc < 180f, "Invalid one-way settings");
            Require(ballEffector != null && ballEffector.useOneWay && ballEffector.useOneWayGrouping
                && Mathf.Approximately(ballEffector.surfaceArc, effector.surfaceArc), "Ball is not one-way");
            var terrainHits = new Collider2D[8];
            var terrainFilter = new ContactFilter2D();
            terrainFilter.SetLayerMask(LayerMask.GetMask("Default", "Walls"));
            terrainFilter.useTriggers = false;
            Physics2D.SyncTransforms();
            Require(Physics2D.OverlapPoint(surface.bounds.center, terrainFilter, terrainHits) == 1
                && terrainHits[0] == solid, "Terrain/chain queries see duplicate platform colliders");

            var playerObject = new GameObject("ValidationPlayer");
            playerObject.transform.SetParent(fixture.transform);
            playerObject.layer = LayerMask.NameToLayer("player");
            playerObject.tag = "Player";
            var body = playerObject.AddComponent<Rigidbody2D>();
            body.freezeRotation = true;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            var capsule = playerObject.AddComponent<CapsuleCollider2D>();
            capsule.size = new Vector2(0.5f, 1f);
            var player = playerObject.AddComponent<Player>();
            player.enabled = false;
            Set(player, "_jumpVoiceClips", new AudioClip[0]);
            Set(player, "_jumpHeld", true);
            float top = surface.bounds.max.y;
            body.position = new Vector2(0f, top - 1.2f);
            Physics2D.SyncTransforms();
            Invoke(player, "PerformJump", false);
            bool passedAbove = false;
            bool landed = false;
            for (int i = 0; i < 200; i++)
            {
                Step(player);
                if (capsule.bounds.min.y > top + 0.05f)
                    passedAbove = true;
                if (capsule.bounds.min.y < top - 0.04f)
                    Require(!player.IsGrounded, "False grounding while passing through from below");
                if (passedAbove && player.IsGrounded)
                {
                    landed = true;
                    break;
                }
            }
            Require(passedAbove && landed && surface.IsTouching(capsule), "Jump-through or landing failed");
            float startX = body.position.x;
            Set(player, "_moveInput", Vector2.right);
            for (int i = 0; i < 5; i++) Step(player);
            Require(body.position.x > startX + 0.03f && player.IsGrounded, "Ground movement failed");
            Set(player, "_moveInput", Vector2.zero);
            float startY = body.position.y;
            Invoke(player, "PerformJump", false);
            for (int i = 0; i < 4; i++) Step(player);
            Require(body.position.y > startY + 0.05f && !player.IsGrounded, "Jump from platform failed");

            body.position = new Vector2(surface.bounds.min.x - 0.6f, top - 0.3f);
            body.linearVelocity = Vector2.zero;
            body.gravityScale = 0f;
            Set(player, "_bjump", false);
            Set(player, "_groundedGraceTimer", 0f);
            Set(player, "_moveInput", Vector2.right);
            Physics2D.SyncTransforms();
            float sideExitX = surface.bounds.max.x + 0.3f;
            for (int i = 0; i < 200 && body.position.x <= sideExitX; i++)
            {
                Step(player);
                Require(!player.IsGrounded, "Side/ball-only surface incorrectly grounded player");
            }
            Require(body.position.x > sideExitX,
                "Side passage blocked: position=" + body.position + " velocity=" + body.linearVelocity + " targetX=" + sideExitX);
            UnityEngine.Object.DestroyImmediate(playerObject);

            var ballObject = new GameObject("ValidationMorningStar");
            ballObject.transform.SetParent(fixture.transform);
            ballObject.layer = LayerMask.NameToLayer("morningstar");
            ballObject.tag = "morningstar";
            var ball = ballObject.AddComponent<Rigidbody2D>();
            ball.gravityScale = 0f;
            ball.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            var circle = ballObject.AddComponent<CircleCollider2D>();
            circle.radius = 0.18f;
            ball.position = new Vector2(0f, top + 0.7f);
            ball.linearVelocity = Vector2.down * 4f;
            Physics2D.SyncTransforms();
            for (int i = 0; i < 20; i++) Physics2D.Simulate(Dt);
            Require(circle.bounds.min.y >= top - 0.03f && solid.IsTouching(circle), "Ball passed through from above");
            Require(!surface.IsTouching(circle), "Ball contacted the Player-only surface");

            float bottom = solid.bounds.min.y;
            foreach (float speed in new[] { 4f, 30f })
            {
                ball.position = new Vector2(0f, bottom - 0.7f);
                ball.linearVelocity = Vector2.up * speed;
                ball.gravityScale = 0f;
                Physics2D.SyncTransforms();
                int steps = Mathf.CeilToInt(1.6f / (speed * Dt));
                for (int i = 0; i < steps; i++) Physics2D.Simulate(Dt);
                Require(circle.bounds.min.y > top + 0.05f && ball.linearVelocity.y > 0f
                    && !solid.IsTouching(circle), "Ball was blocked from below at speed " + speed);
                ball.linearVelocity = Vector2.zero;
                ball.gravityScale = 1f;
                for (int i = 0; i < 60; i++) Physics2D.Simulate(Dt);
                Require(circle.bounds.min.y >= top - 0.03f && solid.IsTouching(circle),
                    "Ball could not land after passing through from below");
            }

            var second = UnityEngine.Object.Instantiate(prefab, new Vector3(6f, 0f, 0f), Quaternion.identity, fixture.transform);
            var secondSolid = second.transform.Find("MorningStarSolid").GetComponent<BoxCollider2D>();
            ball.position = new Vector2(6f, top + 0.7f);
            ball.gravityScale = 0f;
            ball.linearVelocity = Vector2.down * 4f;
            Physics2D.SyncTransforms();
            for (int i = 0; i < 20; i++) Physics2D.Simulate(Dt);
            Require(secondSolid.IsTouching(circle) && !solid.IsTouching(circle)
                && platform.transform.position == Vector3.zero, "Multiple copies interfered");
            ChainOneWayValidation.Validate();
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(fixture);
        }
    }

    private static void Step(Player player)
    {
        Invoke(player, "FixedUpdate");
        Physics2D.Simulate(Dt);
    }

    private static void Set(object target, string field, object value)
    {
        target.GetType().GetField(field, Private).SetValue(target, value);
    }

    private static void Invoke(object target, string method, params object[] arguments)
    {
        target.GetType().GetMethod(method, Private).Invoke(target, arguments);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
