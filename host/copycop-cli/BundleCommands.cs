using System.Text;
using CopyCop.Core;

namespace CopyCop.Cli;

internal static class BundleCommands
{
    public static async Task<int> RunAsync(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += handler;
        try
        {
            if (args[0] == "pack" && args.Length >= 3)
                await PackAsync(args, cancellation.Token);
            else if (args[0] == "unpack" && args.Length == 3)
                await UnpackAsync(args[1], args[2], cancellation.Token);
            else
            {
                Console.Error.WriteLine("copycop-cli pack OUTPUT.copycop INPUT... [--no-compress]");
                Console.Error.WriteLine("copycop-cli unpack INPUT.copycop NEW_DIRECTORY");
                return 64;
            }
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Abgebrochen.");
            return 130;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
        finally { Console.CancelKeyPress -= handler; }
    }

    private static async Task PackAsync(string[] args, CancellationToken cancellationToken)
    {
        var paths = args.Skip(2).Where(arg => arg != "--no-compress").ToArray();
        if (paths.Length is 0 or > CopyCopBundle.MaximumFiles)
            throw new InvalidDataException($"Bitte 1 bis {CopyCopBundle.MaximumFiles} Dateien angeben.");
        var files = new List<BundleFile>();
        var remaining = CopyCopBundle.MaximumExpandedBytes;
        foreach (var path in paths)
        {
            await using var stream = File.OpenRead(path);
            var file = await CopyCopBundle.ReadFileAsync(Path.GetFileName(path), stream, remaining, cancellationToken);
            files.Add(file);
            remaining -= file.Data.Length;
        }
        var bundle = CopyCopBundle.Create(files, !args.Contains("--no-compress"));
        cancellationToken.ThrowIfCancellationRequested();
        // CreateNew intentionally refuses to replace an existing user file.
        await using var output = new FileStream(args[1], FileMode.CreateNew, FileAccess.Write);
        await output.WriteAsync(Encoding.UTF8.GetBytes(bundle.Text), cancellationToken);
        Console.WriteLine($"{bundle.FileCount} Dateien · {bundle.OriginalBytes:N0} Original-Bytes · {bundle.Text.Length:N0} Tippzeichen");
        Console.WriteLine($"Kompression spart {bundle.UncompressedArchiveBytes - bundle.ArchiveBytes:N0} ZIP-Bytes.");
        Console.WriteLine($"Paket gespeichert: {Path.GetFullPath(args[1])}");
    }

    private static async Task UnpackAsync(string input, string destination, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(input);
        var text = await TextFileReader.ReadAsync(stream, cancellationToken);
        var files = CopyCopBundle.Read(text);
        var root = Path.GetFullPath(destination);
        if (Directory.Exists(root) || File.Exists(root))
            throw new IOException("Zum Entpacken bitte einen neuen, noch nicht vorhandenen Ordner angeben.");
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(root);
        foreach (var file in files)
        {
            var target = Path.GetFullPath(Path.Combine(root, file.Name.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new InvalidDataException("Ein Dateipfad verlässt den Zielordner.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write);
            await output.WriteAsync(file.Data, cancellationToken);
        }
        Console.WriteLine($"{files.Count} Dateien wiederhergestellt: {root}");
    }
}
