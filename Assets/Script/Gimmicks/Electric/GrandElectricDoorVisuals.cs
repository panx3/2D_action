using System.Collections.Generic;
using UnityEngine;

/// <summary>大型門の表示だけを所有する。通電判定・扉板・通路Colliderは変更しない。</summary>
[DisallowMultipleComponent]
public sealed class GrandElectricDoorVisuals : MonoBehaviour
{
    [SerializeField] private SpriteRenderer frameBack;
    [SerializeField] private SpriteRenderer frameFront;
    [SerializeField] private SpriteRenderer[] powerRenderers;
    [SerializeField] private Transform[] lockBolts;
    [SerializeField] private Vector3[] boltRetractions;
    [SerializeField] private Transform[] motorRotors;
    [SerializeField] private SpriteRenderer[] chargeSegments;
    [SerializeField] private Transform vibrationRoot;
    [SerializeField] private ElectricReceiverCable receiver;
    [SerializeField] private Sprite receiverClampSprite;
    [SerializeField] private Material receiverClampMaterial;
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
    private LineRenderer cable;
    private MeshRenderer pulse;
    private Mesh pulseMesh;
    private const float CurrentPixelSize = 2f / 32f;
    private readonly List<Vector3> pulseVertices = new List<Vector3>(512);
    private readonly List<Color32> pulseColors = new List<Color32>(512);
    private readonly List<int> pulseTriangles = new List<int>(768);
    private float pulseClock;
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
            MakePixelPulse();
            routeWorld = new Vector3[powerRoute.Length + 1];
            MakeSteam();
        }
    }

    private void Start()
    {
        if (receiver != null)
            receiver.ApplyClampAppearance(receiverClampSprite, receiverClampMaterial);
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
        SetMechanismEffectSorting(renderer);
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
        SetMechanismEffectSorting(line);
        return line;
    }

    private void MakePixelPulse()
    {
        var go = new GameObject("GrandGateCurrentPulse", typeof(MeshFilter), typeof(MeshRenderer))
        {
            hideFlags = HideFlags.DontSave,
            layer = gameObject.layer
        };
        go.transform.SetParent(transform, false);
        pulseMesh = new Mesh { name = "Grand Gate Current Pixels", hideFlags = HideFlags.DontSave };
        pulseMesh.MarkDynamic();
        go.GetComponent<MeshFilter>().sharedMesh = pulseMesh;
        pulse = go.GetComponent<MeshRenderer>();
        pulse.sharedMaterial = wireMaterial;
        pulse.enabled = false;
        SetMechanismEffectSorting(pulse);
    }

    private void SetMechanismEffectSorting(Renderer renderer)
    {
        // Draw the gate's surface effects over its metalwork and behind the player.
        SpriteRenderer frame = frameFront != null ? frameFront : frameBack;
        renderer.sortingLayerID = frame != null ? frame.sortingLayerID : 0;
        renderer.sortingOrder = frameFront != null ? frameFront.sortingOrder + 1 : 6;
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
        DrawPowerRoute(power, dt);
    }

    private void DrawPowerRoute(float power, float deltaTime)
    {
        if (cable == null || pulse == null || routeWorld == null) return;
        routeWorld[0] = receiver != null ? (Vector3)receiver.TipWorld : transform.TransformPoint(powerRoute[0]);
        for (int i = 0; i < powerRoute.Length; i++) routeWorld[i + 1] = transform.TransformPoint(powerRoute[i]);
        cable.positionCount = routeWorld.Length;
        cable.SetPositions(routeWorld);
        Color color = new Color(0.24f, 0.16f, 0.08f, 0.4f);
        cable.startColor = cable.endColor = color;
        bool waitForPower = receiver != null && receiver.UsesConnectionLeadIn;
        pulse.enabled = power > 0.01f && !door.IsFullyOpen && (!waitForPower || receiver.IsPowered);
        if (!pulse.enabled)
        {
            pulseClock = 0f;
            return;
        }

        pulseClock += deltaTime;
        float length = 0f;
        for (int i = 1; i < routeWorld.Length; i++) length += Vector3.Distance(routeWorld[i - 1], routeWorld[i]);

        // Step the pulse on the same grid as the gate art, in blocks of two source pixels.
        int frame = Mathf.FloorToInt(pulseClock * 30f);
        float head = Mathf.Repeat(frame * (0.9f / 30f), 1.12f) * length;
        if (waitForPower && frame == 0)
            head = Mathf.Min(length, 4f * CurrentPixelSize);
        float trailLength = Mathf.Min(length * 0.14f, 3.75f);
        float tail = Mathf.Max(0f, head - trailLength);
        DrawCurrentPixels(tail, Mathf.Min(head, length), head, trailLength, power, frame);
    }

    private void DrawCurrentPixels(float tail, float end, float head, float trailLength, float power, int frame)
    {
        pulseVertices.Clear();
        pulseColors.Clear();
        pulseTriangles.Clear();
        pulseMesh.Clear();
        if (trailLength <= 0f || end <= tail) return;

        byte alpha = (byte)Mathf.RoundToInt(Mathf.Lerp(96f, 255f, power));
        var amber = new Color32(244, 118, 26, alpha);
        var gold = new Color32(255, 198, 55, alpha);
        var core = new Color32(255, 250, 194, alpha);
        int steps = Mathf.CeilToInt((end - tail) / CurrentPixelSize);
        Vector2Int? previous = null;
        for (int i = 0; i <= steps; i++)
        {
            float distance = Mathf.Min(tail + i * CurrentPixelSize, end);
            float brightness = 1f - (head - distance) / trailLength;
            Vector3 point = RoutePoint(distance);
            Vector3 tangent = point - RoutePoint(Mathf.Max(0f, distance - CurrentPixelSize));
            Vector2Int side = Mathf.Abs(tangent.x) >= Mathf.Abs(tangent.y) ? Vector2Int.up : Vector2Int.right;
            int bend = ((i + frame) / 3) % 4;
            var pixel = new Vector2Int(
                Mathf.FloorToInt(point.x / CurrentPixelSize),
                Mathf.FloorToInt(point.y / CurrentPixelSize))
                + side * (bend == 1 ? 1 : bend == 3 ? -1 : 0);
            if (previous == pixel) continue;
            if (brightness < 0.28f && i % 4 >= 2)
            {
                previous = null;
                continue;
            }

            Color32 color = brightness > 0.72f ? core : brightness > 0.28f ? gold : amber;
            if (previous.HasValue)
            {
                Vector2Int bridge = previous.Value;
                while (bridge != pixel)
                {
                    if (bridge.x != pixel.x) bridge.x += bridge.x < pixel.x ? 1 : -1;
                    else bridge.y += bridge.y < pixel.y ? 1 : -1;
                    if (bridge != pixel) AddCurrentPixel(bridge, point.z, color);
                }

                if (brightness > 0.4f && (i + frame) % 7 < 2)
                {
                    if (((i + frame) / 7 & 1) != 0) side = -side;
                    AddCurrentPixel(pixel + side, point.z, gold);
                    if ((i + frame) % 14 == 0)
                        AddCurrentPixel(pixel + side * 3, point.z, amber);
                }
            }
            AddCurrentPixel(pixel, point.z, color);
            previous = pixel;
        }

        pulseMesh.SetVertices(pulseVertices);
        pulseMesh.SetColors(pulseColors);
        pulseMesh.SetTriangles(pulseTriangles, 0);
    }

    private void AddCurrentPixel(Vector2Int pixel, float z, Color32 color)
    {
        Vector3 corner = new Vector3(pixel.x * CurrentPixelSize, pixel.y * CurrentPixelSize, z);
        Vector3 right = Vector3.right * CurrentPixelSize;
        Vector3 up = Vector3.up * CurrentPixelSize;
        int vertex = pulseVertices.Count;
        Transform space = pulse.transform;
        pulseVertices.Add(space.InverseTransformPoint(corner));
        pulseVertices.Add(space.InverseTransformPoint(corner + up));
        pulseVertices.Add(space.InverseTransformPoint(corner + up + right));
        pulseVertices.Add(space.InverseTransformPoint(corner + right));
        for (int i = 0; i < 4; i++) pulseColors.Add(color);
        pulseTriangles.Add(vertex);
        pulseTriangles.Add(vertex + 1);
        pulseTriangles.Add(vertex + 2);
        pulseTriangles.Add(vertex);
        pulseTriangles.Add(vertex + 2);
        pulseTriangles.Add(vertex + 3);
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
        pulseClock = 0f;
        if (steam != null) steam.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (vibrationRoot != null) vibrationRoot.localPosition = vibrationRest;
    }
    private void OnEnable() { if (cable != null) cable.enabled = true; }
    private void OnDestroy() { if (pulseMesh != null) Destroy(pulseMesh); }
}
