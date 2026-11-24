using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class SlashMantisProjectile : MonoBehaviour
{
    private readonly List<RaycastHit2D> hits = new List<RaycastHit2D>(8);
    private SlashMantis owner;
    private int ownerVersion;
    private Vector2 direction;
    private float speed;
    private float distanceLeft;
    private float timeLeft;
    private float radius;
    private int damage;
    private float knockback;
    private LayerMask terrainLayers;
    private ContactFilter2D filter;

    internal void Fire(SlashMantis source, int facing, float flightSpeed, float range, float lifetime,
        float hitRadius, int hitDamage, float hitKnockback, LayerMask terrain, LayerMask player)
    {
        owner = source;
        ownerVersion = source.ProjectileVersion;
        if (gameObject.scene != source.gameObject.scene)
            SceneManager.MoveGameObjectToScene(gameObject, source.gameObject.scene);
        direction = Vector2.right * facing;
        speed = flightSpeed;
        distanceLeft = range;
        timeLeft = lifetime;
        radius = hitRadius;
        damage = hitDamage;
        knockback = hitKnockback;
        terrainLayers = terrain;
        filter = new ContactFilter2D { useTriggers = true };
        filter.SetLayerMask(terrain.value | player.value);
    }

    private void FixedUpdate()
    {
        if (owner == null || !owner.isActiveAndEnabled || owner.ProjectileVersion != ownerVersion)
        {
            Destroy(gameObject);
            return;
        }

        float stepTime = Mathf.Min(Time.fixedDeltaTime, timeLeft);
        float step = Mathf.Min(distanceLeft, speed * stepTime);
        gameObject.scene.GetPhysicsScene2D().CircleCast(transform.position, radius, direction, step, filter, hits);
        PlayerHealth target = null;
        float wallDistance = float.PositiveInfinity;
        float playerDistance = float.PositiveInfinity;
        foreach (var hit in hits)
        {
            if (!hit.collider.isTrigger && (terrainLayers.value & (1 << hit.collider.gameObject.layer)) != 0)
                wallDistance = Mathf.Min(wallDistance, hit.distance);
            if (hit.distance >= playerDistance || !PlayerColliderUtility.IsPlayerBody(hit.collider)) continue;
            var candidate = hit.collider.GetComponentInParent<PlayerHealth>();
            if (candidate == null || !candidate.isActiveAndEnabled || candidate.IsDead || candidate.IsRespawning) continue;
            target = candidate;
            playerDistance = hit.distance;
        }
        if (target != null && playerDistance < wallDistance)
        {
            target.TakeEnemyDamage(damage, direction * knockback);
            Destroy(gameObject);
            return;
        }
        if (wallDistance <= step)
        {
            Destroy(gameObject);
            return;
        }

        transform.position += (Vector3)(direction * step);
        distanceLeft -= step;
        timeLeft -= stepTime;
        if (distanceLeft <= 0f || timeLeft <= 0f) Destroy(gameObject);
    }
}
