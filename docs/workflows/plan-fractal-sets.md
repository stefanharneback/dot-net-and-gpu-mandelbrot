# Plan

- Date: 2026-03-16
- Work item (Feature/Story/Task): Selectable fractal families
- Owner: Codex
- Links: `docs/workflows/feature-requirements-fractal-sets.md`

## Goal
- Generalize the Mandelbrot-only explorer into a multi-set explorer with minimal architectural churn and a clear runtime control surface.

## Scope
- In scope:
  - Add fractal definitions and defaults.
  - Extend compute shaders to multiple formulas.
  - Wire selection into runtime controls, HUDs, titles, and docs.
- Out of scope:
  - User-authored formulas.
  - Interactive Julia constant editing.
  - New automated test suites.

## Assumptions / Dependencies
- OpenGL 4.3 compute shaders remain the execution path.
- Existing render code stays fractal-agnostic because it only consumes the height texture.

## Tasks & Milestones
1. [x] Inspect compute/render/input architecture and confirm the extension point.
2. [x] Add fractal definition/state support and shader-level formula selection.
3. [x] Wire the selector into app controls, reset defaults, HUD state, titles, and docs.
4. [x] Build and smoke-launch the app to catch C# and GLSL startup issues.

## Verification Plan
- Lint: Not configured
- Typecheck: Covered by `dotnet build`
- Tests: Not present in repository
- Manual checks:
  - Launch app and confirm startup succeeds.
  - Confirm console control list includes fractal switching.
  - Confirm HUD exposes the active fractal name/parameter.

## Risks & Mitigations
- Runtime-only shader compilation could hide errors until launch.
  - Mitigation: perform a smoke launch after build and inspect startup logs.
- Non-Mandelbrot formulas could inherit invalid interior shortcuts.
  - Mitigation: gate cardioid/bulb rejection to Mandelbrot only.

## Exit Criteria / Definition of Done
- The explorer can switch between multiple fractal families at runtime.
- Julia and at least three additional families are supported.
- Docs and session artifacts reflect the new control surface.
- Build and startup smoke verification both pass.

## Artifacts to Update
- Requirements
- Session log
- README / architecture notes
