# CLAUDE.md: Dalviiio DDR Launcher

## Language rules (read first)

- Everything in the project is in English: code, identifiers, comments, UI text, error messages, logs, documentation, commit messages, branch names, test names.
- Claude speaks French only when it talks to the user in the chat.
- Every text written for the project (docs, UI copy, commit messages) goes through the `humanizer` skill first.

## Git identity and attribution (mandatory)

- Commits use only the user's own Git identity. Never change the Git config, never pass `--author`.
- Never sign a commit. No `Co-Authored-By` trailer, no "Generated with" line, no mention of Claude, Anthropic or AI in commits, PRs, code, comments or docs. The local `.claude/settings.json` sets `"includeCoAuthoredBy": false`.

## Project vision

A mod manager and data editor for Dakar Desert Rally (Unreal Engine 4.27, Steam), built in the same style as V Mods Manager (`d:\Projet\V-Mod-Manager`): same stack, same layering, same git workflow.

Features, in the order they ship:

1. Mods: add a mod to the library, enable it, disable it. That is all.
2. Database editor with full CRUD on cars, drivers and co-drivers: create new entries and edit every field of existing ones.
3. Editions (the game's `DakarEdition` categories such as Dakar20, Dakar21, Dakar22, Classics, Snowrunner): add entries to an edition and create a new one (for example Dakar26).
4. Liveries, full CRUD: view a car's exploded view, generate its UV wireframe, export it for painting, import the painted texture and build it into the game.
5. A car's home image (the pictures shown in team and vehicle selection).

The research is in `d:\Projet\DAKAR ULTIMATE` (read it, do not copy its mod menu or its car import). Its findings that matter here:

- The game is at `C:\Program Files (x86)\Steam\steamapps\common\Dakar Desert Rally\`. Paks are unencrypted and Oodle compressed (`oo2core_9_win64.dll`). Mods load from `Dakar2Game/Content/Paks/~mods/` (and `LogicMods/`).
- Data tables live in `Dakar2Game/Plugins/Game/DakarGame/Content/Data/DataTables/`. Cars: `DT_VehicleData`. Teams per edition: `Dakar2xTeams/DT_SubTeamDataDK2x`, `DT_TeamDataDK2x`, `OtherEditions/DT_SubTeamClassics`, `DT_SubTeamSnowrunner`. Drivers: `DT_PilotData` and `TeamMembersList` (`DKPilotContainerData`) inside sub-team rows. Pictures: soft object paths in sub-team rows (`TeamSelectionPicture`, `VehiclePicture`, `VehicleSlotNormal`...).
- A changed table is shipped as a mod pak (`repak pack --version V11 --mount-point ../../../`) whose name sorts after the game paks. Deleting that pak undoes every change. Game files are never modified.
- An FName such as `Abc_12` is stored as `Abc` plus a number: the part without the suffix must be in the NameMap.
- Free team numbers start at 790.
- Each car is one skeletal mesh (`sk_<Car>`) with body, wheels and interior as material slots. Body material `M_Vehicle_ext_A` takes diffuse, Normal, RMA, MDE_Mask, ScratchMask, ImpactMask, ScratchNormal.

## Tech stack

- C# / .NET 8, WPF, MVVM without third-party UI libraries, xUnit. Same layout as V Mods Manager:

```
src/
  DdrLauncher.Core/   # mods library, game detection, safety, pak building, data tables
  DdrLauncher.App/    # WPF UI
tests/
  DdrLauncher.Core.Tests/
```

- NuGet versions pinned with lock files (`RestorePackagesWithLockFile`), warnings as errors.

## Design

- Modern and rounded. Background `#101548`, secondary `#6728D0`, tertiary `#2C77DA`. Small variants of these three are allowed (hover, pressed, surfaces), nothing far from them.
- Ease of use comes first: the common action on each screen is obvious, nothing needs a manual. Use the `ui-ux-pro-max` skill for screens.

## Safety rules

- The manager writes only inside `~mods/`, `LogicMods/` and its own data folder (`%LocalAppData%/DalviiioDDRLauncher/`). It never modifies or deletes a game file.
- It deletes only files it placed itself, checked by hash.
- Mod archives are untrusted: paths are validated against Zip Slip, absolute paths, ADS and reserved names, as in V Mods Manager.
- No write while the game is running.
- The manager never executes mod content.

## Git: branches, versions and commits

Same model as V Mods Manager.

- `main` holds the latest validated version. It moves only when a version branch is merged into it, with the user's explicit approval. Never commit to `main`.
- Work happens on the current version branch (currently `v0.1.0`). A released branch is frozen; the next one is created from `main`.
- After each small completed step: `git add -A`, commit with one plain English sentence saying what changed, `git push`.
- Before each push: `dotnet build` and `dotnet test` pass, `git status` shows no unwanted file. Never force push, never rewrite pushed history.
- If a push fails, stop and tell the user.
- Never commit build output, game files, real mods, archives, secrets.
