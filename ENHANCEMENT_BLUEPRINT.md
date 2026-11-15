# Elektra Enhancement Blueprint

**Status:** Proposed product and engineering roadmap
**Scope:** Windows WPF voice assistant in this repository
**Principle:** Deliver improvements in evidence-backed, testable increments; preserve local-first behavior and do not trade user safety for convenience.

## 1. Product direction

Elektra should become a dependable, privacy-conscious desktop voice assistant that is easy to understand and control. Its distinctive plasma-orb interface should remain lightweight and responsive while the application improves speech usability, safe local automation, trustworthy answers, encryption reliability, accessibility, and release quality.

### Product goals

1. **Predictable voice interaction:** clear listening state, useful recovery when speech is misunderstood, and hands-free follow-ups only within an explicit, short interaction window.
2. **Safe desktop actions:** a transparent allowlist for application and folder launches; confirmation for consequential actions; never interpret ordinary answers as arbitrary shell commands.
3. **Useful, attributable answers:** distinguish local answers from provider results, explain uncertainty, and link or cite sources where available.
4. **Privacy by default:** process audio locally, disclose exactly what text is sent to which online provider, minimize retention, and give users straightforward controls.
5. **Trustworthy file protection:** make encryption behavior recoverable and testable, with precise warnings about password loss and the limits of file-level encryption.
6. **Professional operations:** supported runtime, reliable installer/update story, diagnostics suitable for support, and automated regression tests.

### Non-goals

- General-purpose execution of arbitrary spoken PowerShell, CMD, or executable paths.
- Claims of “military-grade” protection, guaranteed recovery, perfect recognition, or leak-free memory without evidence.
- Sending microphone audio to cloud services by default.
- Replacing Windows full-disk encryption or securely erasing every historical copy of a file.

## 2. Current baseline

The project is a .NET 10 LTS WPF application with a tray icon, animated orb, local Vosk recognition, configurable installed Windows speech voice, a bounded utterance channel, fixed application/folder launch targets, provider-based knowledge lookups, SQLite/in-memory caching, and password-derived AES-256-GCM file encryption for five known folders. Nominatim-compatible geocoding can open a resolved place in Google Earth web. The application also supports an `EnableOnlineLookups` local-only mode and has an MSTest project with initial arithmetic, privacy-option, and encryption-path allowlist coverage.

Important constraints informing this plan:

- Keep .NET 10 on current LTS servicing patches and revalidate WPF, Vosk, RNNoise, NAudio, SQLite, and self-contained publishing as dependencies evolve.
- The current public geocoder has limited capacity and usage-policy obligations; it is not an unlimited production service.
- Existing automated tests are an initial foundation, not comprehensive encryption, UI, audio-device, provider, or performance coverage; expand them before claiming release-grade assurance.
- Vosk and audio dependencies include native components. Managed heap counters alone cannot establish whether native memory is growing or identify its owner.
- The current encryption workflow uses PBKDF2-HMAC-SHA-256 and chunked AES-256-GCM. This is a credible cryptographic construction, but does not by itself provide atomic whole-directory transactions, secure erasure, password recovery, or protection from ransomware/other processes running as the same user.

## 3. Prioritized roadmap

### P0 — Release foundation and safety (first release cycle)

#### 3.1 Supported platform and reproducible delivery

**Implemented baseline**
- Migrated the application and test project to .NET 10 LTS, updated Microsoft package families to the current 10.0.15 patch, and verified Debug/Release builds.
- Updated the SQLite native bundle to a patched release and verified the solution has no NuGet advisories.

**Remaining work**
- Continue reviewing package versions; document native runtime files and model licensing/distribution requirements.
- Turn the existing release script into a repeatable, clean publish pipeline with version metadata, artifact checks, and a documented rollback path.
- Define whether releases are portable or installed. Prefer a signed installer/package with an explicit data directory and clean uninstall behavior for supported end users.

**Acceptance criteria**
- Clean restore, build, automated tests, and Release publish pass on a documented Windows build environment.
- Published application starts on a clean Windows profile, reports a missing model clearly, and handles unavailable optional native dependencies without silent degradation.
- Release output includes the correct architecture, model instructions, licenses/notices, and no development-only diagnostics.

#### 3.2 Automated regression and security tests

A solution-registered MSTest project now covers arithmetic evaluation, privacy-mode provider selection, and encryption-folder allowlisting. Expand this foundation and add a CI workflow before broadening high-risk behavior. Prioritize pure logic and service boundaries so tests do not require a microphone, live API, real protected folders, or UI timing.

**Next coverage**
- Command normalization, aliases, intent parsing, yes/no follow-up expiry, wake/listening state transitions, and shutdown intent.
- Path allowlisting and rejection of traversal, reparse-point escapes, unsupported folders, conflicting destinations, and unexpected suffixes.
- Encryption format parsing, wrong password, corrupted/truncated/tampered chunks, empty and multi-chunk files, cancellation, I/O failure, and interrupted operations.
- Cache expiry/bounds, provider timeout/failure/fallback, and location query rate limiting/caching.
- Preferences debounce, concurrent updates, and final persistence during shutdown.

**Acceptance criteria**
- Tests use temporary directories and deterministic fake services; no tests touch a user's actual Pictures/Documents/etc. directories.
- CI builds and executes tests on every change and publishes a Release artifact from a tagged version.

#### 3.3 Encryption workflow hardening

Treat this as a high-impact data-loss feature. Keep the existing cryptography unless review and test evidence support changing it; prioritize transaction safety and transparent user consent.

**Work**
- Introduce an explicit operation plan and preview: number of files, estimated bytes, skipped files, conflicts, and available free space. Revalidate each item immediately before operating because files can change after the preview.
- Define and implement safe interruption/recovery states. Preserve source data until each destination is complete and verified; make retries idempotent and identify leftover temporary files without treating them as valid encrypted files.
- Make whole-directory partial completion explicit in the UI and operation log. Do not promise all-or-nothing behavior unless it is actually implemented for the filesystem and scale involved.
- Ensure output is authenticated before plaintext replacement. Handle I/O errors, cancellation, file locks, disk-full conditions, and races without deleting the only valid copy.
- Review the encrypted-file header/version, nonce uniqueness, authenticated metadata, KDF parameters, and bounds checks. Add format-version migration rules before changing the format.
- Replace any assumption that 600,000 PBKDF2 iterations is universally optimal with a documented security/performance decision backed by compatibility tests and measurements on supported hardware. Benchmark the prompt-to-first-file delay.
- Explain that three failed attempts/cooldown is an interface safeguard, not a cryptographic defense against offline guessing. Consider a configurable, user-visible delay, but do not imply it makes a weak password strong.
- Add a prominent test/restore workflow and recommend a separate backup before bulk operations. Clearly state that the app cannot recover a lost password.
- Audit key and pooled-buffer lifetime/clearing. Minimize secret copies, never log passwords or key material, and avoid claiming managed strings can be reliably erased.

**Acceptance criteria**
- Fault-injection tests demonstrate that crashes, cancellation, and disk-full conditions leave either the original or a complete authenticated output for each file, with detectable and documented partial progress.
- A test matrix covers empty, small, large, nested, locked, conflicting, and tampered files; all unsupported paths are rejected.
- UI clearly distinguishes completed, partially completed, cancelled, incorrect-password, cooldown, and failed operations.

### P1 — Voice experience and everyday reliability

#### 3.4 Explicit conversation state and hands-free follow-ups

**Work**
- Represent the interaction lifecycle as explicit states (idle, wake detected, listening, processing, speaking, awaiting confirmation, stopping, and faulted) rather than allowing separate event handlers to infer state independently.
- Give yes/no prompts a bounded follow-up window that accepts speech without a second wake phrase. Show a visible countdown/state, accept only the expected response, expire safely, and return to normal wake-word listening.
- Add a cancel/stop intent that remains available during speech and confirmation. Keep application exit distinct from Windows shutdown; require an explicit confirmation for exit when an operation is active.
- Prevent stale or duplicated utterances from executing an action after the listening state has changed. Add sequence IDs or equivalent state checks at the recognition-to-command boundary.

**Acceptance criteria**
- Automated tests cover accepted, rejected, late, duplicated, and interrupted yes/no responses.
- UI and audio cues consistently indicate when Elektra is listening, processing, speaking, or waiting for a choice.

#### 3.5 Natural, configurable speech output

**Implemented baseline**
- Added `Assistant:SpeechVoiceName` configuration to select an installed Windows voice by name, with a warning and default-voice fallback when unavailable.

**Remaining work**
- Expose installed voices in a previewable settings selector; do not assume a particular voice exists on every Windows installation.
- Expose rate, volume, and optional pitch/voice choice with safe validated ranges and persistence in user preferences.
- Improve spoken text preparation for abbreviations, URLs, numbers, punctuation, and provider citations without changing the displayed answer.
- Keep synthesis cancellable and non-blocking; release synthesizer resources predictably and prevent overlapping speech from stale answers.

**Acceptance criteria**
- Settings survive restart; unavailable voices fall back with a user-visible explanation.
- Stop interrupts promptly, and a newer answer cannot be spoken after it has been superseded.

#### 3.6 Recognition quality and recovery

**Work**
- Add opt-in diagnostics for recognition confidence, end-of-speech timing, dropped utterances, selected microphone, and recognition initialization failures; do not record raw audio by default.
- Provide microphone level and test-phrase setup, device-loss recovery, and a clear fallback when the selected device is unavailable.
- Measure false wakeups, missed wakeups, command success, and latency on representative devices before changing VAD thresholds or model settings.
- Offer keyboard/menu fallback for common voice actions and a way to repeat or correct a misunderstood command.

**Acceptance criteria**
- Recognition changes are justified by a repeatable test corpus or manual protocol and report regressions as well as improvements.
- Audio callbacks remain short, allocation-light, and independent of UI/network work; performance measurements include sustained speech, not only idle.

### P1 — Trustworthy answers and useful integrations

#### 3.7 Provider routing, source transparency, and failure handling

**Implemented baseline**
- Added `Assistant:EnableOnlineLookups`; when disabled, online knowledge providers are not registered, weather/location lookups are blocked, and the orchestrator bypasses cached online answers. Local arithmetic and clock answers remain available.

**Remaining work**
- Return a structured answer containing text, provider/source, retrieval time, confidence/limitations, and optional source links rather than passing only a string through the pipeline.
- Display a compact source/attribution affordance and provide a “open source” action when the provider supplies a valid URL.
- Distinguish “no concise answer available” from timeout, rate limit, offline mode, malformed response, and provider error. Use bounded retries, cancellation, and per-provider circuit-breaker/backoff behavior where justified.
- Keep user question logging minimized and configurable. Document exactly which providers receive text; provide a local-only mode that disables network providers.
- Add provider health/status diagnostics and test all failure paths with fakes rather than live APIs.

**Acceptance criteria**
- No fabricated success responses on provider failures; users see a useful, non-technical status and logs retain enough sanitized context for diagnosis.
- Source labels and attributions are correct for each provider, including location results.

#### 3.8 Location search and maps

**Work**
- Keep location lookup user-initiated and disclose that place text is sent to the configured geocoder.
- Handle ambiguous matches by showing a short list with name, address, and attribution before opening Google Earth; allow the user to choose, cancel, or refine.
- Make geocoder endpoint/provider configurable with explicit timeout, rate-limit, cache, and attribution behavior. Do not silently rotate among providers or bypass usage policies.
- Add optional copy/open-in-maps actions while keeping Google Earth web as the current simple default. Treat coordinates and URLs as validated data, not command strings.

**Acceptance criteria**
- Tests cover no result, multiple results, malformed coordinates, provider throttling, cache hit, and cancellation.
- The interface warns against submitting sensitive/private addresses to public geocoders.

### P2 — Usability and controlled automation

#### 3.9 Safe launcher and action confirmation

**Work**
- Keep application and folder launch targets allowlisted, configuration-driven only where validation is strict, and independent of arbitrary shell execution.
- Show a confirmation summary for elevated or disruptive actions (for example PowerShell Admin), including target and requested privilege; rely on Windows UAC for actual elevation.
- Add an “action history” view that records action type, time, and success/failure without capturing unrelated conversation text.
- Ensure aliases map to one canonical action and ambiguous recognition asks the user rather than guessing.

**Acceptance criteria**
- Security tests prove spoken strings cannot inject arguments or paths into process launches and cannot invoke arbitrary commands.
- Failed/cancelled launches have accurate user-facing status and sanitized logs.

#### 3.10 Accessible, configurable orb and desktop UI

**Work**
- Add keyboard-accessible controls, UI Automation names, high-contrast and reduced-motion behavior, and scalable text/layout.
- Pair color-based orb states with text/icon/status cues for users with color-vision differences or screen readers.
- Provide a settings window for speech, microphone, privacy, launch-at-startup, and diagnostics with validation and reset-to-default behavior.
- Ensure dragging, tray behavior, display scaling, multi-monitor positioning, and shutdown are robust. Keep the animation optional and pause expensive visual effects when hidden or reduced-motion is enabled.

**Acceptance criteria**
- Core tasks can be performed without voice or a mouse; status is not communicated by color alone.
- UI smoke tests cover show/hide, reopen, settings persistence, and normal shutdown.

### P2 — Performance, observability, and maintainability

#### 3.11 Performance budgets and native-memory diagnostics

**Work**
- Establish repeatable scenarios: cold startup, warmed idle, continuous listening, sustained speech, provider request, orb visible/hidden, and encryption of representative files.
- Track startup-to-ready time, command latency, audio drops, UI frame time, managed allocations/GC, working set/private bytes, and native process memory where suitable tooling allows.
- Use EventPipe/dotnet counters for managed behavior and Windows-native tooling for native Vosk/audio allocations. Compare equivalent warm runs and investigate trends rather than interpreting a single working-set reading as a leak.
- Record baseline and acceptable regression budgets before optimizing; preserve current bounded caches/channels and verify backpressure behavior under load.
- Keep diagnostics opt-in or local, avoid capturing speech content, and provide a one-click sanitized support bundle.

**Acceptance criteria**
- Performance reports state machine, model, workload, duration, warmup, sample count, and limitations.
- A suspected leak is supported by repeated growth after warmup and ownership evidence, not merely high process memory.

#### 3.12 Lifecycle, logging, and fault containment

**Work**
- Document ownership and disposal for audio capture, recognizers/models, denoiser, speech synthesis, windows/tray resources, timers, HTTP clients, cache/database, and background tasks.
- Centralize shutdown cancellation and impose a measured shutdown deadline. Surface cleanup failures through structured logging without blocking the UI indefinitely.
- Replace broad exception swallowing with narrowly classified handling and user-visible outcomes; preserve fallback behavior only when the fallback is understood and logged.
- Use structured event IDs and severity levels, redact secrets and full question text by default, and rotate/limit local logs.
- Ensure dispatcher exceptions are not silently marked handled when the UI may be left in an unsafe state; distinguish recoverable errors from fatal invariant violations.

**Acceptance criteria**
- Repeated start/stop and device-switch tests show stable resource counts and no callbacks after disposal.
- Logs support troubleshooting without passwords, encryption keys, microphone audio, or unnecessary personal query content.

## 4. Recommended delivery sequence

| Release increment | Scope | Exit gate |
|---|---|---|
| 0.1 — Quality foundation | Test project, CI, supported-runtime compatibility spike, threat/data-flow review | Clean build/test/publish; baseline documented |
| 0.2 — Protect user data | Encryption plan/recovery/error-state work and fault-injection tests | No destructive failure path without a tested recovery outcome |
| 0.3 — Voice reliability | Interaction-state model, confirmation window, recognition diagnostics, speech settings | State-machine and device-independent tests pass; usability protocol meets target |
| 0.4 — Answer trust | Structured provider results, source attribution, offline/privacy mode, failure messaging | Provider failure matrix and attribution tests pass |
| 0.5 — Desktop polish | Ambiguous location selection, accessible settings, launcher confirmation, reduced-motion | Accessibility and UI smoke checks pass |
| 1.0 — Distribution | Signed installer/update approach, release support bundle, runtime/model notices | Clean-machine install/upgrade/uninstall and rollback checks pass |

Reorder increments if field evidence or a serious defect changes risk. Do not bundle unrelated refactors into a release increment.

## 5. Cross-cutting engineering standards

- **Security and privacy review:** maintain a concise threat model covering microphone capture, online query disclosure, process launch, local preferences/cache, and encrypted files.
- **Data minimization:** collect only what is needed; make optional diagnostics explicit; provide retention and clear-data controls.
- **Reliability:** every asynchronous workflow supports cancellation, bounded queues/timeouts, and a clear final state.
- **Testability:** isolate clocks, filesystem operations, provider clients, launcher execution, and UI interaction behind small existing-style interfaces only when deterministic tests need them.
- **Compatibility:** validate native architecture, Windows speech voice availability, model version, configuration schema, and user-data migration before release.
- **Documentation:** update README commands/settings/security limitations whenever behavior changes; keep this blueprint aspirational and mark delivered items in release notes rather than implying they already exist.

## 6. Success measures

Set numeric targets after collecting a baseline on representative hardware. Suggested measures:

- Startup-to-ready and wake-to-listening latency.
- Percentage of test utterances correctly routed; false wakeups per hour of representative background audio.
- Stop-speech response latency and UI responsiveness during sustained recognition.
- Provider answer rate, source attribution rate, timeout/error clarity, and location disambiguation success.
- Encryption/decryption completion and recovery outcomes under injected faults; zero silent data-loss cases in the defined test matrix.
- Managed/native memory trend after warmup under repeatable workloads; no unexplained monotonic growth across repeated cycles.
- Release build, test, install, upgrade, and uninstall success on a clean Windows environment.

## 7. Decision log and open questions

Resolve these through implementation discovery, user research, and test evidence before committing to broad changes:

1. Which Windows versions and CPU architectures must be supported?
2. Should Elektra remain fully offline-capable with a local-only mode as a first-class setting?
3. Should location results open Google Earth immediately for unique matches, or always require a confirmation preview?
4. Should encryption support only individual files in the five current folders, or does the user expect a recoverable encrypted archive/backup workflow? These are distinct products and should not be conflated.
5. What log retention, diagnostic opt-in, installer, signing, update, and support expectations apply to the intended audience?
6. Which recognition and startup latency targets are acceptable on the user's actual PC and microphone?

Until these are answered, prefer conservative defaults: no audio recording, no arbitrary command execution, no silent provider switching, no encryption password persistence, and explicit confirmation before destructive or privileged actions.
