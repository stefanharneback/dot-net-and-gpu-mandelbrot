# Feature Requirements

## Metadata
- Feature ID: FRACTAL-SETS-001
- Title: Selectable fractal families in the 3D explorer
- Date: 2026-03-16
- Owner: Codex
- Status: Implemented

## Goal
Allow the explorer to switch between Mandelbrot, Julia, and additional built-in fractal families without changing the rendering pipeline or requiring a restart.

## User Story
As a user, I want to switch the visualized fractal family while exploring, so that I can compare Mandelbrot, Julia, and other sets in the same real-time viewer.

## In Scope
- Add a built-in fractal selection model with curated defaults.
- Support Mandelbrot, Julia, Burning Ship, Tricorn, and Celtic formulas in the compute shaders.
- Surface the active fractal in controls, HUD state, titles, and docs.

## Out of Scope
- Arbitrary user-authored formulas.
- Live Julia constant editing.
- Per-fractal curated hotspot preset libraries.

## Functional Requirements
1. The app must let the user cycle between built-in fractal families at runtime.
2. Switching fractal family must update the compute shader formula and reset to a sensible default view for that family.
3. The HUD and console controls must show the active fractal family and the selection key.
4. Julia mode must use a curated complex constant so the view is immediately interesting after switching.

## Acceptance Criteria
1. Given the app is running, when the user presses `T`, then the active fractal changes and the view resets to that fractal's default center, zoom, and iteration count.
2. Given the user switches to Julia, when the next frame recomputes, then the GPU uses the Julia formula with the configured complex constant.
3. Given the user opens the HUD or console controls, when the feature is present, then the current fractal family is visible in the UI and docs.

## Edge Cases and Failure Modes
- Mandelbrot-only shortcuts must not be applied to non-Mandelbrot formulas.
- The shared compute path must keep working in both FP32 and FP64 modes.
- Shader changes must still launch cleanly at runtime because GLSL compilation is only validated on app startup.

## Data Model Impact
- New tables/fields: None
- Migration required: No
- Backfill required: No

## API/Interface Impact
- Endpoints changed or added: None
- Request/response schema changes: None

## Validation and Security
- Input validation rules: Fractal selection is limited to built-in definitions.
- Auth rules: None
- Ownership/authorization rules: None

## Testing Plan
- Unit: None in repository.
- Integration: `dotnet build MandelbrotGpu/MandelbrotGpu.csproj`
- Manual scenarios: Launch the app, confirm startup succeeds, and verify the new fractal control is surfaced in the console/HUD.

## Rollback Plan
Revert the fractal-definition, compute-shader, HUD, and docs patches to return to Mandelbrot-only behavior.

## Open Questions
- Should Julia constants become user-adjustable in a future iteration?
- Should `1..6` become curated per-fractal hotspot presets instead of Mandelbrot-centric plane jumps?
