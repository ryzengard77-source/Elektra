<!-- Creator: Justin L. Ross | Farmington, Maine. | Remote Location: Iceland | Black Star Research Facility. -->

# Elektra Voice Plasma Assistant

Elektra is a Windows desktop voice assistant built with WPF. It presents a movable plasma-orb window and a system-tray icon, listens for a Vosk wake phrase or a manual listening request, resolves spoken questions through online knowledge providers, displays answers, and reads them aloud using Windows speech synthesis.

> **Runtime note:** The project targets .NET 10 LTS (`net10.0-windows`). Use the .NET 10 SDK and Windows Desktop/WPF targeting support. See [Microsoft's .NET 10 download page](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) and [release support policy](https://learn.microsoft.com/dotnet/core/releases-and-support).

## Contents

- [Features and how to use Elektra](#features-and-how-to-use-elektra)
- [Commands and controls](#commands-and-controls)
- [Opening folders and Windows applications](#opening-folders-and-windows-applications)
- [Encrypting and decrypting protected folders](#encrypting-and-decrypting-protected-folders)
- [Asking questions and web lookups](#asking-questions-and-web-lookups)
- [Vosk speech model setup](#vosk-speech-model-setup)
- [Build and run](#build-and-run)
- [Release publishing](#release-publishing)
- [Packages and assemblies](#packages-and-assemblies)
- [Architecture overview](#architecture-overview)
- [Enhancement blueprint](ENHANCEMENT_BLUEPRINT.md)
- [Troubleshooting](#troubleshooting)
- [Downloads and references](#downloads-and-references)

## Features and how to use Elektra

- **Offline speech recognition:** Vosk runs locally using a model directory on disk. Model discovery and initialization run in the background so model loading does not block the WPF interface. Model inference does not require sending microphone audio to the question-answer APIs. If RNNoise is unavailable, an adaptive energy-based detector adjusts to ambient noise to better capture quieter speech.
- **Wake phrase:** Say **“Elektra”** to begin listening. Vosk's alternate spelling “Electra” is also accepted for recognition robustness. Say **“Stop”** while Elektra is speaking to interrupt playback.
- **Manual listening:** Right-click the orb and choose **Listen Now (Ask Question)**, or double-click the orb. Speak the question after listening starts. The default active-listening timeout is 12 seconds and is configurable.
- **Answers:** Basic arithmetic is evaluated locally; time/weather, Wikipedia, and DuckDuckGo Instant Answer providers handle supported lookups. Answers appear in the response panel and are spoken aloud.
- **Local actions:** Open supported Windows folders and applications, hide or restore the orb, stop speech, clear conversation context, and exit Elektra using recognized phrases.
- **Protected folders:** Encrypt or decrypt files under Pictures, Videos, Documents, Music, or Downloads with a runtime-entered password and authenticated AES-256-GCM file format.
- **Location search:** Type a place name or address into the glass-styled Google Earth HUD, or use a voice command; Elektra uses the configurable OpenStreetMap Nominatim geocoder and opens the result in Google Earth web.
- **Startup greeting:** If `Assets\Elektra.wav` exists beside the application assets, Elektra plays it once before showing the orb. The sound is also included in build and publish output; missing or invalid audio does not prevent startup.
- **Tray controls:** Hide or restore the orb, choose a microphone, configure Windows startup, or exit from the tray menu.
- **Answer controls:** Copy an answer with **Ctrl+C** while the answer panel is active, or dismiss it with **Esc**. The panel also has copy and close buttons.
- **Audio visualization:** The orb changes appearance with listening, thinking, speaking, and security-warning states and responds to microphone energy.

Elektra is a Windows application, not a command-line assistant. Its natural-language voice commands are described below; it does not execute arbitrary spoken shell commands.

## Commands and controls

### Spoken local commands

Say one of these while Elektra is actively listening. Matching is case-insensitive and ignores leading/trailing whitespace.

| Say | Result |
|---|---|
| `hide elektra`, `minimize`, or `go to tray` | Hide the orb to the system tray. |
| `show elektra` or `wake up` | Restore and activate the orb. |
| `dismiss`, `clear`, or `close answer` | Dismiss the answer panel. |
| `stop`, `stop speaking`, or `quiet` | Stop speech synthesis. The word **“Stop”** can also interrupt Elektra while it is speaking. |
| `forget context`, `new topic`, or `reset memory` | Clear conversational context. |
| `exit`, `quit`, `shut down`, or `close Elektra` | Terminate the Elektra application. |
| `open Pictures` (or Videos, Documents, Music, Downloads) | Open that known Windows user folder in File Explorer. |
| `launch Steam`, `open Notepad`, or `open Control Panel` | Launch a supported Windows application. See the supported list below. |
| `open Google Earth` | Open Google Earth web in the default browser. |
| `find Eiffel Tower`, `locate 1600 Pennsylvania Avenue`, or `open Eiffel Tower in Google Earth` | Resolve a place name/address and open its coordinates in Google Earth web. |
| `encrypt Pictures directory` / `decrypt Pictures directory` | Open the security password dialog and process that protected folder. See encryption details below. |

Local actions are handled without an internet lookup, except for explicit location-search commands, which send the place query to the configured geocoding service. The `exit`/`quit` commands terminate Elektra; they do not shut down Windows.

### Mouse, keyboard, and tray

| Control | Action |
|---|---|
| Double-click the orb | Begin listening for a question. |
| **Search Google Earth** button | Open the location HUD; enter a place or address and press **Enter** or **Find in Google Earth**. |
| Right-click the orb | Open the orb menu: listen, stop speaking, choose microphone, hide, or exit. |
| Drag the orb | Move it. Its position is saved in user preferences. |
| **Win+Alt+E** | Toggle between showing and hiding the orb. This hotkey is also shown in the tray menu. |
| **Ctrl+C** | Copy the current answer when the response panel is available. |
| **Esc** | Dismiss the response panel. |
| Double-click the tray icon | Toggle the orb's visibility. |
| Right-click the tray icon | Show/hide, select microphone, toggle **Start with Windows**, or exit. |

Microphone devices are enumerated by Windows/NAudio. The default configured device index is `0`; the selected device can also be changed from either microphone menu.

## Opening folders and Windows applications

Commands are recognized while Elektra is actively listening. Use the wake phrase or start listening manually, then say `open` or `launch` followed by a supported folder or application. Folder phrases also accept forms such as `open the Pictures folder` or `browse the Downloads directory`.

Supported folders are **Pictures**, **Videos**, **Documents**, **Music**, and **Downloads**. Elektra resolves these to the current Windows user's known folders (Downloads is the user's `Downloads` directory) and opens them in File Explorer.

Supported applications and aliases include:

- **Steam:** `Steam`.
- **Halo: The Master Chief Collection:** `Halo`, `Halo path`, or the full game name. Elektra looks for `Halo The Master Chief Collection.url` on the current user's Desktop.
- **Visual Studio IDE:** `Visual Studio`, `Visual Studio IDE`, or `VS`. Elektra checks the latest Visual Studio installation with `vswhere` and then tries `devenv.exe`.
- **Windows tools:** Notepad, PowerShell, PowerShell Admin, CMD / Command Prompt, Control Panel, File Explorer, Windows Terminal, Task Manager, Calculator, Snipping Tool, Device Manager, Services, Event Viewer, Disk Management, Registry Editor, Paint, and Windows Settings.
- **Microsoft Office (if installed):** Word, Excel, PowerPoint, and Outlook.
- **Microsoft Edge:** `Edge` or `Microsoft Edge`.

This is a fixed list of launch targets, not arbitrary command execution. `PowerShell Admin` requests elevation and displays the standard Windows UAC prompt; canceling it is handled as a canceled launch. Elektra does not run spoken shell commands or arbitrary executable paths.

For location lookup, say **“find [place]”**, **“locate [address]”**, **“take me to [place]”**, **“Google Earth [place]”**, or **“open [place] in Google Earth.”** Elektra geocodes the request and opens the returned coordinates in Google Earth web using the default browser; a Google Earth API key is not required. Alternatively, say **“open Google Earth”**: Elektra opens Earth, asks what location to find, and listens for your reply without another wake word. For addresses, say the street number, street, city, and state. Elektra shows and speaks the recognized location, then waits for “yes” before searching; say “no” to repeat/correct it or “cancel” to stop. Numbered street-address searches are limited to US results and must match the requested house number, so it will not silently navigate to a different numbered property; other place searches remain international. The lookup is performed only after confirmation, then Elektra opens the Google Earth coordinate view.

The **Search Google Earth** button opens a type-in HUD for place names and street addresses. Press **Enter** or **Find in Google Earth** to use the same validated geocoding and navigation flow as voice searches; **Esc** or **Cancel** closes the HUD. Search text is sent only when online lookups are enabled. The HUD explains this before each search.

The default geocoder is OpenStreetMap's public Nominatim service, configured by `Assistant:LocationSearchEndpoint`. Results are cached in memory for 24 hours, searches are serialized and limited to at most one Nominatim request per second, and the response displays OpenStreetMap attribution.
Place queries are sent to the configured geocoding endpoint; do not use the public service for private or sensitive addresses. Follow the [Nominatim usage policy](https://operations.osmfoundation.org/policies/nominatim/) and [OpenStreetMap attribution requirements](https://www.openstreetmap.org/copyright). Nominatim's public service has limited capacity and policies that may change; for a larger deployment, use a provider or self-hosted geocoder you control and change the endpoint accordingly.


## Encrypting and decrypting protected folders

Say `encrypt <folder> directory` or `decrypt <folder> directory` for **Pictures**, **Videos**, **Documents**, **Music**, or **Downloads**. The security workflow is restricted to those five known folders; other paths are rejected. The encryption warning turns the orb red and presents a password dialog with a lightning animation. Encryption requires a password of at least eight characters and confirmation; decryption requires the same password used for encryption.

The password is entered at operation time and is not hard-coded or saved by Elektra. Each encrypted file receives a `.elektra` suffix. The file format uses a password-derived 256-bit key (PBKDF2-HMAC-SHA-256, 600,000 iterations) and chunked AES-256-GCM authenticated encryption. Elektra writes a temporary replacement before moving it into place, skips reparse-point files and directories, and refuses to overwrite an existing destination. During decryption, all discovered encrypted files are authenticated before any plaintext replacement is written, so a wrong password does not partially decrypt the folder.

Three consecutive failed decryption attempts trigger a 30-second cooldown. Elektra does not retain a recovery password: losing the password means encrypted data cannot be recovered. Back up important files before using this feature. Replacing and deleting originals is **not secure erasure**; old disk sectors, backups, cloud-sync copies, snapshots, or other copies may remain. This is per-file encryption, not full-disk encryption.

## Asking questions and web lookups

1. Start Elektra and confirm the tray icon is present.
2. Start listening by saying a configured wake phrase, double-clicking the orb, or using **Listen Now (Ask Question)** in its right-click menu.
3. Ask a concise question aloud. Examples:
   - “Who is Ada Lovelace?”
   - “What is the capital of Japan?”
   - “How tall is [person]?”
   - “What is 12 plus 5?”
   - “Tell me about the solar system.”
4. Elektra displays the response and reads it aloud. Use **Ctrl+C** to copy it or **Esc** to dismiss the panel.

### What “web search” means in this application

Elektra does **not** currently open a browser for general question searches, show a list of search-result links, or crawl the general web. Explicit location commands can open a resolved place in Google Earth web. Its `DuckDuckGoProvider` calls DuckDuckGo's Instant Answer API and uses its abstract or direct-answer fields when available.
Its `WikipediaProvider` requests Wikipedia page summaries and selected infobox facts. Basic arithmetic is parsed and evaluated locally; it does not depend on Math.js or an internet connection. The first provider to produce a usable answer wins; unsupported or unavailable results may lead to another provider or a “couldn't find a verified answer” response.

Provider order is registered in `App.xaml.cs`: real-time Waterville time/weather queries are checked first, followed by local arithmetic, Wikipedia, and DuckDuckGo Instant Answer. Arithmetic recognizes digit and spoken-number forms for addition, subtraction, multiplication, and division, including standard operator precedence and parentheses. Wikipedia handles article summaries and supported name-meaning questions (for example, “What does Jennifer mean?”). The other providers are considered for non-empty questions. Since DuckDuckGo's Instant Answer API may have no concise answer for a query, a missing response is not the same as a failed full-web search.

When Elektra asks whether to open a Wikipedia page, answer **“yes”** or **“no”** directly during the follow-up listening window; that reply does not require repeating the wake phrase. Other interactions still require the configured wake phrase or a manual listening action.

Online question text is sent to the relevant provider only when online lookups are enabled and a network lookup is made. Set `Assistant:EnableOnlineLookups` to `false` in the executable's `appsettings.json` to disable DuckDuckGo, Wikipedia, weather, location requests, and cached online answers; local arithmetic and the local clock remain available. Restart Elektra after changing the setting. Do not speak secrets or sensitive personal information. Vosk speech recognition itself uses the local model; network lookups are a separate step.

## Vosk speech model setup

### Recommended download

The default configuration expects the small English US model `vosk-model-small-en-us-0.15`. Download it from the official [Vosk model list](https://alphacephei.com/vosk/models) (the page includes model sizes and licenses). The direct ZIP link is [vosk-model-small-en-us-0.15.zip](https://alphacephei.com/vosk/models/vosk-model-small-en-us-0.15.zip).

### Install next to the executable

Extract the ZIP so the model's `am` and `conf` directories are inside a model directory. For a standard Debug build, the expected layout is:

```text
bin\Debug\net10.0-windows\
  Elektra.exe
  appsettings.json
  Models\
	vosk-model-small-en-us-0.15\
	  am\
	  conf\
	  graph\
	  ivector\
	  README
```

The exact model archive may include additional files. The essential check is that `am` and `conf` are directly inside the directory Vosk loads. The resolver also searches nested model directories, so an extra enclosing model-named folder is accepted, but the normal one-level layout is preferable.

From PowerShell, after downloading the model ZIP, you can extract it into the Debug output like this (adjust the ZIP path if needed):

```powershell
$zip = Join-Path $env:USERPROFILE 'Downloads\vosk-model-small-en-us-0.15.zip'
$models = 'C:\path\to\Elektra\bin\Debug\net10.0-windows\Models'
New-Item -ItemType Directory -Path $models -Force | Out-Null
Expand-Archive -LiteralPath $zip -DestinationPath $models -Force
```

Replace `C:\path\to\Elektra` with the actual project path. For a Release published app, put the model under the published app's `Models` directory beside `Elektra.exe`, or configure another path. The model is external data and is not embedded in the application assembly or single-file executable.

### Configure a different path

Edit the `Assistant` section in `appsettings.json`:

```json
{
  "Assistant": {
	"VoskModelPath": "Models/vosk-model-small-en-us-0.15"
  }
}
```

The app resolves relative paths from the executable directory. If the configured path is not valid, Elektra checks the app directory, `Models` folders beside the executable and in nearby build outputs (including a previous target-framework output), and nested model folders for a directory containing `am` and `conf`. This allows an existing model under `bin\Debug\net9.0-windows\Models` to be found when running the upgraded .NET 10 build. For a permanent setup, keep the model at `Models\vosk-model-small-en-us-0.15` beside the executable or set `VoskModelPath` to its extracted root. An extra enclosing model-named directory is supported. Check that the selected model is compatible with the Vosk API and suits the configured wake-word grammar.

### Other speech settings

The `Assistant` section currently includes:

| Setting | Default | Purpose |
|---|---:|---|
| `VoskModelPath` | `Models/vosk-model-small-en-us-0.15` | Model directory path, relative to the executable directory unless absolute. |
| `WakePhrases` | `elektra` | Wake-word recognizer grammar and phrase matches; “electra” is also accepted as an ASR spelling variant. |
| `ActiveListeningTimeoutSeconds` | `12` | Duration of active question mode. |
| `AudioDeviceNumber` | `0` | Default NAudio microphone device index. |
| `VadProbabilityThreshold` | `0.65` | Voice activity gate threshold for RNNoise or the adaptive energy-based fallback. |
| `VadHangoverMs` | `700` | Speech tail retained after voice activity. |
| `VadPreRollMs` | `300` | Audio retained immediately before detected speech. |
| `SpeechRate` | `1` | Windows speech synthesizer rate. |
| `SpeechVolume` | `95` | Windows speech synthesizer volume (0–100). |
| `SpeechVoiceName` | empty | Exact installed Windows voice name; empty selects Zira when present, then the Windows default. |
| `EnableOnlineLookups` | `true` | Set to `false` to disable external knowledge/weather/geocoding requests and cached online answers; local arithmetic and clock remain available. |
| `CacheTtlHours` | `72` | Knowledge-cache TTL setting. |
| `LocationSearchEndpoint` | `https://nominatim.openstreetmap.org/search` | HTTPS Nominatim-compatible endpoint used for user-initiated place searches. |

## Build and run

### Requirements

- Windows with the **.NET 10 SDK** and Windows Desktop/WPF targeting support.
- Visual Studio with the **.NET desktop development** workload is an alternative to the CLI.
- A microphone and an installed Windows speech voice are needed for interactive use. Configure `Assistant:SpeechVoiceName` to select an installed voice by its exact Windows voice name; when empty, Elektra prefers a voice containing “Zira” when available, otherwise Windows' default voice is used.
- Internet access is needed for DuckDuckGo and Wikipedia lookups; basic arithmetic runs locally. Local Vosk model files are required for speech recognition.

Install/select the SDK from [Microsoft's .NET 10 download page](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). .NET 10 is the current LTS target for this project; review Microsoft's support policy when planning future upgrades.

### CLI commands

Run these commands from the project directory (`C:\path\to\Elektra` in the examples):

```powershell
Set-Location 'C:\path\to\Elektra'

dotnet --info
dotnet restore .\Elektra.sln
dotnet build .\Elektra.sln --configuration Debug
dotnet run --project .\Elektra.csproj --configuration Debug
```

A successful build produces the application under `bin\Debug\net10.0-windows\`. `dotnet run` runs it; it does not launch tests. The solution includes the Windows-targeted `tests\Elektra.Tests` MSTest project. When `Assets\Elektra.wav` is present, it is copied to `bin\Debug\net10.0-windows\Assets\Elektra.wav` and plays once before the orb appears; it is also copied to publish output. Remove the asset to disable the greeting. A playback error is logged and startup continues.

To build only the project:

```powershell
dotnet build .\Elektra.csproj --configuration Debug
dotnet test .\Elektra.sln --configuration Debug
```

Tests use deterministic local inputs and do not require a microphone or online provider. To run only the application behavior tests, use `dotnet test .\tests\Elektra.Tests\Elektra.Tests.csproj --configuration Debug`. The GitHub Actions workflow at `.github\workflows\ci.yml` restores, builds the Release solution, and runs the test project on Windows with .NET 10 for pushes and pull requests.

To rebuild cleanly:

```powershell
dotnet clean .\Elektra.csproj --configuration Debug
dotnet build .\Elektra.csproj --configuration Debug
```

### Build output and assemblies

The primary managed assembly is `Elektra.dll`, launched by the generated `Elektra.exe` app host. The build output also contains `Elektra.deps.json`, `Elektra.runtimeconfig.json`, `appsettings.json`, framework/package assemblies, and native runtime libraries required by dependencies. The Release publish configuration targets `win-x64` and is self-contained, so the .NET runtime does not need to be separately installed on the target PC.

## Release publishing

The repository's `build-release.ps1` script compiles the plasma pixel shader (unless skipped), generates the application icon, publishes a self-contained single-file Windows x64 release, and copies a root-level `Models` directory to the publish output when it exists.

Run from PowerShell:

```powershell
Set-Location 'C:\path\to\Elektra'
.\build-release.ps1
```

Optional script parameters:

```powershell
.\build-release.ps1 -Configuration Release -Runtime win-x64 -OutputDir '.\dist\Elektra-win-x64'
.\build-release.ps1 -SkipShaderCompile
```

The script defaults to `Release`, `win-x64`, and `dist\Elektra-win-x64`. The Windows SDK's `fxc.exe` is required unless `-SkipShaderCompile` is passed. The script looks for the model source at the project-root `Models` directory and copies it into the publish directory. If absent, copy/extract the model to `dist\Elektra-win-x64\Models` beside the published executable.

The underlying publish can also be run directly:

```powershell
dotnet publish .\Elektra.csproj --configuration Release --runtime win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  --output .\dist\Elektra-win-x64
```

## Packages and assemblies

The following are direct NuGet package references in `Elektra.csproj`. They are restored by `dotnet restore`; do not manually copy package DLLs into the project.

| Package | Version | Role | Package page |
|---|---:|---|---|
| `Microsoft.Data.Sqlite` | 10.0.15 | SQLite data access. | [NuGet](https://www.nuget.org/packages/Microsoft.Data.Sqlite/10.0.15) |
| `Microsoft.Extensions.Caching.Memory` | 10.0.15 | In-memory caching. | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Caching.Memory/10.0.15) |
| `Microsoft.Extensions.Hosting` | 10.0.15 | Host, dependency injection, configuration, and background-service lifecycle. | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Hosting/10.0.15) |
| `Microsoft.Extensions.Http.Polly` | 10.0.15 | HTTP resilience/retry policy integration. | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Http.Polly/10.0.15) |
| `NAudio` | 2.2.1 | Windows audio capture and microphone enumeration. | [NuGet](https://www.nuget.org/packages/NAudio/2.2.1) |
| `RNNoise.NET` | 1.0.0 | Managed wrapper for RNNoise audio denoising/VAD. | [NuGet](https://www.nuget.org/packages/RNNoise.NET/1.0.0) |
| `SQLitePCLRaw.bundle_e_sqlite3` | 2.1.14 | Patched SQLite native bundle used by Microsoft.Data.Sqlite. | [NuGet](https://www.nuget.org/packages/SQLitePCLRaw.bundle_e_sqlite3/2.1.14) |
| `System.Speech` | 10.0.15 | Windows text-to-speech synthesis. | [NuGet](https://www.nuget.org/packages/System.Speech/10.0.15) |
| `Vosk` | 0.3.38 | Local speech recognition API. | [NuGet](https://www.nuget.org/packages/Vosk/0.3.38) |

NuGet also restores transitive dependencies. Depending on configuration, the output includes assemblies such as `Vosk.dll`, `RNNoise.NET.dll`, NAudio assemblies, Microsoft.Extensions assemblies, Polly assemblies, and SQLite provider assemblies. Runtime-specific native files can include Vosk (`libvosk.dll` and its native dependencies) and SQLite native libraries.

**RNNoise packaging note:** The current `RNNoise.NET` package restore in this project supplies the managed wrapper but the tested output did not contain the native `rnnoise` library. Elektra catches that native-library initialization failure, logs a warning, and continues with an energy-based voice-activity fallback without RNNoise denoising. To use RNNoise denoising, the compatible native library and its dependencies must be supplied for the target architecture and packaged with the app.

## Architecture overview

- **`App.xaml` / `App.xaml.cs`** — WPF entry point, host configuration, dependency registration, HTTP clients, and startup/shutdown orchestration. Configuration content root is anchored to the executable directory.
- **`UI\MainWindow.xaml` / `.xaml.cs`** — Orb window, answer HUD, visibility/position, keyboard and mouse controls.
- **`UI\Controls\PlasmaSphereControl.xaml` / `.xaml.cs`** — Orb visuals, plasma effect bindings, and orb context menu.
- **`UI\TrayIconManager.cs`** — Windows notification-area icon and tray commands.
- **`Audio\AudioCaptureService.cs`** — Microphone capture, Vosk model/recognizers, wake phrase handling, voice activity gate, and audio resampling.
- **`Audio\VoiceSynthesisService.cs`** — Windows speech synthesis and speaking-state telemetry.
- **`Workers\ElektraBackgroundService.cs`** — Speech-model startup, local voice commands, password prompts, and the question/answer loop.
- **`Platform\WindowsToolLauncher.cs`** — Fixed allowlist of supported folders and Windows applications.
- **`Security\DirectoryEncryptionService.cs` and `UI\PasswordPromptWindow.*`** — Protected-folder file encryption/decryption, password entry, and security warning UI.
- **`Knowledge\QueryOrchestrator.cs` and `Knowledge\Providers\`** — Context-aware provider selection, result caching, and online knowledge lookups. If the persistent SQLite cache is unavailable, the in-memory cache and providers continue to operate.
- **`Configuration\AssistantOptions.cs` / `appsettings.json`** — Assistant defaults and model/audio/speech settings.
- **`Configuration\UserPreferencesService.cs`** — Debounced local preference persistence under `%LOCALAPPDATA%\Elektra`.
- **`UI\Effects\PlasmaOrbEffect.*`** — Shader source and compiled pixel-shader resource.

## Local data and diagnostics

User preferences are stored in `%LOCALAPPDATA%\Elektra\user-preferences.json`. Knowledge results are cached in memory and, when available, in `%LOCALAPPDATA%\Elektra\knowledge.db`; expired records are removed and the persistent cache is limited to 10,000 entries. The in-memory cache is bounded by the application's configured 512-entry size limit. Cache failures are logged and do not prevent online providers from being used.

The application uses .NET logging for startup, audio, provider, cache, and file-operation failures. Provider failure logs include the provider and query length rather than logging the full question. Vosk processes microphone audio locally; online providers receive question text only when a network lookup is made.

## Troubleshooting

### “Speech Model Missing”

- Confirm the model has been extracted; Vosk cannot load the ZIP file directly.
- Confirm the resolved model directory directly contains `am` and `conf` directories.
- Confirm `appsettings.json` is beside the executable and `Assistant:VoskModelPath` points to the intended folder. Relative paths start at the executable directory, not the current shell directory.
- The resolver searches the configured location first, then the executable folder, nearby `Models` folders (including sibling framework build outputs), and a few nested model directories. Avoid leaving several unrelated models in those locations, as the first matching directory may be selected. The selected path is written to the application log.

### “Speech Initialization Failed” or native DLL errors

- The model may be found while another audio dependency fails. Read the logged exception to distinguish Vosk model loading from native audio setup.
- If the message names `rnnoise`, the app can currently continue without RNNoise via energy-based VAD; denoising is unavailable in that mode.
- Confirm the release architecture and native libraries agree (`win-x64` for the supplied Release configuration).
- Confirm a microphone is available and not exclusively locked by another application.

### Questions return no answer

- Check internet access and try a direct factual or arithmetic question.
- DuckDuckGo is queried through its Instant Answer API, not a general results page. It may return no answer for a valid search query.
- Wikipedia answers depend on a matching article/summary; the math provider handles supported arithmetic expressions.
- Inspect application logs for provider timeouts or failures. The configured HTTP clients use short timeouts and retry transient HTTP errors.

### Orb does not appear

- Check whether Elektra is running in the system tray. The user preference `StartHiddenInTray` can start the app hidden.
- Double-click the tray icon or use **Show / Hide Elektra (Win+Alt+E)**.
- Windows may place notification icons in the overflow area.

## Downloads and references

- [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) — required by the current project target; .NET 10 is an LTS release.
- [Visual Studio downloads](https://visualstudio.microsoft.com/downloads/) — install the .NET desktop development workload for WPF.
- [Vosk model catalogue](https://alphacephei.com/vosk/models) — model choices, sizes, and license details.
- [Default English US small Vosk model ZIP](https://alphacephei.com/vosk/models/vosk-model-small-en-us-0.15.zip).
- [Vosk .NET package](https://www.nuget.org/packages/Vosk/0.3.38) and [Vosk API project](https://github.com/alphacep/vosk-api).
- [DuckDuckGo Instant Answer API overview](https://duckduckgo.com/api) — source for concise instant-answer lookups.
- [Wikipedia REST API](https://en.wikipedia.org/api/rest_v1/) and [MediaWiki API](https://www.mediawiki.org/wiki/API:Main_page) — sources used by the Wikipedia provider.
- [NAudio](https://github.com/naudio/NAudio) and [RNNoise](https://github.com/xiph/rnnoise) — audio-related dependencies.

For exact package versions, use the `PackageReference` entries in `Elektra.csproj`; the project file is authoritative if this document and the source ever differ.
