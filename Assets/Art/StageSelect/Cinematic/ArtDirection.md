# Cartridge theatre finish

Generated with the built-in image generation tool. The source images are kept as project assets; Unity import settings and sprite slices are owned by the Editor.

## Visual direction

Preserve crisp pixel clusters, silhouettes and typography. Use rose light on the left and cool CRT light on the right. Blur belongs to reflected images, the distant CRT during the crystal reveal, and a few foreground splinters. Avoid a full-screen blur or heavy chromatic separation. The printed cartridge illustration uses the same layered stage composition as the CRT.

`TheatreRoom.png`: an empty, dark tiled room; hardware, reflection and particles remain separate animated elements.

`CrystalShards.png`: eight transparent silhouettes in a 4 × 2 atlas. Needles, plates, wedges and chipped facets replace repetition of one diamond shape. Use Point filtering, no compression or mipmaps, and exact source dimensions for slicing.

## Final generation prompts

### TheatreRoom.png

Create a production-quality 16:9 background plate for a premium Japanese pixel-art action game's retro cartridge selection screen. ONLY the empty room/background, no TV, no console, no cartridge, no screens, no furniture, no lettering, no UI, no particles. A moody dark late-1990s game room / intimate forgotten arcade alcove, straight-on symmetrical composition, deep navy charcoal indigo wall with very subtle aged square panel seams and texture. The empty middle region from x=23% to77%, y=2% to65% will be obscured by a CRT so keep this region quiet. Floor horizon at y=72%; the bottom 28% is a polished dark tiled stone floor in gentle perspective with small restrained rose-pink light reflections on the left and ice-blue reflections on the right, with a very dark central contact area for a console, no mirror images of objects. A subtle black coiled controller cable lies along the far-right floor edge, cropped. Top corners and outer sides very dark, slightly soft depth of field on outer walls; beautifully hand-authored pixel clusters and restrained dithering, microtexture, stepped edges, reminiscent of a cinematic HD-2D game environment. Image should look authored at about 640x360 art resolution with coherent pixel clusters, not random pixel mosaic. Sophisticated cinematic chiaroscuro, low-key near black environment with enough visible texture, restrained magenta bounce on lower left wall and cyan bounce on lower right wall, soft atmospheric depth, slight volumetric light. Premium game art, carefully controlled bloom, no bright neon strips, no visual clutter. Full bleed landscape, no border. The center of attention will be separate foreground hardware added by the game.

### CrystalShards.png

Production game sprite atlas, 4 columns by 2 rows, EIGHT completely isolated individual different fragments of a shattered pink rose-quartz crystal. Transparent background, no floor, no cast shadows, no UI, no text, no glows outside silhouettes, no extra tiny disconnected chips. Each sprite centered in its own equal-size cell with generous transparent padding on every side, no fragments crossing cell boundaries. Crisp hand-authored pixel art, deliberately stepped edges and flat pixel clusters about 2-4 screen pixels wide, 16-bit premium Japanese RPG crystal effect palette. Angular broken irregular silhouettes, not identical perfect diamonds: top row a long needle splinter, broad triangular plate, jagged chunky wedge, narrow crooked blade; bottom row a squat chipped pentagonal chunk, long forked splinter, flattened trapezoid facet, tiny sharp triangular chip. Eight distinct sizes and shapes but arranged centered in regular 4x2 cells. Rich dark plum and burgundy shaded faces, saturated magenta and rose-pink middle faces, sharply defined pale blush and ivory reflective edges, one cool lavender facet per fragment. Three to six flat planes each, absolutely no smooth 3D gradient rendering, no painterly strokes, no blur, no bloom. The transparent shape and chunky deliberate dot-art texture are essential. These shards will assemble into a pink crystal and explode in a retro game menu.
