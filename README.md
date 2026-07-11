<div align="center">

# WordReviewReminder

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
- Focused review sessions with Know, Review Later, Skip, pronunciation, and details
- Spaced-review progress, streaks, activity history, statistics, and JSONL logs
- 25 illustrated achievements with progress tracking, filters, unlock celebrations, and subtle motion
- Local JSON storage with no account or cloud dependency

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

## Add Wordlists

Import `*.wordlist.json` files from the Wordlists page. The required schema, optional fields, naming rules, and complete example are documented in [docs/wordlist-json-format.md](docs/wordlist-json-format.md).

## Data And Privacy

Settings, review progress, achievements, imported wordlists, and history remain on the local Windows device under the app's local data directory.

---

<div align="center">
  <sub>Built with C#, WinUI 3, .NET 8, and the Windows App SDK.</sub>
</div>
