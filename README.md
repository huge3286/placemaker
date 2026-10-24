# citymaker

An independent experimental Cities: Skylines II mod, forked from
[Placemaker](https://github.com/jackwrichards/placemaker) at `f851771`.
Select an area, preview a procedural layout, then build roads, parking, parks and other places.

This is a local development project with no Paradox Mods listing. It does not need one to run.
This baseline contains only the independent citymaker identity and a simplified Chinese localization patch. Planner, preview and build algorithms are unchanged from upstream f851771.

The simplified Chinese patch covers all 7,729 English localization keys in 60 locale files.
Translations started with Google Translate; common UI terms, settings, warnings and Outgrow terminology
have manual overrides. Longer descriptions are a first draft and still need review during play.
Placeholder and markup checks cover every translated entry.

Baseline branch: `codex/citymaker-baseline`. Previous CPU/GPU performance experiments are excluded.

## Identity

| Item | Value |
| --- | --- |
| Display name and assembly | `citymaker`, `citymaker.dll` |
| C# namespace | `Citymaker` |
| UI module and binding group | `citymaker` |
| Local installation | `<CS2 user data>/Mods/citymaker/` |
| Settings | `<CS2 user data>/ModsSettings/citymaker/` |
| Recipes and development data | `<CS2 user data>/ModsData/citymaker/` |
| Log | `<CS2 user data>/Logs/citymaker.log` |

This fork has its own type, prefab, binding, settings and data identifiers. It does not migrate
Placemaker's existing saved groups or settings. Disable the original Placemaker when testing citymaker.
Use a copy of a save for development tests.

## Development

Requirements: .NET 9 SDK for offline tests, Node.js 18 or later for the UI, and the installed game's
`Cities2_Data/Managed` directory for references. The game mod itself targets .NET Framework 4.8.

```powershell
cd UI
npm ci
cd ..
dotnet build citymaker.csproj -c Release -p:Deploy=false -p:ManagedPath="<game>/Cities2_Data/Managed"
dotnet test Tests/Citymaker.Tests -c Release -p:ManagedPath="<game>/Cities2_Data/Managed"
```

The UI can also be built directly with the local Node.js runtime and webpack.
For a local mod folder, combine the build's `citymaker.dll`, `0Harmony.dll`, `0Harmony-LICENSE.txt`,
`LICENSE`, `Lang/`, `Recipes/` and the contents of `UI/build/` (including `citymaker.mjs`,
`citymaker.css` and `images/`). A DLL on its own is not the complete mod.

The documents under `docs/` were inherited from upstream and may still use its old project name
and the original developer's machine paths. The README and `AGENTS.md` describe this fork.

## Credits and license

Original project: **Placemaker**, by **jackwrichards / ockidj** and contributors.
Includes code originating in [Parking Lot Tool](https://github.com/KruemelGames/ParkingLotTool)
by KruemelGames, [ParkManager](https://github.com/tomkpunkt/ParkManager) by tomkpunkt,
and Grid Road Generator by Ti4goc. See source notices and upstream history.

GPL-3.0; see [LICENSE](LICENSE). Harmony is MIT; see `Library/Harmony/LICENSE`.
