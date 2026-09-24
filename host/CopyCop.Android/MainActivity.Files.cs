using Android.App;
using Android.Content;
using Android.OS;
using Android.Provider;
using Android.Views;
using Android.Widget;
using CopyCop.Core;
using System.Text;
using System.Text.Json;
using Uri = Android.Net.Uri;
using OperationCanceledException = System.OperationCanceledException;

namespace CopyCop.AndroidApp;

public sealed partial class MainActivity
{
    private const int BundleFilesRequest = 1002;
    private const int SaveBundleRequest = 1003;
    private const int BundleFolderRequest = 1004;
    private const string PendingRequestState = "copycop.pendingFileRequest";
    private const string CompressionState = "copycop.compressBundle";
    private const string TextCompressionState = "copycop.compressText";
    private const string PendingExportState = "copycop.pendingExportPath";
    private int pendingFileRequest;
    private bool pendingCompression = true;
    private string? pendingExportPath;
    private const string DraftPathState = "copycop.draftPath";
    private string? draftPath;
    private readonly BundleFileSelection fileSelection = new();
    private bool selectionNeedsBuild;
    private sealed record Draft(string Text, BundleFile[] Files, bool NeedsBuild);

    private void RestoreFileState(Bundle? state)
    {
        pendingFileRequest = state?.GetInt(PendingRequestState) ?? 0;
        pendingCompression = state?.GetBoolean(CompressionState, true) ?? true;
        pendingExportPath = state?.GetString(PendingExportState);
        compressBundle.Checked = pendingCompression;
        compressText.Checked = state?.GetBoolean(TextCompressionState, false) ?? false;
        draftPath = state?.GetString(DraftPathState);
        if (draftPath is not null && File.Exists(draftPath))
        {
            try
            {
                var draft = JsonSerializer.Deserialize(File.ReadAllText(draftPath), DraftJsonContext.Default.Draft);
                if (draft is not null)
                {
                    fileSelection.Replace(draft.Files);
                    SetImportedText(draft.Text);
                    RefreshFileSelection(changed: false);
                    selectionNeedsBuild = draft.NeedsBuild;
                    UpdateSelectionSummary();
                }
            }
            catch (Exception exception) { SetActivity($"Dateiauswahl konnte nicht wiederhergestellt werden: {exception.Message}", Red); }
        }
        UpdateActionState();
    }

    protected override void OnSaveInstanceState(Bundle outState)
    {
        outState.PutBoolean(FilePickerPendingState, filePickerPending);
        outState.PutInt(PendingRequestState, pendingFileRequest);
        outState.PutBoolean(CompressionState, filePickerPending ? pendingCompression : compressBundle.Checked);
        outState.PutBoolean(TextCompressionState, compressText.Checked);
        // Store only a private cache path, never a multi-megabyte payload in Android's state Bundle.
        outState.PutString(PendingExportState, pendingExportPath);
        try
        {
            draftPath ??= Path.Combine(CacheDir!.AbsolutePath, $"draft-{Guid.NewGuid():N}.json");
            File.WriteAllText(draftPath, JsonSerializer.Serialize(
                new Draft(editor.Text ?? "", fileSelection.Files.ToArray(), selectionNeedsBuild), DraftJsonContext.Default.Draft));
            outState.PutString(DraftPathState, draftPath);
        }
        catch (Exception exception) { SetActivity($"Zwischenstand konnte nicht gesichert werden: {exception.Message}", Amber); }
        base.OnSaveInstanceState(outState);
    }

    private void OpenFilePicker(int request)
    {
        if (isBusy || isImporting) return;
        isImporting = true;
        pendingCompression = compressBundle.Checked;
        UpdateActionState();
        try
        {
            using var intent = new Intent(request == BundleFolderRequest
                ? Intent.ActionOpenDocumentTree : Intent.ActionOpenDocument);
            if (request != BundleFolderRequest)
            {
                intent.AddCategory(Intent.CategoryOpenable);
                intent.SetType("*/*");
                intent.PutExtra(Intent.ExtraAllowMultiple, request == BundleFilesRequest);
            }
            intent.AddFlags(ActivityFlags.GrantReadUriPermission);
            pendingFileRequest = request;
            filePickerPending = true;
            StartActivityForResult(intent, request);
        }
        catch (Exception exception)
        {
            FinishFileOperation();
            SetActivity($"Dateiauswahl konnte nicht geöffnet werden: {exception.Message}", Red);
        }
    }

    private async Task SaveBundleAsync()
    {
        if (isBusy || isImporting || textPreparation.Result.Error is not null || string.IsNullOrEmpty(editor.Text)) return;
        isImporting = true;
        UpdateActionState();
        var source = compressText.Checked ? textPreparation.Result.Text : editor.Text!;
        pendingCompression = compressBundle.Checked;
        try
        {
            SetActivity("CopyCop-Paket wird zum Speichern vorbereitet …", Primary);
            var content = await Task.Run(() => CopyCopBundle.PrepareForSave(source, pendingCompression), lifetime.Token);
            lifetime.Token.ThrowIfCancellationRequested();
            var cache = CacheDir?.AbsolutePath ?? throw new IOException("Kein temporärer App-Speicher verfügbar.");
            pendingExportPath = Path.Combine(cache, $"export-{Guid.NewGuid():N}.copycop");
            await File.WriteAllTextAsync(pendingExportPath, content, new UTF8Encoding(false), lifetime.Token);
            using var intent = new Intent(Intent.ActionCreateDocument);
            intent.AddCategory(Intent.CategoryOpenable);
            intent.SetType("application/octet-stream");
            intent.PutExtra(Intent.ExtraTitle, "paket.copycop");
            intent.AddFlags(ActivityFlags.GrantWriteUriPermission);
            pendingFileRequest = SaveBundleRequest;
            filePickerPending = true;
            StartActivityForResult(intent, SaveBundleRequest);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            DeletePendingExport();
            FinishFileOperation();
        }
        catch (Exception exception)
        {
            DeletePendingExport();
            FinishFileOperation();
            SetActivity($"Paket konnte nicht gespeichert werden: {exception.Message}", Red);
        }
    }

    protected override async void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode is not (OpenTextFileRequest or BundleFilesRequest or BundleFolderRequest or SaveBundleRequest)
            || lifetime.IsCancellationRequested) return;
        filePickerPending = false;
        isImporting = true;
        UpdateActionState();
        try
        {
            if (resultCode != Result.Ok) return;
            var resolver = ContentResolver ?? throw new IOException("Der Dateizugriff ist nicht verfügbar.");
            var cancellation = lifetime.Token;
            if (requestCode == SaveBundleRequest)
            {
                var uri = data?.Data ?? throw new IOException("Kein Speicherort ausgewählt.");
                var source = pendingExportPath;
                if (source is null || !File.Exists(source))
                    throw new IOException("Die vorbereitete Datei ist nicht mehr verfügbar. Bitte erneut speichern.");
                await Task.Run(async () =>
                {
                    cancellation.ThrowIfCancellationRequested();
                    await using var input = File.OpenRead(source);
                    using var output = resolver.OpenOutputStream(uri, "wt")
                        ?? throw new IOException("Der Speicherort konnte nicht geöffnet werden.");
                    await input.CopyToAsync(output, cancellation);
                    await output.FlushAsync(cancellation);
                }, cancellation);
                SetActivity("CopyCop-Paket gespeichert. Es kann später über „Datei öffnen …“ wieder geladen werden.", Green);
                return;
            }

            if (requestCode is BundleFilesRequest or BundleFolderRequest)
            {
                SetActivity("Weitere Dateien werden eingelesen …", Primary);
                var files = await Task.Run(async () =>
                {
                    return requestCode == BundleFolderRequest
                        ? await ReadFolderAsync(resolver, data?.Data ?? throw new IOException("Kein Ordner ausgewählt."), cancellation)
                        : await ReadSelectedFilesAsync(resolver, data, cancellation);
                }, cancellation);
                cancellation.ThrowIfCancellationRequested();
                var added = fileSelection.Add(files);
                if (added > 0) RefreshFileSelection(changed: true);
                SetActivity(added == 0 ? "Die ausgewählten Dateien sind bereits in der Liste."
                    : $"{added} Dateien hinzugefügt. Die bisherigen bleiben erhalten. Jetzt Paket erstellen.", Green);
                return;
            }

            var selected = data?.Data ?? throw new IOException("Es wurde keine Datei zurückgegeben.");
            SetActivity("Datei wird eingelesen …", Primary);
            var file = await Task.Run(async () =>
            {
                var name = GetDocumentName(resolver, selected);
                using var stream = resolver.OpenInputStream(selected)
                    ?? throw new IOException("Die ausgewählte Datei konnte nicht geöffnet werden.");
                var text = await TextFileReader.ReadAsync(stream, cancellation);
                var isBundle = Path.GetExtension(name).Equals(".copycop", StringComparison.OrdinalIgnoreCase)
                    || CopyCopBundle.LooksLikeBundle(text);
                var files = isBundle ? CopyCopBundle.Read(text) : [];
                return (Name: name, Text: text, IsBundle: isBundle, Files: files);
            }, cancellation);
            cancellation.ThrowIfCancellationRequested();
            SetImportedText(file.Text);
            fileSelection.Replace(file.Files);
            RefreshFileSelection(changed: false);
            if (file.IsBundle && !assessment.FitsCapacity) SplitText(afterImport: true);
            var message = !assessment.HasText ? $"„{file.Name}“ ist leer."
                : assessment.HasBlockingUnsupported ? $"„{file.Name}“ eingelesen. Bitte nicht unterstützte Zeichen prüfen."
                : assessment.FitsCapacity ? $"„{file.Name}“ eingelesen und geprüft. Bereit zum Speichern."
                : file.IsBundle ? $"„{file.Name}“ geprüft und in {parts.Count} Teile aufgeteilt. Bitte einen Teil auf das Gerät laden."
                : $"„{file.Name}“ eingelesen. Bitte in {assessment.RequiredParts:N0} Teile aufteilen.";
            SetActivity(message, assessment.CanTransfer || file.IsBundle ? Green : Amber);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (!lifetime.IsCancellationRequested)
                SetActivity($"Dateiaktion fehlgeschlagen: {exception.Message}", Red);
        }
        finally
        {
            if (requestCode == SaveBundleRequest) DeletePendingExport();
            FinishFileOperation();
        }
    }

    private void SetImportedText(string text)
    {
        if (editor.Text == text) Reassess(clearParts: true);
        else editor.Text = text;
        editor.SetSelection(0);
    }

    private void RefreshFileSelection(bool changed)
    {
        selectionNeedsBuild = changed && fileSelection.Files.Count > 0;
        if (changed && CopyCopBundle.LooksLikeBundle(editor.Text ?? "")) SetImportedText("");
        var adapter = new ArrayAdapter<string>(this, global::Android.Resource.Layout.SimpleSpinnerItem,
            fileSelection.Files.Select(file => file.ToString()).ToArray());
        adapter.SetDropDownViewResource(global::Android.Resource.Layout.SimpleSpinnerDropDownItem);
        bundleFilesSpinner.Adapter = adapter;
        bundleFilesSpinner.Visibility = fileSelection.Files.Count > 0 ? ViewStates.Visible : ViewStates.Gone;
        UpdateSelectionSummary();
        UpdateActionState();
    }

    private void UpdateSelectionSummary() => bundleSelectionSummary.Text =
        $"{fileSelection.Files.Count} Dateien · {fileSelection.TotalBytes:N0} Bytes"
        + (selectionNeedsBuild ? " · Paket neu erstellen" : "");

    private async Task BuildSelectedBundleAsync()
    {
        if (isBusy || isImporting || fileSelection.Files.Count == 0) return;
        isImporting = true;
        UpdateActionState();
        try
        {
            var files = fileSelection.Files.ToArray();
            var compress = compressBundle.Checked;
            SetActivity("Paket wird erstellt …", Primary);
            var bundle = await Task.Run(() => CopyCopBundle.Create(files, compress), lifetime.Token);
            lifetime.Token.ThrowIfCancellationRequested();
            SetImportedText(bundle.Text);
            RefreshFileSelection(changed: false);
            if (!assessment.FitsCapacity) SplitText(afterImport: true);
            SetActivity($"{bundle.FileCount} Dateien gebündelt · {bundle.OriginalBytes:N0} Original-Bytes · "
                + $"{bundle.Text.Length:N0} Tippzeichen · {bundle.UncompressedArchiveBytes - bundle.ArchiveBytes:N0} ZIP-Bytes gespart. "
                + "Als .copycop speichern oder auf CopyCop laden.", Green);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception exception) { SetActivity($"Paket konnte nicht erstellt werden: {exception.Message}", Red); }
        finally { FinishFileOperation(); }
    }

    private void FinishFileOperation()
    {
        filePickerPending = false;
        pendingFileRequest = 0;
        isImporting = false;
        if (!lifetime.IsCancellationRequested) UpdateActionState();
    }

    private void DeletePendingExport()
    {
        if (pendingExportPath is null) return;
        try { File.Delete(pendingExportPath); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        pendingExportPath = null;
    }

    private static string GetDocumentName(ContentResolver resolver, Uri uri)
    {
        using var cursor = resolver.Query(uri, [IOpenableColumns.DisplayName], null, null, null);
        var column = cursor?.GetColumnIndex(IOpenableColumns.DisplayName) ?? -1;
        if (column >= 0 && cursor!.MoveToFirst() && cursor.GetString(column) is { Length: > 0 } name) return name;
        throw new IOException("Der Dokumentanbieter liefert keinen Dateinamen.");
    }

    private static async Task<List<BundleFile>> ReadSelectedFilesAsync(ContentResolver resolver, Intent? data,
        CancellationToken cancellation)
    {
        var uris = new List<Uri>();
        if (data?.ClipData is { } clip)
        {
            if (clip.ItemCount > CopyCopBundle.MaximumFiles) throw new InvalidDataException("Maximal 256 Dateien pro Paket.");
            for (var index = 0; index < clip.ItemCount; index++)
                uris.Add(clip.GetItemAt(index)?.Uri ?? throw new IOException("Ungültige Dateiauswahl."));
        }
        else if (data?.Data is { } uri) uris.Add(uri);
        if (uris.Count == 0) throw new IOException("Keine Dateien ausgewählt.");
        var files = new List<BundleFile>();
        var remaining = CopyCopBundle.MaximumExpandedBytes;
        foreach (var uri in uris)
        {
            cancellation.ThrowIfCancellationRequested();
            using var stream = resolver.OpenInputStream(uri) ?? throw new IOException("Datei konnte nicht geöffnet werden.");
            var file = await CopyCopBundle.ReadFileAsync(GetDocumentName(resolver, uri), stream, remaining, cancellation);
            files.Add(file);
            remaining -= file.Data.Length;
        }
        return files;
    }

    private static async Task<List<BundleFile>> ReadFolderAsync(ContentResolver resolver, Uri tree,
        CancellationToken cancellation)
    {
        var rootId = DocumentsContract.GetTreeDocumentId(tree) ?? throw new IOException("Ungültiger Ordner.");
        using var rootUri = DocumentsContract.BuildDocumentUriUsingTree(tree, rootId)
            ?? throw new IOException("Ordner konnte nicht geöffnet werden.");
        var rootName = GetDocumentName(resolver, rootUri);
        CopyCopBundle.ValidateName(rootName);
        var queue = new Queue<(string Id, string Prefix)>();
        queue.Enqueue((rootId, rootName));
        var visited = new HashSet<string>();
        var files = new List<BundleFile>();
        var remaining = CopyCopBundle.MaximumExpandedBytes;
        while (queue.TryDequeue(out var folder))
        {
            cancellation.ThrowIfCancellationRequested();
            if (!visited.Add(folder.Id) || visited.Count > 4096)
                throw new InvalidDataException("Der Ordner enthält eine Schleife oder zu viele Unterordner.");
            using var children = DocumentsContract.BuildChildDocumentsUriUsingTree(tree, folder.Id);
            using var cursor = resolver.Query(children!,
                [DocumentsContract.Document.ColumnDocumentId, DocumentsContract.Document.ColumnDisplayName,
                 DocumentsContract.Document.ColumnMimeType], null, null, null)
                ?? throw new IOException("Ordnerinhalt konnte nicht gelesen werden.");
            while (cursor.MoveToNext())
            {
                cancellation.ThrowIfCancellationRequested();
                var id = cursor.GetString(0) ?? throw new IOException("Ungültige Dokument-ID.");
                var name = folder.Prefix + "/" + cursor.GetString(1);
                CopyCopBundle.ValidateName(name);
                if (cursor.GetString(2) == DocumentsContract.Document.MimeTypeDir)
                {
                    if (queue.Count + visited.Count >= 4096) throw new InvalidDataException("Zu viele Unterordner.");
                    queue.Enqueue((id, name));
                }
                else
                {
                    if (files.Count >= CopyCopBundle.MaximumFiles) throw new InvalidDataException("Maximal 256 Dateien pro Paket.");
                    using var uri = DocumentsContract.BuildDocumentUriUsingTree(tree, id);
                    using var stream = resolver.OpenInputStream(uri!) ?? throw new IOException($"Datei nicht lesbar: {name}");
                    var file = await CopyCopBundle.ReadFileAsync(name, stream, remaining, cancellation);
                    files.Add(file);
                    remaining -= file.Data.Length;
                }
            }
        }
        if (files.Count == 0) throw new InvalidDataException("Der Ordner enthält keine Dateien.");
        return files;
    }

    [System.Text.Json.Serialization.JsonSerializable(typeof(Draft))]
    private partial class DraftJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
}
