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
