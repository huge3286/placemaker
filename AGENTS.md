# citymaker development

This checkout is the user's independent experimental mod, derived from Placemaker (`f851771`).
The product name and assembly are lowercase `citymaker`; C# types use the `Citymaker` namespace.
Keep the GPL-3.0 license and the upstream/source-mod notices.

## Structure

- `Core/`: planner, geometry and shared contracts; .NET plus Unity.Mathematics only.
- `Game/`: native game ECS systems, build pipeline, preview, bindings and settings.
- `UI/`: React/TypeScript UI; `UI/src/contract.ts` defines the binding contract.
- `Lang/`, `Recipes/`: localization and shipped recipe data.
- `Tests/Citymaker.Tests/`: offline xUnit tests; `Tests/Citymaker.RoadLab/`: real road planners on synthetic sites.
- `citymaker.csproj`: game DLL and UI build.

## Workflow

The user is iterating on a local experimental mod. Complete requested edits and relevant checks.
Building and testing do not imply authorization to install into their game, publish on Paradox Mods,
open an upstream PR or upload logs. Ask only when the next action actually needs user input.
Do not claim an in-game fix or compatibility without game validation.

```powershell
dotnet build citymaker.csproj -c Release -p:Deploy=false -p:ManagedPath="<game>/Cities2_Data/Managed"
dotnet test Tests/Citymaker.Tests -c Release -p:ManagedPath="<game>/Cities2_Data/Managed" --filter "<relevant tests>"
```

The game build accepts `-p:SkipUI=true` for a C# compile check.
Keep installation off unless requested.
Installation requires the DLL plus UI bundle, language files, recipes and dependency/license files.

## Runtime rules

- Baseline: upstream `f851771` with only the citymaker identity and simplified Chinese localization.
- Do not carry over road, terrain, threading, preview-worker or timeout optimizations into this baseline.

- Keep Core engine-free, deterministic and cancellable; no ECS access from workers.
- Continue supporting C# 9 / net48 in Core. No SystemAPI/IJobEntity/aspects requiring generators.
- Use the game's native definition/temporary/apply flow for construction. Keep versioned save formats.
- Preserve identifier stability within citymaker. Do not introduce migrations from Placemaker implicitly.
- citymaker owns `Mods/citymaker`, `ModsSettings/citymaker`, `ModsData/citymaker` and `Logs/citymaker.log`.
- Inherited `docs/` describe engine constraints and design history; old machine paths and project names
  are historical. Check local paths before running inherited game-suite/deploy scripts.
- Use meaningful focused regression tests for behavior changes and inspect the relevant UI for UI changes.

Never stop or overwrite a user-started game. Only run game-suite commands through their queue after
adapting and checking the inherited runner for this machine; do not assume its hardcoded C: paths exist.
