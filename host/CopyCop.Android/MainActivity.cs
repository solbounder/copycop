using Android.App;
using Android.Content;
using Android.Graphics.Drawables;
using Android.Hardware.Usb;
using Android.OS;
using Android.Text;
using Android.Views;
using Android.Widget;
using CopyCop.Core;
using Color = Android.Graphics.Color;
using ColorStateList = Android.Content.Res.ColorStateList;
using OperationCanceledException = System.OperationCanceledException;

namespace CopyCop.AndroidApp;

[Activity(
    Label = "CopyCop",
    MainLauncher = true,
    Exported = true,
    LaunchMode = global::Android.Content.PM.LaunchMode.SingleTop,
    Theme = "@style/AppTheme",
    ConfigurationChanges = global::Android.Content.PM.ConfigChanges.Orientation
                           | global::Android.Content.PM.ConfigChanges.ScreenSize
                           | global::Android.Content.PM.ConfigChanges.UiMode)]
[IntentFilter(new[] { UsbManager.ActionUsbDeviceAttached })]
[MetaData(UsbManager.ActionUsbDeviceAttached, Resource = "@xml/copycop_device_filter")]
public sealed class MainActivity : Activity
{
    private const string UsbPermissionAction = "de.copycop.app.USB_PERMISSION";

    private static readonly Color Background = Color.ParseColor("#090E1B");
    private static readonly Color CardBackground = Color.ParseColor("#111A2C");
    private static readonly Color SoftBackground = Color.ParseColor("#0D1525");
    private static readonly Color Border = Color.ParseColor("#23304A");
    private static readonly Color Primary = Color.ParseColor("#70A5FF");
    private static readonly Color TextPrimary = Color.ParseColor("#EAF0FC");
    private static readonly Color Muted = Color.ParseColor("#8D9AB5");
    private static readonly Color Green = Color.ParseColor("#63E6A5");
    private static readonly Color Amber = Color.ParseColor("#FFC66D");
    private static readonly Color Red = Color.ParseColor("#FF7A90");

    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim connectionGate = new(1, 1);

    private UsbManager usbManager = null!;
    private UsbBroadcastReceiver usbReceiver = null!;
    private AndroidUsbTransport? transport;
    private TransferClient? client;
    private CancellationTokenSource? connectionCancellation;
    private CancellationTokenSource? transferCancellation;
    private Task? copyEventTask;

    private EditText editor = null!;
    private CheckBox replaceUnsupported = null!;
    private TextView connectionTitle = null!;
    private TextView connectionDetail = null!;
    private TextView capacityTitle = null!;
    private TextView capacityDetail = null!;
    private TextView unsupportedDetail = null!;
    private TextView durationDetail = null!;
    private TextView storedDetail = null!;
    private TextView activityDetail = null!;
    private ProgressBar capacityProgress = null!;
    private ProgressBar transferProgress = null!;
    private Button splitButton = null!;
    private Button sendButton = null!;
    private Button cancelButton = null!;
    private Spinner partsSpinner = null!;

    private TextAssessment assessment = TextCapacity.Assess(string.Empty, false);
    private IReadOnlyList<TextPart> parts = [];
    private int maximumBytes = TextCapacity.FirmwareMaximumBytes;
    private uint storedBytes;
    private bool isConnected;
    private bool isBusy;
    private bool isForeground;
    private int? permissionRequestedDeviceId;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Window?.SetStatusBarColor(Background);
        Window?.SetNavigationBarColor(Background);

        usbManager = (UsbManager?)GetSystemService(UsbService)
            ?? throw new InvalidOperationException("Android stellt keinen USB-Dienst bereit.");

        BuildUi();
        usbReceiver = new UsbBroadcastReceiver(this);
        RegisterUsbReceiver();
        HandleUsbIntent(Intent);
        _ = FindAndConnectAsync();
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        HandleUsbIntent(intent);
    }

    protected override void OnResume()
    {
        base.OnResume();
        isForeground = true;
        _ = FindAndConnectAsync();
    }

    protected override void OnPause()
    {
        isForeground = false;
        base.OnPause();
    }

    protected override void OnDestroy()
    {
        lifetime.Cancel();
        transferCancellation?.Cancel();
        try { UnregisterReceiver(usbReceiver); }
        catch (Java.Lang.IllegalArgumentException) { }

        try { DisconnectAsync("App beendet").GetAwaiter().GetResult(); }
        catch { }

        transferCancellation?.Dispose();
        lifetime.Dispose();
        connectionGate.Dispose();
        base.OnDestroy();
    }

    private void RegisterUsbReceiver()
    {
        var filter = new IntentFilter();
        filter.AddAction(UsbPermissionAction);
        filter.AddAction(UsbManager.ActionUsbDeviceAttached);
        filter.AddAction(UsbManager.ActionUsbDeviceDetached);

        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
#pragma warning disable CA1416
            RegisterReceiver(usbReceiver, filter, ReceiverFlags.Exported);
#pragma warning restore CA1416
        else
#pragma warning disable CA1422
            RegisterReceiver(usbReceiver, filter);
#pragma warning restore CA1422
    }

    private void HandleUsbIntent(Intent? intent)
    {
        if (intent?.Action != UsbManager.ActionUsbDeviceAttached) return;
        var device = GetUsbDevice(intent);
        if (device is not null && AndroidUsbTransport.IsCopyCop(device))
            _ = FindAndConnectAsync(device);
    }

    internal void HandleUsbBroadcast(Intent intent)
    {
        var device = GetUsbDevice(intent);
        switch (intent.Action)
        {
            case UsbPermissionAction:
                permissionRequestedDeviceId = null;
                if (device is null || !AndroidUsbTransport.IsCopyCop(device)) return;
                if (intent.GetBooleanExtra(UsbManager.ExtraPermissionGranted, false)
                    && usbManager.HasPermission(device))
                {
                    _ = FindAndConnectAsync(device);
                }
                else
                {
                    SetConnectionState(
                        "USB-Zugriff abgelehnt",
                        "Verbinden antippen und Zugriff erlauben",
                        Red);
                }
                break;

            case UsbManager.ActionUsbDeviceAttached:
                if (device is not null && AndroidUsbTransport.IsCopyCop(device))
                    _ = FindAndConnectAsync(device);
                break;

            case UsbManager.ActionUsbDeviceDetached:
                if (device is not null && permissionRequestedDeviceId == device.DeviceId)
                    permissionRequestedDeviceId = null;
                if (device is not null && transport?.DeviceId == device.DeviceId)
                    _ = DisconnectAsync("CopyCop wurde getrennt");
                break;
        }
    }

    private static UsbDevice? GetUsbDevice(Intent intent)
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
        {
#pragma warning disable CA1416
            return intent.GetParcelableExtra(
                UsbManager.ExtraDevice,
                Java.Lang.Class.FromType(typeof(UsbDevice))) as UsbDevice;
#pragma warning restore CA1416
        }

#pragma warning disable CA1422
#pragma warning disable CS0618
        return intent.GetParcelableExtra(UsbManager.ExtraDevice) as UsbDevice;
#pragma warning restore CS0618
#pragma warning restore CA1422
    }

    private async Task FindAndConnectAsync(UsbDevice? preferredDevice = null)
    {
        if (lifetime.IsCancellationRequested) return;
        await connectionGate.WaitAsync(lifetime.Token);
        try
        {
            var attachedDevices = usbManager.DeviceList;
            var device = preferredDevice is not null && AndroidUsbTransport.IsCopyCop(preferredDevice)
                ? preferredDevice
                : attachedDevices?.Values.FirstOrDefault(AndroidUsbTransport.IsCopyCop);

            if (device is null)
            {
                await DisposeConnectionCoreAsync();
                SetConnectionState(
                    "LOAD-Gerät wird gesucht",
                    "C halten und CopyCop per USB-OTG einstecken",
                    Primary);
                return;
            }

            if (client is not null && transport?.DeviceId == device.DeviceId) return;

            if (!usbManager.HasPermission(device))
            {
                SetConnectionState(
                    "USB-Freigabe erforderlich",
                    "Bitte den Android-Dialog bestätigen",
                    Amber);
                if (permissionRequestedDeviceId != device.DeviceId)
                {
                    permissionRequestedDeviceId = device.DeviceId;
                    RequestUsbPermission(device);
                }
                return;
            }

            permissionRequestedDeviceId = null;

            await DisposeConnectionCoreAsync();
            SetConnectionState("CopyCop wird verbunden", "HID-Protokoll wird geprüft …", Primary);

            AndroidUsbTransport? openedTransport = null;
            TransferClient? openedClient = null;
            try
            {
                openedTransport = AndroidUsbTransport.Open(usbManager, device);
                openedClient = new TransferClient(openedTransport);
                await openedClient.HelloAsync(lifetime.Token);
                var info = await openedClient.GetInfoAsync(lifetime.Token);

                transport = openedTransport;
                client = openedClient;
                maximumBytes = checked((int)info.MaxBytes);
                storedBytes = info.StoredLength;
                connectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                isConnected = true;
                copyEventTask = MonitorCopyEventsAsync(openedClient, connectionCancellation.Token);

                RunOnUiThread(() =>
                {
                    connectionTitle.Text = "CopyCop verbunden";
                    connectionTitle.SetTextColor(Green);
                    connectionDetail.Text = "Blauer LOAD-Modus · bereit zum Speichern";
                    Reassess(clearParts: true);
                });
            }
            catch (Exception exception)
            {
                if (openedClient is not null) await openedClient.DisposeAsync();
                else if (openedTransport is not null) await openedTransport.DisposeAsync();
                SetConnectionState("Verbindung fehlgeschlagen", FriendlyMessage(exception), Red);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            connectionGate.Release();
        }
    }

    private void RequestUsbPermission(UsbDevice device)
    {
        var intent = new Intent(UsbPermissionAction).SetPackage(PackageName);
        var flags = PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable;
        var permissionIntent = PendingIntent.GetBroadcast(this, device.DeviceId, intent, flags);
        usbManager.RequestPermission(device, permissionIntent);
    }

    private async Task DisconnectAsync(string reason)
    {
        await connectionGate.WaitAsync();
        try
        {
            await DisposeConnectionCoreAsync();
            SetConnectionState(
                reason,
                "C halten und CopyCop per USB-OTG einstecken",
                Muted);
        }
        finally
        {
            connectionGate.Release();
        }
    }

    private async Task DisposeConnectionCoreAsync()
    {
        var oldCancellation = connectionCancellation;
        var oldClient = client;
        var oldCopyTask = copyEventTask;

        connectionCancellation = null;
        client = null;
        transport = null;
        copyEventTask = null;
        isConnected = false;
        oldCancellation?.Cancel();

        if (oldClient is not null)
        {
            try { await oldClient.DisposeAsync(); }
            catch { }
        }

        if (oldCopyTask is not null)
        {
            try { await oldCopyTask; }
            catch (OperationCanceledException) { }
            catch { }
        }

        oldCancellation?.Dispose();
        RunOnUiThread(UpdateActionState);
    }

    private async Task MonitorCopyEventsAsync(
        TransferClient activeClient,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await activeClient.WaitForCopyAsync(cancellationToken);
                if (!isForeground)
                {
                    SetActivity(
                        "C erkannt. Öffne CopyCop und tippe auf „Zwischenablage übernehmen“.",
                        Amber);
                    continue;
                }

                await RunOnUiAsync(() => PasteClipboardAsync(sendWhenPossible: true));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                await RecoverConnectionAsync(activeClient, exception);
            }
        }
    }

    private async Task RecoverConnectionAsync(
        TransferClient failedClient,
        Exception exception)
    {
        await connectionGate.WaitAsync();
        try
        {
            if (!ReferenceEquals(client, failedClient)) return;

            var oldCancellation = connectionCancellation;
            connectionCancellation = null;
            client = null;
            transport = null;
            copyEventTask = null;
            isConnected = false;
            oldCancellation?.Cancel();
            try { await failedClient.DisposeAsync(); }
            catch { }
            oldCancellation?.Dispose();

            SetConnectionState(
                "USB-Verbindung beendet",
                FriendlyMessage(exception),
                Amber);
        }
        finally
        {
            connectionGate.Release();
        }

        try { await Task.Delay(700, lifetime.Token); }
        catch (OperationCanceledException) { return; }
        await FindAndConnectAsync();
    }

    private async Task PasteClipboardAsync(bool sendWhenPossible)
    {
        try
        {
            var clipboard = (global::Android.Content.ClipboardManager?)GetSystemService(ClipboardService);
            var clip = clipboard?.PrimaryClip;
            var value = clip is null || clip.ItemCount == 0
                ? null
                : clip.GetItemAt(0)?.CoerceToText(this)?.ToString();

            if (string.IsNullOrEmpty(value))
            {
                SetActivity("Die Zwischenablage enthält keinen Text.", Amber);
                return;
            }

            editor.Text = value;
            editor.SetSelection(editor.Text?.Length ?? 0);
            SetActivity("Zwischenablage übernommen und geprüft.", Green);

            if (sendWhenPossible)
            {
                if (assessment.CanTransfer) await SendSelectedAsync();
                else if (!assessment.FitsCapacity)
                    SetActivity("C erkannt: Text ist zu groß. Bitte zuerst aufteilen.", Amber);
                else if (assessment.HasBlockingUnsupported)
                    SetActivity("C erkannt: Nicht unterstützte Zeichen blockieren die Übertragung.", Amber);
            }
        }
        catch (Exception exception)
        {
            SetActivity($"Zwischenablage nicht lesbar: {FriendlyMessage(exception)}", Red);
        }
    }

    private void SplitText()
    {
        if (!assessment.HasText || assessment.FitsCapacity || assessment.HasBlockingUnsupported) return;

        parts = TextSplitter.Split(assessment.Analysis.Text, maximumBytes);
        var adapter = new ArrayAdapter<string>(
            this,
            global::Android.Resource.Layout.SimpleSpinnerItem);
        foreach (var part in parts) adapter.Add(part.ToString());
        adapter.SetDropDownViewResource(global::Android.Resource.Layout.SimpleSpinnerDropDownItem);
        partsSpinner.Adapter = adapter;
        partsSpinner.Visibility = ViewStates.Visible;
        partsSpinner.SetSelection(0);
        SetActivity($"{parts.Count:N0} Teile vorbereitet. Wähle den gewünschten Teil.", Primary);
        UpdateActionState();
    }

    private async Task SendSelectedAsync()
    {
        var activeClient = client;
        if (activeClient is null || isBusy) return;

        var selectedIndex = partsSpinner.Visibility == ViewStates.Visible
            ? partsSpinner.SelectedItemPosition
            : -1;
        var value = selectedIndex >= 0 && selectedIndex < parts.Count
            ? parts[selectedIndex].Text
            : assessment.Analysis.Text;
        if (string.IsNullOrEmpty(value)) return;

        transferCancellation?.Dispose();
        transferCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        isBusy = true;
        transferProgress.Progress = 0;
        transferProgress.Visibility = ViewStates.Visible;
        SetActivity("Übertragung läuft …", Primary);
        UpdateActionState();

        try
        {
            var utf8 = System.Text.Encoding.UTF8.GetBytes(value);
            await activeClient.TransferAsync(utf8, (sent, total) => RunOnUiThread(() =>
            {
                transferProgress.Progress = total == 0 ? 1000 : sent * 1000 / total;
            }), transferCancellation.Token);

            storedBytes = (uint)utf8.Length;
            transferProgress.Progress = 1000;
            SetActivity($"Erfolgreich verifiziert und gespeichert · {utf8.Length:N0} Bytes", Green);
        }
        catch (OperationCanceledException) when (transferCancellation.IsCancellationRequested)
        {
            SetActivity("Übertragung abgebrochen.", Amber);
        }
        catch (Exception exception)
        {
            SetActivity($"Übertragung fehlgeschlagen: {FriendlyMessage(exception)}", Red);
        }
        finally
        {
            isBusy = false;
            transferProgress.Visibility = ViewStates.Gone;
            UpdateActionState();
        }
    }

    private void Reassess(bool clearParts)
    {
        assessment = TextCapacity.Assess(
            editor.Text ?? string.Empty,
            replaceUnsupported.Checked,
            maximumBytes);

        if (clearParts)
        {
            parts = [];
            partsSpinner.Adapter = null;
            partsSpinner.Visibility = ViewStates.Gone;
        }

        var headlineColor = !assessment.HasText ? Muted
            : assessment.HasBlockingUnsupported ? Red
            : assessment.FitsCapacity ? Green : Amber;
        capacityTitle.Text = !assessment.HasText ? "Noch kein Text"
            : assessment.HasBlockingUnsupported ? "Nicht unterstützte Zeichen"
            : assessment.FitsCapacity ? "Passt vollständig"
            : $"Text benötigt {assessment.RequiredParts:N0} Teile";
        capacityTitle.SetTextColor(headlineColor);

        var difference = assessment.MaximumBytes - assessment.Utf8Bytes;
        var remaining = difference >= 0
            ? $"{difference:N0} Bytes frei"
            : $"{-difference:N0} Bytes zu viel";
        capacityDetail.Text =
            $"{assessment.Analysis.OriginalCharacterCount:N0} Zeichen · "
            + $"{assessment.Utf8Bytes:N0} / {assessment.MaximumBytes:N0} UTF-8-Bytes\n{remaining}";
        capacityProgress.Progress = (int)Math.Round(assessment.UsagePercent * 10d);
        capacityProgress.ProgressTintList = ColorStateList.ValueOf(headlineColor);

        if (!assessment.HasUnsupported)
        {
            unsupportedDetail.Text = "Alle Zeichen sind auf deutscher QWERTZ-Tastatur darstellbar.";
            unsupportedDetail.SetTextColor(Muted);
        }
        else
        {
            var examples = string.Join("  ", assessment.Analysis.Unsupported.Take(5)
                .Select(item => $"U+{item.CodePoint:X4} {item.Display}"));
            var action = replaceUnsupported.Checked ? "werden durch ? ersetzt" : "blockieren den Transfer";
            unsupportedDetail.Text =
                $"{assessment.Analysis.Unsupported.Count:N0} Zeichen {action}: {examples}";
            unsupportedDetail.SetTextColor(replaceUnsupported.Checked ? Amber : Red);
        }

        if (assessment.HasText && !assessment.HasBlockingUnsupported)
        {
            var workload = TypingDurationEstimator.Analyze(assessment.Analysis.Text);
            var estimates = TypingDurationEstimator.SpeedLevelsMilliseconds
                .Select(delay => $"{delay,4} ms  {TypingDurationEstimator.Format(workload.Estimate(delay))}");
            durationDetail.Text =
                $"{workload.StrokeCount:N0} Tastenfolgen\n" + string.Join("\n", estimates);
        }
        else
        {
            durationDetail.Text = "Nach Eingabe eines übertragbaren Textes verfügbar.";
        }

        splitButton.Visibility = assessment.HasText && !assessment.FitsCapacity
            ? ViewStates.Visible : ViewStates.Gone;
        splitButton.Enabled = !assessment.HasBlockingUnsupported;
        UpdateActionState();
    }

    private void UpdateActionState()
    {
        if (sendButton is null) return;
        var hasSelectedPart = partsSpinner.Visibility == ViewStates.Visible
                              && partsSpinner.SelectedItemPosition >= 0
                              && partsSpinner.SelectedItemPosition < parts.Count;
        sendButton.Enabled = isConnected && !isBusy && assessment.HasText
                             && !assessment.HasBlockingUnsupported
                             && (assessment.FitsCapacity || hasSelectedPart);
        sendButton.Alpha = sendButton.Enabled ? 1f : 0.45f;
        sendButton.Text = hasSelectedPart
            ? $"Teil {partsSpinner.SelectedItemPosition + 1} auf CopyCop speichern"
            : "Text auf CopyCop speichern";
        cancelButton.Visibility = isBusy ? ViewStates.Visible : ViewStates.Gone;
        storedDetail.Text = isConnected
            ? $"Auf dem Gerät: {storedBytes:N0} Bytes · Kapazität {maximumBytes:N0} Bytes"
            : "Gerätespeicher wird nach Verbindung angezeigt";
    }

    private void SetConnectionState(string title, string detail, Color color) =>
        RunOnUiThread(() =>
        {
            connectionTitle.Text = title;
            connectionTitle.SetTextColor(color);
            connectionDetail.Text = detail;
            UpdateActionState();
        });

    private void SetActivity(string detail, Color color) =>
        RunOnUiThread(() =>
        {
            activityDetail.Text = detail;
            activityDetail.SetTextColor(color);
        });

    private Task RunOnUiAsync(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        RunOnUiThread(async () =>
        {
            try
            {
                await action();
                completion.TrySetResult();
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });
        return completion.Task;
    }

    private void BuildUi()
    {
        var scroll = new ScrollView(this)
        {
            FillViewport = true,
            Background = new ColorDrawable(Background)
        };
        var root = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Vertical
        };
        root.SetPadding(Dp(18), Dp(20), Dp(18), Dp(30));
        scroll.AddView(root, MatchWrap());

        var header = new LinearLayout(this) { Orientation = global::Android.Widget.Orientation.Horizontal };
        header.SetGravity(GravityFlags.CenterVertical);
        var logo = new ImageView(this);
        logo.SetImageResource(Resource.Drawable.copycop_logo);
        header.AddView(logo, new LinearLayout.LayoutParams(Dp(54), Dp(54)));
        var heading = new LinearLayout(this) { Orientation = global::Android.Widget.Orientation.Vertical };
        heading.SetPadding(Dp(12), 0, 0, 0);
        heading.AddView(Label("COPYCOP", 12, Primary, true));
        heading.AddView(Label("Portable Zwischenablage", 22, TextPrimary, true));
        header.AddView(heading, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1));
        root.AddView(header, WithBottomMargin(MatchWrap(), 16));

        var connectionCard = Card(SoftBackground);
        connectionTitle = Label("LOAD-Gerät wird gesucht", 17, Primary, true);
        connectionDetail = Label("C halten und CopyCop per USB-OTG einstecken", 13, Muted);
        connectionCard.AddView(connectionTitle);
        connectionCard.AddView(connectionDetail, WithTopMargin(MatchWrap(), 4));
        var reconnect = Button("Erneut verbinden", Primary);
        reconnect.Click += (_, _) => _ = FindAndConnectAsync();
        connectionCard.AddView(reconnect, WithTopMargin(MatchWrap(), 12));
        root.AddView(connectionCard, WithBottomMargin(MatchWrap(), 14));

        var editorCard = Card(CardBackground);
        editorCard.AddView(Eyebrow("TEXT"));
        editorCard.AddView(Label("Zwischenablage prüfen", 19, TextPrimary, true),
            WithTopMargin(MatchWrap(), 3));
        var clipboardButton = Button("Aus Zwischenablage übernehmen", Primary);
        clipboardButton.Click += async (_, _) => await PasteClipboardAsync(sendWhenPossible: false);
        editorCard.AddView(clipboardButton, WithTopMargin(MatchWrap(), 12));

        editor = new EditText(this)
        {
            Gravity = GravityFlags.Top | GravityFlags.Start,
            InputType = InputTypes.ClassText | InputTypes.TextFlagMultiLine
                        | InputTypes.TextFlagNoSuggestions,
            Hint = "Text hier eingeben oder aus der Zwischenablage übernehmen …"
        };
        editor.SetMinLines(8);
        editor.SetTextColor(TextPrimary);
        editor.SetHintTextColor(Muted);
        editor.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 15);
        editor.SetPadding(Dp(14), Dp(12), Dp(14), Dp(12));
        editor.Background = RoundedDrawable(Color.ParseColor("#0B1220"), Border, 12);
        editor.TextChanged += (_, _) => Reassess(clearParts: true);
        editorCard.AddView(editor, WithTopMargin(new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, Dp(230)), 12));
        root.AddView(editorCard, WithBottomMargin(MatchWrap(), 14));

        var analysisCard = Card(CardBackground);
        analysisCard.AddView(Eyebrow("ANALYSE"));
        capacityTitle = Label("Noch kein Text", 20, Muted, true);
        capacityDetail = Label(string.Empty, 13, Muted);
        analysisCard.AddView(capacityTitle, WithTopMargin(MatchWrap(), 5));
        analysisCard.AddView(capacityDetail, WithTopMargin(MatchWrap(), 4));
        capacityProgress = new ProgressBar(
            this, null, global::Android.Resource.Attribute.ProgressBarStyleHorizontal)
        {
            Max = 1000,
            Progress = 0
        };
        analysisCard.AddView(capacityProgress, WithTopMargin(new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, Dp(8)), 10));

        replaceUnsupported = new CheckBox(this)
        {
            Text = "Unbekannte Zeichen durch ? ersetzen"
        };
        replaceUnsupported.SetTextColor(TextPrimary);
        replaceUnsupported.ButtonTintList = ColorStateList.ValueOf(Primary);
        replaceUnsupported.CheckedChange += (_, _) => Reassess(clearParts: true);
        analysisCard.AddView(replaceUnsupported, WithTopMargin(MatchWrap(), 12));

        unsupportedDetail = Label(string.Empty, 13, Muted);
        analysisCard.AddView(unsupportedDetail, WithTopMargin(MatchWrap(), 7));
        splitButton = Button("Automatisch aufteilen", Amber);
        splitButton.Visibility = ViewStates.Gone;
        splitButton.Click += (_, _) => SplitText();
        analysisCard.AddView(splitButton, WithTopMargin(MatchWrap(), 12));
        partsSpinner = new Spinner(this) { Visibility = ViewStates.Gone };
        partsSpinner.ItemSelected += (_, _) => UpdateActionState();
        analysisCard.AddView(partsSpinner, WithTopMargin(MatchWrap(), 10));

        analysisCard.AddView(Eyebrow("GESCHÄTZTE TIPPDAUER"), WithTopMargin(MatchWrap(), 18));
        durationDetail = Label(string.Empty, 12, Muted);
        durationDetail.SetTypeface(
            global::Android.Graphics.Typeface.Monospace,
            global::Android.Graphics.TypefaceStyle.Normal);
        analysisCard.AddView(durationDetail, WithTopMargin(MatchWrap(), 6));
        root.AddView(analysisCard, WithBottomMargin(MatchWrap(), 14));

        var deviceCard = Card(SoftBackground);
        deviceCard.AddView(Eyebrow("GERÄT"));
        storedDetail = Label("Gerätespeicher wird nach Verbindung angezeigt", 13, Muted);
        deviceCard.AddView(storedDetail, WithTopMargin(MatchWrap(), 4));
        sendButton = Button("Text auf CopyCop speichern", Primary);
        sendButton.Click += async (_, _) => await SendSelectedAsync();
        deviceCard.AddView(sendButton, WithTopMargin(MatchWrap(), 13));
        cancelButton = Button("Übertragung abbrechen", Amber);
        cancelButton.Visibility = ViewStates.Gone;
        cancelButton.Click += (_, _) => transferCancellation?.Cancel();
        deviceCard.AddView(cancelButton, WithTopMargin(MatchWrap(), 8));
        transferProgress = new ProgressBar(
            this, null, global::Android.Resource.Attribute.ProgressBarStyleHorizontal)
        {
            Max = 1000,
            Visibility = ViewStates.Gone,
            ProgressTintList = ColorStateList.ValueOf(Green)
        };
        deviceCard.AddView(transferProgress, WithTopMargin(new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, Dp(7)), 10));
        root.AddView(deviceCard, WithBottomMargin(MatchWrap(), 14));

        var helpCard = Card(SoftBackground);
        helpCard.AddView(Eyebrow("SO GEHT'S"));
        helpCard.AddView(Label(
            "1. CopyCop abziehen.\n"
            + "2. C am Gerät halten und per USB-OTG mit dem Handy verbinden.\n"
            + "3. Text übernehmen, prüfen und speichern.\n"
            + "4. CopyCop abziehen und am Ziel-PC im grünen Modus einstecken.\n"
            + "5. Cursor platzieren und V drücken.",
            13, Muted), WithTopMargin(MatchWrap(), 7));
        root.AddView(helpCard, WithBottomMargin(MatchWrap(), 14));

        activityDetail = Label("Bereit für deine Zwischenablage.", 13, Muted);
        activityDetail.SetPadding(Dp(12), Dp(10), Dp(12), Dp(10));
        activityDetail.Background = RoundedDrawable(SoftBackground, Border, 10);
        root.AddView(activityDetail, MatchWrap());

        SetContentView(scroll);
        Reassess(clearParts: true);
    }

    private LinearLayout Card(Color color)
    {
        var card = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Vertical
        };
        card.SetPadding(Dp(17), Dp(16), Dp(17), Dp(16));
        card.Background = RoundedDrawable(color, Border, 16);
        return card;
    }

    private TextView Eyebrow(string text) => Label(text, 11, Muted, true);

    private TextView Label(string text, float size, Color color, bool bold = false)
    {
        var view = new TextView(this) { Text = text };
        view.SetTextColor(color);
        view.SetTextSize(global::Android.Util.ComplexUnitType.Sp, size);
        view.SetLineSpacing(0, 1.12f);
        if (bold)
            view.SetTypeface(
                global::Android.Graphics.Typeface.Default,
                global::Android.Graphics.TypefaceStyle.Bold);
        return view;
    }

    private Button Button(string text, Color tint)
    {
        var button = new Button(this) { Text = text };
        button.SetTextColor(Color.White);
        button.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 14);
        button.SetAllCaps(false);
        button.BackgroundTintList = ColorStateList.ValueOf(tint);
        return button;
    }

    private GradientDrawable RoundedDrawable(Color fill, Color stroke, int radiusDp)
    {
        var drawable = new GradientDrawable();
        drawable.SetColor(fill);
        drawable.SetCornerRadius(Dp(radiusDp));
        drawable.SetStroke(Dp(1), stroke);
        return drawable;
    }

    private int Dp(int value) => (int)Math.Round(value * Resources!.DisplayMetrics!.Density);

    private static LinearLayout.LayoutParams MatchWrap() => new(
        ViewGroup.LayoutParams.MatchParent,
        ViewGroup.LayoutParams.WrapContent);

    private LinearLayout.LayoutParams WithTopMargin(
        LinearLayout.LayoutParams parameters,
        int marginDp)
    {
        parameters.TopMargin = Dp(marginDp);
        return parameters;
    }

    private LinearLayout.LayoutParams WithBottomMargin(
        LinearLayout.LayoutParams parameters,
        int marginDp)
    {
        parameters.BottomMargin = Dp(marginDp);
        return parameters;
    }

    private static string FriendlyMessage(Exception exception) => exception switch
    {
        TimeoutException => "CopyCop antwortet nicht.",
        UnauthorizedAccessException => "USB-Zugriff nicht erlaubt.",
        _ => exception.Message
    };

    internal sealed class UsbBroadcastReceiver(MainActivity activity) : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            if (intent is not null) activity.HandleUsbBroadcast(intent);
        }
    }
}
