using UnityEngine;

/// <summary>ネジネズミの表示専用。Root/Collider/速度は一切変更しない。</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Enemy), typeof(EnemyHealth))]
public sealed class RatEnemyFeedback : MonoBehaviour
{
    [SerializeField] private SpriteRenderer _renderer;
    [SerializeField, Range(0f, 0.08f)] private float _animationStrength = 0.065f;
    [SerializeField] private bool _enableEffects = true;
    [SerializeField] private Material _sparkMaterial;
    [SerializeField] private Material _dustMaterial;

    private Enemy _owner;
    private EnemyHealth _health;
    private Collider2D _body;
    private Transform _visual;
    private Vector3 _baseScale, _basePosition;
    private Quaternion _baseRotation;
    private Color _baseColor;
    private Enemy.ActionState _lastState;
    private float _hitTime = float.NegativeInfinity;
    private float _hitStrength;
    private float _nextDustTime;
    private ParticleSystem _sparks, _dust;
    private System.Random _random;

    private void Awake()
    {
        _owner = GetComponent<Enemy>();
        _health = GetComponent<EnemyHealth>();
        _body = GetComponent<Collider2D>();
        if (_renderer == null) _renderer = GetComponentInChildren<SpriteRenderer>();
        if (_renderer == null || _renderer.transform == transform) return;
        _visual = _renderer.transform;
        _baseScale = _visual.localScale;
        _basePosition = _visual.localPosition;
        _baseRotation = _visual.localRotation;
        _baseColor = _renderer.color;
        _random = new System.Random(GetInstanceID());
        _health.HitConfirmed += OnHitConfirmed;
    }

    private void OnEnable()
    {
        _hitTime = float.NegativeInfinity;
        _nextDustTime = 0f;
        if (_owner != null) _lastState = _owner.State;
    }

    private void LateUpdate()
    {
        if (_visual == null || _health.IsDeathHandled) return;
        if (!_owner.enabled) { RestorePose(); return; }
        float elapsed = _owner.StateElapsed;
        // 10fpsのSprite歩行と並行し、変形は12fpsの段階的なポーズにする。
        float poseTime = Mathf.Floor(elapsed * 12f) / 12f;
        float sx = 1f, sy = 1f, tilt = 0f, offsetX = 0f;
        float strength = _animationStrength;
        switch (_owner.State)
        {
            case Enemy.ActionState.Windup:
                float preparation = Mathf.Clamp01((poseTime + 0.1f) / _owner.WindupDuration);
                float amount = Mathf.Ceil(preparation * 3f) / 3f * strength;
                sx += amount;
                sy -= amount;
                if (preparation > 0.45f && strength > 0f)
                    offsetX = ((Mathf.FloorToInt(poseTime * 12f) & 1) == 0 ? 1f : -1f) * LocalPixel();
                break;
            case Enemy.ActionState.Dash:
                if (elapsed < 0.085f) { sx += strength; sy -= strength * 0.5f; }
                break;
            case Enemy.ActionState.Brake:
                if (elapsed < 0.085f)
                {
                    tilt = -_owner.AttackDirection * 3f * (strength / 0.065f);
                    sx -= strength * 0.5f;
                    sy += strength * 0.3f;
                }
                break;
            case Enemy.ActionState.Recovery:
                if (elapsed < 0.16f) { sx += strength * 0.4f; sy -= strength * 0.4f; }
                break;
        }

        float hitElapsed = Time.time - _hitTime;
        if (hitElapsed < 0.16f)
        {
            float amount = Mathf.Min(0.08f, strength * _hitStrength) * (hitElapsed < 0.08f ? 1f : 0.4f);
            sx = 1f + amount;
            sy = 1f - amount;
            tilt = offsetX = 0f;
        }
        _visual.localScale = Vector3.Scale(_baseScale, new Vector3(sx, sy, 1f));
        // 足元を基準に縮め、位置は素材1ドット単位に丸める。
        float foot = _renderer.sprite != null ? _renderer.sprite.bounds.min.y : 0f;
        float offsetY = foot * _baseScale.y * (1f - sy);
        float pixel = LocalPixel();
        _visual.localPosition = _basePosition + new Vector3(offsetX, Mathf.Round(offsetY / pixel) * pixel, 0f);
        _visual.localRotation = _baseRotation * Quaternion.Euler(0f, 0f, tilt);
        _renderer.color = hitElapsed < 0.1f ? new Color(1f, 0.84f, 0.6f, _baseColor.a) : _baseColor;

        if (_enableEffects && Time.timeScale > 0f)
        {
            EnsureEffects();
            if (_lastState != _owner.State)
            {
                switch (_owner.State)
                {
                    case Enemy.ActionState.Windup: Emit(_sparks, 2, false, true); break;
                    case Enemy.ActionState.Dash: Emit(_dust, 4, true, false); break;
                    case Enemy.ActionState.Brake:
                        Emit(_sparks, 3, true, true);
                        Emit(_dust, 2, true, false);
                        break;
                }
            }
            if (_owner.IsMovingVisual && Time.time >= _nextDustTime
                && (_owner.State == Enemy.ActionState.Dash || _owner.State == Enemy.ActionState.Retreat))
            {
                Emit(_dust, 1, true, false);
                _nextDustTime = Time.time + 0.1f;
            }
        }
        _lastState = _owner.State;
    }

    private float LocalPixel() => _renderer.sprite != null
        ? Mathf.Max(0.001f, Mathf.Abs(_baseScale.y) / _renderer.sprite.pixelsPerUnit) : 0.02f;

    private void OnHitConfirmed(MorningStarHitContext context, bool lethal)
    {
        if (!isActiveAndEnabled || _visual == null) return;
        _hitTime = Time.time;
        _hitStrength = context.ImpactSpeed >= 16f || context.ChargeMultiplier > 1.2f ? 1.3f : 0.8f;
        if (!_enableEffects) return;
        EnsureEffects();
        Emit(_sparks, _hitStrength > 1f ? 7 : 4, false, true);
        // 撃破フレームの火花だけは本体のDestroy後も短く残す。金属破片は既存EnemyHealth。
        if (lethal && _sparks != null)
        {
            _sparks.transform.SetParent(null, true);
            var main = _sparks.main;
            main.loop = false;
            main.stopAction = ParticleSystemStopAction.Destroy;
            _sparks.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            Destroy(_sparks.gameObject, 0.4f);
            _sparks = null;
        }
    }

    private void EnsureEffects()
    {
        if (_sparks == null && _sparkMaterial != null) _sparks = CreateSystem("Rat Sparks", _sparkMaterial, 0.55f);
        if (_dust == null && _dustMaterial != null) _dust = CreateSystem("Rat Dust", _dustMaterial, -0.025f);
    }

    private ParticleSystem CreateSystem(string label, Material material, float gravity)
    {
        var child = new GameObject(label);
        child.transform.SetParent(transform, false);
        var system = child.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = system.main;
        main.playOnAwake = false;
        main.loop = true;
        main.startSpeed = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Shape;
        main.maxParticles = 48;
        main.gravityModifier = gravity;
        var emission = system.emission;
        emission.enabled = false;
        var shape = system.shape;
        shape.enabled = false;
        var color = system.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        color.color = gradient;
        var renderer = system.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sortingLayerID = _renderer.sortingLayerID;
        renderer.sortingOrder = _renderer.sortingOrder + 1;
        return system;
    }

    private float Range(float min, float max) => Mathf.Lerp(min, max, (float)_random.NextDouble());

    private void Emit(ParticleSystem system, int count, bool feet, bool spark)
    {
        if (system == null || _body == null) return;
        Bounds bounds = _body.bounds;
        float pixel = Mathf.Clamp(LocalPixel() * Mathf.Abs(transform.lossyScale.y), 0.015f, 0.075f);
        Vector3 origin = new Vector3(bounds.center.x, feet ? bounds.min.y + pixel : bounds.center.y, _renderer.transform.position.z);
        for (int i = 0; i < count; i++)
        {
            system.Emit(new ParticleSystem.EmitParams {
                position = origin + new Vector3(Range(-0.12f, 0.12f), 0f, 0f),
                velocity = new Vector3(Range(-1.5f, 1.5f) - (feet ? _owner.AttackDirection * 0.65f : 0f), Range(0.3f, spark ? 2.2f : 0.7f), 0f),
                startSize = pixel * (spark ? 2f : 4f),
                startLifetime = spark ? Range(0.1f, 0.22f) : Range(0.16f, 0.28f),
                startColor = spark ? new Color(1f, 0.65f, 0.2f, 0.85f) : new Color(0.6f, 0.57f, 0.48f, 0.45f),
                randomSeed = (uint)_random.Next(1, int.MaxValue)
            }, 1);
        }
        system.Play();
    }

    private void RestorePose()
    {
        if (_visual == null) return;
        _visual.localPosition = _basePosition;
        _visual.localScale = _baseScale;
        _visual.localRotation = _baseRotation;
        _renderer.color = _baseColor;
    }

    private void OnDisable()
    {
        RestorePose();
        if (_sparks != null) _sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (_dust != null) _dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void OnDestroy() { if (_health != null) _health.HitConfirmed -= OnHitConfirmed; }
}
