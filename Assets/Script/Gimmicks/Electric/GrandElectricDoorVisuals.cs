using UnityEngine;

/// <summary>大型門の表示だけを所有する。通電判定・扉板・通路Colliderは変更しない。</summary>
[DisallowMultipleComponent]
public sealed class GrandElectricDoorVisuals : MonoBehaviour
{
    [SerializeField] private SpriteRenderer frameBack;
    [SerializeField] private SpriteRenderer[] powerRenderers;
    [SerializeField] private Transform[] lockBolts;
    [SerializeField] private Vector3[] boltRetractions;
    [SerializeField] private Transform[] motorRotors;
    [SerializeField] private SpriteRenderer[] chargeSegments;
    [SerializeField] private Transform vibrationRoot;
    [SerializeField] private ElectricReceiverCable receiver;
    [SerializeField] private Vector3[] powerRoute;
    [SerializeField] private Material wireMaterial;
    [SerializeField] private AudioSource mechanismAudio;
    [SerializeField] private AudioClip lockSound;
    [SerializeField] private AudioClip stopSound;
    [SerializeField] private Vector3[] steamVents = { new Vector3(-4.8f, 4.8f), new Vector3(4.3f, 8.7f) };
    private ElectricDoor door;
    private MaterialPropertyBlock block;
    private Vector3[] boltClosed;
    private Quaternion[] rotorClosed;
    private Vector3 vibrationRest;
    private LineRenderer cable, pulse;
    private ParticleSystem steam;
    private Vector3[] routeWorld;
    private float clock, impactRemaining;
    private int lockBeat;
    private bool wasMoving;
    private static readonly int PowerId = Shader.PropertyToID("_GatePower");
    private static readonly int PulseId = Shader.PropertyToID("_GatePulse");
    public Bounds ArtworkBounds => frameBack != null ? frameBack.bounds : new Bounds(transform.position + Vector3.up * 8f, new Vector3(12f, 16f, 0f));

    private void Awake()
    {
        door = GetComponent<ElectricDoor>();
        block = new MaterialPropertyBlock();
        boltClosed = new Vector3[lockBolts != null ? lockBolts.Length : 0];
        for (int i = 0; i < boltClosed.Length; i++) if (lockBolts[i] != null) boltClosed[i] = lockBolts[i].localPosition;
        rotorClosed = new Quaternion[motorRotors != null ? motorRotors.Length : 0];
        for (int i = 0; i < rotorClosed.Length; i++) if (motorRotors[i] != null) rotorClosed[i] = motorRotors[i].localRotation;
        if (vibrationRoot != null) vibrationRest = vibrationRoot.localPosition;
        if (powerRoute != null && powerRoute.Length >= 2 && wireMaterial != null)
        {
            cable = MakeLine("GrandGatePowerCable", 0.055f);
            pulse = MakeLine("GrandGateCurrentPulse", 0.065f);
            routeWorld = new Vector3[powerRoute.Length + 1];
            MakeSteam();
        }
    }

    private void MakeSteam()
    {
        var go = new GameObject("GrandGateSteam") { hideFlags = HideFlags.DontSave };
        go.layer = gameObject.layer;
        go.transform.SetParent(transform, false);
        steam = go.AddComponent<ParticleSystem>();
        steam.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = steam.main;
        main.playOnAwake = false;
        main.loop = false;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.3f);
        main.startSpeed = 0f;
        main.maxParticles = 48;
        var emission = steam.emission;
        emission.enabled = false;
        var shape = steam.shape;
        shape.enabled = false;
        var fade = steam.colorOverLifetime;
        fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(new Color(0.75f, 0.7f, 0.6f), 0f), new GradientColorKey(new Color(0.5f, 0.48f, 0.43f), 1f) },
            new[] { new GradientAlphaKey(0.32f, 0f), new GradientAlphaKey(0f, 1f) });
        fade.color = gradient;
        var growth = steam.sizeOverLifetime;
        growth.enabled = true;
        growth.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 2.5f));
        var renderer = steam.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = wireMaterial;
        renderer.sortingLayerID = frameBack != null ? frameBack.sortingLayerID : 0;
        renderer.sortingOrder = 6;
    }

    private void EmitSteam(int count)
    {
        if (steam == null || steamVents == null) return;
        steam.Play();
        foreach (Vector3 vent in steamVents)
            for (int i = 0; i < count; i++)
            {
                float side = vent.x < 0f ? -1f : 1f;
                var emit = new ParticleSystem.EmitParams
                {
                    position = vent + new Vector3(0f, i * 0.025f, 0f),
                    velocity = new Vector3(side * (0.6f + i * 0.07f), 0.5f + i * 0.12f, 0f)
                };
                steam.Emit(emit, 1);
            }
    }

    private LineRenderer MakeLine(string name, float width)
    {
        var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
        go.layer = gameObject.layer;
        go.transform.SetParent(transform, false);
        var line = go.AddComponent<LineRenderer>();
        line.sharedMaterial = wireMaterial;
        line.useWorldSpace = true;
        line.widthMultiplier = width;
        line.numCapVertices = 0;
        line.numCornerVertices = 0;
        line.sortingLayerID = frameBack != null ? frameBack.sortingLayerID : 0;
        line.sortingOrder = 6;
        return line;
    }

    private void LateUpdate()
    {
        if (door == null) return;
        float dt = door.IsPresentationControlled ? Time.unscaledDeltaTime : Time.deltaTime;
        clock += dt;
        impactRemaining = Mathf.Max(0f, impactRemaining - dt);
        float power = door.ChargeProgress;
        float opening = door.OpeningProgress;
        float unlock = door.UnlockProgress;
        bool moving = opening > 0f && opening < 1f;
        if (steam != null)
        {
            var main = steam.main;
            main.useUnscaledTime = door.IsPresentationControlled;
        }
        if (moving && !wasMoving) EmitSteam(4);
        wasMoving = moving;
        if (chargeSegments != null)
            for (int i = 0; i < chargeSegments.Length; i++)
                if (chargeSegments[i] != null)
                    chargeSegments[i].color = Color.Lerp(new Color(0.07f, 0.045f, 0.015f), new Color(1f, 0.9f, 0.32f), Mathf.Clamp01(power * chargeSegments.Length - i));
        if (powerRenderers != null)
            foreach (SpriteRenderer renderer in powerRenderers)
            {
                if (renderer == null) continue;
                renderer.GetPropertyBlock(block);
                block.SetFloat(PowerId, power);
                block.SetFloat(PulseId, power > 0f && !door.IsFullyOpen ? (Mathf.Sin(clock * 15f) * 0.5f + 0.5f) * 0.16f : 0f);
                renderer.SetPropertyBlock(block);
            }
        for (int i = 0; i < boltClosed.Length; i++)
            if (lockBolts[i] != null)
            {
                float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(i * 0.12f, 0.60f + i * 0.12f, unlock));
                Vector3 offset = boltRetractions != null && i < boltRetractions.Length ? boltRetractions[i] : Vector3.zero;
                lockBolts[i].localPosition = boltClosed[i] + offset * t;
            }
        int beat = Mathf.Min(4, Mathf.FloorToInt(unlock * 4.001f));
        if (beat > lockBeat)
        {
            if (mechanismAudio != null && lockSound != null) mechanismAudio.PlayOneShot(lockSound, 0.5f);
            lockBeat = beat;
        }
        for (int i = 0; i < rotorClosed.Length; i++)
            if (motorRotors[i] != null)
                motorRotors[i].localRotation = rotorClosed[i] * Quaternion.Euler(0f, 0f, opening * (i % 2 == 0 ? -720f : 1080f));
        if (vibrationRoot != null)
        {
            float motor = opening > 0f && opening < 1f ? Mathf.Sin(clock * 47f) * 0.012f : 0f;
            float stop = Mathf.Sin((0.22f - impactRemaining) * 68f) * impactRemaining * 0.22f;
            vibrationRoot.localPosition = vibrationRest + Vector3.right * (motor + stop);
        }
        DrawPowerRoute(power);
    }

    private void DrawPowerRoute(float power)
    {
        if (cable == null || pulse == null || routeWorld == null) return;
        routeWorld[0] = receiver != null ? (Vector3)receiver.TipWorld : transform.TransformPoint(powerRoute[0]);
        for (int i = 0; i < powerRoute.Length; i++) routeWorld[i + 1] = transform.TransformPoint(powerRoute[i]);
        cable.positionCount = routeWorld.Length;
        cable.SetPositions(routeWorld);
        Color color = new Color(0.24f, 0.16f, 0.08f, 0.4f);
        cable.startColor = cable.endColor = color;
        pulse.enabled = power > 0.01f && !door.IsFullyOpen;
        if (!pulse.enabled) return;
        float length = 0f;
        for (int i = 1; i < routeWorld.Length; i++) length += Vector3.Distance(routeWorld[i - 1], routeWorld[i]);
        float head = Mathf.Repeat(clock * 0.9f, 1.12f);
        float tail = Mathf.Max(0f, head - 0.14f);
        var points = new System.Collections.Generic.List<Vector3>();
        points.Add(RoutePoint(Mathf.Clamp01(tail) * length));
        float distance = 0f;
        for (int i = 1; i < routeWorld.Length; i++)
        {
            distance += Vector3.Distance(routeWorld[i - 1], routeWorld[i]);
            if (distance > tail * length && distance < head * length) points.Add(routeWorld[i]);
        }
        points.Add(RoutePoint(Mathf.Clamp01(head) * length));
        pulse.positionCount = points.Count;
        pulse.SetPositions(points.ToArray());
        pulse.startColor = new Color(1f, 0.66f, 0.08f, power * 0.65f);
        pulse.endColor = new Color(1f, 0.98f, 0.72f, power);
    }

    private Vector3 RoutePoint(float distance)
    {
        for (int i = 1; i < routeWorld.Length; i++)
        {
            float segment = Vector3.Distance(routeWorld[i - 1], routeWorld[i]);
            if (distance <= segment) return Vector3.Lerp(routeWorld[i - 1], routeWorld[i], segment > 0f ? distance / segment : 1f);
            distance -= segment;
        }
        return routeWorld[routeWorld.Length - 1];
    }

    public void PlayStopImpact()
    {
        impactRemaining = 0.22f;
        EmitSteam(8);
        if (mechanismAudio != null && stopSound != null) mechanismAudio.PlayOneShot(stopSound, 0.7f);
    }

    private void OnDisable()
    {
        if (cable != null) cable.enabled = false;
        if (pulse != null) pulse.enabled = false;
        if (steam != null) steam.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (vibrationRoot != null) vibrationRoot.localPosition = vibrationRest;
    }
    private void OnEnable() { if (cable != null) cable.enabled = true; }
}
