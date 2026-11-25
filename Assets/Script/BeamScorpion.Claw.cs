using UnityEngine;
using UnityEngine.SceneManagement;

public sealed partial class BeamScorpionController
{
    [Header("Claw counter")]
    [SerializeField, Min(0.1f)] private float clawWindupTime = 0.46f;
    [SerializeField, Min(0.25f)] private float clawSwingDuration = 0.6f;
    [SerializeField, Min(0.2f)] private float clawReach = 1.9f;
    [SerializeField, Min(1)] private int clawDamage = 1;
    [SerializeField] private Vector2 clawPlayerImpulse = new Vector2(8f, 9f);
    [SerializeField] private Vector2 clawBallVelocity = new Vector2(14f, 9f);

    private const float ClawSwingTime = 0.14f;
    private const float ClawWindupClipLength = 0.46f;
    private const float ClawSwingClipLength = 0.6f;
    private const float ClawLowAngle = -10f;
    private const float ClawHighAngle = 62f;
    private static readonly Color ClawColor = new Color(1f, 0.64f, 0.25f);
    private static readonly Vector2 ClawHingePixel = new Vector2(39f, 25f);

    private readonly Collider2D[] clawTargets = new Collider2D[12];
    private ContactFilter2D clawFilter;
    private bool clawHitPlayer, clawHitBall;

    private Vector2 ClawPivot(int facing)
    {
        // 新しいコマ絵もビーム最初のコマと同じ付け根・接地位置を使う。
        Sprite source = attackFrames != null && attackFrames.Length > 0 ? attackFrames[0] : null;
        if (source == null || visualRenderer == null)
            return (Vector2)bodyCollider.bounds.center + Vector2.right * (0.26f * facing);
        Vector2 local = (ClawHingePixel - source.pivot) / source.pixelsPerUnit;
        local.x *= -facing;
        return visualRenderer.transform.TransformPoint(local);
    }

    private bool PlayerWithinClawReach(int facing)
    {
        Vector2 pivot = ClawPivot(facing);
        Vector2 contact = playerCollider.ClosestPoint(pivot);
        Vector2 delta = contact - pivot;
        return delta.x * facing >= -0.3f && Mathf.Abs(delta.x) <= clawReach
            && delta.y >= -0.65f && delta.y <= 1f && ClawPathClear(pivot, contact);
    }

    private bool ClawPathClear(Vector2 pivot, Vector2 contact)
    {
        Vector2 delta = contact - pivot;
        float distance = delta.magnitude;
        if (distance < 0.01f) return true;
        int count = gameObject.scene.GetPhysicsScene2D().Raycast(pivot, delta / distance, distance, terrainFilter, hits);
        for (int i = 0; i < count; i++)
            if (IsTerrain(hits[i])) return false;
        return true;
    }

    private void BeginClawSwing()
    {
        clawHitPlayer = clawHitBall = false;
        Vector2 pivot = ClawPivot(direction);
        if (muzzleVfxPrefab != null)
        {
            var trail = Instantiate(muzzleVfxPrefab, pivot, Quaternion.identity);
            SceneManager.MoveGameObjectToScene(trail.gameObject, gameObject.scene);
            trail.PlayClawArc(ClawColor, direction, clawReach, ClawSwingTime, ClawLowAngle, ClawHighAngle);
        }
        TraceClaw();
    }

    private void TraceClaw()
    {
        Vector2 pivot = ClawPivot(direction);
        float angle = Mathf.Lerp(ClawLowAngle, ClawHighAngle, StateElapsed / ClawSwingTime) * Mathf.Deg2Rad;
        Vector2 sweep = new Vector2(direction * Mathf.Cos(angle), Mathf.Sin(angle));
        Vector2 center = pivot + sweep * (clawReach * 0.56f);
        int count = gameObject.scene.GetPhysicsScene2D().OverlapBox(center, new Vector2(clawReach * 0.88f, 0.72f),
            Vector2.SignedAngle(Vector2.right, sweep), clawFilter, clawTargets);
        for (int i = 0; i < count; i++)
        {
            Collider2D target = clawTargets[i];
            Vector2 contact = target.ClosestPoint(center);
            if ((contact.x - bodyCollider.bounds.center.x) * direction <= 0f || !ClawPathClear(pivot, contact)) continue;
            if (PlayerColliderUtility.IsPlayerBody(target))
            {
                if (clawHitPlayer) continue;
                clawHitPlayer = true;
                var hp = target.GetComponentInParent<PlayerHealth>();
                if (hp != null && hp.TakeEnemyDamage(clawDamage, new Vector2(direction * clawPlayerImpulse.x, clawPlayerImpulse.y)))
                    PlayClawImpact(contact, playerHitVfxPrefab);
                continue;
            }
            if (clawHitBall) continue;
            var launcher = FindMorningStar(target);
            if (launcher == null || launcher.IsEnemyMagnetPulling || launcher.IsElectricHeld
                || launcher.State == MorningStarLauncher.MorningStarState.Hooked
                || launcher.State == MorningStarLauncher.MorningStarState.Swinging || !launcher.BallBody.simulated) continue;
            clawHitBall = true;
            if (launcher.ApplyBladeGuardKnockback(new Vector2(direction * clawBallVelocity.x, clawBallVelocity.y)))
                PlayClawImpact(contact, environmentHitVfxPrefab);
        }
    }

    private void PlayClawImpact(Vector2 contact, BeamScorpionBurstVfx prefab)
    {
        SpawnBurst(prefab, contact, new Vector2(direction, 0.75f), tint: ClawColor, strength: 0.85f);
        OneShotAudioUtility.Play2D(audioSource, beamHitSE, beamSEVolume * 0.8f, contact);
    }
}
