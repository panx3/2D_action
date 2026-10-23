using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class ChainOneWayValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Validate()
    {
        var fixture = new GameObject("ChainOneWayValidationFixture");
        try
        {
            Vector2 origin = new Vector2(1000f, 1000f);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Gimmicks/OneWayPlatform.prefab");
            var platform = UnityEngine.Object.Instantiate(prefab, origin, Quaternion.identity, fixture.transform);
            var platformB = UnityEngine.Object.Instantiate(prefab, origin + Vector2.right * 10f, Quaternion.identity, fixture.transform);
            var box = platform.transform.Find("MorningStarSolid").GetComponent<BoxCollider2D>();
            Physics2D.SyncTransforms();
            float top = box.bounds.max.y;
            var owner = new GameObject("Constraint");
            owner.transform.SetParent(fixture.transform);
            owner.layer = LayerMask.NameToLayer("morningstar");
            var ball = owner.AddComponent<Rigidbody2D>();
            ball.gravityScale = 0f;
            owner.AddComponent<CircleCollider2D>().radius = 0.18f;
            var constraint = owner.AddComponent<ChainConstraint2D>();
            constraint.enabled = false;
            constraint.ConfigureTerrainPath(LayerMask.GetMask("Default", "Walls"), 0.14f, 0.01f);
            var playerObject = new GameObject("Player");
            playerObject.transform.SetParent(fixture.transform);
            playerObject.layer = LayerMask.NameToLayer("player");
            playerObject.tag = "Player";
            var playerBody = playerObject.AddComponent<Rigidbody2D>();
            playerBody.gravityScale = 0f;
            playerBody.freezeRotation = true;
            playerObject.AddComponent<BoxCollider2D>();
            var player = playerObject.AddComponent<Player>();
            player.enabled = false;
            Set(constraint, "playerRb", playerBody);
            Set(constraint, "morningStarRb", ball);
            Set(constraint, "handAnchor", playerObject.transform);

            Vector2 below = new Vector2(origin.x, box.bounds.min.y - 1f);
            Vector2 above = new Vector2(origin.x, top + 1f);
            Rebuild(constraint, below, above);
            Require(constraint.RopeContactPointCount == 0 && !constraint.HasOneWaySupport,
                "Underside generated wrap contacts");
            Require(Mathf.Abs(constraint.CurrentRopeLength - Vector2.Distance(below, above)) < 0.002f,
                "Underside inflated rope length");
            var filter = new ContactFilter2D();
            filter.SetLayerMask(LayerMask.GetMask("Default", "Walls"));
            var visual = new[] { (Vector3)below, (Vector3)above, (Vector3)(above + Vector2.up) };
            Require(ChainVisualCollision2D.Resolve(visual, 3, 0.14f, 0.01f, filter, new RaycastHit2D[16]) == 0,
                "Visual pass recaptured underside");

            // Rising through the floor remains ignored until the rope has cleared it.
            for (int i = 0; i <= 10; i++)
            {
                Vector2 a = below + Vector2.up * (i * 0.1f);
                Rebuild(constraint, a, above);
                Require(!constraint.HasOneWaySupport, "Upward passage acquired support");
            }
            Invoke(constraint, "OnDisable");
            Land(playerBody, ball, origin.x, origin.x, top);
            Vector2 start = playerBody.position;
            Vector2 end = ball.position;
            Rebuild(constraint, below, above);
            Require(!constraint.HasOneWaySupport, "Grounding forcibly captured an underside path");
            Invoke(constraint, "OnDisable");
            Rebuild(constraint, start, end);
            Require(constraint.HasOneWaySupport && constraint.RopeContactPointCount > 0, "Top sag did not acquire support");
            Require(constraint.CurrentRopeLength <= constraint.MaxRopeLength + 0.01f, "Slack support overlength");
            Require(!player.IsGrounded, "Chain support changed Player grounding");
            for (int i = 0; i < constraint.RopeContactPointCount; i++)
            {
                Vector2 point = constraint.GetRopeContactPoint(i);
                if (point.x >= box.bounds.min.x && point.x <= box.bounds.max.x)
                    Require(point.y >= top + 0.139f, "Supported point penetrated top");
            }
            ValidateRenderers(fixture, constraint, playerBody, ball, start, end);

            // Real simulated contacts, including the different Player/ball effector
            // colliders, must each resolve to a OneWayPlatform instance.
            for (int i = 0; i < 20; i++)
            {
                Physics2D.Simulate(0.02f);
                Rebuild(constraint, playerBody.position, ball.position);
                Require(constraint.HasOneWaySupport, "Resting contacts flickered");
            }
            playerBody.AddForce(Vector2.up * 3f, ForceMode2D.Impulse);
            Rebuild(constraint, playerBody.position, ball.position);
            Require(!constraint.HasOneWaySupport && constraint.RopeContactPointCount == 0,
                "Same-step jump retained a cached ground contact");

            CheckPair(constraint, playerBody, ball, origin.x, origin.x, top, true, "Both on A");
            ball.AddForce(Vector2.up * 3f, ForceMode2D.Impulse);
            Rebuild(constraint, playerBody.position, ball.position);
            Require(!constraint.HasOneWaySupport && constraint.RopeContactPointCount == 0,
                "Same-step ball launch retained support");
            CheckPair(constraint, playerBody, ball, origin.x, origin.x, top, true, "Ball re-land on A");
            Move(playerBody, playerBody.position + Vector2.up * 2f);
            Physics2D.Simulate(0.02f);
            Rebuild(constraint, playerBody.position, ball.position);
            Require(!constraint.HasOneWaySupport, "Ball-only A contact supported rope");
            CheckPair(constraint, playerBody, ball, origin.x, origin.x, top, true, "Re-land on A");
            Move(ball, ball.position + Vector2.up * 2f);
            Physics2D.Simulate(0.02f);
            Rebuild(constraint, playerBody.position, ball.position);
            Require(!constraint.HasOneWaySupport, "Player-only A contact supported rope");
            constraint.SetMaxRopeLength(16f); // Slack across the gap between A and B.
            CheckPair(constraint, playerBody, ball, origin.x, origin.x + 10f, top, true, "Player A / ball B");
            ValidateRenderers(fixture, constraint, playerBody, ball, playerBody.position, ball.position);
            CheckPair(constraint, playerBody, ball, origin.x + 10f, origin.x, top, true, "Player B / ball A");
            ValidateRenderers(fixture, constraint, playerBody, ball, playerBody.position, ball.position);
            constraint.SetMaxRopeLength(4.5f);
            CheckPair(constraint, playerBody, ball, origin.x + 10f, origin.x + 10f, top, true, "Both on B");
            Move(playerBody, playerBody.position + Vector2.up * 2f);
            Move(ball, ball.position + Vector2.up * 2f);
            Physics2D.Simulate(0.02f);
            Rebuild(constraint, playerBody.position, ball.position);
            Require(!constraint.HasOneWaySupport, "Airborne pair retained support");
            CheckPair(constraint, playerBody, ball, origin.x, origin.x, top, true, "Return to A");
            start = playerBody.position;
            end = ball.position;

            var otherOwner = new GameObject("IndependentConstraint");
            otherOwner.transform.SetParent(fixture.transform);
            var otherConstraint = otherOwner.AddComponent<ChainConstraint2D>();
            otherConstraint.enabled = false;
            otherConstraint.ConfigureTerrainPath(LayerMask.GetMask("Default", "Walls"), 0.14f, 0.01f);
            Rebuild(otherConstraint, below, above);
            Require(!otherConstraint.HasOneWaySupport && constraint.HasOneWaySupport,
                "Support history leaked between ropes");

            constraint.enabled = true;
            constraint.SetFlyingTrajectoryPriority(true);
            Invoke(constraint, "FixedUpdate");
            Require(constraint.HasOneWaySupport && ball.linearVelocity == Vector2.zero,
                "A slack supported launch changed ball velocity");
            float climbLength = constraint.CurrentRopeLength;
            constraint.ConfigurePlayerClimb(Vector2.zero, 1f, 0.1f);
            Invoke(constraint, "FixedUpdate");
            Require(constraint.MaxRopeLength < climbLength && playerBody.linearVelocity.magnitude > 0f
                && ball.linearVelocity == Vector2.zero && !player.IsGrounded,
                "Climb shared-path check: before=" + climbLength + " max=" + constraint.MaxRopeLength
                + " current=" + constraint.CurrentRopeLength + " playerV=" + playerBody.linearVelocity
                + " ballV=" + ball.linearVelocity + " grounded=" + player.IsGrounded);
            constraint.StopPlayerClimb();
            constraint.SetFlyingTrajectoryPriority(false);
            constraint.SetMaxRopeLength(4.5f);

            // Leaving the platform must discard history, even if the old sag
            // curve still crosses its surface.
            Vector2 hangingStart = new Vector2(box.bounds.min.x - 0.5f, top - 0.5f);
            Vector2 hangingEnd = new Vector2(box.bounds.max.x + 0.5f, top - 0.5f);
            Move(playerBody, hangingStart);
            Move(ball, hangingEnd);
            Physics2D.Simulate(0.02f);
            Rebuild(constraint, hangingStart, hangingEnd);
            Require(!constraint.HasOneWaySupport && constraint.RopeContactPointCount == 0, "Airborne opposite-side endpoints retained support");
            Vector2 leaveStart = new Vector2(box.bounds.max.x + 1f, top - 0.5f);
            Vector2 leaveEnd = new Vector2(box.bounds.max.x + 2f, top - 0.5f);
            Rebuild(constraint, leaveStart, leaveEnd);
            Require(!constraint.HasOneWaySupport && constraint.RopeContactPointCount == 0, "Edge exit retained support");

            Invoke(constraint, "OnDisable");
            CheckPair(constraint, playerBody, ball, origin.x, origin.x, top, true, "Reacquire after exit");
            Require(constraint.HasOneWaySupport, "Could not reacquire top support");
            constraint.enabled = true;
            constraint.enabled = false;
            Require(!constraint.HasOneWaySupport && constraint.RopeContactPointCount == 0, "Recall/disable retained history");
            Rebuild(constraint, below, above);
            Require(!constraint.HasOneWaySupport, "Reset history leaked into a new launch");

            // Normal terrain still uses the original bounded wrap solver.
            var wallObject = new GameObject("OrdinaryTerrain");
            wallObject.transform.SetParent(fixture.transform);
            var wall = wallObject.AddComponent<BoxCollider2D>();
            wall.size = new Vector2(4f, 1f);
            wallObject.transform.position = origin + Vector2.right * 30f;
            Physics2D.SyncTransforms();
            Land(playerBody, ball, origin.x, origin.x + 30f, top);
            Rebuild(constraint, start, end);
            Require(!constraint.HasOneWaySupport && constraint.RopeContactPointCount == 0,
                "Player on OneWay / ball on ordinary ground enabled support");
            Land(playerBody, ball, origin.x + 30f, origin.x + 10f, top);
            Rebuild(constraint, start, end);
            Require(!constraint.HasOneWaySupport && constraint.RopeContactPointCount == 0,
                "Player on ordinary ground / ball on OneWay enabled support");
            Land(playerBody, ball, origin.x + 30f, origin.x + 30f, origin.y + 0.5f);
            // Keep the probe curve on A: generic grounded flags alone would
            // wrongly allow this support despite both bodies resting elsewhere.
            Rebuild(constraint, start, end);
            Require(!constraint.HasOneWaySupport && constraint.RopeContactPointCount == 0,
                "Ordinary-ground pair enabled OneWay support");
            platform.SetActive(false);
            platformB.SetActive(false);
            wall.size = new Vector2(2f, 1f);
            wallObject.transform.position = origin;
            Physics2D.SyncTransforms();
            Invoke(constraint, "OnDisable");
            Rebuild(constraint, origin + Vector2.down, origin + Vector2.up);
            Require(constraint.RopeContactPointCount > 0 && constraint.CurrentRopeLength > 2f
                && !constraint.HasOneWaySupport, "Ordinary-terrain wrapping changed");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(fixture);
        }
    }

    private static void Move(Rigidbody2D body, Vector2 position)
    {
        body.transform.position = position;
        body.position = position;
        body.linearVelocity = Vector2.zero;
        Physics2D.SyncTransforms();
    }

    private static void Land(Rigidbody2D player, Rigidbody2D ball, float playerX, float ballX, float top)
    {
        Move(player, new Vector2(playerX - 1f, top + 0.6f));
        Move(ball, new Vector2(ballX + 1f, top + 0.28f));
        player.gravityScale = ball.gravityScale = 1f;
        for (int i = 0; i < 50; i++) Physics2D.Simulate(0.02f);
        player.gravityScale = ball.gravityScale = 0f;
        player.linearVelocity = ball.linearVelocity = Vector2.zero;
    }

    private static void CheckPair(ChainConstraint2D constraint, Rigidbody2D player, Rigidbody2D ball,
        float playerX, float ballX, float top, bool expected, string label)
    {
        Land(player, ball, playerX, ballX, top);
        Rebuild(constraint, player.position, ball.position);
        Require(constraint.HasOneWaySupport == expected, label + " support=" + constraint.HasOneWaySupport);
        if (!expected) Require(constraint.RopeContactPointCount == 0, label + " left stale route contacts");
    }

    private static void ValidateRenderers(GameObject fixture, ChainConstraint2D constraint,
        Rigidbody2D playerBody, Rigidbody2D ball, Vector2 start, Vector2 end)
    {
        // This synchronous fixture has not advanced a physics frame yet. Match
        // Transform and Rigidbody poses before the real anchor/climb code reads
        // them through its two normal access paths.
        playerBody.transform.position = start;
        ball.transform.position = end;
        playerBody.position = start;
        ball.position = end;
        Physics2D.SyncTransforms();
        Require(Vector2.Distance(playerBody.position, start) < 0.001f
            && Vector2.Distance(playerBody.transform.position, start) < 0.001f
            && Vector2.Distance(ball.position, end) < 0.001f
            && Vector2.Distance(ball.transform.position, end) < 0.001f, "Fixture poses were not synchronized");
        var renderOwner = new GameObject("InactiveRendererFixture");
        renderOwner.transform.SetParent(fixture.transform);
        renderOwner.SetActive(false);
        var launcher = renderOwner.AddComponent<MorningStarLauncher>();
        Set(launcher, "chainConstraint", constraint);
        Set(launcher, "handAnchor", playerBody.transform);
        Set(launcher, "_playerRb", playerBody);
        var line = renderOwner.AddComponent<LineRenderer>();
        var lineController = renderOwner.AddComponent<ChainLineController>();
        Set(lineController, "_line", line);
        Set(lineController, "launcher", launcher);
        object drawn = Invoke(lineController, "TryDrawWrappedChain", (Vector3)start, (Vector3)end);
        Require((bool)drawn && line.positionCount == constraint.RopeContactPointCount + 2, "Line did not use shared route");
        var links = renderOwner.AddComponent<ChainLinkVisualController>();
        Set(links, "launcher", launcher);
        Set(links, "endPoint", ball.transform);
        Invoke(links, "UpdatePathPoints");
        var linkPoints = (List<Vector3>)typeof(ChainLinkVisualController).GetField("_pathPoints", Private).GetValue(links);
        Require(linkPoints.Count == line.positionCount, "Renderers disagree on path point count");
        for (int i = 0; i < constraint.RopeContactPointCount; i++)
        {
            Vector3 expected = constraint.GetRopeContactPoint(i);
            Require(Vector3.Distance(line.GetPosition(i + 1), expected) < 0.001f
                && Vector3.Distance(linkPoints[i + 1], expected) < 0.001f, "Renderer support differs from physics");
        }
    }

    private static void Rebuild(ChainConstraint2D constraint, Vector2 start, Vector2 end)
    {
        Invoke(constraint, "RebuildRopePath", start, end);
    }

    private static void Set(object target, string field, object value)
    {
        target.GetType().GetField(field, Private).SetValue(target, value);
    }

    private static object Invoke(object target, string method, params object[] arguments)
    {
        return target.GetType().GetMethod(method, Private).Invoke(target, arguments);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
