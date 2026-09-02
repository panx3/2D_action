# AGENTS.md

## Project

* Applies to the entire repository. A deeper `AGENTS.md` takes priority.
* Unity `6000.4.2f1`, 2D action game **鉄球少女**.
* Uses Unity Input System.
* Main scenes: `Assets/Scenes/TitleScene.unity`, `Assets/Scenes/CompletScene.unity`.
* Runtime code: `Assets/Script`; Editor/test code: `Assets/Editor`.
* Do not edit generated files/folders (`Library`, `Temp`, `Logs`, `obj`, generated project/solution files).
* `_Recovery` and `_MergeReference` are not production sources unless explicitly requested.

## Core Policy

Priority: **correctness -> regression safety -> focused diff -> token efficiency**.

Decision rule:

> If skipping a step is likely to cause a wrong fix or rework, do it. Otherwise skip it.

Do not sacrifice correctness just to save tokens.

## Default Workflow

1. Read `AGENTS.md`.
2. Check `git status --short`; preserve existing user/team changes.
3. Inspect only files/assets relevant to the request.
4. Reuse the existing owner/system instead of creating duplicates.
5. Implement one focused change.
6. Compile and check relevant new Console Errors/Exceptions.
7. Stop and report briefly.

Do not rescan the whole project every task.

Default cycle:

`AI implements once -> minimum validation -> user Play Mode test -> targeted feedback -> targeted revision`

Final game-feel evaluation is normally performed by the user.

## Investigation / Validation Level

**Minor:** obvious isolated fixes, Inspector defaults, Tooltips, small UI changes.
→ Inspect only the target; compile is normally enough.

**Normal:** known gameplay/UI/enemy/gimmick changes.
→ Inspect relevant dependencies; implement once; compile + relevant Console check.

**Core / unknown bug:** Player, Morning Star, chain/tension, Rigidbody2D, state transitions, Input, Respawn, Scene flow, unexplained regressions.
→ Investigate sufficiently, trace ownership/state, identify root cause, then perform minimum necessary regression checks.

Do not reduce investigation for core/unknown issues merely to save tokens.

## Token Efficiency

Avoid unnecessary:

* repository-wide rescans
* unrelated Script/Prefab/Scene/Hierarchy inspection
* repeated validation of untouched known-good systems
* repeated autonomous Play Mode tuning
* unrelated refactoring or cleanup
* long plans, reasoning traces, investigation logs, or test dumps

Reuse already-confirmed project knowledge unless the current change may invalidate it.

## Change Safety

* Never overwrite/revert unrelated user or team changes.
* Keep changes scoped; no unrelated refactors, renames, file moves, or formatting.
* Preserve serialized references, `.meta` files, and GUIDs.
* Be careful with serialized field names/types, Tags, Layers, Input Actions, Animator parameters, Scene names, and `Time.timeScale`.
* Avoid unintended Scene/Prefab dirty changes.
* Do not modify Packages or Project Settings unless required by the task.

Use Unity MCP when Scene/Prefab/GameObject/Inspector/serialized state matters.
Do not use MCP automatically for ordinary C# changes.
When available, prefer MCP over manual `.unity` / `.prefab` / `.asset` YAML editing.

## Debugging

Do not mask unknown causes with arbitrary Force, speed, gravity, damping, or multiplier changes.

For unknown bugs:

`reproduce -> identify responsible system/state -> find root cause -> fix -> minimum regression check`

Prefer root-cause fixes over symptom compensation.

## Input

* Keep Unity Input System.
* Gamepad is the primary target.
* Right-stick Morning Star launch is a core mechanic.
* Do not redesign working input mappings unless explicitly requested.

## Morning Star Core

Preserve the current state-based architecture.

* `TransitionToState()` remains the central state-transition authority.
* Initial Rest and post-Recall Rest use the same physics setup.
* Launch/Recall history must not leak Rigidbody2D settings into Rest.
* Avoid competing Rigidbody2D writers across states/Update/FixedUpdate/Coroutines.
* Do not casually restore removed legacy systems such as duplicated tension, dragging-only assist Force, large snap Impulses, or launch-history compensation.

Game-feel target:

> **Weighty but responsive**

Weight should come mainly from inertia, delayed ball response, tension, falling, impact, sound, animation, camera, and effects — not frustrating Player slowdown.

Ground: **the girl drags the heavy Morning Star.**

Air: **the Morning Star pulls the girl.**

Air launch should follow:

`Ball launches -> Ball travels ahead -> chain becomes taut -> Player is pulled toward Ball`

Favor a short sprint / pseudo-blink feel. Prefer pulling the Player toward the Ball instead of pulling the Ball back toward the Player.

## Play Mode

Do not use Play Mode automatically for every task.

Use it when required for a core change, unknown gameplay bug, or explicit request.

Do not repeatedly auto-tune subjective gameplay values. The user performs final feel evaluation.

## Completion Report

Keep reports concise, but include enough information for the next AI/user decision.

Normally report:

1. **What changed**
   - Briefly describe what was implemented or adjusted.

2. **Root cause**
   - For bugs, briefly state why the issue occurred.
   - For simple tuning or feature work, write `Adjustment only` or equivalent.

3. **Changed files**
   - List changed Scripts, Prefabs, Scenes, `.meta`, or other relevant files.

4. **Inspector / serialized settings**
   - List added or changed Inspector fields.
   - Include the current effective values when relevant.
   - State whether existing user-adjusted values were preserved.

5. **Validation performed**
   - Compile result.
   - Relevant Console Error / Exception result.
   - If Play Mode or another check was used, briefly state what was verified.

6. **Validation not performed**
   - Explicitly state important checks that were intentionally left to the user or skipped for token efficiency.
   - Example: `Final game-feel Play Mode evaluation not performed; user verification required.`

7. **Scene / Prefab state**
   - State whether any Scene or Prefab was modified or saved.
   - Mention remaining dirty state if relevant.
   - Confirm whether unintended Scene/Prefab changes were avoided.

8. **Remaining issues / notes**
   - Briefly mention anything still requiring user confirmation, temporary values, known limitations, or follow-up work.
   - If nothing remains, write `None`.

Do not include:
- long investigation logs
- reasoning traces
- repetitive summaries
- excessive test output
- unrelated project information

The report should stay short, but must not omit facts needed for the next implementation decision.
