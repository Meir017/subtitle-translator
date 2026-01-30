# Usage — SubtitleTranslator

## Modes

SubtitleTranslator can run in two modes:

1. **CLI Mode**: Fast, non-interactive mode for automation and scripting. Requires all arguments upfront.
2. **Interactive Mode**: Guided experience with prompts, AI-powered filename suggestions, and visual progress feedback.

---

## CLI Mode

### Synopsis

dotnet run --project SubtitleTranslator.csproj -- "<input-file>" "<output-srt>" "<target-lang>"

### Arguments

- `<input-file>`: a path to a movie file (e.g. .mkv, .mp4) or an existing .srt file.
- `<output-srt>`: path where the translated .srt will be written.
- `<target-lang>`: target language code (ISO 639-1), e.g. en, fr, es, de, he, ja, pt, ru, zh.

### Examples

**Translate subtitles from a video file to English:**

  dotnet run --project SubtitleTranslator.csproj -- "D:\Videos\The.Copenhagen.Test.S01E01.1080p.WEB-DL-[Feranki1980].mkv" "D:\Videos\The.Copenhagen.Test.S01E01.en.srt" "en"

**Translate an existing SRT file to French:**

  dotnet run --project SubtitleTranslator.csproj -- "episode1.srt" "episode1.fr.srt" "fr"

**Translate MKV subtitles to Spanish:**

  dotnet run --project SubtitleTranslator.csproj -- "movie.mkv" "movie.es.srt" "es"

**Translate MP4 subtitles to German:**

  dotnet run --project SubtitleTranslator.csproj -- "C:\Movies\documentary.mp4" "C:\Movies\documentary.de.srt" "de"

**Translate subtitles to Hebrew:**

  dotnet run --project SubtitleTranslator.csproj -- "series\episode_03.mkv" "series\episode_03.he.srt" "he"

**Translate subtitles to Japanese:**

  dotnet run --project SubtitleTranslator.csproj -- "anime.mkv" "anime.ja.srt" "ja"

**Translate subtitles to Portuguese:**

  dotnet run --project SubtitleTranslator.csproj -- "show.srt" "show.pt.srt" "pt"

**Translate subtitles to Russian:**

  dotnet run --project SubtitleTranslator.csproj -- "film.mkv" "film.ru.srt" "ru"

**Translate subtitles to Chinese:**

  dotnet run --project SubtitleTranslator.csproj -- "drama.srt" "drama.zh.srt" "zh"

---

## Interactive Mode

### Synopsis

dotnet run --project SubtitleTranslator.csproj

### Description

When no arguments are provided, the tool launches in **Interactive Mode** with a guided, step-by-step workflow:

1. **Phase 1: Select Input File**  
   You'll be prompted to enter the path to a video file (.mkv, .mp4, etc.) or an existing .srt file.

2. **Phase 2: Select Target Language**  
   Enter a target language code (e.g., en, es, fr, de, he, ja, pt, ru, zh).

3. **Phase 3: Set Output File**  
   The tool uses AI (GitHub Copilot SDK) to generate creative filename suggestions based on your input file and target language. You can select one or enter a custom name.

4. **Phase 4: Translation**  
   The tool processes translations with real-time progress indicators and visual feedback.

### Example Session

```
$ dotnet run --project SubtitleTranslator.csproj

╔══════════════════════════════════════════════════════════════════════╗
║                      Subtitle Translator                             ║
╚══════════════════════════════════════════════════════════════════════╝

Phase 1: Select Input File
Enter input file path (video file or .srt): movie.mkv

Phase 2: Select Target Language
Enter target language code (e.g., en, es, fr, he, ja, pt, ru, zh): en

Phase 3: Set Output File
⠋ Generating file name suggestions...
Suggested output file names:
  ➤ movie.en.srt
    movie-english.srt
    movie_translated_en.srt
    movie.translated.en.srt
    movie-en-subtitles.srt
    movie.english-subs.srt
    [Enter custom name]

Phase 4: Translation
✓ Processing translations...
```

### When to Use Interactive Mode

- **First-time users**: Guided prompts help you understand what inputs are needed.
- **Experimenting with filenames**: AI suggestions provide creative, context-aware naming options.
- **Better UX**: Visual progress indicators and colorful output make the process more engaging.
- **No automation needed**: When you're working manually and don't need scripting.

---

## Behavior

- When the input is a video, the CLI extracts the first subtitle stream using ffmpeg. If extraction of the first stream fails, it falls back to mapping all subtitle streams.
- When the input is an .srt file, it is used directly and no extraction is performed.
- Each subtitle entry is translated independently in chunks (default: 10 entries per chunk) and written to the output .srt preserving timestamps.
- Translations are powered by GitHub Copilot SDK using GPT-4.1.

---

## Notes

- **Mode selection**: If 3+ arguments are provided, CLI mode is used. Otherwise, interactive mode launches.
- Installation and authentication setup are documented in the project README.
- For better performance and lower API usage, the tool batches translations in chunks.

---

## Troubleshooting

- **Subtitle extraction fails**: Run the CLI with a video that has subtitle streams or inspect ffmpeg output printed by the tool.
- **Translation API errors**: See the project's README for GitHub Copilot SDK setup and authentication instructions.
- **Invalid language codes**: Use ISO 639-1 codes (2-letter codes like en, fr, es) or 5-character codes for regional variants.
- **File not found errors**: Use absolute paths or ensure the file exists at the specified location.

