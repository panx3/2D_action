using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static partial class PlayerMotionV2Setup
{
    private const string RefinementCalibrationPath = "Docs/Art/PlayerMotionV2/ArtRefinement/calibration.json";

    /// <summary>指定した動きのみ差し替え、既存の未指定コマ・吊り下がりと設定値を保つ。</summary>
    public static string BuildArtRefinement()
    {
        var set = AssetDatabase.LoadAssetAtPath<PlayerMotionSet>(MotionAssetPath);
        if (set == null) throw new InvalidOperationException("Existing motion set missing.");
        BuildArtRefinementPreview();
        set.run = Sequence("RunRefined", 0, 6, 6f / .85f, true);
        set.fall = Sequence("FallRefined", 0, 2, 1f / .18f, false);
        set.throws[4].ground = Sequence("ThrowDownRefined", 0, 3, 16.666667f, false);
        set.throws[4].air = Sequence("ThrowDownRefined", 3, 3, 16.666667f, false);
        set.throws[3].ground = Sequence("ThrowDownRightRefined", 0, 3, 16.666667f, false);
        set.throws[3].air = Sequence("ThrowDownRightRefined", 3, 3, 16.666667f, false);
        set.throws[1].air = Sequence("ThrowUpRightAirRefined", 0, 3, 16.666667f, false);
        ReduceChargeMotion(set.charge);
        ReduceChargeMotion(set.chargeAirborne);
        set.RebuildLookup();
        EditorUtility.SetDirty(set);
        UpdateClip("Walk", set.run, .85f);
        UpdateClip("JumpFall", set.fall, .25f);
        AssetDatabase.SaveAssetIfDirty(set);
        foreach (string name in new[] { "Walk", "JumpFall" })
            AssetDatabase.SaveAssetIfDirty(AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Animation/" + name + ".anim"));
        return "Refined running, fall, downward throws and airborne diagonal-up throw; preserved hanging and other existing sprites.";
    }

    /// <summary>ゲームの参照を書き換えず原寸Spriteを出力し、見た目を先に確認する。</summary>
    public static string BuildArtRefinementPreview()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before creating sprite previews.");
        if (!File.Exists(RefinementCalibrationPath)) throw new FileNotFoundException("Refinement calibration missing.");
        var refinement = JsonUtility.FromJson<CalmCalibration>(File.ReadAllText(RefinementCalibrationPath));
        if (refinement?.sheets == null || refinement.sheets.Length != 5)
            throw new InvalidOperationException("Expected five calibrated source sheets.");
        foreach (var sheet in refinement.sheets)
            if (!File.Exists(sheet.source)) throw new FileNotFoundException("Missing art source: " + sheet.source);

        LoadCalmCalibration();
        if (calmCalibration == null) throw new InvalidOperationException("Original calibration missing.");
        calmCalibration.sheets = calmCalibration.sheets.Concat(refinement.sheets).ToArray();
        palette = ReadPalette();
        colorCache.Clear();
        built.Clear();
        BuildSheet("RunRefined", 3, 2, .285f);
        BuildSheet("FallRefined", 2, 1, .285f);
        BuildSheet("ThrowDownRefined", 3, 2, .285f);
        BuildSheet("ThrowDownRightRefined", 3, 2, .285f);
        BuildSheet("ThrowUpRightAirRefined", 3, 1, .285f);
        return "Built five native-size preview sheets; existing animation and motion references unchanged.";
    }

    private static void ReduceChargeMotion(PlayerMotionSet.Sequence sequence)
    {
        if (sequence == null || sequence.frames == null || sequence.frames.Length != 8)
            throw new InvalidOperationException("Expected the existing eight charge phases.");
        PlayerMotionSet.Frame Find(int index)
        {
            string suffix = "_" + index.ToString("00");
            return sequence.frames.FirstOrDefault(frame => frame?.sprite != null && frame.sprite.name.EndsWith(suffix, StringComparison.Ordinal));
        }
        var first = Find(0); var second = Find(1); var turn = Find(3); var settle = Find(7);
        if (first == null || second == null || turn == null || settle == null)
            throw new InvalidOperationException("Charge source frames are missing.");
        // 位相は鉄球に同期したまま、動く部分と切り替え回数を控えめにする。
        sequence.frames = new[] { first, first, second, second, turn, turn, settle, settle };
    }
}
