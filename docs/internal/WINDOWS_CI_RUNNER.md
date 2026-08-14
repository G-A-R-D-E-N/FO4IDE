# Registering a Windows self-hosted runner (the gated `windows-tests` leg)

The `branch-validation` workflow's Windows integration suite (`FO4RecordEditor.Tests`, the WPF
project) **only runs on a Windows self-hosted runner**. There is no Windows runner in the org today
(Linux `debian-wine-msvc` is a Debian box; Wine cannot reliably host the WPF testhost -- vstest
hangs under Wine, process-spawning tests stack-overflow, and Mutagen static-state tests spin). So
this job stays **skipped** until two things exist, in this order:

1. a Windows self-hosted runner registered with the standard labels, and
2. the `RUN_WINDOWS_TESTS` variable set to `true`.

Everything below is the complete checklist; nothing else is needed on the machine.

## Prerequisites on the machine

- **Windows 10/11 x64** (or Server 2019+). Nothing newer or older.
- **No VC++ redistributable, no .NET, no Node install required.** .NET 9/10 on Windows needs only
  the Universal C Runtime, which is in-box since Windows 10; WPF's native DLLs ship inside the
  runtime; the runner software bundles its own .NET 8; the workflow's `setup-dotnet`/`setup-node`
  steps install and cache the SDKs it needs.
- **Network access on the first run** (and disk for ~2 GB of cached SDKs): checkout, `setup-dotnet`
  (SDK 9.0.x for the `net9.0-windows` testhost's WindowsDesktop runtime, 10.0.x as the build SDK),
  `npm ci`, NuGet restore. All cached after the first run.
- If the machine has Fallout 4 installed, the archive/codec fixture tests can run too -- see
  `FO4RE_TEST_DATA` below. Without it they skip cleanly.

## Step 1 -- register the runner

GitHub generates the exact commands with a short-lived token; use that rather than typing a token
from memory:

1. **Org Settings → Actions → Runners → New runner → New self-hosted runner → Windows → x64.**
2. Copy the two commands it shows onto the machine (it downloads the same
   `actions-runner-win-x64-<ver>.zip` from the `actions/runner` releases either way).
3. In the extracted folder:

   ```powershell
   .\config.cmd --url https://github.com/PRISMA-USER-INTERFACE-FRAMEWORK --token <TOKEN_FROM_THE_PAGE>
   .\run.cmd
   ```

   The **default labels are already `self-hosted, Windows, X64`** -- exactly what
   `branch-validation.yml` targets. Do not add custom labels; do not rename the runner group
   (keep `Default`).

4. Leave it running in a terminal, or install it as a service so it survives reboots:

   ```powershell
   .\svc.ps1 install
   .\svc.ps1 start
   ```

5. Confirm it shows as **Idle** on the same Settings → Runners page.

**Troubleshooting:** a job stuck `queued` for minutes means no runner matched the labels -- check
the Runners page and that the labels are exactly `self-hosted, Windows, X64`.

## Step 2 -- enable the gate

Set the repository variable (Settings → Secrets and variables → Actions → **Variables**):

```
RUN_WINDOWS_TESTS = true
```

**Order matters.** The `windows-tests` job is gated with a job-level `if: vars.RUN_WINDOWS_TESTS == 'true'`.
If the variable is set while no Windows runner exists, every push queues the job for its full
50-minute timeout and red-fails. Runner first, variable second.

Once both are in place, the next push runs the full WPF suite on your hardware, zero cloud minutes.

## Step 3 (optional) -- point the fixture tests at a real Data folder

The archive/codec fixture tests (`DdsCodec`, `Ba2*`, `Bgem`, `PrecombinePlan`, `TypeScopedIndex`,
...) skip whenever `TestDataRoots` finds no Fallout 4 Data folder. To make them really execute,
set a second repository variable to the runner's Data folder:

```
FO4RE_TEST_DATA = D:\SteamLibrary\steamapps\common\Fallout 4\Data
```

(`TestDataRoots` also auto-detects several known layouts, so on a machine with the game at
one of those paths this step is unnecessary. A wrong path degrades to a clean skip, never a
failure.)

## What the leg runs on a fresh runner

- The full `FO4RecordEditor.Tests` suite (net9.0-windows). Everything that needs something not on a
  fresh box no-ops or is skipped by design:
  - native-tool smoke tests (`NifService`, `AudioService`, `ArchiveService`, `ClaudeCodeMcpE2E`) are
    `[Fact(Skip = ...)]` and never run by default;
  - MO2-instance tests are `Available`-guarded no-ops;
  - fixture tests skip without `FO4RE_TEST_DATA`;
  - Windows-only tests (`ArchiveWindowsAclReviewTests`, `ProcessRunnerTests`) genuinely run -- real
    Windows is what they were written for.
- The Linux CI (portable suite + compile gate) already covers everything that can run there; this
  leg is the WPF-execution proof.

## Branch protection: never require the gated leg

A job skipped by a job-level `if:` still creates a check run, and a **required** check with a
`skipped` conclusion blocks every PR at "Expected -- Waiting for status to be reported" forever.
If branch protection is ever enabled on this repo, require only `portable-core-and-web` and
`windows-compile` (both always run and report a real conclusion). Reconsider `windows-tests` only
after the runner is stable with the gate on for a while.
