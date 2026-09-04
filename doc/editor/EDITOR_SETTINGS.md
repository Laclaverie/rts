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

## Logs

Runtime logs go to `Application.persistentDataPath\Logs\rts_<utc>.log`, which on this
machine is:

```
C:\Users\<user>\AppData\LocalLow\Laclaverie\RTS Port\Logs\
```

Newest ten kept. `LogBoot` installs the sinks before the first scene loads and applies
`StreamingAssets/Config/logging.csv`.

The file is opened `FileShare.ReadWrite` so a reader can tail it while the game runs.

Columns are seconds since start, in-game day, level, channel. **The day is stamped by
`ReplayRun.AdvanceDay`**, the only place a day advances. It went unset for three phases and
every line read `Day 0` — harmless while only the boot channel was emitting, and exactly the
kind of thing nobody notices until they are reading a log to answer a question. Lines written
before the first day boundary still read `Day 0`, which is correct rather than missing:
nothing has happened yet.

**The two sinks have different thresholds, on purpose.** The file takes whatever
`logging.csv` allows and ships with `Pipeline` and `Commands` at `Debug`: every system as it
runs with what it emitted, and every command as it is queued, applied or refused. The Unity
console has its own floor, set in `LogBoot` at `Warn`, and drops everything below it.

They answer different questions. The file is the record of what the engine did; the console is
for noticing that something is wrong while the editor happens to be open. A console carrying
every day boundary is a console nobody reads, and a real warning scrolls past unseen.

To hunt something in the console, construct the sink with a lower floor —
`new UnityConsoleLogSink(LogLevel.Debug)` — rather than turning channels down in
`logging.csv`, which would cost the file its record too.

Unity's own `Editor.log` and `Player.log` are separate and still exist; ours is the filtered
one.

---

## Player and quality

| Setting | Value | Was |
|---|---|---|
| Company name | `Laclaverie` | `DefaultCompany` |
| Product name | `RTS Port` | `RTS_Sandbox` |
| Application identifier | `com.laclaverie.rtsport` | the URP template's, on all three platforms |

`RTS_Sandbox` was the name Unity Hub generated when it created the project, and the identifier
was still `com.Unity-Technologies.com.unity.template.urp-blank`. The product name is the
working title from `GDD.md`.

**Changing the product name moves `persistentDataPath`,** so logs written before this are
orphaned under the old folder and can be deleted.

Set through the `PlayerSettings` API rather than by editing the YAML. Worth knowing if you do
it again: assigning the properties is not enough, because a domain reload re-reads the file
and discards the change. The assignment and `File/Save Project` have to happen in the same
call.

Otherwise unchanged from the URP template defaults. Record changes here, particularly anything
affecting determinism or the fixed timestep.

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
