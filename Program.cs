using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using GitHub.Copilot.SDK;
using Spectre.Console;

namespace SubtitleTranslatorApp;

internal class Program
{
    private static async Task<int> Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .CreateLogger();

        try
        {
            using var host = Host.CreateDefaultBuilder(args)
                .UseSerilog()
                .ConfigureServices((ctx, services) =>
                {
                    services.AddSingleton(new CopilotClient());
                    services.AddTransient<ITranslator, CopilotTranslator>();
                })
                .Build();

            var logger = host.Services.GetRequiredService<ILogger<Program>>();

            // Check if CLI arguments provided (input, output, language)
            if (args.Length >= 3)
            {
                return await RunCliMode(args, host, logger);
            }
            else
            {
                return await RunInteractiveMode(host, logger);
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule("[red]Error[/]").RuleStyle("red"));
            AnsiConsole.WriteLine();
            AnsiConsole.WriteException(ex, ExceptionFormats.ShortenPaths | ExceptionFormats.ShortenTypes);
            Log.Fatal(ex, "Unhandled exception");
            return 3;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static async Task<int> RunCliMode(string[] args, IHost host, ILogger<Program> logger)
    {
        var input = args[0];
        var output = args[1];
        var lang = args[2];

        // Validate input file
        if (!File.Exists(input))
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Input file not found: {input.EscapeMarkup()}");
            return 1;
        }

        AnsiConsole.Write(new Rule("[bold blue]Subtitle Translator[/] [dim](CLI Mode)[/]").RuleStyle("blue").Centered());
        AnsiConsole.WriteLine();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title("[bold blue]Configuration[/]")
            .AddColumn("[bold]Setting[/]")
            .AddColumn("[bold]Value[/]")
            .AddRow("Input", input.EscapeMarkup())
            .AddRow("Output", output.EscapeMarkup())
            .AddRow("Target Language", lang)
            .AddRow("Subtitle Track", "0 (Default)")
            .AddRow("Chunk Size", "10 entries");
        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        return await ProcessTranslation(input, output, lang, 0, host, logger);
    }

    private static async Task<int> RunInteractiveMode(IHost host, ILogger<Program> logger)
    {
        // Welcome header
        AnsiConsole.Write(new Rule("[bold blue]Subtitle Translator[/]").RuleStyle("blue").Centered());
        AnsiConsole.WriteLine();

        // Phase 1: Input file
        AnsiConsole.MarkupLine("[cyan]Phase 1:[/] [bold]Select Input File[/]");
        var input = AnsiConsole.Prompt(
            new TextPrompt<string>("Enter input file path [bold](video file or .srt)[/]: ")
                .PromptStyle("cyan")
                .ValidationErrorMessage("[red]✗ File not found[/]")
                .Validate(path =>
                {
                    if (!File.Exists(path))
                        return ValidationResult.Error();
                    return ValidationResult.Success();
                }));
        AnsiConsole.WriteLine();

        // Phase 1.5: Select Subtitle Track
        var subtitleTrackIndex = 0;
        if (!string.Equals(Path.GetExtension(input), ".srt", StringComparison.OrdinalIgnoreCase))
        {
            AnsiConsole.MarkupLine("[cyan]Phase 1.5:[/] [bold]Select Subtitle Track[/]");
            var tracks = await GetSubtitleTracks(input, logger);

            if (tracks.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]⚠[/] No subtitle tracks found in the video file.");
            }
            else if (tracks.Count == 1)
            {
                AnsiConsole.MarkupLine($"[green]✓[/] Automatically selected the only subtitle track: [bold]{tracks[0]}[/]");
                subtitleTrackIndex = tracks[0].SubtitleIndex;
            }
            else
            {
                var selection = AnsiConsole.Prompt(
                    new SelectionPrompt<SubtitleTrack>()
                        .Title("Select the subtitle track to translate:")
                        .PageSize(10)
                        .AddChoices(tracks));
                subtitleTrackIndex = selection.SubtitleIndex;
            }
            AnsiConsole.WriteLine();
        }

        // Phase 2: Target language
        AnsiConsole.MarkupLine("[cyan]Phase 2:[/] [bold]Select Target Language[/]");
        var commonLanguages = new[] { "en", "es", "fr", "de", "he", "ja", "pt", "ru", "zh" };
        var lang = AnsiConsole.Prompt(
            new TextPrompt<string>("Enter target language code [dim](e.g., en, es, fr, he, ja, pt, ru, zh)[/]: ")
                .PromptStyle("cyan")
                .ValidationErrorMessage("[red]✗ Invalid language code[/]")
                .Validate(code =>
                {
                    if (string.IsNullOrWhiteSpace(code) || code.Length < 2 || code.Length > 5)
                        return ValidationResult.Error();
                    return ValidationResult.Success();
                }));
        AnsiConsole.WriteLine();

        // Phase 3: Output file with suggestions from Copilot
        AnsiConsole.MarkupLine("[cyan]Phase 3:[/] [bold]Set Output File[/]");
        var extension = Path.GetExtension(input);
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(input);
        var directory = Path.GetDirectoryName(input) ?? ".";

        // Generate suggestions using Copilot
        var suggestions = new List<string>();

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("[yellow]Generating file name suggestions...[/]", async ctx =>
            {
                var copilotClient = host.Services.GetRequiredService<CopilotClient>();
                await using var session = await copilotClient.CreateSessionAsync(new SessionConfig { Model = "gpt-4.1", Streaming = false });

                var prompt = $@"Given an input video file named ""{Path.GetFileName(input)}"" that will be translated to language code ""{lang}"", suggest 6 creative and clear output filenames for the translated subtitle file (.srt).
Rules:
- Include the language code ""{lang}"" in the filename
- Keep the original filename structure but add translation indicators
- Use different naming patterns (e.g., suffix, prefix, underscore vs dash)
- Only respond with the filenames, one per line, no numbering or extra text
- Use only the filename without directory path

Response Format:
<filename1>.srt
<filename2>.srt
<filename3>.srt
<filename4>.srt
<filename5>.srt
<filename6>.srt";

                var response = await session.SendAndWaitAsync(new MessageOptions { Prompt = prompt });
                var aiSuggestions = response!.Data.Content.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s) && s.EndsWith(".srt", StringComparison.OrdinalIgnoreCase))
                    .Take(6)
                    .Select(s => Path.Combine(directory, s))
                    .ToList();

                suggestions.AddRange(aiSuggestions);
            });

        // Add a fallback suggestion in case AI doesn't provide any
        if (suggestions.Count == 0)
        {
            suggestions.Add(Path.Combine(directory, $"{fileNameWithoutExt}.{lang}.srt"));
        }

        AnsiConsole.MarkupLine("[dim]Suggested output file names:[/]");
        var choicesList = suggestions.Concat(["[Enter custom name]"]).ToList();
        var selectionPrompt = new SelectionPrompt<string>()
            .Title("[cyan]Select output file or enter manually[/]")
            .PageSize(5)
            .UseConverter(s => s.EscapeMarkup())
            .AddChoices(choicesList);

        var selectedOption = AnsiConsole.Prompt(selectionPrompt);

        string output;
        if (selectedOption == "[Enter custom name]")
        {
            output = AnsiConsole.Prompt(
                new TextPrompt<string>("Enter output .srt file path: ")
                    .PromptStyle("cyan")
                    .ValidationErrorMessage("[red]✗ Please provide a valid path[/]")
                    .Validate(path =>
                    {
                        if (string.IsNullOrWhiteSpace(path))
                            return ValidationResult.Error();
                        return ValidationResult.Success();
                    }));
        }
        else
        {
            output = selectedOption;
        }
        AnsiConsole.WriteLine();

        // Display configuration as a table
        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title("[bold blue]Configuration Summary[/]")
            .AddColumn("[bold]Setting[/]")
            .AddColumn("[bold]Value[/]")
            .AddRow("Input", input.EscapeMarkup())
            .AddRow("Output", output.EscapeMarkup())
            .AddRow("Target Language", lang)
            .AddRow("Subtitle Track", subtitleTrackIndex.ToString())
            .AddRow("Chunk Size", "10 entries");
        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        return await ProcessTranslation(input, output, lang, subtitleTrackIndex, host, logger);
    }

    private static async Task<int> ProcessTranslation(string input, string output, string lang, int subtitleTrackIndex, IHost host, ILogger<Program> logger)
    {
        var tempSrt = string.Empty;
        var deleteTemp = false;

        if (string.Equals(Path.GetExtension(input), ".srt", StringComparison.OrdinalIgnoreCase))
        {
            AnsiConsole.MarkupLine("[cyan]ℹ[/] Input is an SRT file; skipping extraction.");
            tempSrt = input;
        }
        else
        {
            tempSrt = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".srt");
            deleteTemp = true;

            AnsiConsole.Write(new Rule("[blue]Extraction Phase[/]").RuleStyle("grey").LeftJustified());
            AnsiConsole.WriteLine();

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("[yellow]Extracting subtitles with ffmpeg...[/]", async ctx =>
                {
                    var ffmpegArgs = $"-y -i \"{input}\" -map 0:s:{subtitleTrackIndex} \"{tempSrt}\"";
                    var psi = new ProcessStartInfo("ffmpeg", ffmpegArgs) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
                    var p = Process.Start(psi);
                    if (p == null)
                    {
                        AnsiConsole.MarkupLine("[red]✗[/] Failed to start ffmpeg process.");
                        Environment.Exit(2);
                    }
                    var stderrTask = p.StandardError.ReadToEndAsync();
                    await p.WaitForExitAsync();
                    var stderr = await stderrTask;
                    if (p.ExitCode != 0 || !File.Exists(tempSrt))
                    {
                        ctx.Status("[yellow]Primary extraction failed, trying fallback...[/]");
                        AnsiConsole.MarkupLine("[yellow]⚠[/] Primary extraction failed, trying fallback mapping of all subtitle streams...");
                        ffmpegArgs = $"-y -i \"{input}\" -map 0:s \"{tempSrt}\"";
                        psi = new ProcessStartInfo("ffmpeg", ffmpegArgs) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
                        p = Process.Start(psi);
                        if (p == null)
                        {
                            AnsiConsole.MarkupLine("[red]✗[/] Failed to start ffmpeg process for fallback.");
                            logger.LogDebug(stderr);
                            Environment.Exit(2);
                        }
                        stderrTask = p.StandardError.ReadToEndAsync();
                        await p.WaitForExitAsync();
                        stderr = await stderrTask;
                        if (p.ExitCode != 0 || !File.Exists(tempSrt))
                        {
                            AnsiConsole.MarkupLine("[red]✗[/] Failed to extract subtitles with ffmpeg. Ensure ffmpeg is installed and the file contains subtitle streams.");
                            logger.LogDebug(stderr);
                            Environment.Exit(2);
                        }
                    }
                });

            AnsiConsole.MarkupLine("[green]✓[/] Subtitle extraction succeeded.");
            AnsiConsole.WriteLine();
        }

        var entries = SrtParser.Parse(tempSrt);
        var total = entries.Count;

        AnsiConsole.Write(new Rule("[blue]Translation Phase[/]").RuleStyle("grey").LeftJustified());
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[cyan]ℹ[/] Translating {total} subtitle entries in chunks of 10...");
        AnsiConsole.WriteLine();

        var translated = new string[total];
        var chunkSize = 10; // Translate 10 entries at a time per chunk

        // Process entries in chunks sequentially
        var chunks = new List<(int startIdx, int endIdx)>();
        for (int i = 0; i < total; i += chunkSize)
        {
            var endIdx = Math.Min(i + chunkSize - 1, total - 1);
            chunks.Add((i, endIdx));
        }

        using (var scope = host.Services.CreateScope())
        {
            var translator = scope.ServiceProvider.GetRequiredService<ITranslator>();

            var completed = 0;
            var translationStartTime = DateTime.UtcNow;
            await AnsiConsole.Progress()
                .AutoRefresh(true)
                .AutoClear(false)
                .HideCompleted(false)
                .Columns(
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new RemainingTimeColumn(),
                    new SpinnerColumn())
                .StartAsync(async ctx =>
                {
                    var task = ctx.AddTask($"[green]Translating subtitles ({completed}/{total})[/]", maxValue: total);

                    foreach (var chunk in chunks)
                    {
                        var (startIdx, endIdx) = chunk;
                        var chunkEntries = new List<string>();
                        for (int i = startIdx; i <= endIdx; i++)
                        {
                            chunkEntries.Add(entries[i].Text);
                        }

                        logger.LogDebug("Translating chunk from entry {start} to {end}", startIdx + 1, endIdx + 1);

                        var results = await translator.TranslateBulkAsync(chunkEntries, lang);

                        // Store results back in the translated array preserving order
                        for (int i = 0; i < results.Count; i++)
                        {
                            translated[startIdx + i] = results[i];
                        }

                        completed += chunkEntries.Count;
                        task.Description = $"[green]Translating subtitles ({completed}/{total})[/]";
                        task.Increment(chunkEntries.Count);
                    }
                });

            var totalDuration = DateTime.UtcNow - translationStartTime;
            var avgChunkTime = totalDuration.TotalSeconds / chunks.Count;

            AnsiConsole.WriteLine();
            var summaryTable = new Table()
                .Border(TableBorder.Rounded)
                .Title("[bold blue]Translation Summary[/]")
                .AddColumn("[bold]Metric[/]")
                .AddColumn("[bold]Value[/]")
                .AddRow("Total Subtitles", total.ToString())
                .AddRow("Total Chunks", chunks.Count.ToString())
                .AddRow("Total Duration", $"{totalDuration:hh\\:mm\\:ss}")
                .AddRow("Average Chunk Time", $"{avgChunkTime:F2}s");
            AnsiConsole.Write(summaryTable);
        }
        AnsiConsole.WriteLine();

        // Build translated entries preserving original order
        var translatedEntries = new List<SrtEntry>(total);
        for (int i = 0; i < total; i++)
        {
            var e = entries[i];
            translatedEntries.Add(new SrtEntry { Index = e.Index, Start = e.Start, End = e.End, Text = translated[i] });
        }

        AnsiConsole.Write(new Rule("[blue]Output Phase[/]").RuleStyle("grey").LeftJustified());
        AnsiConsole.WriteLine();

        SrtParser.Write(output, translatedEntries);
        if (deleteTemp)
        {
            try
            {
                File.Delete(tempSrt);
                AnsiConsole.MarkupLine($"[dim]Deleted temporary SRT: {tempSrt.EscapeMarkup()}[/]");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete temporary SRT: {tempSrt}", tempSrt);
                AnsiConsole.MarkupLine($"[yellow]⚠[/] Failed to delete temporary SRT: {tempSrt.EscapeMarkup()}");
            }
        }

        AnsiConsole.MarkupLine($"[green]✓[/] [bold]Translated subtitles written to:[/] {output.EscapeMarkup()}");
        await host.StopAsync();
        return 0;
    }

    private record SubtitleTrack(int StreamIndex, int SubtitleIndex, string Language, string Title)
    {
        public override string ToString()
        {
            var info = new List<string>();
            if (!string.IsNullOrEmpty(Language)) info.Add($"Lang: {Language}");
            if (!string.IsNullOrEmpty(Title)) info.Add($"Title: {Title}");
            var infoStr = info.Count > 0 ? $" ({string.Join(", ", info)})" : "";
            return $"Subtitle Track #{SubtitleIndex}{infoStr}";
        }
    }

    private static async Task<List<SubtitleTrack>> GetSubtitleTracks(string input, ILogger<Program> logger)
    {
        var tracks = new List<SubtitleTrack>();
        try
        {
            var ffprobeArgs = $"-v error -select_streams s -show_entries stream=index:stream_tags=language,title -of json \"{input}\"";
            var psi = new ProcessStartInfo("ffprobe", ffprobeArgs)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return [];

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0) return [];

            using var doc = JsonDocument.Parse(output);
            if (doc.RootElement.TryGetProperty("streams", out var streams))
            {
                int subtitleIdx = 0;
                foreach (var stream in streams.EnumerateArray())
                {
                    int streamIdx = stream.GetProperty("index").GetInt32();
                    string lang = string.Empty;
                    string title = string.Empty;

                    if (stream.TryGetProperty("tags", out var tags))
                    {
                        if (tags.TryGetProperty("language", out var langProp)) lang = langProp.GetString() ?? string.Empty;
                        if (tags.TryGetProperty("title", out var titleProp)) title = titleProp.GetString() ?? string.Empty;
                    }

                    tracks.Add(new SubtitleTrack(streamIdx, subtitleIdx++, lang, title));
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to get subtitle tracks with ffprobe");
        }
        return tracks;
    }
}

