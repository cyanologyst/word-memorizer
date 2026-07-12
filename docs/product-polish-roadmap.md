# Word Review Product Polish Roadmap

## Product Goal

Transform Word Review Reminder from a capable vocabulary utility into a cohesive, dependable daily learning product that makes the next useful action obvious, supports focused keyboard-first review with minimal friction, and turns local review history into understandable progress without sacrificing native Windows behavior, data ownership, or performance.

## Measurable Success Criteria

1. **Visual consistency**
   - Equivalent page headers, cards, buttons, inputs, badges, lists, dialogs, and empty states use shared resources or reusable controls.
   - Typography, spacing, radius, border, color, elevation, and motion values come from centralized tokens.
   - No page-specific styling duplicates an equivalent shared pattern.
2. **UX clarity**
   - Every page states one primary purpose and presents one prioritized primary action when an action is appropriate.
   - Due, new, difficult, paused, saved, error, and destructive states are understandable in place.
   - Reversible destructive actions provide undo; irreversible actions require explicit confirmation.
3. **Responsiveness**
   - The app enforces a practical minimum window size and has no clipped controls, headings, values, or primary actions at that size.
   - Layouts are verified at approximately 1280x720, 1366x768, 1440x900, 1920x1080, and the supported minimum.
   - Grids stack or reduce columns intentionally; horizontal scrolling appears only for genuinely tabular content.
4. **Interaction quality**
   - Interactive elements have hover, pressed, focus, disabled, selected, loading, empty, error, and success behavior where applicable.
   - Core review can be configured, completed, and exited by keyboard.
   - Imports, edits, deletions, exports, refreshes, backups, and settings changes provide immediate non-blocking feedback.
5. **Accessibility**
   - Focus order follows visual order and keyboard focus is visible.
   - Icon-only controls have accessible names and tooltips.
   - Status is never communicated by color alone; contrast remains sufficient in dark and high-contrast modes where supported.
   - Motion respects the Windows animation setting.
6. **Engineering quality**
   - UI, domain logic, persistence, navigation, notifications, and analytics calculations have explicit boundaries.
   - Imports, scheduling, review persistence, statistics, and settings retain behavior unless a documented and tested change is made.
   - The solution builds without warnings and tests pass after each implementation phase.
7. **Product usefulness**
   - Dashboard explains what needs attention today and starts a sensible session in no more than two actions.
   - Session setup estimates workload and explains eligibility.
   - Session completion summarizes outcomes, time spent, mastery movement, and a useful next action.
   - Statistics answer learning questions instead of repeating raw dashboard counters.

## Repository And Product Audit

### Architecture Baseline

- **Platform:** C# 12, .NET 8, WinUI 3, Windows App SDK 1.8, packaged and unpackaged Windows distribution paths.
- **Application shape:** One WinUI executable plus a framework-independent `WordReviewReminder.Core` domain/storage project and xUnit tests.
- **Presentation architecture:** NavigationView shell with Frame navigation. Pages are XAML plus substantial code-behind; there is no MVVM layer or dependency-injection container.
- **State:** A process-wide `App.Data` (`AppDataService`) owns settings, wordlists, progress, recent events, achievements, scheduler, backup, and enrichment services.
- **Persistence:** Local JSON wordlists, settings/progress/achievement JSON, enrichment cache, and append-only review JSONL under `%LOCALAPPDATA%\WordReviewReminder`.
- **Scheduling:** `ReviewScheduler` selects enabled due/new words and updates stability, difficulty, due dates, lapses, and response time.
- **Navigation:** NavigationView pages are recreated on selection; page transitions use Composition animations and respect Windows `UISettings.AnimationsEnabled`.
- **Theme:** Dark acrylic shell with coral accent. A small resource set exists, but dimensions, spacing, typography, and semantic states remain partly page-local.
- **Native capabilities:** Acrylic backdrop, custom title bar, toast notifications, overlay reminder window, global hotkey, tray icon, startup registration, taskbar progress, speech synthesis, clipboard prompts, App Installer updates, MSI/MSIX packaging.
- **Tests:** 13 domain/storage tests cover validation, duplicate detection, quiet hours, scheduling, enriched storage, log round trips, and achievements. Session building, statistics, activity aggregation, backup restore, migrations, and settings persistence lack direct coverage.
- **Build:** `dotnet build WordReviewReminder.sln -c Release`; tests use `dotnet test WordReviewReminder.Tests/WordReviewReminder.Tests.csproj -c Release`.

### Verified Runtime Findings

- Fresh x64 build and Release solution build succeed with zero warnings; all 13 tests pass.
- At 686 px wide, Statistics clips the daily-goal value and subtitle; About truncates a section heading; Mistake Lab meanings become ambiguous ellipses.
- At 443 px wide, Wordlists loses its usable information hierarchy and columns collapse into clipped fragments. At 122 px, the window remains resizable and the entire product becomes unusable. No practical minimum size is enforced.
- Review setup is understandable but leaves most of the page empty and does not show an eligibility/time summary or one-click recommended configuration.
- Active review has strong focus and keyboard reveal/scoring hints, but no explicit pause/exit control, no progressive details beyond meaning, and no completion evidence was visible without mutating review history.
- Dashboard is calm and readable, but repeats metrics found in Statistics and does not distinguish due, new, and difficult work or explain why the recommended action is useful.
- Mistake Lab exposes only urgency filtering; it lacks search, sorting, reason text, details access, and meaningful empty/completed states.
- Wordlists has import validation and a useful preview, but actions are a loose toolbar, destructive deletion has no confirmation or undo, search has no result count, and no sort/filter/bulk workflow exists.
- Statistics primarily repeats dashboard counters and static distributions; there is no time range, recall trend, review volume trend, or insufficient-data explanation.
- Activity renders review counts but has no month labels, weekday axis, range selection, quality signal, or keyboard-visible temporal structure.
- Logs has a readable virtualized list and one event filter, but lacks search, date range, wordlist/result filters, grouping, details access, and clear export completion feedback.
- Achievements is the most visually mature page and adapts well to two columns at 686 px. It still needs semantic status text, richer sorting/details, and a final accessibility pass.
- Settings responds well at 686 px and safely confirms restore, but lacks search, reset defaults, appearance/accessibility sections, and unified save feedback.
- About reports the current assembly version in the fresh build and exposes local data/update information, but lacks diagnostics, licenses, and a concise privacy/support summary.
- Accessibility coverage is uneven: review and several icon actions have names, while most page controls and dynamic status regions lack explicit automation metadata. Hard-coded translucent colors create high-contrast risk.
- Performance risks include repeated full JSONL reads, repeated full data refreshes during review, up to 2,000 events retained in memory, page reconstruction on every navigation, and synchronous list filtering on every keystroke.

## Prioritized Roadmap

Status values: `Not started`, `In progress`, `Complete`, `Deferred`.

### P0 - Correctness, Layout, And Accessibility Blockers

#### P0.1 Enforce A Supported Window Contract

- **Problem:** The native window can shrink below any usable width; several pages clip between 443 and 686 px.
- **User impact:** Primary information and actions become unreadable or unreachable.
- **Proposed solution:** Set a practical minimum window size, centralize breakpoints, and make page headers/metric grids/content panels stack before clipping.
- **Likely files/systems:** `MainWindow.xaml.cs`, shared resources, Home, Statistics, Wordlists, Mistake Lab, About.
- **Risk:** Medium; changing window constraints and responsive thresholds can expose new scroll behavior.
- **Acceptance criteria:** No clipping or overlap at the supported minimum and all five target sizes; primary actions remain reachable without horizontal scrolling.
- **Status:** Complete

#### P0.2 Repair Destructive And Failure States

- **Problem:** Word deletion is immediate, several async event handlers have no visible failure state, and import/export/backup feedback is fragmented.
- **User impact:** Users can lose data accidentally or be unsure whether an operation succeeded.
- **Proposed solution:** Add confirmation plus undo for word deletion, reusable operation feedback, disabled/loading states, and actionable error messages.
- **Likely files/systems:** Wordlists, Logs, Settings, `AppDataService`, new feedback service/control.
- **Risk:** Medium; undo must preserve identifiers and review history safely.
- **Acceptance criteria:** Destructive actions are explicit; reversible deletes can be undone; common file operations surface success and failure without crashing.
- **Status:** In progress

#### P0.3 Establish Accessibility Baseline

- **Problem:** Automation names, help text, focus behavior, and high-contrast compatibility are inconsistent.
- **User impact:** Keyboard and assistive-technology users cannot reliably understand or operate every core workflow.
- **Proposed solution:** Audit tab order and names, add labels/tooltips/live regions, replace color-only states, and gate motion through a shared reduced-motion service.
- **Likely files/systems:** Shell, all pages, overlay, shared resources.
- **Risk:** Low to medium; native focus behavior must not be disrupted by custom styling.
- **Acceptance criteria:** Core review is keyboard-completable; icon-only controls are named; focus is visible; state has text/icon support; reduced motion disables nonessential transitions.
- **Status:** In progress

#### P0.4 Add Missing Empty, Loading, And Error Surfaces

- **Problem:** Most pages assume populated local data and successful I/O.
- **User impact:** New, filtered, corrupted, or temporarily unavailable states look blank or broken.
- **Proposed solution:** Shared empty/error/loading patterns with page-specific recovery actions.
- **Likely files/systems:** Shared resources/components and every data page.
- **Risk:** Low.
- **Acceptance criteria:** Dashboard, Review, Mistake Lab, Wordlists, Statistics, Activity, Logs, and Achievements each exercise populated and empty/no-results states; I/O pages exercise errors.
- **Status:** In progress

### P1 - Design System, Shell, And Core Learning

#### P1.1 Complete The Shared Design System

- **Problem:** Existing resources cover color and a few controls but not spacing, typography scale, dimensions, semantic surfaces, responsive widths, or all interaction states.
- **User impact:** Pages feel related but not consistently authored.
- **Proposed solution:** Add centralized semantic tokens and shared styles for page containers, headers, sections, cards, buttons, inputs, chips, status, empty/error/loading states, list rows, shortcut hints, and tooltips.
- **Likely files/systems:** `App.xaml`, new resource dictionaries or lightweight reusable controls.
- **Risk:** Medium; resource changes have broad visual impact.
- **Acceptance criteria:** Equivalent UI uses shared resources; arbitrary styling is materially reduced; dark Fluent identity and coral accent remain intact.
- **Status:** In progress

#### P1.2 Stabilize Shell And Navigation

- **Problem:** Navigation recreates pages, collapsed navigation lacks verified tooltip/focus behavior, and last-page/window state is not restored.
- **User impact:** Context can be lost and navigation feels less dependable than a mature desktop app.
- **Proposed solution:** Central navigation map, selected-page persistence, appropriate page caching, reliable collapsed tooltips, keyboard navigation, and unified page transitions.
- **Likely files/systems:** `MainWindow.xaml`, `MainWindow.xaml.cs`, settings model.
- **Risk:** Medium; caching must not leave stale data or event subscriptions.
- **Acceptance criteria:** Resize/restart retains the appropriate page; collapsed navigation remains understandable; expensive pages are not rebuilt unnecessarily; transitions respect reduced motion.
- **Status:** In progress

#### P1.3 Recommended Review And Compact Session Setup

- **Problem:** Session setup lacks guidance, live eligibility, duration estimate, and a recommended preset.
- **User impact:** Users must understand scheduling details before starting a sensible session.
- **Proposed solution:** Add a recommended-session service/result, live summary, validation, saved presets, no-eligible-words explanation, and a compact responsive configuration layout.
- **Likely files/systems:** Core session planner, Review page, settings persistence, tests.
- **Risk:** High; selection behavior must remain deterministic and compatible with scheduling.
- **Acceptance criteria:** A useful session starts in one action; custom setup shows word count/due/new/difficult/time; invalid and empty eligibility states are explained; planner behavior is tested.
- **Status:** Complete

#### P1.4 Finish The Review Experience

- **Problem:** Focused review lacks explicit pause/exit, progressive details, accidental-score protection, and a proven completion summary.
- **User impact:** Users can lose session context or score before intentionally revealing; learning feedback stops at the final card.
- **Proposed solution:** Disable scoring until reveal, add pause/exit confirmation, progressive context/details, immediate answer feedback, and a completion summary with review-again.
- **Likely files/systems:** Review page, word details, taskbar progress, session records, tests.
- **Risk:** High; must not double-record events or silently alter scheduling.
- **Acceptance criteria:** Full session is keyboard-completable; each action records once; exit is safe; summary reports counts, time, difficult changes, mastery movement, and review-again options.
- **Status:** In progress

#### P1.5 Turn Dashboard Into A Daily Briefing

- **Problem:** Dashboard repeats totals but does not distinguish due/new/difficult work or explain the next action.
- **User impact:** Users see numbers without a clear plan.
- **Proposed solution:** Prioritize recommended review, split actionable workload, show daily-goal meaning, attention needed, and compact recent activity while removing duplicate metrics.
- **Likely files/systems:** Home page, session planner, analytics aggregation.
- **Risk:** Medium; summaries must remain honest with sparse history.
- **Acceptance criteria:** Page answers what to do, why, due load, today progress, and recent change at a glance; new-user state provides one clear start action.
- **Status:** In progress

### P2 - Productive Data Workspaces And Feedback

#### P2.1 Professional Wordlist And Import Workspace

- **Problem:** Library management lacks sort/filter/bulk/context actions, result counts, metadata, safe delete, and a guided import report.
- **User impact:** Managing large or multiple lists is slow and error-prone.
- **Proposed solution:** Responsive master/detail workspace, grouped command bar, sort/filter/result count, multi-select, context menu, import wizard/report, duplicate policy, metadata, export, and undo.
- **Likely files/systems:** Wordlists page, validator/import services, storage schema, docs, tests.
- **Risk:** High; imports and identifiers affect persistent progress.
- **Acceptance criteria:** Valid/invalid/duplicate imports are previewed and summarized; large lists remain responsive; deletion is safe; selected state and actions remain clear at supported sizes.
- **Status:** In progress

#### P2.2 Make Mistake Lab Actionable

- **Problem:** Urgency is coarse and the list lacks reason, sort, search, details, and completed states.
- **User impact:** Users cannot decide which difficult words deserve attention or why.
- **Proposed solution:** Explicit qualification reasons, search, sortable fields, semantic urgency, details, and focused practice controls.
- **Likely files/systems:** Mistake Lab, domain qualification/aggregation, tests.
- **Risk:** Medium; qualification definitions must be documented and stable.
- **Acceptance criteria:** Every row explains inclusion; sorting/filtering/search work together; empty/completed states offer a useful next step; qualification logic is tested.
- **Status:** In progress

#### P2.3 Learning-Focused Statistics And Activity

- **Problem:** Statistics repeat dashboard counters; Activity lacks temporal axes and quality context.
- **User impact:** History does not explain improvement, consistency, or changing difficulty.
- **Proposed solution:** Tested analytics service, time ranges, volume/recall/mastery/difficulty trends, wordlist comparison, accurate calendar axes, and honest insufficient-data messages.
- **Likely files/systems:** Core analytics, Statistics, Activity, tests.
- **Risk:** High; definitions and date boundaries can mislead if incorrect.
- **Acceptance criteria:** Every chart answers a named question; calculations are documented/tested across time zones and sparse data; calendar has month/weekday context and keyboard details.
- **Status:** In progress

#### P2.4 Searchable Review History

- **Problem:** Logs supports only one filter and loads/parses the full JSONL history.
- **User impact:** Finding or correcting a past review becomes difficult as history grows.
- **Proposed solution:** Search, date/type/list/result filters, date grouping, sortable virtualized rows, details access, paged log reads, and precise export feedback.
- **Likely files/systems:** Logs page, `ReviewLogService`, tests.
- **Risk:** Medium; pagination must preserve chronological correctness.
- **Acceptance criteria:** Combined filters and no-results states work; large logs remain responsive; export names the destination and scope.
- **Status:** In progress

#### P2.5 Organize Settings And Support

- **Problem:** Settings is long and lacks search/reset/accessibility grouping; About lacks diagnostics/licenses/support information.
- **User impact:** Advanced options and troubleshooting are hard to discover.
- **Proposed solution:** Logical categories, search, reset defaults, restart indicators, unified save status, appearance/accessibility controls where supported, diagnostics copy, licenses, and privacy summary.
- **Likely files/systems:** Settings, About, settings model/migration, assembly metadata.
- **Risk:** Medium; defaults and migrations must preserve existing users' choices.
- **Acceptance criteria:** Settings are searchable and resettable; changes persist; restart requirements are explicit; About exposes version, data path, privacy, licenses, update state, and support diagnostics.
- **Status:** In progress

### P3 - Optional Delight And Advanced Customization

#### P3.1 Restrained Achievement And Milestone Delight

- **Problem:** Achievement visuals are strong but status/detail/unlock behavior is not fully unified with learning feedback.
- **User impact:** Gamification can feel detached from useful next actions.
- **Proposed solution:** Semantic status, meaningful sort/detail, contextual unlock feedback, and review action from relevant milestones.
- **Likely files/systems:** Achievements, shell unlock host, milestone service.
- **Risk:** Low.
- **Acceptance criteria:** Locked/in-progress/unlocked are clear without color; motion is subtle and reduced-motion aware; achievements never obscure core review actions.
- **Status:** Not started

#### P3.2 Optional Compact, Light, And Personalization Modes

- **Problem:** The product offers one density and fixed dark presentation.
- **User impact:** Some users cannot adapt the interface to their environment or information-density preference.
- **Proposed solution:** Evaluate compact density, light theme, and high-contrast-safe resources after core consistency is complete.
- **Likely files/systems:** Theme resources, settings, all major pages.
- **Risk:** High; multiplies visual QA surface.
- **Acceptance criteria:** Implement only when every major page can be verified across the added modes without contrast or clipping regressions.
- **Status:** Deferred

## Validation Log

- **2026-07-13 baseline:** Fresh x64 Debug launch inspected. Release solution build succeeded with zero warnings. All 13 tests passed. Dashboard, Review setup, active/revealed review, command palette, Mistake Lab, Wordlists, Statistics, Activity, Logs, Achievements, Settings, and About were inspected. Widths of 1920, 1426, 686, 443, and 122 px exposed the responsive findings above.
- **2026-07-13 P0 responsive pass:** Added a DPI-aware native minimum window contract of 760x640. Verified the window cannot shrink below approximately 746x633 client pixels. Dashboard, Statistics, Wordlists, Mistake Lab, and About remain usable at that boundary; Statistics now stacks metrics/insights and Wordlists becomes a vertical master/detail layout.
- **2026-07-13 feedback and safety pass:** Added a native global InfoBar service for informational, success, warning, error, and action feedback. Verified settings save feedback and a persisted delete/undo cycle from 550 to 549 and back to 550 words. Review history remained intact and the original `/dɪˈzɑːlv/` pronunciation was verified after restoration.
- **2026-07-13 accessibility/empty-state pass:** Added more accessible names, polite live regions, reduced-motion handling in Review, reveal-before-rating protection, and shared empty/no-results surfaces for Dashboard wordlists, Mistake Lab, Logs, and Wordlists. Debug x64 build succeeds with zero warnings and all 13 tests pass. Final visual exercise of this sub-pass remains pending because Windows inspection returned `GetCursorPos: Access is denied` on two retries.
- **2026-07-13 shell and session-planning pass:** Centralized common page padding/title/subtitle resources, made destructive Wordlist actions semantically distinct, centralized the navigation map, and persisted the last safe page without attempting to resume an interrupted Review. Added a domain `ReviewSessionPlanner`, due/new/difficult estimates, tiny-list goal capping, a recommended-session action, and live custom-session summaries. Debug x64 build succeeds with zero warnings and all 16 tests pass.
- **2026-07-13 daily-briefing pass:** Dashboard now explains the recommended workload and reason, distinguishes due/new/difficult words, moves streak into a compact status chip, and starts the recommended session in one action. Custom session setup remains available from the overflow menu.
- **2026-07-13 Mistake Lab pass:** Moved difficulty qualification and human-readable reasons into tested domain logic. Added combined search, urgency filtering, sorting by urgency/misses/lapses/last review/alphabetical order, reason text, and double-click details. Debug x64 build succeeds with zero warnings and all 17 tests pass.
- **2026-07-13 Wordlists workspace pass:** Added result counts, part-of-speech filtering, four sort modes, extended multi-selection, compact selected-word actions, contextual details/edit/delete commands, review metadata, list export, reversible list and bulk-word deletion, busy feedback, and an import preview with explicit duplicate handling. Duplicate list IDs can no longer silently overwrite local data. A cold start restored directly to Wordlists remains open and responsive after fixing an initialization-order crash. Debug x64 build succeeds with zero warnings and all 19 tests pass; final responsive visual QA remains pending.
- **2026-07-13 analytics and activity pass:** Added a tested, time-zone-aware analytics domain model for 7/30/90-day ranges, review volume, recall, first versus repeat work, difficult responses, consistency, best day, per-list comparison, and event-derived mastery movement. Statistics now explains those measures and exposes honest no-data states. Activity now provides 4/8/13-week ranges, month and weekday axes, review counts plus recall details, keyboard-focusable days, compact summaries, and horizontal overflow instead of clipping. Both pages survive direct cold starts with no fatal logs. Debug x64 build succeeds with zero warnings and all 22 tests pass; visual width and keyboard QA remain pending.
- **2026-07-13 review-history pass:** Replaced page-level full-file loading with a tested streaming, bounded-memory query API and a paged 2,000-event recent cache. Logs now supports debounced search, result/list/date filters, newest/oldest/alphabetical sorting, date grouping, 100-row pages, response-time context, word details, filtered JSONL export, precise result feedback, and responsive filter wrapping. Malformed JSONL lines are skipped instead of crashing history. The page survives a direct cold start with no fatal logs. Debug x64 build succeeds with zero warnings and all 24 tests pass; final visual and keyboard QA remain pending.
- **2026-07-13 settings and support pass:** Organized Settings into searchable review, notification, Windows/shortcut, audio, word-details/accessibility, and data sections. Added a preserved dictionary-enrichment preference, Windows motion/high-contrast status, no-results state, and confirmed reset-to-defaults workflow without touching learning data. About now includes local-data privacy language, copied support diagnostics, an issue link, responsive cards, and packaged third-party notices. About survives a direct cold start and the notices file is present in output. Debug x64 build succeeds with zero warnings and all 24 tests pass; Settings interaction and minimum-width visual QA remain pending.
- **2026-07-13 review completion pass:** Added a tested retry-only session filter and persisted the most recent custom goal/list/difficulty/timer/focus choices. Active review now guards rapid duplicate scoring, never repeats exhausted candidates to pad a goal, exposes pause/resume and confirmed early exit, excludes paused time from response/summary timing, records partial sessions safely, progressively reveals examples/related words/notes, and ends with recall metrics, mastery movement, retry-missed, another-session, and done actions. Normal startup still refuses to resume an interrupted review. Both setup and a one-word active card survive dedicated runtime launches. Debug x64 build succeeds with zero warnings and all 25 tests pass; interactive keyboard/pause/exit/summary QA remains pending.

## Current Implementation Focus

1. Resume visual QA when Windows inspection is available and exercise Wordlists at normal/minimum widths plus the reveal/scoring guard and empty/no-results states.
2. Finish P0 operation failure handling and the accessibility/high-contrast audit.
3. Finish visual QA for Wordlists, Statistics, Activity, Logs, Settings, and About, then complete review-session exit and summary behavior.
4. Continue replacing page-local values with shared design tokens and reusable styles.
5. Stabilize shell page caching and reduced-motion transitions.
