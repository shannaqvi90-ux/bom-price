# FPF Quotations — Mobile (iOS)

React Native + Expo mobile app for SalesPerson and ManagingDirector roles of the BOM & Price Approval system.

## Dev setup

1. Install deps:
   ```bash
   cd bom-mobile
   npm install --legacy-peer-deps
   ```
2. Create your dev env file:
   ```bash
   # bom-mobile/.env.development
   EXPO_PUBLIC_API_BASE_URL=http://<YOUR-LAN-IP>:7300
   ```
   Find your LAN IP on Windows with `ipconfig` (IPv4 Address of the WiFi adapter, e.g. `192.168.1.42`).
3. Start backend from repo root: `dotnet run --project BomPriceApproval.API`.
4. Allow inbound traffic to port 7300 through Windows Firewall on your dev machine's LAN profile.
5. Start Expo:
   ```bash
   npx expo start
   ```
6. Install **Expo Go** on a real iPhone (same WiFi), scan the QR code.

## Tests

```bash
npx jest
```

Jest is split into two projects:
- **node** — pure logic tests (`client.test.ts`, `loginSchema.test.ts`) under `ts-jest + testEnvironment: node`.
- **rn** — component / hook tests (`roleGuard.test.tsx`) under `jest-expo`.

This split exists because `jest-expo`'s polyfills crash `axios-mock-adapter` at import time.

## Shared types

`src/types/api.ts` is a manual copy of `bom-web/src/types/api.ts`. When the web types change, re-copy and re-run `npx tsc --noEmit`. A monorepo migration is a phase-2 option.

## Environments

- `.env.development` — local LAN dev (gitignored; create from the template above)
- `.env.production` — created from `.env.production.example` before EAS production builds

## Install note

`npm install` must be run with `--legacy-peer-deps` because of a transitive peer conflict between `react-dom@19.2.5` and Expo-pinned `react@19.1.0`. This will self-resolve when Expo aligns on React 19.2.

## What's implemented

**Plan 1 (merged):** login, role-based routing, secure-store tokens, axios 401 refresh, profile + logout, placeholder home screens.

**Plan 2 (merged):** SalesPerson flow — requisitions list, create multi-item requisition, detail view with per-item stage indicators and PDF download. Detail is read-only.

**Plan 3a (this work):** MD pending-approvals list, MD approval detail with per-item prices + approve/reject flow, notifications screen with bell badge, SignalR foreground live updates.

**Plan 3b (merged):** EAS Android deployment — build profiles, Play Store Internal Testing runbook. See [`docs/DEPLOY.md`](docs/DEPLOY.md).

## Distribution

**Android V1 (current):** EAS cloud builds → preview APK installed directly on devices, or Play Store Internal Testing track. See [`docs/DEPLOY.md`](docs/DEPLOY.md).

**iOS:** deferred to Phase 2. Requires Apple Developer account and is a separate plan.
