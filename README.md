# Nebula Galaxy

This repository now houses both targets:

- `GalaxyViewer/` — existing Windows WinForms + OpenTK desktop app.
- `web/` — browser build scaffold (Vite + React + WebGL2) that mirrors the same galaxy math and rendering approach.

## Desktop (existing)
- Build/run: `dotnet run --project GalaxyViewer/GalaxyViewer.csproj`

## Web (new)
- Prereqs: Node 18+
- Install deps: `cd web && npm install`
- Dev server: `npm run dev` (open the shown local URL)
- Production build: `npm run build` (outputs to `web/dist`)

## Notes
- Generator math, camera, palette, and shader structure are ported to TypeScript in `web/src/domain` and `web/src/gl`.
- The web UI is a lightweight scaffold (canvas + status). The richer controls (scrubbables, presets, themes) from the desktop app can be added onto this foundation.
- Both apps are isolated: outputs land in `GalaxyViewer/bin` and `web/dist` respectively.
