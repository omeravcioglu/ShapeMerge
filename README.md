# Shape Merge

**A soft-body merge game made in Unity 6, just started.** Shoot squishy shapes into a glass tub. Two equal shapes that touch fuse into the next one up the ladder: circle, triangle, square, pentagon, hexagon, heptagon, octagon, star. Keep the pile under the dashed line.

---

## Gameplay

- **The cannon** rides a rail above the tub and follows the pointer.
  - Hold to charge (the barrel squashes and the waiting shape squeezes), drag to aim, and release to fire.
  - A quick tap drops the shape gently; a full charge rams it into the pile. A dotted line previews where the shot will land.
  - The next shape waits in a bubble, then hops into the cannon.
- **Merging:** each step up the ladder has about 1.6× the area of the one before.
  - The new shape starts as a tiny seed and inflates, shoving its neighbours aside.
  - Two stars that meet both vanish in a blast that shoves the whole pile.
- **Score:** every new shape scores its value. Merges less than 1.4 s apart chain into a combo that multiplies the points, and a star blast is worth eight stars.
- **The shots grow with you:** circles and triangles at first, then squares once you've made a square, and pentagons once you've made a hexagon.
- **Danger:** if the pile stays above the dashed line for 3 seconds, the game ends. While it's over, the vignette turns red and a warning tick speeds up. At the end the pile pops one shape at a time, top to bottom, and the best score is saved.
- **Controls:** mouse, touch, keyboard or gamepad.
- **Feel:** hitstop on big merges, slow motion at game over, screen shake and zoom punches, confetti in the shapes' own forms, shockwave rings and bloom.

**Status:** started on 25 October 2026. The whole loop is playable in one scene: aim, shoot, merge, combos, danger, game over and the best score, with finished-looking visuals and sound made in code. There are no docs or tests yet.

## Tech stack

| Area | What it uses |
|---|---|
| Engine | **Unity 6** (6000.0.85f1), **URP** 17 |
| Physics | **Custom XPBD soft bodies.** Each shape is a ring of 20 to 64 point masses held by perimeter springs, an area (pressure) constraint and shape matching. The world runs up to 12 substeps a frame at 480 Hz, with one constraint pass each, so stacks stay stable and fast shots squeeze into gaps without tunnelling. |
| Rendering | Everything is drawn into dynamic meshes rebuilt every frame: anti-aliased 2D primitives with a one-pixel alpha fringe (no MSAA needed), one unlit shader, premultiplied linear colour |
| Shapes | Painted as glossy gummy candy: drop shadow, thick outline, pillow shading, inner shade, a gloss crescent and a specular dot. Every layer follows the deforming outline, so squash and stretch read clearly. |
| Effects | Pooled particles and shockwave rings in one mesh. A runtime URP volume adds bloom for the flashes, a vignette that turns red in danger, and chromatic aberration on big merges. |
| Audio | Sound effects synthesized at runtime, with no audio files: merges pop lower as shapes grow, and combos climb a pentatonic scale |
| UI | A uGUI overlay pinned to world anchors, so the score and labels follow the tub, and its shake, at any aspect ratio |
| Input | Input System: mouse, touch, keyboard and gamepad behind one hold-to-charge button and a pointer |

## What I built

All of it: 22 C# scripts (about 4.3k lines) and one shader, in **`Assets/ShapeMerge/`**.

| Area | Where |
|---|---|
| The game: shooting, merging, combos, danger and game over | `Scripts/Game/` (`ShapeMergeGame`, `Cannon`, `Arena`, `Shape`, `GameInput`) |
| Soft-body physics | `Scripts/Physics/` (`SoftWorld`, `SoftBody`) |
| The shapes: the merge ladder, their outlines and colours | `Scripts/Shapes/` (`ShapeCatalog`, `ShapeGeometry`, `Palette`) |
| Rendering: mesh layers, primitives, the candy painter, the backdrop, particles | `Scripts/Rendering/` (`MeshLayer`, `Draw`, `ShapePainter`, `Backdrop`, `FxSystem`) |
| Camera, post effects, sound and HUD | `Scripts/Fx/`, `Scripts/Audio/Sfx.cs`, `Scripts/UI/Hud.cs` |
| Tools: the scene builder and Windows build, and a bot that plays a build | `Editor/ShapeMergeSceneBuilder.cs`, `Scripts/Dev/AutoplayCapture.cs` |

### Code highlights

- **`Physics/SoftWorld.cs`:** a small-step XPBD world inside an open-topped tub. Spring stiffness is given in Hz, so the feel stays the same however many substeps a frame runs.
- **`Shapes/ShapeGeometry.cs`:** rounded outlines with evenly spaced points, scaled to enclose exactly the tier's area. Sampling starts on a corner tip, so symmetric counts put a point on every tip.
- **`Rendering/Draw.cs`:** anti-aliased discs, rings, arcs, lines, strokes, polygons, rounded rectangles and smooth closed outlines, written straight into mesh layers. Colours are linear and premultiplied, for one shared blend mode.
- **`Util/Spring.cs`:** damped springs (frequency in Hz and a damping ratio) behind the scale pops, the recoil and the aim.
- **`Dev/AutoplayCapture.cs`:** start a player build with `-shapemerge-autoplay <folder>`, and a bot plays for a while, saving screenshots and frame timings, then quits.

## Scenes

| Scene | Purpose |
|---|---|
| `Assets/ShapeMerge/Scenes/ShapeMerge.unity` | The game, first in Build Settings. `ShapeMergeGame` builds everything at runtime, so any scene with that component works. |

## Integrated third-party assets

None: everything is drawn and synthesized in code. Unity's URP template leftovers are still in the project (`TutorialInfo/`, `Readme.asset`, `Scenes/SampleScene.unity`).

## About this repository

This public repository is a **showcase**. It contains the documentation and the **23 source files I wrote** for this project. The complete Unity project, with its scene, settings and Unity Cloud link, is kept in a private repository.

Copyright © Omer Avcioglu (McHunter Studio). **All rights reserved.** Viewing only; see [LICENSE](LICENSE).
