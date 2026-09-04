# Editor settings

Everything configured inside the Unity editor rather than in code: the value, the reason, and
what breaks if it drifts.

Kept separate from `doc/code/` because these live in `ProjectSettings/*.asset`, change by
clicking, and usually fail quietly when wrong.

Add an entry when you change something in the editor.

---

## Version control

| Setting | Value | Where |
|---|---|---|
| Mode | Visible Meta Files | Project Settings ▸ Version Control |
| Asset Serialization | Force Text | Project Settings ▸ Editor |

`Visible Meta Files` keeps `.meta` files on disk and in the tree. A `.meta` holds the asset's
GUID and importer settings; lose one and Unity generates a new GUID, breaking every reference
to that asset. `Force Text` makes scenes and assets YAML so git can diff and merge them.

Has drifted once: the project was created on `Unity Version Control` (Plastic). After being
corrected in the editor, Unity wrote the old value back on exit, restored from in-memory state
during a domain reload. If `git status` shows this file modified after a Unity session, check
the value before committing.

---

## External tools

| Setting | Value |
|---|---|
| External Script Editor | `C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\devenv.exe` |

Was pointing at a Visual Studio 2019 install that no longer exists. Symptom is not an error:
Unity generates no `.csproj` and no `.slnx` at all. Both IDE packages
(`com.unity.ide.visualstudio`, `com.unity.ide.rider`) are installed, so VS 2026 should appear
in the dropdown; if not, Browse to the path above.

`.vsconfig` is tracked and declares the `ManagedGame` workload.

---

## Assembly definitions

| Assembly | `noEngineReferences` | References |
|---|---|---|
| `Sim` | true | `Content` |
| `Content` | true | — |
| `Game` | false | `Sim`, `Content` |
| `Game.Tests.EditMode` | false | `Game`, `Sim`, `Content`, test runners |

`noEngineReferences: true` makes `using UnityEngine;` a compile error in the sim rather than a
review comment, which is what lets the sim run headless. Verified able to fail: adding that
using produces `CS0246` from both the editor and the headless build.

Never reference `Game` from `Sim` or `Content`.

---

## Player and quality

Unchanged from the URP template defaults. Record changes here, particularly anything affecting
determinism or the fixed timestep.

---

## Packages

Beyond the URP template defaults: none. `com.unity.test-framework` (1.6.0) came with the
template and backs the EditMode suite.

Removed from the template: `Assets/TutorialInfo`, `Assets/Readme.asset`.

---

## Not editor settings

Listed because this is where people look for them:

| | Lives in |
|---|---|
| System run order | `StreamingAssets/Balance/pipeline.csv` (§4.2) |
| Balance numbers | `StreamingAssets/Balance/*.csv` — never a ScriptableObject (§1.1) |
| Log channel levels | `StreamingAssets/Config/logging.csv` |
| Random seed | saved with the game (§7.1) |
