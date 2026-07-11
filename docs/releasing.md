# Releasing Word Review Reminder

Word Review Reminder is distributed as a signed x64 MSIX through a Windows App Installer file. App Installer checks for updates at launch every six hours and also registers a background update check.

## One-time setup

1. Create a long-lived code-signing certificate whose subject is `CN=cyanologyst`.
2. Base64-encode the PFX and add it as the GitHub Actions secret `WINDOWS_CERTIFICATE_BASE64`.
3. Add the PFX password as `WINDOWS_CERTIFICATE_PASSWORD`.

For local development only, create a self-signed certificate:

```powershell
.\scripts\New-DevelopmentCertificate.ps1 -Password "choose-a-private-password"
```

The generated PFX and CER are ignored by Git. Never commit the PFX or its password.

## Build locally

```powershell
.\scripts\Build-Release.ps1 `
  -Version 1.0.0.0 `
  -CertificatePassword "your-private-password"
```

The command creates these files under `artifacts/release`:

- `WordReviewReminder-x64.msix`
- `WordReviewReminder.appinstaller`
- `WordReviewReminder.cer`
- `SHA256SUMS.txt`

Users of a self-signed build must trust `WordReviewReminder.cer` in the local machine's **Trusted People** certificate store before installing. This requires administrator approval. A certificate from a publicly trusted code-signing authority does not require that manual trust step.

## Publish a release

Push a semantic version tag:

```powershell
git tag v1.0.0
git push origin v1.0.0
```

The release workflow tests the app, signs the MSIX with the configured certificate, generates the App Installer feed, and uploads all release assets. Keep the same package identity, publisher, and signing certificate across releases.

The App Installer URL remains stable:

```text
https://github.com/cyanologyst/word-memorizer/releases/latest/download/WordReviewReminder.appinstaller
```

Increment the tag for every release. Windows only installs an update when its four-part package version is higher than the installed version.
