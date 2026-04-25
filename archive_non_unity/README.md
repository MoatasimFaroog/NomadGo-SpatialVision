# Non-Unity Workspace Scope

This repository contains additional web/backend and environment artifacts that are **not** required for Unity Android APK builds.

## Excluded from Unity production build scope

- `client/`
- `server/` (if present)
- `shared/` (if present)
- `package.json`, `package-lock.json`, `tsconfig*.json`, `tailwind.config.ts`, `vite.config.*`
- `attached_assets/`
- `local/`
- duplicate technical folders `git/` and `github/`

These are intentionally excluded from the Unity build workflow and runtime pipeline.

## Unity production scope

- `Assets/`
- `Packages/`
- `ProjectSettings/`
- `.github/workflows/`
