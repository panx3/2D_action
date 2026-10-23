using UnityEngine;

/// <summary>レンチカニの構え・踏み込み・接地予告。表示だけを制御し、判定と移動はAIが所有する。</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(WrenchCrab), typeof(EnemyHealth))]
public sealed class WrenchCrabFeedback : MonoBehaviour
{
    [SerializeField] private SpriteRenderer _renderer;
    [SerializeField, Range(0f, 0.08f)] private float _animationStrength = 0.065f;
    [SerializeField] private bool _enableEffects = true;
    [SerializeField] private Texture2D _warningTexture;
    [SerializeField] private Material _sparkMaterial;
    [SerializeField] private Material _dustMaterial;
    [SerializeField] private AudioClip _slamClip;
    [SerializeField, Range(0f, 1f)] private float _slamVolume = 0.4f;

    private WrenchCrab _owner;
    private EnemyHealth _health;
    private Transform _visual;
    private Vector3 _basePosition, _baseScale;
    private Quaternion _baseRotation;
    private float _hitAt = float.NegativeInfinity;
    private float _hitStrength;
    private SpriteRenderer[] _warning;
    private Sprite _warningSprite;
    private ParticleSystem _sparks, _dust;
    private System.Random _random;
    private AudioSource _audio;

    private void Awake()
    {
        _owner = GetComponent<WrenchCrab>();
        _health = GetComponent<EnemyHealth>();
        if (_renderer == null) _renderer = GetComponentInChildren<SpriteRenderer>();
        if (_renderer == null || _renderer.transform == transform) return;
        _visual = _renderer.transform;
        _basePosition = _visual.localPosition;
        _baseScale = _visual.localScale;
        _baseRotation = _visual.localRotation;
        if (_warningTexture != null)
            _warningSprite = Sprite.Create(_warningTexture,
                new Rect(_warningTexture.width / 2, _warningTexture.height / 2, 1f, 1f), new Vector2(0.5f, 0.5f), 100f);
        _random = new System.Random(GetInstanceID());
        _audio = OneShotAudioUtility.FindWorldImpactSource();
        _owner.Slammed += OnSlam;
        _health.HitConfirmed += OnHitConfirmed;
    }

    private void OnEnable() { _hitAt = float.NegativeInfinity; }

    private void LateUpdate()
    {
        if (_visual == null) return;
        if (!_owner.enabled || _health.IsDeathHandled) { RestorePose(); HideWarning(); return; }
        float elapsed = _owner.StateElapsed;
        float poseTime = Mathf.Floor(elapsed * 10f) / 10f;
        float sx = 1f, sy = 1f, tilt = 0f, x = 0f;
        float strength = _animationStrength;
        switch (_owner.State)
        {
            case WrenchCrab.ActionState.Brace:
                float progress = Mathf.Clamp01((poseTime + 0.1f) / _owner.WindupDuration);
                float preparation = Mathf.Ceil(progress * 3f) / 3f * strength;
                // ネズミの震える縮みと区別し、ゆっくりレンチを持ち上げる構え。
                sx -= preparation * 0.55f;
                sy += preparation;
                tilt = _owner.Facing * 2f * progress * (strength / 0.065f);
                break;
            case WrenchCrab.ActionState.Slam:
                sx += strength;
                sy -= strength;
                tilt = -_owner.Facing * 3f * (strength / 0.065f);
                x = _owner.Facing * LocalPixel() * 2f * (strength / 0.065f);
                break;
            case WrenchCrab.ActionState.Recovery:
                if (elapsed < 0.22f)
                {
                    float settle = elapsed < 0.1f ? 0.45f : 0.2f;
                    sx += strength * settle;
                    sy -= strength * settle;
                }
                break;
        }
        float hitElapsed = Time.time - _hitAt;
        if (hitElapsed < 0.16f)
        {
            float squash = Mathf.Min(0.08f, strength * _hitStrength) * (hitElapsed < 0.08f ? 1f : 0.4f);
            sx = 1f + squash;
            sy = 1f - squash;
            tilt = x = 0f;
        }
        _visual.localScale = Vector3.Scale(_baseScale, new Vector3(sx, sy, 1f));
        float pixel = LocalPixel();
        float foot = _renderer.sprite != null ? _renderer.sprite.bounds.min.y : 0f;
        float y = foot * _baseScale.y * (1f - sy);
        _visual.localPosition = _basePosition + new Vector3(x, Mathf.Round(y / pixel) * pixel, 0f);
        _visual.localRotation = _baseRotation * Quaternion.Euler(0f, 0f, tilt);
        UpdateWarning();
    }

    private float LocalPixel() => _renderer.sprite != null
        ? Mathf.Max(0.001f, Mathf.Abs(_baseScale.y) / _renderer.sprite.pixelsPerUnit) : 0.02f;

    private void UpdateWarning()
    {
        if (!_enableEffects || _warningSprite == null || _owner.State != WrenchCrab.ActionState.Brace)
        { HideWarning(); return; }
        if (_warning == null)
        {
            _warning = new SpriteRenderer[7];
            for (int i = 0; i < _warning.Length; i++)
            {
                var child = new GameObject("Claw Warning " + i);
                child.transform.SetParent(transform, false);
                var renderer = child.AddComponent<SpriteRenderer>();
                renderer.sprite = _warningSprite;
                renderer.sortingLayerID = _renderer.sortingLayerID;
                renderer.sortingOrder = _renderer.sortingOrder - 1;
                _warning[i] = renderer;
            }
        }
        Bounds area = _owner.GetAttackBounds();
        float progress = Mathf.Clamp01(_owner.StateElapsed / _owner.WindupDuration);
        float alpha = progress > 0.72f && ((Mathf.FloorToInt(_owner.StateElapsed * 12f) & 1) == 0) ? 0.9f : 0.5f;
        float thickness = Mathf.Clamp(LocalPixel() * Mathf.Abs(transform.lossyScale.y) * 2f, 0.025f, 0.08f);
        for (int i = 0; i < _warning.Length; i++)
        {
            var marker = _warning[i];
            float x = Mathf.Lerp(area.min.x, area.max.x, (i + 0.5f) / _warning.Length);
            bool onGround = _owner.TryGetAttackGround(x, out Vector2 point);
            marker.enabled = area.size.x > 0.03f && onGround;
            if (!marker.enabled) continue;
            marker.transform.position = new Vector3(point.x, point.y, transform.position.z);
            marker.transform.rotation = Quaternion.identity;
            marker.transform.localScale = Vector3.one;
            Vector3 inherited = marker.transform.lossyScale;
            marker.transform.localScale = new Vector3(
                area.size.x / _warning.Length * 0.72f / (_warningSprite.bounds.size.x * Mathf.Max(0.001f, Mathf.Abs(inherited.x))),
                thickness / (_warningSprite.bounds.size.y * Mathf.Max(0.001f, Mathf.Abs(inherited.y))), 1f);
            marker.color = new Color(1f, Mathf.Lerp(0.65f, 0.3f, progress), 0.12f, alpha);
        }
    }

    private void HideWarning()
    {
        if (_warning == null) return;
        foreach (var marker in _warning) if (marker != null) marker.enabled = false;
    }

    private void OnSlam()
    {
        if (!isActiveAndEnabled || !_enableEffects || _visual == null) return;
        EnsureParticles();
        Emit(_sparks, 9, true);
        Emit(_dust, 6, false);
        OneShotAudioUtility.Play2D(_audio, _slamClip, _slamVolume, transform.position);
    }

    private void OnHitConfirmed(MorningStarHitContext context, bool lethal)
    {
        if (!isActiveAndEnabled || _visual == null) return;
        _hitAt = Time.time;
        _hitStrength = context.ImpactSpeed >= 16f || context.ChargeMultiplier > 1.2f ? 1.2f : 0.75f;
        HideWarning();
        if (!_enableEffects) return;
        EnsureParticles();
        Emit(_sparks, lethal ? 8 : 4, true, (Vector3)context.ImpactPoint);
    }

    private void EnsureParticles()
    {
        if (_sparks == null && _sparkMaterial != null) _sparks = CreateParticles("Crab Sparks", _sparkMaterial, 0.6f);
        if (_dust == null && _dustMaterial != null) _dust = CreateParticles("Crab Ground Dust", _dustMaterial, -0.02f);
    }

    private ParticleSystem CreateParticles(string label, Material material, float gravity)
    {
        var child = new GameObject(label);
        child.transform.SetParent(transform, false);
        var particles = child.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main;
        main.playOnAwake = false;
        main.loop = true;
        main.startSpeed = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Shape;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        main.maxParticles = 64;
        main.gravityModifier = gravity;
        var emission = particles.emission;
        emission.enabled = false;
        var shape = particles.shape;
        shape.enabled = false;
        var fade = particles.colorOverLifetime;
        fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        fade.color = gradient;
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sortingLayerID = _renderer.sortingLayerID;
        renderer.sortingOrder = _renderer.sortingOrder + 1;
        return particles;
    }

    private float Range(float min, float max) => Mathf.Lerp(min, max, (float)_random.NextDouble());

    private void Emit(ParticleSystem particles, int count, bool spark, Vector3? impactPoint = null)
    {
        if (particles == null) return;
        Bounds area = _owner.GetAttackBounds();
        float pixel = Mathf.Clamp(LocalPixel() * Mathf.Abs(transform.lossyScale.y), 0.015f, 0.06f);
        for (int i = 0; i < count; i++)
        {
            particles.Emit(new ParticleSystem.EmitParams {
                position = impactPoint ?? new Vector3(Range(area.min.x, area.max.x), area.min.y + pixel, transform.position.z),
                velocity = new Vector3(Range(0.3f, 1.8f) * _owner.Facing, Range(0.5f, spark ? 2.4f : 0.9f), 0f),
                startLifetime = spark ? Range(0.13f, 0.28f) : Range(0.2f, 0.35f),
                startSize = pixel * (spark ? 2f : 5f),
                startColor = spark ? new Color(1f, 0.72f, 0.3f, 0.85f) : new Color(0.6f, 0.58f, 0.52f, 0.5f),
                randomSeed = (uint)_random.Next(1, int.MaxValue)
            }, 1);
        }
        particles.Play();
    }

    private void RestorePose()
    {
        if (_visual == null) return;
        _visual.localPosition = _basePosition;
        _visual.localScale = _baseScale;
        _visual.localRotation = _baseRotation;
    }

    private void OnDisable()
    {
        RestorePose();
        HideWarning();
        if (_sparks != null) _sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (_dust != null) _dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void OnDestroy()
    {
        if (_owner != null) _owner.Slammed -= OnSlam;
        if (_health != null) _health.HitConfirmed -= OnHitConfirmed;
        if (_warningSprite != null)
        {
            if (Application.isPlaying) Destroy(_warningSprite);
            else DestroyImmediate(_warningSprite);
        }
    }
}
