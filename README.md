# iRacing Spotter Generator

A Windows desktop tool (WPF, .NET 10) for creating custom **spotter voice packs** for [iRacing](https://www.iracing.com/). Manage spotter message text, generate speech using Google Cloud Text-to-Speech or your own recordings, apply radio/squelch effects, and export a ready-to-use pack that iRacing can load directly.

## Features

- **Project-based workflow** – create, save, and reopen spotter pack projects (`.json`) with per-message text, source, voice, and review status.
- **Two audio sources per message**
  - **Google Cloud TTS** – synthesize speech from text using a configurable voice (Neural2, Studio, Wavenet, Polyglot, etc.).
  - **Recording** – record your own voice or import existing WAV files.
- **Takes management** – record multiple takes per message, trim them on an interactive waveform, rate them, and keep every take safely stored (nothing is lost when the recording dialog is closed).
- **Machine translation** – translate message text with the Google Cloud Translation API, with built-in protection for racing terminology (black flag, blue flag, etc.) across multiple languages.
- **German informalizer** – automatically converts formal "Sie" phrasing to the informal "Du" form commonly used in motorsport communication.
- **Audio effects**
  - Radio bandpass filter with optional soft-clip distortion, to emulate a race radio.
  - Squelch burst effect at the start/end of a message.
  - Per-message toggles for all effects.
- **iRacing-ready output** – automatically downsamples audio to the format iRacing expects and writes a `spmsg.txt`/`spmsg.ini` file alongside the generated WAV files.
- **Incremental exports** – only regenerates audio files that actually changed since the last export.
- **Import existing packs** – load an existing `spmsg.ini`/`spmsg.txt` pack to review, edit, or extend it.
- **Review workflow** – track each message's status (To Do, Satisfactory, Rework Needed, Done).
- **Localized UI** – available in English and German.

## Requirements

- Windows 10/11
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download) (or the SDK, for building from source)
- A Google Cloud API key with the **Text-to-Speech** and **Translation** APIs enabled (only required if you want to use TTS/translation instead of manual recordings)

## Getting Started
Download and install the current release or follow the next steps if you want to build it yourself.

1. Clone the repository:
   ```powershell
   git clone https://github.com/DonutzAndCoffee/iRacing-Spotter-Generator.git
   ```
2. Open `iRacing Spotter Generator.slnx` in Visual Studio 2022+ (or Visual Studio 2026).
3. Build and run the `iRacing Spotter Generator` project.
4. On first launch, open **Settings** to configure your Google API key (optional) and default recording/audio quality options.

### Building from the command line

```powershell
dotnet build "iRacing Spotter Generator.csproj"
```

## Usage

1. Create a new project or import an existing spotter pack (`spmsg.ini`/`spmsg.txt`).
2. For each spotter message, enter the text and choose an audio source:
   - Select a Google TTS voice, or
   - Record/import your own audio via the **Takes** window.
3. Optionally translate messages, adjust phrasing, and enable radio/squelch effects.
4. Mark messages with a review status as you go.
5. Export the project to generate the final pack folder, ready to be copied into your iRacing installation.

## Project Structure

- `Models/` – data models for projects, messages, settings, and takes.
- `Services/` – core logic: pack generation, TTS/translation clients, audio recording/effects, settings, localization.
- `Converters/` – WPF value converters used by the UI.
- `Resources/` – localized string resources (`Strings.en.xaml`, `Strings.de.xaml`).
- `INFO/` – sample spotter message reference files.

## License

See [LICENSE.txt](LICENSE.txt) for license details.

## Contributing

Issues and pull requests are welcome. Please open an issue to discuss significant changes before submitting a PR.
