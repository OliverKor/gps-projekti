# AGENTS.md

## Project Context
- Repository: `gps-projekti`
- Platform: Windows
- Runtime/SDK: .NET 10 (`net10.0`, `net10.0-windows`)
- UI: WPF (`Gps.Ui.Wpf`)
- Core library: `Gps.Core`

## Purpose
This project reads binary GNSS/GPS data from a connected GPS device (UBX stream over serial), decodes location fixes, and displays them in a WPF UI with a live map and fix table.

## Architecture At A Glance
- `src/Gps.Core`: UBX stream parsing, NAV-PVT decoding, fix model, CSV read/write helpers.
- `src/Gps.Ui.Wpf`: live serial session, UI state, map drawing, fix list rendering.
- `tests/Gps.Core.Tests`: parser/decoder/core behavior tests.

## Core Development Rules
- Optimize for readability first: code should be easy for the next developer to understand quickly.
- Keep changes minimal and targeted to the task; avoid unrelated cleanup/refactors.
- Prefer straightforward control flow with guard clauses over clever abstractions.
- Do not introduce new layers, patterns, or generic frameworks unless clearly needed by requirements.
- Use explicit names and intent-revealing methods; avoid unclear abbreviations.
- Keep methods focused and short when practical; one clear responsibility per method.
- Prefer constants for protocol bytes/offsets and important limits; avoid unexplained magic numbers.
- Add short comments only for non-obvious protocol logic (binary offsets/checksum/math), not for obvious code.
- Preserve existing behavior unless change is requested explicitly.

## GPS/Protocol-Specific Rules
- Keep stream parser behavior robust: sync recovery, checksum validation, and noise tolerance must remain correct.
- Maintain NAV-PVT validity handling (date/time validity and fix filtering).
- Preserve deduplication behavior for repeated timestamps unless explicitly changed.
- In hot paths (serial read/parsing), avoid unnecessary allocations or complex LINQ chains.

## UI/Threading Rules
- Keep serial I/O and parsing off the UI thread.
- Keep WPF updates on `Dispatcher`.
- Maintain stable live-session behavior: connect/disconnect lifecycle, status messaging, and deterministic CSV toggle behavior.

## Testing Rules
- Any parser/decoder behavior change requires corresponding unit test updates in `tests/Gps.Core.Tests`.
- Prefer deterministic tests with synthetic UBX frames.
- Validate edge cases: checksum failure recovery, noise between frames, invalid validity flags, duplicate timestamps.
- Run:
  - `dotnet test gps-projekti.sln`
  - `dotnet run --project src/Gps.Ui.Wpf/Gps.Ui.Wpf.csproj` (for manual smoke check when UI/session logic changes)

## Dependency and Scope Rules
- Prefer BCL and existing project code; avoid adding NuGet dependencies unless requested.
- Do not change target frameworks (`net10.0`, `net10.0-windows`) unless explicitly requested.
- If requirements are ambiguous, choose the simpler implementation and document assumptions in PR/task notes.
