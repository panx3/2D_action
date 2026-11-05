using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static partial class PlayerMotionV2Setup
{
    /// <summary>飲用6コマだけを取り込む。既存モーション・Scene・Prefabは保存しない。</summary>
    public static string BuildDrink()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
        const int columns = 3, rows = 2, count = 6;
        var set = AssetDatabase.LoadAssetAtPath<PlayerMotionSet>(MotionAssetPath);
        if (set == null) throw new InvalidOperationException("Player motion set is missing.");
        var input = ReadImage(SourceFolder + "/Drink.png");
        var cup = ReadImage(TapiocaHealingItemSetup.SpritePath);
        var output = new Texture2D(Cell * columns, Cell * rows, TextureFormat.RGBA32, false);
        try
        {
            // 既存キャラの色に、タピオカのストローとミルクティーの色だけを加える。
            palette = ReadPalette().Concat(cup.GetPixels32().Where(c => c.a >= 220)
                .GroupBy(c => (c.r << 16) | (c.g << 8) | c.b).OrderByDescending(g => g.Count())
                .Take(24).Select(g => new Color32(g.First().r, g.First().g, g.First().b, 255))).ToArray();
            colorCache.Clear();
            Color32[] pixels = input.GetPixels32();
            var result = new Color32[output.width * output.height];
            var frames = new PlayerMotionSet.Frame[count];
            for (int i = 0; i < count; i++)
            {
                int left = i % columns * input.width / columns, right = (i % columns + 1) * input.width / columns;
                int top = i / columns * input.height / rows, bottom = (i / columns + 1) * input.height / rows;
                HashSet<int> character = FindCharacter(pixels, input.width, input.height, left, top, right, bottom);
                // FindCharacterの添字は画像上端基準。
                int minY = character.Min(n => n / input.width);
                int maxY = character.Max(n => n / input.width);
                float scale = 112f / (maxY - minY + 1);
                var boots = character.Where(n => n / input.width >= maxY - (maxY - minY) * .08f).ToArray();
                float footCenter = (boots.Min(n => n % input.width) + boots.Max(n => n % input.width)) * .5f;
                // Idleと同じ接地線・足の中心へそろえる。物理RootとPivotは変更しない。
                float rootX = footCenter - 3.5f / scale;
                Vector2 weapon = DetectWeaponTip(pixels, input.width, input.height,
                    left, top, right, bottom, rootX, minY, maxY, "Drink", i);
                for (int y = 0; y < Cell; y++) for (int x = 0; x < Cell; x++)
                {
                    int sx = Mathf.RoundToInt(rootX + (x - Cell * .5f) / scale);
                    int sy = Mathf.RoundToInt(maxY + (y - 159f) / scale);
                    if (!character.Contains(sy * input.width + sx) || sx < left || sx >= right || sy < top || sy >= bottom) continue;
                    result[(output.height - 1 - i / columns * Cell - y) * output.width + i % columns * Cell + x] =
                        NativeColor(TopPixel(pixels, input.width, input.height, sx, sy));
                }
                frames[i] = new PlayerMotionSet.Frame
                {
                    chainAnchor = new Vector2((weapon.x - rootX) * scale, Cell * .5f - 159f + (maxY - weapon.y) * scale) / 32f
                };
            }
            output.SetPixels32(result); output.Apply();
            string path = SpriteFolder + "/Drink.png";
            File.WriteAllBytes(path, output.EncodeToPNG());
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            ImportSprites(path, count, columns);
            Sprite[] sprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().OrderBy(s => s.name, StringComparer.Ordinal).ToArray();
            if (sprites.Length != count) throw new InvalidOperationException("Drink sheet must contain six sprites.");
            for (int i = 0; i < count; i++) frames[i].sprite = sprites[i];
            var pickup = AssetDatabase.LoadAssetAtPath<GameObject>(TapiocaHealingItemSetup.PrefabPath).GetComponent<TapiocaHealingPickup>();
            set.drink = new PlayerMotionSet.Sequence { frames = frames, framesPerSecond = count / pickup.DrinkingDuration, loop = false };
            set.RebuildLookup();
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssetIfDirty(set);
            return "Installed six drink frames, 192px cells / 32 PPU, duration=" + pickup.DrinkingDuration;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(input);
            UnityEngine.Object.DestroyImmediate(cup);
            UnityEngine.Object.DestroyImmediate(output);
        }
    }
}
