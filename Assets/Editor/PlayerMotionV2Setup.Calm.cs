using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static partial class PlayerMotionV2Setup
{
    [Serializable] private sealed class CalmCalibration { public CalmSheet[] sheets; }
    [Serializable] private sealed class CalmSheet
    {
        public string name;
        public string source;
        public int sourceColumns, sourceRows;
        public CalmPose[] poses;
    }
    [Serializable] private sealed class CalmPose
    {
        public int sourceIndex = -1;
        public float headX, headY, skullHeight;
        public float targetX = 103f, targetY = 73f, targetSkull = 32f;
        public bool grounded;
        public float bodyStretch = 1f;
        public float importScaleCompensation = 1f;
        public bool hasVisualAnchor;
        public float visualAnchorX, visualAnchorY;
    }
    private static CalmCalibration calmCalibration;
    private const string CalmCalibrationPath = "Docs/Art/PlayerMotionV2/CalmSources/calibration.json";

    private static void LoadCalmCalibration()
        => calmCalibration = File.Exists(CalmCalibrationPath)
            ? JsonUtility.FromJson<CalmCalibration>(File.ReadAllText(CalmCalibrationPath)) : null;

    private static CalmSheet CalmSheetFor(string name)
    {
        if (calmCalibration == null) return null;
        foreach (CalmSheet sheet in calmCalibration.sheets) if (sheet.name == name) return sheet;
        return null;
    }

    private static string CalmSourcePath(string name)
        => CalmSheetFor(name)?.source ?? SourceFolder + "/" + name + ".png";

    private static int CalmSourceIndex(string name, int index)
    {
        CalmSheet sheet = CalmSheetFor(name);
        return sheet != null && sheet.poses[index].sourceIndex >= 0 ? sheet.poses[index].sourceIndex : index;
    }

    private static int CalmSourceColumns(string name, int fallback)
    {
        CalmSheet sheet = CalmSheetFor(name);
        return sheet != null && sheet.sourceColumns > 0 ? sheet.sourceColumns : fallback;
    }

    private static int CalmSourceRows(string name, int fallback)
    {
        CalmSheet sheet = CalmSheetFor(name);
        return sheet != null && sheet.sourceRows > 0 ? sheet.sourceRows : fallback;
    }

    private static bool TryCalmVisualAnchor(string name, int index, out Vector2 anchor)
    {
        CalmSheet sheet = CalmSheetFor(name);
        CalmPose pose = sheet != null && sheet.poses != null && index < sheet.poses.Length ? sheet.poses[index] : null;
        anchor = pose != null ? new Vector2(pose.visualAnchorX, pose.visualAnchorY) : default;
        return pose != null && pose.hasVisualAnchor;
    }

    // 描画用の寸法合わせ。頭蓋・首より下・足の接地を別々に扱い、武器や髪の外周で原点を決めない。
    private struct PoseMapping
    {
        public bool valid;
        public Vector2 sourceHead, targetHead;
        public float scale, bodyStretch, targetSkull;
        private float NeckOffset => targetSkull * 0.6f;
        public float SourceX(float x) => sourceHead.x + (x - targetHead.x) / scale;
        public float SourceY(float y)
        {
            float delta = y - targetHead.y;
            if (delta > NeckOffset) delta = NeckOffset + (delta - NeckOffset) / bodyStretch;
            return sourceHead.y + delta / scale;
        }
        public Vector2 WorldLocal(Vector2 source)
        {
            float delta = (source.y - sourceHead.y) * scale;
            if (delta > NeckOffset) delta = NeckOffset + (delta - NeckOffset) * bodyStretch;
            return new Vector2(targetHead.x + (source.x - sourceHead.x) * scale - Cell * .5f,
                Cell * .5f - targetHead.y - delta) / 32f;
        }
    }

    private static PoseMapping ResolveCalmMapping(string name, int index, int x, int y, int bottom, int bootBottom)
    {
        CalmSheet sheet = CalmSheetFor(name);
        if (sheet == null) return default;
        if (sheet.poses == null || index >= sheet.poses.Length) throw new InvalidOperationException("Missing calibrated pose: " + name + index);
        CalmPose pose = sheet.poses[index];
        if (pose.skullHeight <= 1f) throw new InvalidOperationException("Missing skull measure: " + name + index);
        var mapping = new PoseMapping
        {
            valid = true, sourceHead = new Vector2(x + pose.headX, y + pose.headY),
            targetHead = new Vector2(pose.targetX, pose.targetY), targetSkull = pose.targetSkull,
            scale = pose.targetSkull / pose.skullHeight, bodyStretch = pose.bodyStretch
        };
        if (pose.grounded)
        {
            float neck = pose.targetSkull * .6f;
            float sourceSpan = (bootBottom - mapping.sourceHead.y) * mapping.scale - neck;
            mapping.bodyStretch = (159f - pose.targetY - neck) / Mathf.Max(1f, sourceSpan);
            if (mapping.bodyStretch < .70f || mapping.bodyStretch > 1.40f)
                throw new InvalidOperationException("Review body proportions: " + name + index + " stretch=" + mapping.bodyStretch);
        }
        // 既存JumpUpの0.95ルートScaleは物理ごと維持し、先頭Spriteだけ取り込み寸法で相殺。
        if (pose.importScaleCompensation != 1f)
        {
            mapping.scale *= pose.importScaleCompensation;
            mapping.targetHead = new Vector2(Cell * .5f, Cell * .5f)
                + (mapping.targetHead - new Vector2(Cell * .5f, Cell * .5f)) * pose.importScaleCompensation;
            mapping.targetSkull *= pose.importScaleCompensation;
        }
        return mapping;
    }

    private static int DetectFootY(Color32[] pixels, int width, int height, HashSet<int> character, int top, int bottom)
    {
        int result = top;
        foreach (int p in character)
        {
            int x = p % width, y = p / width;
            if (y < Mathf.Lerp(top, bottom, .7f)) continue;
            Color32 color = TopPixel(pixels, width, height, x, y);
            if (color.r > 25 && color.r < 130 && color.g > 15 && color.r - color.g > 7 && color.b <= color.g + 12)
                result = Mathf.Max(result, y);
        }
        return result > top ? result : bottom;
    }
}
