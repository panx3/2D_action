# Stage2 checkpoint exterior

Generated with the built-in image_gen tool. The existing checkpoint and the Stage2 furnace are the design references.

Final asset: `Assets/image_/Stage/Stage2/Decorations/MachineCityCheckpoint.png` (141 x 137 px, 100 PPU, center pivot, full rectangular sprite mesh, Point filtering, no mipmaps or compression).

Only the root SpriteRenderer sprite on the two existing Stage2 checkpoints is replaced. Existing scripts, trigger geometry, respawn references, transforms, glow sprites, glow timing, activation VFX and audio remain unchanged. Stage1 and the shared Checkpoint prefab retain the original design.

Import preparation registers the generated exterior to the original sprite's occupied bounds using nearest-neighbor sampling. The original upper crystal/collar and front sigil pixels are retained in the new image to match the unchanged glow overlays precisely. Original source files are not overwritten. Full source art is stored as `Source/MachineCityCheckpoint_Source.png`.

## Final generation prompt

```text
Use case: precise-object-edit
Asset type: single transparent pixel-art checkpoint sprite for the Unity 2D side-scroller 鉄球少女, Stage 2 blazing mechanical city.
Input images: image 1 is the EXACT existing checkpoint cropped to its actual Unity sprite rectangle (141 x 137 logical pixels), the edit target. Image 2 is a Stage 2 furnace, reference for copper/iron materials and pixel style only.
Primary request: redraw the checkpoint's exterior into a coherent industrial version that still instantly reads as the same checkpoint. Preserve the existing pink faceted crystal at the top, the pink square skull-like sigil on the front, a squat tiered pedestal, paired side pipes, the small right-hand hanging cable, the existing three-quarter frontal viewpoint with only a shallow visible top plane. Replace mossy ruin stone with dark soot-black iron plates, restrained warm copper/brass bevels, small square rivets, a few recessed vents, a compact bolted metal plinth. Match the restrained orange/copper Stage 2 furnace material, do not turn the whole object bright orange.
CRITICAL geometry: keep the EXACT image 1 framing, silhouette, pixel footprint and subject positions. The source design is slightly left-of-center: the top crystal is centered at x=58.5/141, y=34.5/137 measured from top; the front pink emblem is centered x=58/141,y=78.5/137 from top. Preserve their original apparent width and height and do not relocate or enlarge them. The top crystal occupies x=39..77,y=12..56 on the logical 141x137 canvas; front emblem occupies x=44..71,y=66..90. Maintain this alignment so existing independent glow overlay sprites still line up. Preserve full object extent, no cropping, no added floor or floating island. The mechanical base has the same compact stepped footprint as the old mossy plinth. All original moss and vegetation must be replaced by riveted metal.
Style: crisp low-resolution 2D game pixel art with clear dark pixel outlines, small readable pixel clusters and limited cel-shaded palette, matching the source sprite's pixel density rather than detailed painting. Pink crystal and skull-like sigil remain small and recognizable, not giant glowing panels. Do not bake particles, bloom, light rays, smoke, halos or external effects into the sprite. No gradients, no anti-aliased illustration style.
Output: exactly one single isolated checkpoint sprite on genuine alpha transparency, facing in the same direction, with the same layout as image 1. No text, labels, watermark, shadow on a background or surrounding scene.
```
