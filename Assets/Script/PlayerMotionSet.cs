using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Playerの描画データ。移動・戦闘・通常のロープ物理は所有しない。</summary>
[CreateAssetMenu(menuName = "鉄球少女/Player Motion Set")]
public sealed class PlayerMotionSet : ScriptableObject
{
    [Serializable]
    public sealed class Frame
    {
        public Sprite sprite;
        public Vector2 chainAnchor;
        public Vector2 chargeGrip;
    }

    [Serializable]
    public sealed class Sequence
    {
        public Frame[] frames = Array.Empty<Frame>();
        public float framesPerSecond = 10f;
        public bool loop;
        public bool IsValid => frames != null && frames.Length > 0;

        public Frame Sample(float seconds)
        {
            if (!IsValid) return null;
            int index = Mathf.Max(0, Mathf.FloorToInt(seconds * framesPerSecond));
            return frames[loop ? index % frames.Length : Mathf.Min(index, frames.Length - 1)];
        }
    }

    [Serializable]
    public sealed class DirectionalThrow
    {
        public Sequence ground = new Sequence();
        public Sequence air = new Sequence();
    }

    public Sequence run = new Sequence();
    public Sequence jumpUp = new Sequence();
    public Sequence apex = new Sequence();
    public Sequence fall = new Sequence();
    public Sequence land = new Sequence();
    public Sequence charge = new Sequence();
    public Sequence chargeRunning = new Sequence();
    public Sequence chargeAirborne = new Sequence();
    public Sequence recall = new Sequence();
    public Sequence brace = new Sequence();
    public Sequence pulled = new Sequence();
    public Sequence hanging = new Sequence();
    public Sequence climbing = new Sequence();
    [Tooltip("横、斜め上、上、斜め下、下。左右は既存のFacingを使う。")]
    public DirectionalThrow[] throws = new DirectionalThrow[5];
    [Min(0.1f)] public float chargeOrbitRadius = 1.25f;
    [Min(0.01f)] public float chargeEntryTime = 0.12f;
    [Min(0f)] public float chargeSlackDepth = 0.9f;

    private readonly Dictionary<Sprite, Frame> frameBySprite = new Dictionary<Sprite, Frame>(96);

    private void OnEnable() => RebuildLookup();

    public void RebuildLookup()
    {
        frameBySprite.Clear();
        Add(run); Add(jumpUp); Add(apex); Add(fall); Add(land); Add(charge);
        Add(chargeRunning); Add(chargeAirborne);
        Add(recall); Add(brace); Add(pulled); Add(hanging); Add(climbing);
        if (throws == null) return;
        foreach (DirectionalThrow direction in throws)
        {
            if (direction == null) continue;
            Add(direction.ground); Add(direction.air);
        }
    }

    private void Add(Sequence sequence)
    {
        if (sequence == null || sequence.frames == null) return;
        foreach (Frame frame in sequence.frames)
            if (frame != null && frame.sprite != null) frameBySprite[frame.sprite] = frame;
    }

    public bool TryGetFrame(Sprite sprite, out Frame frame)
    {
        frame = null;
        return sprite != null && frameBySprite.TryGetValue(sprite, out frame);
    }

    public Frame ChargeFrame(float angle, bool airborne = false, bool moving = false, float locomotionTime = 0f)
    {
        // 脚の踏み替えを720度/秒の鉄球回転へ結び付けない。
        if (!airborne && moving && chargeRunning.IsValid)
            return chargeRunning.Sample(locomotionTime);
        Sequence sequence = airborne && chargeAirborne.IsValid ? chargeAirborne
            : charge;
        if (sequence == null || !sequence.IsValid) return null;
        int index = Mathf.FloorToInt(Mathf.Repeat(angle / (Mathf.PI * 2f), 1f) * sequence.frames.Length);
        return sequence.frames[Mathf.Min(index, sequence.frames.Length - 1)];
    }

    public Sequence Throw(Vector2 direction, bool airborne)
    {
        if (throws == null || throws.Length < 5) return null;
        float angle = Mathf.Atan2(direction.y, Mathf.Abs(direction.x)) * Mathf.Rad2Deg;
        int sector = Mathf.Clamp(Mathf.FloorToInt((angle + 22.5f) / 45f), -2, 2);
        int index = sector >= 0 ? sector : sector == -1 ? 3 : 4;
        DirectionalThrow selected = throws[index];
        return selected == null ? null : airborne ? selected.air : selected.ground;
    }
}
