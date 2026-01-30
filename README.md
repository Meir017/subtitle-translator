# SubtitleTranslator

A powerful CLI tool for translating video subtitles and SRT files using GitHub Copilot AI. Supports multiple languages and offers both CLI and interactive modes.

## Features

- 🎬 **Video File Support**: Extract and translate subtitles from MKV, MP4, and other video formats
- 📝 **SRT File Support**: Translate existing SRT subtitle files directly
- 🌍 **Multi-Language**: Support for 15+ languages including English, French, Spanish, German, Japanese, Chinese, and more
- 🤖 **AI-Powered**: Uses GitHub Copilot SDK for intelligent, context-aware translations
- 💻 **Dual Modes**:
  - **CLI Mode**: Automated batch processing for scripting
  - **Interactive Mode**: Guided step-by-step workflow with smart suggestions
- 📊 **Real-Time Progress**: Visual feedback during translation process
- 🔧 **Subtitle Extraction**: Automatic subtitle stream extraction using ffmpeg

## Requirements

- **.NET 8.0** SDK or later
- **ffmpeg** (available on PATH for video file processing)
- **GitHub Copilot** authentication configured (for translation functionality)

## Installation

1. Clone the repository:

   ```bash
   git clone https://github.com/Meir017/subtitle-translator.git
   cd subtitle-translator
   ```

2. Ensure you have .NET 8.0 installed:

   ```bash
   dotnet --version
   ```

3. Install ffmpeg (if not already installed):
   - **Windows**: `winget install ffmpeg` or download from [ffmpeg.org](https://ffmpeg.org)
   - **macOS**: `brew install ffmpeg`
   - **Linux**: `sudo apt install ffmpeg`

## Quick Start

### CLI Mode

Translate a video file to French:

```bash
dotnet run --project SubtitleTranslator.csproj -- "movie.mkv" "movie.fr.srt" "fr"
```

Translate an existing SRT file to English:

```bash
dotnet run --project SubtitleTranslator.csproj -- "subtitles.srt" "subtitles.en.srt" "en"
```

### Interactive Mode

Start the interactive wizard:

```bash
dotnet run --project SubtitleTranslator.csproj
```

## Usage Guide

For detailed usage instructions, examples, and language codes, see [usage.md](usage.md).

## How It Works

1. **Subtitle Extraction**: If a video file is provided, ffmpeg extracts the first subtitle stream
2. **SRT Parsing**: The subtitle file is parsed to extract timing and content
3. **AI Translation**: Each line is translated using GitHub Copilot SDK
4. **SRT Generation**: Translated subtitles are written to the output SRT file with original timing intact

## Supported Languages

English, French, Spanish, German, Italian, Japanese, Korean, Polish, Portuguese (Brazil), Russian, Turkish, Chinese (Simplified), Chinese (Traditional), Hebrew, and more.

## Architecture

- **CopilotTranslator.cs**: Implements AI-powered translation using GitHub Copilot SDK
- **SrtParser.cs**: Parses and generates SRT subtitle format
- **ITranslator.cs**: Interface for translation implementations
- **Program.cs**: Main entry point with CLI and interactive modes

## License

See [LICENSE](LICENSE) for details.

## Notes

- Requires valid GitHub Copilot authentication for translation functionality
- Only the first subtitle stream from video files is extracted
- Maintains original subtitle timing and formatting
