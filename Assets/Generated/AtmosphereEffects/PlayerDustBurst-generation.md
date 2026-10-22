# Player jump and landing dust

Asset: `Assets/Resources/PlayerDustBurst.png` (transparent PNG).
Generated with the built-in image_gen tool on 2026-10-21. The source PNG is
preserved; Unity imports it at 256 x 128 with Point filtering, no mipmaps and no
compression. PlayerMotionFeedback creates eight 64 x 64 frames at runtime.
No scene reference is required. The pivot is at the center of the ground baseline.

## Generation prompt

Create a production 2D pixel art VFX spritesheet for a side-view platformer.
Subject: a small ground dust cloud that bursts gently to both sides and dissipates.
Transparent alpha background. Layout is STRICTLY 4 columns by 2 rows, 8 equal square
cells, no borders, no padding between cells, no labels/text/gridlines. Wide 2:1
canvas. Each cell has the SAME ground baseline at 78 percent down its cell, same
central origin; animation reads left to right then second row. Frame 1 tiny low
two-lobe dust at ground; frame 2 expanding left and right rounded but sharply
pixel-stepped curling plumes; frame 3 slightly wider puffs with little crumb pixels;
frame 4 widest cloud no taller than 40 percent cell; frames 5-8 separating thinner
wisps and a few tiny dust fragments progressively dissipating, final frame almost
empty. Keep transparent negative space within clouds increasing in later frames.
Flat limited 4-color palette: desaturated gray taupe #8b8581 shadow, pale stone gray
#b0a69b midtone, warm light gray beige #c9c0b0 main, restrained highlight #e0d7c4. The
game is a detailed pixel-art fantasy ruined stone walkway with green grass, blue
sky and a pink-haired girl. The dust should be restrained and tactile, soft muted
stone dust, no dark outlines, no smoke bomb, no flame, no sparkles, no dramatic
comic impact. AUTHENTIC LOW RESOLUTION PIXEL ART: each cell corresponds to a logical
48 by 48 pixel sprite, enlarged with exact crisp nearest-neighbor square pixel
blocks, hard edges, tiny deliberate clusters, NO blur, NO gradients, NO smooth
vector art, NO anti-aliased round curves. Keep all dust at least 4 logical pixels
inside each cell. Evenly aligned cell centers and identical scale across the 8
animation frames.

## Final edit prompt

Edit this dust animation spritesheet. Keep the exact 4 column by 2 row layout,
frame contents, progression and fixed ground baselines, transparent background.
CLEAN THE ART: remove ALL red, yellow and white fringes around the clouds, all thin
outlines and stray noise. Only muted stone-gray and warm taupe dust colors, clean
hard-edged pixel clusters. Authentic crisp pixel art with chunky uniform square
pixels, 48 by 48 logical pixel cells, no anti-aliasing or blur. Preserve transparent
holes, the eight-frame cloud expansion and breakup, no other effects. The top row
ground baseline and bottom row ground baseline should each lie 88 percent down
every square cell so the clouds have a consistent anchor point. Transparent genuine
alpha, not a checkerboard.

## Effective defaults

- Jump width: 1.4 world units; duration: 0.32 seconds; opacity: 0.62.
- Landing width: 1.8 to 3.2 world units; duration: 0.40 to 0.48 seconds;
  opacity: 0.68 to 0.80.
- Landing size interpolates by distance below the airborne apex, starting at 3
  world units and reaching maximum at 10 world units.
- Overall burst scale: 1.8, applied equally to width and height. Effective jump
  width is 2.52; landing width is 3.24 to 5.76 world units. Existing individual
  width settings are preserved.
- Four reusable burst renderers; animation uses scaled game time.
- Existing running dust, movement, input and Morning Star physics are preserved.
