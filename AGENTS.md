# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its features and
controls; this file covers how the code is put together and the rules that keep it working.

## What this is

A swarm shooter in the style of Namco's 1979 arcade game Galaxian, in C# on .NET 10 and Avalonia 12, drawn through
Avalonia's `DrawingContext`. One self-contained executable per platform (Windows, macOS and Linux, x64 and ARM). Everything in it is
original: the graphics are drawn in code, and the sounds and tunes are synthesised by `tools/make_sounds.py`.

## Commands

```bash
dotnet build AVAGalaxian.slnx                     # the SDK version is in global.json (10.0.400 or later)
dotnet test AVAGalaxian.slnx                      # every test runs headless: no window opens
dotnet run --project src/AVAGalaxian              # play it; --no-audio for silence
GALAXIAN_SHOTS=$PWD/Images dotnet test tests/AVAGalaxian.Tests --filter ScreenshotTests   # refresh the README's pictures
GALAXIAN_PROBE=1 dotnet test tests/AVAGalaxian.Tests --filter PilotProbe   # how the computer pilot does ("lazy" for one that sits and fires)
python3 tools/make_sounds.py                      # rebuild src/AVAGalaxian/Sounds (standard-library Python only)
```

`Directory.Build.props` sets `TreatWarningsAsErrors`, nullable reference types and implicit usings for every
project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVAGalaxian/Engine/`: the game, with no Avalonia drawing in it.
  - `GameEngine.cs` runs the states (title, demonstration, play, pause, game over, initials, high scores) and the
    fixed-step loop.
  - `GameEngine.Play.cs` plays a wave: the swarm's formation, the dives and escorts, the missile and the bullets.
  - `Entities.cs` has `Controls`, the screen measures (`Field`), the fighter, missile, bullets and aliens.
  - `ShipPilot.cs` is the computer pilot, used by the attract mode and the tests.
  - `InputHandler.cs` (keys) and `ControllerManager.cs` (SDL2 gamepads) produce `Controls`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVAGalaxian/Views/`: `GameCanvas` (with `GameCanvas.Draw.cs`) draws the 1024x768 frame and runs the 60 Hz
  timer. Ctrl+S saves a screenshot to the Pictures folder.
- `src/AVAGalaxian/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `src/AVAGalaxian/Sounds/`: the generated WAV files, copied next to the executable.
- `tests/AVAGalaxian.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`. `GameplayTests.cs` holds most tests;
  `PilotProbe.cs` and `ScreenshotTests.cs` are switched on by the variables above.

## Rules that keep it working

- **Fixed steps.** `GameEngine.Update` runs the game in fixed 1/60 s steps (`Field.Step`) from an accumulator,
  independent of the frame rate, so tests can play whole waves deterministically from a seed. Draw random numbers
  from the engine's seeded `_rng` (a `Random(seed)`), never from `Random.Shared`.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the engine and draws it.
- **Input goes through `InputHandler`.** The fighter only moves left and right: `InputHandler` always sends a `Dy`
  of 0, and Up (like Space, Z or X) fires. Test new controls through `InputHandler`, not by building `Controls`
  directly.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up.
- **Tests never touch the player's files.** High scores live in the app-data folder (`AVAGalaxian`); tests load the
  table from a fresh folder under the system temp folder and run silent (`Silent = true`).

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If the swarm's behaviour changes, run the pilot probe to see how the computer pilot copes.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features, controls) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push and pull request. It then publishes self-contained
single-file builds for win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64. Every run keeps the builds as
workflow artifacts. Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a
proper release. After a release it asks the portfolio site to rebuild, if the `SITE_REFRESH_TOKEN` secret is set.
