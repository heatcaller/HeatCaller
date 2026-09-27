# HeatCaller.Tests

Automated tests for the HeatCaller app (xUnit, .NET 8, Windows).

## Running them

From the solution folder (`C:\Users\asher\Downloads\HeatCaller\HeatCaller`):

```powershell
dotnet test
```

Or in Visual Studio: **Test → Run All Tests**. The first run downloads the test packages from NuGet.

## The one rule: tests never touch the club's real data

Every file the app reads or writes lives under `AppPaths.Root` (`%AppData%\HeatCaller`) or `AppPaths.LocalRoot` (`%LocalAppData%\HeatCaller`). Under test, `TestSetup` redirects both to `%TEMP%\HeatCaller.Tests\<run>` before any test runs, and `AppPaths.RedirectForTests` refuses any folder that overlaps the real ones.

- A test that reads or writes files takes a `TempAppData` field and disposes it. It gets an empty folder of its own, which is deleted afterwards.
- Tests run one at a time (`DisableTestParallelization` in `TestSetup.cs`), because the redirect is one folder for the whole process.
- **Not covered by the redirect:** the default backup folder and results exports, which go to Documents on purpose. A test of `BackupService.CreateBackup` or a results export must pass its own folder.

## What is here

| File | Covers |
|---|---|
| `AppPathsTests` | The redirect itself: it is in force, it refuses the real folders, and stores write where it points |
| `SafeFileTests` | A locked file: the write says it failed, the old contents survive, no temp file is left, `WriteFailed` names the file |
| `SessionHistoryStoreTests` | Results survive a reload; a result that could not be written returns false and raises nothing (25 Sep audit) |
| `QualifyingWindowTests` | Best-N-consecutive windows, and the start dash never counting |
| `DriverPhotosTests` | The name hash that used to stop the app opening (25 Sep audit) |
| `SoundEffectsTests` | The 26 Sep sounds: defaults, older files taking them in, the jump-start migration, save and load |
| `QualifyingTests` | The full ranking: most laps, best-N consecutive, best 2 of 3, too few laps sorting last, DQ'd heats, ghosts, demo races, other classes, one race day, spare cars, a heat being re-run |
| `MainsTests` | Seeding into A/B/C, the lowest main running first, who moves up (ghosts, spare cars, unknown places, already moved by hand), applying it, and when it is allowed |
| `ResultEditorTests` | Phantom crossings, lap and time penalties, DNF and DQ order, corrections as totals, undo back to the race, preview, the log, what is refused |
| `StoreFailureTests` | A file locked at startup is never written over (race history, program, drivers, championships, classes, race days) and the operator is told; a damaged file is copied aside before any of 14 stores starts afresh |
| `RaceFilingTests` | A finished race: nothing happens unless the history write landed; then the heat is linked, the page is told, and only then does the crash journal go. Demo races, deleted heats, no heat |
| `NoRaceDayTests` | With no race day open, qualifying counts nothing rather than the club's whole history; Seed Mains says why; an open day counts only its own heats (25 Sep audit) |
| `RemoteCommandGateTests` | `/api/command`'s brake: one command at a time, a per-device and an all-devices limit a minute, a busy answer costing nothing, a flood of 300 letting exactly one through |
| `RoundPickerTests` | "Start the next race automatically": tonight's rounds only, nothing with no race day open (and the log says why), class by majority of the drivers on track |
| `AchievementGridTests` | Grid badges (Pole Converted, Charge) count start positions among the real cars, not the ghost-inclusive roster slot |
| `NetworkRulesTests` | The relay token never goes over plain http to a .local/.lan name; check-in stays shut while the saved PINs are locked |
| `PinKdfTests` | Salted, slow PIN hashes and the manager phone's PIN key: vectors shared with the relay's `pinsalt.test.ts`; a request signed with any other PIN, altered, stale or unsigned is refused |
| `RestoredSettingsTests` | A restored backup's new relay, voice relay or mail server address is held until checked; the club's own backup asks nothing; a backup can't bring its own record; the publisher, voice and mailer all wait |
| `CommentaryMarginTests` | The recap's winning margin: a win by thousandths is written in thousandths and never as a tie (rehearsal 1, 27 Sep) |
| `FreezeDiagnosticsTests` | The session log and freeze-dump clean-up keeps the newest files of its own kind only; only `--write-freeze-dump` makes the exe the dump helper, and a bad command line dumps nothing (rehearsal 2, 27 Sep) |
| `LapRejectionTests` | A pass thrown out as too soon is logged with the gap it was judged on - since the previous pass - and the lap time beside it, each saying what it runs from (rehearsal 3, 27 Sep) |
| `ParkedOnLoopTests` | A car sitting on the loop since staging is not called as a jump start; a car placed across the loop and then going early, or driven onto it in the countdown, still is (rehearsal 3, 27 Sep) |
| `RoundIntroTidyTests` | The round intro after the model: shouting put back into normal case (a word of emphasis kept), a wrong heat number replaced with the round's label, and an intro past the word limit cut to whole sentences or dropped (rehearsal 3, 27 Sep) |
| `EasyLapCaptureTests` | Rehearsal 3's EasyLap night played back from `Captures\easylap-rehearsal3.log`: every HID report decodes line for line as it did on the night, a pass split across reports after a stray byte is still read, and the A-Main's passes count the same laps through the race (27 Sep) |

## What comes next

From `claude/project-review-2026-09-26.md`, in order of what a wrong answer costs:

1. ~~Each store under failure~~ — done (`StoreFailureTests`, `RaceFilingTests`)
2. ~~Qualifying with no race day open, a write limit on `/api/command`, the restore marker for mail and relay settings~~ — done (`NoRaceDayTests`, `RemoteCommandGateTests`, `RestoredSettingsTests`)
3. ~~The rest of the 25 Sep audit's "not checked yet" list~~ — done 26 Sep (see the review doc)
4. ~~Salted relay PINs~~ — done 26 Sep with manager phones on the PIN (`PinKdfTests`; relay `test/pinsalt.test.ts`). The relay must be deployed before this laptop build runs (see `claude/relay-deployment.md`)
