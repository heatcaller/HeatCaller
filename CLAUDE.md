# HeatCaller

A Windows Forms (.NET 10) race-timing and announcing app for RC car clubs, plus a small Node/TypeScript relay that publishes a club's pages to phones. In beta.

## Where things are

| Path | What |
|---|---|
| `HeatCaller/HeatCaller/` | The app (C#, WinForms). `HeatCaller/README.md` describes it. |
| `HeatCaller/HeatCaller.Timing/` | The timing engine and decoders, on their own layer. |
| `HeatCaller/HeatCaller.Tests/` | The app's tests (xUnit). |
| `relay/` | The relay (`src/`, `test/`), hosted on Fly.io. `relay/README.md` describes it. |
| `website/docs/` | The public documentation (MkDocs). Read `website/STYLE.md` fully before writing any of it. |
| `plans/` | Design plans and bug hunt reports, indexed in `plans/README.md`. |

The version is `<Version>` in `HeatCaller/HeatCaller/HeatCaller.csproj` and nowhere else. What changed in each release goes in `website/docs/release-notes.md`.

## Before a change is done

Run `check.cmd` from the repo root. It builds and tests the app and the relay, and a change is not done until it ends with `ALL GREEN`.

- `check.cmd app` runs just the app; `check.cmd relay` just the relay.
- `test-progress.ps1 -Filter <name>` runs the app tests whose names contain `<name>`.
- The full app suite takes about 20 minutes; the relay suite about a minute.

Things that go wrong when running the tests:

- If HeatCaller is open, the build cannot replace `bin\Debug\...\HeatCaller.exe`. Close the app, or build with `--artifacts-path` pointing at a folder **inside the repo** and delete it afterwards.
- Never build or test from a folder outside the repo. Several hundred tests look for the repo root (the folder with `check.cmd`) above the test files, and fail with "Couldn't find the repo root".
- GitHub Actions runs the same `check.cmd` on every push (`.github/workflows/check.yml`).

## Branches, commits and pull requests

- Work on a branch named `claude/<topic>`, never straight on `main`. Open a pull request against `main`; pull requests are merged with a merge commit.
- Commit subjects name the area, then say what is now true in plain words: `Run sheet: racing done under half a minute late is +1 min, not +0`.
- **"Ship it"** means the whole run, without asking at each step: commit, push, open the pull request, merge it, then switch back to `main` and pull.
- Leave the `*Throwaway*.cs` test files, `videos/` and `.claude/` out of commits unless asked.

## How the code is written

- Comments explain why, in plain words, with the date and who asked: `WHY (20 Nov 2026, bug hunt 22): ...`. Keep that style when changing code, and say what the old behaviour did wrong.
- Every fix gets a regression test. Bug hunt tests are named `BugHunt<N>_<Area>Tests.cs` in the app and `bughunt<N>-<area>.test.ts` in the relay.
- Logic that has to be right is kept as pure static functions away from the window (`ProgramNextStep`, `NightPlan`, `GapWords`, `TimeLeftCalls`) so it can be tested without a form. Put new rules there, not in a control.
- Words the user sees are plain English, with no jargon and no internal names. Button and page names in docs are copied from the code character for character.
- A race night runs 6am to 6am, not midnight to midnight.

## Bug hunts

"Run a bug hunt" means: read the source merged since the last hunt (and any uncommitted work), try to refute each finding against the surrounding code before reporting it, and report findings as a table with severity, file and line, and what the room or the director would see. Fixes go on `claude/bug-hunt-<N>` with regression tests. Earlier reports are in `plans/bug-hunt-*.md`; skip what they already fixed.

## Working in this repo

- Searching with a plain recursive `grep` over `HeatCaller/HeatCaller/` is very slow because of `bin/` and `obj/`. Use ripgrep, or `git grep`.
- Python is not installed on this machine.
