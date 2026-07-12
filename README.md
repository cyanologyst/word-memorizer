<div align="center">

# Word Review Reminder

**A calm Windows 11 vocabulary companion for building durable recall.**

<p>
  <img src="WordReviewReminder/Assets/Achievements/first-review.png" width="112" alt="First Step achievement" />
  <img src="WordReviewReminder/Assets/Achievements/words-550.png" width="112" alt="TOEFL Master achievement" />
  <img src="WordReviewReminder/Assets/Achievements/complete-mastery.png" width="112" alt="Grand Lexicon achievement" />
</p>

</div>

WordReviewReminder is a local-first WinUI 3 desktop app that surfaces vocabulary at useful intervals through native Windows notifications and focused review cards. It ships with a 550-word TOEFL collection and supports additional JSON wordlists.

## Highlights

- Fluent Windows 11 interface with Mica, native navigation, and responsive layouts
- Popup cards, Windows toast notifications, quiet hours, and configurable schedules
- Adaptive review sessions with configurable goals, timed recall, focus mode, and taskbar progress
- Offline-first word details with optional dictionary enrichment, examples, related words, and personal notes
- Mistake Lab, a 13-week activity calendar, streaks, statistics, and inspectable JSONL history
- Global `Ctrl+Alt+R` review shortcut, system tray controls, a mini widget, and optional clipboard quick-add
- 25 illustrated achievements with progress tracking, unlock celebrations, and exportable milestone cards
- Portable backup and restore with local JSON storage and no account or cloud dependency

## Run Locally

Requirements: Windows 11 and the .NET 8 SDK.

```powershell
dotnet build .\WordReviewReminder\WordReviewReminder.csproj -p:Platform=x64
dotnet run --project .\WordReviewReminder\WordReviewReminder.csproj -p:Platform=x64
```

Run the test suite with:

```powershell
dotnet test .\WordReviewReminder.Tests\WordReviewReminder.Tests.csproj -p:Platform=x64
```

## Install And Update

Production releases provide three signed x64 installation paths:

- `WordReviewReminder-Setup-x64.exe` is the recommended installer. It includes Microsoft's official Windows App Runtime prerequisite and provides install, repair, upgrade, and uninstall maintenance.
- `WordReviewReminder-x64.msi` is intended for managed or enterprise deployment where Windows App Runtime 1.8 is already provisioned.
- `WordReviewReminder.appinstaller` provides MSIX installation and automatic Windows update checks at launch and in the background.

The setup and MSI create Start menu launch and uninstall entries and register a standard Apps & Features maintenance experience. The About page also provides a manual update check for App Installer releases.

Release signing, local packaging, GitHub Actions secrets, and version-tag instructions are documented in [docs/releasing.md](docs/releasing.md).

## Add Wordlists

Import `*.wordlist.json` files from the Wordlists page. The required schema, optional fields, naming rules, and complete example are documented in [docs/wordlist-json-format.md](docs/wordlist-json-format.md).

## Data And Privacy

Settings, review progress, achievements, imported wordlists, and history remain on the local Windows device under the app's local data directory.

---

<div align="center">
  <sub>Built with C#, WinUI 3, .NET 8, and the Windows App SDK.</sub>
</div>
