using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class SlashMantisProjectile : MonoBehaviour
{
    [SerializeField] private SpriteRenderer bladeRenderer;
    [SerializeField] private Sprite hitSparkSprite;
    [SerializeField, Min(0.01f)] private float dissipationDuration = 0.24f;

    private static readonly int Dissolve = Shader.PropertyToID("_Dissolve");
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
    private Vector3 flightScale;
    private Color flightColor;
    private MaterialPropertyBlock dissolveProperties;
    private bool dissipating;
    private float dissipationElapsed;

    internal void Fire(SlashMantis source, Vector2 flightDirection, float flightSpeed, float range, float lifetime,
        float hitRadius, int hitDamage, float hitKnockback, LayerMask terrain, LayerMask player)
    {
        owner = source;
        ownerVersion = source.ProjectileVersion;
        if (gameObject.scene != source.gameObject.scene)
            SceneManager.MoveGameObjectToScene(gameObject, source.gameObject.scene);
        direction = flightDirection.normalized;
        transform.rotation = Quaternion.AngleAxis(Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg, Vector3.forward);
        speed = flightSpeed;
        distanceLeft = range;
        timeLeft = lifetime;
        radius = hitRadius;
        if (bladeRenderer != null && bladeRenderer.sprite != null)
        {
            float height = bladeRenderer.sprite.bounds.size.y;
            flightScale = Vector3.one * (2f * radius * SlashMantisEffect.SizeMultiplier / height);
            flightColor = bladeRenderer.color;
            bladeRenderer.transform.localScale = flightScale;
            bladeRenderer.flipY = direction.x < 0f;
        }
        damage = hitDamage;
        knockback = hitKnockback;
        terrainLayers = terrain;
        filter = CreateHitFilter(terrain, player);
    }

    private void FixedUpdate()
    {
        if (owner == null || !owner.isActiveAndEnabled || owner.ProjectileVersion != ownerVersion)
        {
            Destroy(gameObject);
            return;
        }
        if (dissipating) return;

        float stepTime = Mathf.Min(Time.fixedDeltaTime, timeLeft);
        float step = Mathf.Min(distanceLeft, speed * stepTime);
        if (TryGetImpact(gameObject.scene.GetPhysicsScene2D(), transform.position, radius, direction, step,
            filter, terrainLayers, hits, out var impact, out var target))
        {
            if (target != null)
                target.TakeEnemyDamage(damage, Vector2.right * (Mathf.Sign(direction.x) * knockback));
            FinishHit(impact);
            return;
        }

        transform.position += (Vector3)(direction * step);
        distanceLeft -= step;
        timeLeft -= stepTime;
        if (distanceLeft <= 0f || timeLeft <= 0f)
        {
            if (bladeRenderer == null || bladeRenderer.sprite == null)
            {
                Destroy(gameObject);
                return;
            }

            dissipating = true;
            dissolveProperties = new MaterialPropertyBlock();
            bladeRenderer.GetPropertyBlock(dissolveProperties);
        }
    }

    private void Update()
    {
        if (!dissipating) return;

        dissipationElapsed += Time.deltaTime;
        float progress = Mathf.Clamp01(dissipationElapsed / Mathf.Max(0.01f, dissipationDuration));
        Color color = flightColor;
        color.a *= 1f - Mathf.SmoothStep(0f, 1f, progress);
        bladeRenderer.color = color;
        bladeRenderer.transform.localScale = Vector3.Scale(flightScale,
            new Vector3(Mathf.Lerp(1f, 1.30f, progress), Mathf.Lerp(1f, 0.22f, progress), 1f));
        dissolveProperties.SetFloat(Dissolve, progress);
        bladeRenderer.SetPropertyBlock(dissolveProperties);
        if (progress >= 1f) Destroy(gameObject);
    }

    internal static ContactFilter2D CreateHitFilter(LayerMask terrain, LayerMask player)
    {
        var hitFilter = new ContactFilter2D { useTriggers = false };
        hitFilter.SetLayerMask(terrain.value | player.value);
        return hitFilter;
    }

    internal static bool TryGetImpact(PhysicsScene2D scene, Vector2 origin, float radius, Vector2 direction,
        float distance, ContactFilter2D filter, LayerMask terrain, List<RaycastHit2D> hits,
        out RaycastHit2D impact, out PlayerHealth target)
    {
        scene.CircleCast(origin, radius, direction, distance, filter, hits);
        impact = default;
        target = null;
        float closest = float.PositiveInfinity;
        foreach (var hit in hits)
        {
            if (hit.collider == null || hit.distance > closest) continue;

            bool wall = (terrain.value & (1 << hit.collider.gameObject.layer)) != 0;
            PlayerHealth candidate = null;
            if (!wall)
            {
                if (hit.distance >= closest || !PlayerColliderUtility.IsPlayerBody(hit.collider)) continue;
                candidate = hit.collider.GetComponentInParent<PlayerHealth>();
                if (candidate == null || !candidate.isActiveAndEnabled || candidate.IsDead || candidate.IsRespawning)
                    continue;
            }
            impact = hit;
            target = candidate;
            closest = hit.distance;
        }
        return impact.collider != null;
    }

    private void FinishHit(RaycastHit2D hit)
    {
        enabled = false;
        Vector2 point = hit.distance > 0f ? hit.point : (Vector2)transform.position;
        SlashMantisEffect.Play(hitSparkSprite, bladeRenderer, gameObject.scene, point,
            direction.x > 0f ? 1 : -1, Vector2.one * 0.7f, 0.2f);
        if (bladeRenderer != null) bladeRenderer.enabled = false;
        Destroy(gameObject);
    }
}
