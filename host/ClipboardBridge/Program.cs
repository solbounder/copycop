using System.Globalization;
using System.Text;
using CopyCop.Core;
using TextCopy;

namespace CopyCop.ClipboardBridge;

internal static class Program
{
    private sealed record Options(bool ReplaceUnsupported, bool Once, int? Part);

    private static async Task<int> Main(string[] args)
    {
        if (!TryParseOptions(args, out var options)) return 64;
        if (options is null) return 0;

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        try
        {
            Console.WriteLine("Suche CopyCop im blauen LOAD-Modus …");
            string? lastWaitStatus = null;
            var device = await CopyCopDevice.WaitForLoadDeviceAsync(
                cancellation.Token,
                issue =>
                {
                    if (!string.IsNullOrWhiteSpace(issue) && issue != lastWaitStatus)
                    {
                        Console.WriteLine(issue);
                        lastWaitStatus = issue;
                    }
                });
            await using var client = new TransferClient(device);
            await client.HelloAsync(cancellation.Token);
            var info = await client.GetInfoAsync(cancellation.Token);
            var maximumBytes = checked((int)info.MaxBytes);

            Console.WriteLine("Verbunden: CopyCop Clipboard Loader");
            Console.WriteLine($"Gerätespeicher: {info.StoredLength:N0} / {info.MaxBytes:N0} Bytes");

            do
            {
                Console.WriteLine("Warte auf C …");
                await client.WaitForCopyAsync(cancellation.Token);
                Console.WriteLine("C gedrückt – lese Zwischenablage.");

                var clipboard = await ClipboardService.GetTextAsync();
                if (string.IsNullOrEmpty(clipboard))
                {
                    Console.WriteLine("Die Zwischenablage enthält keinen Text.");
                    if (options.Once) return 2;
                    continue;
                }

                var assessment = TextCapacity.Assess(
                    clipboard, options.ReplaceUnsupported, maximumBytes);
                PrintAssessment(assessment);

                if (assessment.HasUnsupported && !options.ReplaceUnsupported)
                {
                    Console.WriteLine("Transfer abgebrochen. Optional: --replace-unsupported");
                    if (options.Once) return 3;
                    continue;
                }

                string transferText;
                if (assessment.FitsCapacity)
                {
                    transferText = assessment.Analysis.Text;
                }
                else
                {
                    var parts = TextSplitter.Split(assessment.Analysis.Text, maximumBytes);
                    var selected = SelectPart(parts, options.Part);
                    if (selected is null)
                    {
                        Console.WriteLine("Nichts gespeichert.");
                        if (options.Once) return 4;
                        continue;
                    }
                    transferText = selected.Text;
                    Console.WriteLine($"Es wird nur {selected} gespeichert.");
                }

                var utf8 = Encoding.UTF8.GetBytes(transferText);
                Console.WriteLine($"Sende {utf8.Length:N0} Bytes …");
                var lastDecile = -1;
                void ReportProgress(int sent, int total)
                {
                    var percent = total == 0 ? 100 : sent * 100 / total;
                    var decile = percent / 10;
                    if (decile != lastDecile)
                    {
                        lastDecile = decile;
                        Console.WriteLine($"  {percent}%");
                    }
                }

                await client.TransferAsync(utf8, ReportProgress, cancellation.Token);
                Console.WriteLine("Verifiziert und gespeichert.");
                if (options.Once) return 0;
            } while (!cancellation.IsCancellationRequested);
        }
        catch (OperationCanceledException)
        {
            return 130;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Fehler: {exception.Message}");
            return 1;
        }

        return 0;
    }

    private static void PrintAssessment(TextAssessment assessment)
    {
        Console.WriteLine(
            $"{assessment.Analysis.OriginalCharacterCount:N0} Zeichen · "
            + $"{assessment.Utf8Bytes:N0} / {assessment.MaximumBytes:N0} UTF-8-Bytes · "
            + $"{assessment.UsagePercent:N1}%");

        if (assessment.HasUnsupported)
        {
            Console.WriteLine($"Nicht unterstützt: {assessment.Analysis.Unsupported.Count:N0} Zeichen");
            foreach (var unsupported in assessment.Analysis.Unsupported.Take(20))
                Console.WriteLine($"  U+{unsupported.CodePoint:X4} {unsupported.Display}");
        }

        Console.WriteLine(assessment.FitsCapacity
            ? "Kapazität: passt vollständig."
            : $"Kapazität: zu groß, benötigt {assessment.RequiredParts:N0} Teile.");
    }

    private static TextPart? SelectPart(IReadOnlyList<TextPart> parts, int? requestedPart)
    {
        Console.WriteLine($"Der Text kann verlustfrei in {parts.Count:N0} Teile aufgeteilt werden:");
        foreach (var part in parts) Console.WriteLine($"  {part}");

        if (requestedPart.HasValue)
            return requestedPart.Value >= 1 && requestedPart.Value <= parts.Count
                ? parts[requestedPart.Value - 1]
                : throw new ArgumentOutOfRangeException(nameof(requestedPart),
                    $"--part muss zwischen 1 und {parts.Count} liegen.");

        if (Console.IsInputRedirected)
        {
            Console.WriteLine("Für nicht-interaktive Nutzung einen Teil mit --part N auswählen.");
            return null;
        }

        Console.Write("Aufteilen und einen Teil auswählen? [j/N] ");
        var answer = Console.ReadLine()?.Trim();
        if (!string.Equals(answer, "j", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(answer, "ja", StringComparison.OrdinalIgnoreCase))
            return null;

        Console.Write($"Teilnummer [1–{parts.Count}]: ");
        return int.TryParse(Console.ReadLine(), NumberStyles.None,
                   CultureInfo.InvariantCulture, out var number)
               && number >= 1 && number <= parts.Count
            ? parts[number - 1]
            : null;
    }

    private static bool TryParseOptions(string[] args, out Options? options)
    {
        var replace = false;
        var once = false;
        int? part = null;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index].ToLowerInvariant())
            {
                case "--replace-unsupported": replace = true; break;
                case "--once": once = true; break;
                case "--part" when index + 1 < args.Length
                                   && int.TryParse(args[++index], out var parsed):
                    part = parsed;
                    break;
                case "--help":
                case "-h":
                    PrintHelp();
                    options = null;
                    return true;
                default:
                    Console.Error.WriteLine($"Unbekannte Option: {args[index]}");
                    PrintHelp();
                    options = null;
                    return false;
            }
        }

        options = new Options(replace, once, part);
        return true;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("ClipboardBridge [--replace-unsupported] [--part N] [--once]");
        Console.WriteLine("  --replace-unsupported  unbekannte Zeichen durch ? ersetzen");
        Console.WriteLine("  --part N              bei zu großem Text genau Teil N speichern");
        Console.WriteLine("  --once                nach einem Ladeversuch beenden");
    }
}
