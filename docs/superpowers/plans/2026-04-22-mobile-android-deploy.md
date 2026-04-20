# Mobile Android Deployment Implementation Plan (3b of 3)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the mobile app to the user's real Android device via an EAS-built APK, and document the path from "dev commits" → "APK the MD/sales team actually installs". Covers EAS cloud builds, Google Play Internal Testing track setup, versioning, and the release runbook.

**Architecture:** Expo's **EAS Build** produces Android builds in the cloud (no local Android Studio / JDK / Gradle on the dev machine needed). Three profiles in `eas.json`:
- **development** — dev client with fast refresh, for local iteration (not used for V1 since Expo Go covers dev already).
- **preview** — signed APK, distributed via direct download or Play Store Internal Testing, used for MD + sales team beta testing.
- **production** — signed AAB uploaded to Play Store production track.

Credentials (keystore) are EAS-managed — EAS generates and stores the upload keystore, which Play Store requires to be stable across releases. Zero backend changes.

**Tech Stack:** EAS CLI (`eas-cli`, installed via npx), Google Play Console (browser-based), `app.config.ts` (existing). No new runtime dependencies.

**Builds on:** Plans 1 + 2 + 3a (all merged or merging). Plan 3a branch (`feature/mobile-md-features`) should be merged to master before Plan 3b begins, so deploy builds include the full V1 feature set.

---

## Scope boundaries

- **In scope:** Android only. EAS preview APK distribution. Google Play Internal Testing setup + release runbook.
- **Out of scope (Phase 2):** iOS builds / TestFlight / App Store. Those need an Apple Developer account + Mac-less cert management and should be their own plan (`2026-04-23-mobile-ios-deploy.md` or similar) when the organization decides to invest.
- **Deferred:** OTA updates (Expo Updates). Can be added later without breaking the build pipeline.

## Prerequisites (user actions, not plan tasks)

The user must do these **before** the first EAS build runs. They are documented in `bom-mobile/docs/DEPLOY.md` (created by this plan) — the engineer executing this plan just wires up the configuration; the user handles the accounts.

1. **Expo account** — free, sign up at https://expo.dev (one-time).
2. **Google Play Console account** — $25 one-time registration fee at https://play.google.com/console (required only for Play Store distribution; APK direct download works without it).
3. **EAS CLI** available as `npx eas-cli` (no global install required; the plan uses `npx`).

---

## File structure (created/modified by this plan)

```
bom-mobile/
  app.config.ts                 # MODIFIED — add android block + versionCode from env
  eas.json                      # NEW — 3 build profiles
  docs/
    DEPLOY.md                   # NEW — full deployment runbook
  README.md                     # MODIFIED — pointer to DEPLOY.md + version strategy note
```

No source code changes. No test changes.

---

## Task 1: Android config in app.config.ts

**Files:**
- Modify: `bom-mobile/app.config.ts`

- [ ] **Step 1: Replace file contents**

```ts
import type { ExpoConfig } from "expo/config";

const androidVersionCode = Number(process.env.ANDROID_VERSION_CODE ?? "1");

export default (): ExpoConfig => ({
  name: "FPF Quotations",
  slug: "fpf-quotations",
  version: "0.1.0",
  orientation: "portrait",
  userInterfaceStyle: "light",
  icon: "./assets/icon.png",
  splash: {
    image: "./assets/splash-icon.png",
    resizeMode: "contain",
    backgroundColor: "#ffffff",
  },
  ios: {
    supportsTablet: false,
    bundleIdentifier: "ae.fpf.quotations",
  },
  android: {
    package: "ae.fpf.quotations",
    versionCode: androidVersionCode,
    adaptiveIcon: {
      foregroundImage: "./assets/adaptive-icon.png",
      backgroundColor: "#ffffff",
    },
  },
  plugins: ["expo-router", "expo-secure-store"],
  scheme: "fpfquotations",
  extra: {
    apiBaseUrl: process.env.EXPO_PUBLIC_API_BASE_URL ?? "http://localhost:7300",
    eas: {
      projectId: process.env.EAS_PROJECT_ID ?? "",
    },
  },
  experiments: { typedRoutes: false },
});
```

Notes on the config:
- `android.package = "ae.fpf.quotations"` matches `ios.bundleIdentifier` — same identifier across stores.
- `android.versionCode` is a monotonically increasing integer required by Play Store. It defaults to `1` for the first build; subsequent builds set `ANDROID_VERSION_CODE=2`, `3`, etc. via env.
- `extra.eas.projectId` is populated by `npx eas init` on first run (Task 3).

- [ ] **Step 2: Verify the config parses**

```bash
cd bom-mobile && npx expo config --type prebuild 2>&1 | grep -E '"package"|"versionCode"|"adaptiveIcon"'
```

Expected: three matches showing `"package": "ae.fpf.quotations"`, a numeric `"versionCode"`, and an `"adaptiveIcon"` block.

- [ ] **Step 3: Verify tsc still passes**

```bash
cd bom-mobile && npx tsc --noEmit
```

Expected: no errors.

- [ ] **Step 4: Commit**

```bash
git add bom-mobile/app.config.ts
git commit -m "feat(mobile): add Android package, versionCode, and adaptive icon to app config"
```

---

## Task 2: eas.json with 3 build profiles

**Files:**
- Create: `bom-mobile/eas.json`

- [ ] **Step 1: Create the file**

```json
{
  "cli": {
    "version": ">= 12.0.0",
    "appVersionSource": "local"
  },
  "build": {
    "development": {
      "developmentClient": true,
      "distribution": "internal",
      "android": {
        "buildType": "apk",
        "gradleCommand": ":app:assembleDebug"
      }
    },
    "preview": {
      "distribution": "internal",
      "channel": "preview",
      "android": {
        "buildType": "apk"
      }
    },
    "production": {
      "channel": "production",
      "android": {
        "buildType": "app-bundle"
      }
    }
  },
  "submit": {
    "production": {
      "android": {
        "track": "internal",
        "releaseStatus": "draft"
      }
    }
  }
}
```

Notes:
- `appVersionSource: "local"` means `eas build` reads `android.versionCode` from `app.config.ts` (no EAS server-side auto-increment). Each release bumps the env var.
- **preview** produces an **APK** — installable by direct download / ADB.
- **production** produces an **AAB** — required by Play Store.
- **submit.production.track: "internal"** posts the AAB to the Play Store Internal Testing track by default (not Production). The user promotes it from Internal → Closed → Production in the Play Console when ready.
- `releaseStatus: "draft"` means submitted builds are NOT auto-rolled out; the user reviews and presses "Review release" in Play Console.

- [ ] **Step 2: Verify the file parses**

```bash
cd bom-mobile && node -e "JSON.parse(require('fs').readFileSync('eas.json', 'utf8')); console.log('OK')"
```

Expected: `OK`.

- [ ] **Step 3: Commit**

```bash
git add bom-mobile/eas.json
git commit -m "feat(mobile): add eas.json with development, preview, production profiles"
```

---

## Task 3: Deployment runbook — `docs/DEPLOY.md`

This is the reference doc the user (and any future engineer) follows to build and ship. It is self-contained so no other doc is needed to deploy.

**Files:**
- Create: `bom-mobile/docs/DEPLOY.md`

- [ ] **Step 1: Create the file with the full runbook**

Write the following content verbatim to `bom-mobile/docs/DEPLOY.md`. The outer fence below uses four backticks so the triple-backtick code blocks inside render correctly as nested markdown:

````markdown
# Mobile Deployment — Android

End-to-end runbook for taking the mobile app from a merged commit on `master` to an installable APK in a tester's hand, and from there to the Google Play Store.

## Who should read this

Anyone releasing a new mobile build. Assumes you have `bash`, `git`, and Node 18+. **No Android Studio / JDK install needed** — EAS builds in the cloud.

---

## One-time setup (per dev machine / per person)

### 1. Create an Expo account

Free. Visit https://expo.dev and sign up. One account covers the organization; you can invite collaborators later.

### 2. Log in from the terminal

From `bom-mobile/`:

```bash
cd bom-mobile
npx eas-cli login
```

Enter the credentials from step 1.

### 3. Link this repo to an EAS project

First time only, run:

```bash
npx eas-cli init
```

This prints an `extra.eas.projectId` UUID. **Copy it** — you'll use it in step 4.

### 4. Persist the project ID

Add to your shell profile (e.g. `~/.bashrc` or Windows env vars):

```bash
export EAS_PROJECT_ID="<uuid-from-step-3>"
```

Or prefix each command: `EAS_PROJECT_ID=<uuid> npx eas-cli ...`

### 5. (Optional) Google Play Console account

Only needed for Play Store distribution (preview APKs can be installed directly without Play Console).

- Register at https://play.google.com/console ($25 one-time fee)
- Create a new app:
  - **App name:** FPF Quotations
  - **Default language:** English
  - **App or game:** App
  - **Free or paid:** Free
  - **Declarations:** fill per your org (privacy policy, ads, etc.)
- The **package name** must be `ae.fpf.quotations` — matches `app.config.ts`.

---

## Build flows

### Preview APK (for internal testing on your phone)

Fast path: no Play Store needed.

```bash
cd bom-mobile
ANDROID_VERSION_CODE=1 npx eas-cli build --profile preview --platform android
```

- Takes ~15–20 minutes (EAS cloud build).
- EAS will ask about keystore on first run — choose **"Generate new keystore"** (EAS-managed, recommended).
- At the end you get a download URL. Open it on your Android phone to install, or scan the QR from EAS's page.
- First install requires enabling "Install unknown apps" for your browser/file manager in Android settings.

### Development build (alternative to Expo Go)

Needed only if you hit Expo Go's JS engine limitations (rare for this app). Most iteration should continue in Expo Go.

```bash
ANDROID_VERSION_CODE=1 npx eas-cli build --profile development --platform android
```

Installs a dev client that connects to your local Metro bundler like Expo Go does.

### Production AAB (for Play Store submission)

```bash
ANDROID_VERSION_CODE=2 npx eas-cli build --profile production --platform android
```

- **Bump `ANDROID_VERSION_CODE` every production build** — Play Store requires strictly-increasing integers.
- Keep `version` in `app.config.ts` semantic (`"0.1.0"`, `"0.1.1"`, etc.) — Play Store shows this as the user-visible version.
- Artifact is an `.aab` (Android App Bundle).

---

## Play Store submission

Once you have a production AAB from the step above, submit it:

```bash
npx eas-cli submit --profile production --platform android
```

- EAS uploads the AAB to Play Console as a **draft** (configured in `eas.json` → `submit.production.releaseStatus: "draft"`).
- EAS will ask for a Google service account key the first time — follow the EAS prompt (https://docs.expo.dev/submit/android/ has the 5-minute walkthrough for creating one in GCP).
- In the Play Console, open **Testing → Internal testing → Create new release** and select the uploaded AAB. Add release notes. Save → Review release → Start rollout.

### Adding testers

In Play Console → **Testing → Internal testing → Testers**:

1. Create an email list (e.g., "FPF Internal").
2. Add the MD's and sales team's Gmail addresses.
3. Share the opt-in URL with them — they tap it once, then the app shows up as a normal Play Store install.

---

## Versioning strategy

| Change type | `version` | `ANDROID_VERSION_CODE` |
|---|---|---|
| New feature | `0.1.0` → `0.2.0` | `+1` |
| Bug fix | `0.1.0` → `0.1.1` | `+1` |
| Hotfix | `0.1.1` → `0.1.2` | `+1` |

- `ANDROID_VERSION_CODE` **always increments**, never resets.
- `version` is set in `app.config.ts` (`version: "0.2.0"` etc.). Update it before running a production build.
- Play Store tracks each uploaded `versionCode` as a distinct release — duplicates are rejected.

---

## Release checklist

Before a production build:

- [ ] All planned changes are merged to `master`.
- [ ] `npx jest` passes cleanly in `bom-mobile/`.
- [ ] `npx tsc --noEmit` passes.
- [ ] `npx expo-doctor` is 17/17.
- [ ] A preview APK install on a real device has been tested for the new changes.
- [ ] `version` in `app.config.ts` is bumped.
- [ ] `ANDROID_VERSION_CODE` is set one higher than the last production build (check the EAS Build dashboard at https://expo.dev for the previous number).
- [ ] Release notes are drafted (paste into Play Console later).

After the production build:

- [ ] `npx eas-cli submit --profile production --platform android` uploaded the AAB.
- [ ] Play Console Internal Testing release is created with release notes.
- [ ] Testers have received the opt-in link (first time) or notification of the new build (ongoing).
- [ ] Smoke test on at least one real device using the Play Store install (not the preview APK — it's signed differently).

---

## Troubleshooting

**"Project not linked" on first build** — you skipped `npx eas-cli init`. Run it, then retry.

**"Invalid keystore"** — if you ever switched from EAS-managed to user-managed keystore or lost the keystore, Play Store rejects builds signed with a new one. **Never delete the EAS-managed keystore.** Export it from the EAS dashboard if you need a backup.

**"Version code XX is already used"** — bump `ANDROID_VERSION_CODE` higher. Play Store requires strictly-increasing codes even across rejected / discarded builds.

**Build fails with "Error: package.json main field points to..."** — regenerate the Expo cache: `rm -rf node_modules .expo && npm install --legacy-peer-deps`, then rebuild.

**Dev client won't connect to Metro on a preview APK** — preview APKs are standalone; they don't connect to Metro. Use the **development** profile if you need hot-reload.

---

## What this doc does not cover

- iOS builds + TestFlight (separate phase; requires Apple Developer account).
- OTA JS updates via Expo Updates (later optimization).
- Custom keystore generation (use EAS-managed unless the org mandates otherwise).
- F-Droid / alternative app stores.
````

- [ ] **Step 2: Verify the file exists**

```bash
cd bom-mobile && wc -l docs/DEPLOY.md
```

Expected: a line count > 100.

- [ ] **Step 3: Commit**

```bash
git add bom-mobile/docs/DEPLOY.md
git commit -m "docs(mobile): add Android deployment runbook (EAS + Play Store)"
```

---

## Task 4: README pointer + distribution strategy note

**Files:**
- Modify: `bom-mobile/README.md`

- [ ] **Step 1: Replace the "What's implemented" section**

Find the existing section and replace the final "Plan 3b (next)" line with:

```markdown
**Plan 3b (merged):** EAS Android deployment — build profiles, Play Store Internal Testing runbook. See [`docs/DEPLOY.md`](docs/DEPLOY.md).
```

Also append a new section immediately below "What's implemented":

```markdown
## Distribution

**Android V1 (current):** EAS cloud builds → preview APK installed directly on devices, or Play Store Internal Testing track. See [`docs/DEPLOY.md`](docs/DEPLOY.md).

**iOS:** deferred to Phase 2. Requires Apple Developer account and is a separate plan.
```

- [ ] **Step 2: Verify the README compiles as markdown (no broken links)**

```bash
cd bom-mobile && test -f docs/DEPLOY.md && echo "link target exists"
```

Expected: `link target exists`.

- [ ] **Step 3: Commit**

```bash
git add bom-mobile/README.md
git commit -m "docs(mobile): link README to deployment runbook and note distribution strategy"
```

---

## Task 5: Milestone verify + hand-off note

**Files:**
- no code changes

- [ ] **Step 1: Full local-check suite**

```bash
cd bom-mobile
npx tsc --noEmit
npx jest
npx expo-doctor
npx expo config --type prebuild > /dev/null && echo "app.config.ts parses OK"
node -e "JSON.parse(require('fs').readFileSync('eas.json', 'utf8')); console.log('eas.json parses OK')"
```

Expected: all five commands exit 0 with no errors.

- [ ] **Step 2: (Manual — user action) First-time EAS setup**

This step requires the user to run, because it creates a record in the Expo account (can't be automated here without their credentials). The engineer reports this to the user:

> The plan has wired up everything. To produce your first APK, run from `bom-mobile/`:
>
>     npx eas-cli login
>     npx eas-cli init
>     ANDROID_VERSION_CODE=1 npx eas-cli build --profile preview --platform android
>
> Then follow the URL printed at the end to install on your Android phone. Full details in [`docs/DEPLOY.md`](docs/DEPLOY.md).

- [ ] **Step 3: (Manual — user action) First APK install smoke test**

Report the success criteria the user must eyeball after their first preview APK install:

- [ ] APK installs on Android device (one-time "allow unknown sources" prompt).
- [ ] App launches; login screen renders.
- [ ] Login as any valid user works (reaches role home).
- [ ] No obvious rendering difference vs Expo Go during dev.
- [ ] Log out button still present in header.
- [ ] Notifications bell still present.

If all six hold, V1 Android distribution is real.

---

## Milestone

At the end of this plan:

- `app.config.ts` has the Android block required for EAS builds.
- `eas.json` defines dev/preview/production profiles.
- `docs/DEPLOY.md` is a complete runbook covering EAS setup, build commands, Play Store registration, versioning, release checklist, and troubleshooting.
- `npx tsc --noEmit` + `npx jest` + `npx expo-doctor` all pass.
- The user can, on their own schedule, run `npx eas-cli init` + one `build --profile preview` command to get an APK on their phone.

Mobile V1 (Plans 1 + 2 + 3a + 3b merged): the SalesPerson and MD can both use the app on real Android devices. iOS is Phase 2 when the org decides to invest in Apple Developer Program.
