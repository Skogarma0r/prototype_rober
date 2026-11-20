# Gameplay modules

Mobile (iPhone 16 Pro, landscape) clone of ROB RUN's core loop: scout as a citizen, put on a mask,
deal with cameras / clerks / alarm panel, loot into a grid inventory, escape the police, sell to a fence.

## Rules for every module

- Unity 6000.6 URP, C#. Code under `Assets/Ashline/Scripts/Runtime/Gameplay/<Module>/`, namespace
  `Ashline.Gameplay` (or `Ashline.Gameplay.<Module>`). Editor-only code under `Scripts/Editor/`.
- No scene or prefab edits: everything is built from code at runtime (like the city and HUD).
- Install yourself without editing `GameBootstrap`:
  ```csharp
  static class MyModule
  {
      [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
      static void Hook() => GameEvents.WorldReady += () => new GameObject("MyModule").AddComponent<MySystem>();
  }
  ```
- Shared contracts live in `Gameplay/Core/` (`GameContext`, `GameEvents`, `PlayerStatus`, `Health`,
  `IDamageable`, `IInteractable`/`Interactables`/`InteractionDriver`, `ShopLayout`/`ShopRegistry`).
  Don't change their existing members; additive changes are fine if you really need them, list them
  in your summary.
- UI: uGUI built in code, legacy font `TouchControls.Font`, reference resolution 2000×920 (match
  height), widgets parented to `GameContext.Hud.Root` (safe area), `FootPanel` or `DrivePanel`.
  Semi-transparent dark/white panels, bold uppercase captions, big touch targets (≥ 140 px).
- Desktop fallbacks for testing in the Editor: keyboard/mouse via `UnityEngine.InputSystem`
  (`Keyboard.current`, `Mouse.current`). Taken keys: WASD/arrows move, Shift run, Space jump/handbrake,
  F enter/exit car, E use, Z/C look, right mouse look.
- Performance budget (whole frame on iPhone): ~1.5M triangles, ~400 draw calls. Characters ≤ 15k tris,
  props are a handful of boxes merged per building with `MeshBuilder`, no per-frame allocations in
  Update loops, physics queries with explicit layer masks / `QueryTriggerInteraction`.
- Check compilation without Unity: `Tools/compile-check.sh <path-to-your-unity-worktree>`.

## HUD layout (landscape, reference 2000×920, offsets from the named corner)

| Area | Owner | Contents |
|---|---|---|
| bottom-left 0..42% width | core | floating move stick (foot) / steering arrows (car) |
| bottom-right (-100,70) (-300,70) | core | JUMP, RUN (foot); GAS/BRK/DRIFT/NOS (car) |
| bottom-right (-200,290) | core | USE (interaction, tap or hold with fill) |
| right side, y 450..700 | combat | AIM (hold/toggle) and FIRE, RELOAD; ammo counter |
| top-right (-110,-150) | core | ENTER / EXIT car |
| top-right (-330,-150) | inventory | BAG button (opens inventory) |
| top-left | heist | money, wanted stars (0..3), role (CITIZEN/CRIMINAL), MASK toggle button |
| top-center | heist | alert banner (camera spotting meter, "ALARM!", "POLICE CALLED") |
| full screen modal | inventory | inventory / loot / fence screens (set `InteractionDriver.Blocked`) |

## Ownership

| Module | Folder | Owns |
|---|---|---|
| Inventory | `Gameplay/Inventory/` | items, grid containers, equipment, loot tables, `LootContainer` (IInteractable), inventory/loot/fence UI, fence NPC spot |
| Interiors | `City/` (+ `Gameplay/Interiors/`) | enterable shop buildings, interiors, props, door openings, `ShopRegistry` filling |
| Heist | `Gameplay/Heist/` | mask/role HUD, `HeistSite` per shop, cameras, alarm panel, clerks & guards, alarm → `GameEvents.RaiseAlarm`, wanted level |
| Combat | `Gameplay/Combat/` | pistol, aiming, hitscan, damage, police dispatch & AI (cars + on foot), arrest/death |
