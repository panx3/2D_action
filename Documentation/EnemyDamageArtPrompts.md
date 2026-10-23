# Enemy damage feedback: image generation prompts

Built-in `image_gen` was used with `transparent_background=true`. Source PNG pixels are retained; Unity owns slicing and Point/uncompressed import settings.

## Gauge

```text
Use case: stylized-concept. Asset type: production sprite atlas for a small enemy health bar in a Japanese mechanical-fantasy 2D pixel action game. Genuine transparent background. Exactly THREE separate assets, one centered inside each cell of a strict 1 row by 3 columns equal grid. Left cell: one EMPTY mechanical HP-cell frame, a sharp dark charcoal and gunmetal rectangular beveled bracket, outer proportion about 3:1, thin crisp stepped border, completely transparent central opening. Middle cell: one lit HP-cell interior tile, warm crimson-pink with restrained pale warm highlight along its top edge, narrow sharp rectangular shape about 3:1, no surrounding frame. Right cell: exactly matching unlit HP-cell interior tile, dark slate gray with subtle steel highlight, same shape and dimensions as the lit tile. Components will be repeated side by side for 2,3,4,5 or more HP. Flat front view, crisp deliberate coarse pixel art, small readable shapes, 8-12 color limited palette, no anti-aliasing, no text or numbers, no labels, no character, no shadows outside sprites, no backdrop, no checkerboard. Wide transparent padding inside every equal cell, no overlap. Keep all three graphic pieces centered on the same horizontal baseline.
```

## Hit

```text
Use case: stylized-concept. Asset type: production 2D pixel-art hit spark animation sprite sheet. Genuine transparent background. Exactly FOUR frames in a strict horizontal 1 row by 4 columns equal-cell grid, left to right chronological animation. Each cell contains only a single small impact spark centered at the exact same fixed anchor, generous transparent margins. Frame1 tiny white-hot star nick with pale coral pixels; frame2 sharp asymmetric white and warm cream impact star with 5-7 thin angular orange/red sparks; frame3 broken pink-orange spark shards traveling slightly outward; frame4 a few separated dim orange embers fading out. Mechanical-fantasy action game palette: ivory white, pale rose, hot coral, orange, deep wine outline. Compact crisp chunky pixel clusters, sharp stepped edges, 12-16-bit era pixel art with some detail, no smooth gradients or anti-aliasing, no photographic glow, no explosions, no character, no weapon, no text or labels. Constant cell sizes, no overlap between cells. Readable when each sprite is displayed at about 32-48 pixels high.
```

## Critical

```text
Use case: stylized-concept. Asset type: production 2D pixel-art damaged-machine VFX sprite atlas. Genuine transparent background. Exactly EIGHT separate frames in a strict 2 rows by 4 columns equal-cell grid. Top row is a FOUR-frame tiny electrical malfunction spark animation, centered in each cell, fixed anchor: one short bent ivory/coral lightning crack, two branching tiny pink-white arcs, broken orange pin sparks, one dim ember. Bottom row is a FOUR-frame small gray smoke puff animation, centered in each cell with fixed anchor: tiny dark gray puff, slightly expanded slate-gray puff, sparse broken gray wisps, nearly vanished dark gray wisps. These are subtle intermittent low-health effects for a red mechanical scorpion enemy, not a large aura. Restrained charcoal-gray smoke, warm ivory/coral/pink sparks, no blue electricity. Crisp coarse pixel-art clusters, stepped silhouette, limited palette, no blurry soft rendering or anti-aliasing. No character, no backdrop, no ground, no text, no labels, no grid lines. Lots of transparent margin around every frame; all frames fit within their individual identical grid cells.
```

## Defeat

```text
Use case: stylized-concept. Asset type: production 2D pixel-art mechanical enemy destruction animation sprite sheet. Genuine transparent background. Exactly SIX chronological animation frames in a strict 2 rows by 3 columns equal-cell grid, read left to right across top row then bottom row. All six frames centered at the same fixed anchor with equal cell dimensions and generous transparent margins. Frame1 compact white-pink ignition star; frame2 small angular bright ivory and rose burst; frame3 peak medium compact red-pink mechanical explosion with white center, orange sparks and a few dark metal shards; frame4 broken coral fire petals plus charcoal-gray smoke puff; frame5 sparse small warm embers and fragmented slate smoke; frame6 just two dim embers and faint broken gray wisps. World style: detailed retro 16-bit pixel action game with red mechanical enemies and dark steel, readable modest explosion that does not fill the screen. Crisp blocky pixel clusters, stepped edges, 10-16 color palette, no anti-aliasing, no smooth gradients or giant realistic fireballs. No character, no text, no labels, no grid lines, no ground or background. No frame overlaps.
```

## Defeat refinement

```text
Refine this exact production pixel-art explosion sprite sheet. Preserve the SIX frames, identical 3 columns by 2 rows grid, frame order, fixed centers, and mechanical red-pink/orange/ivory/charcoal palette. Remove EVERY soft blurred glow, haze, vignette and smooth gradient surrounding the sprites. Replace smoke with distinct hard-edged pixel clusters using a few flat gray tones. Every nontransparent pixel must belong to a deliberate crisp chunky pixel cluster; empty background and empty space around each frame must be genuinely alpha-zero transparent. Keep the modest small-to-medium explosion stages and metal debris. No new objects, labels, text, grid lines or backdrop. The result must be a clean transparent PNG sprite atlas that reads well at 48-64 game pixels per frame.
```

## Imported source PNGs

| Kind | Workspace asset | Generated source |
|---|---|---|
| Mini gauge | `Assets/Art/EnemyDamageFeedback/GaugeAtlas.png` | `exec-b91184f8-e4b4-48ed-8dbe-0fea42045062.png` |
| Hit spark | `Assets/Art/EnemyDamageFeedback/HitAtlas.png` | `exec-6910202b-e2f5-436b-8f5b-b55e245f5ab9.png` |
| Critical spark/smoke | `Assets/Art/EnemyDamageFeedback/CriticalAtlas.png` | `exec-6f683864-db69-45a4-a3ba-76f7546cc4ef.png` |
| Defeat | `Assets/Art/EnemyDamageFeedback/DefeatAtlas.png` | `exec-38097231-2e07-4469-a0c5-d0ec5898fe82.png` |

The initial gauge atlas was `exec-8eef0c77-3f9f-4fec-bf54-d4bb77a997b3.png`; the simplification prompt below produced the selected final source. The initial defeat atlas was `exec-8500db43-96aa-4252-b11c-40dbfa3ee926.png`; the refinement above produced the selected final source. Originals remain in `C:/Users/PCUser/.codex/generated_images/01a16636-704d-7900-8c23-9aac71125e22/`.

## Gauge simplification requested during visual review

Built-in `image_gen`, `transparent_background=true`. This new atlas replaces the original decorative gauge while preserving Unity asset and sprite IDs.

```text
Use case: stylized-concept. Asset type: VERY SIMPLE production pixel-art enemy health-bar components, genuine transparent PNG. EXACTLY three separate components in a strict horizontal 1 row by 3 equal-cell atlas, each component centered, same proportions about 3:1, plenty of transparent margins. LEFT: a plain rectangular HP-cell FRAME only: one crisp dark charcoal pixel-outline border with a very subtle thin light-gray top rim; completely alpha-zero transparent interior. No decorative corners, rivets, bolts, metal panels or bevels. MIDDLE: a solid warm red-pink rectangular HP fill tile with only a narrow pale coral 1-pixel highlight along the top edge; flat clean shape, no frame. RIGHT: an exactly matching solid dark slate-gray empty HP tile, same dimensions, flat and simple. Deliberate chunky pixel-art grid with clean stepped edges, restrained palette of at most 6 colors, no gradients, no shadows, no blurred glow, no antialiasing, no text, no numbers, no labels, no character, no backdrop or checkerboard. These pieces repeat side by side into a large readable three-segment enemy gauge above a red mechanical scorpion. Prioritize immediate readability at tiny on-screen sizes over ornament. Keep all three components aligned to the same horizontal baseline.
```
