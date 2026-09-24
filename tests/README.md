# Tests

Die Tests benötigen .NET 8; die Abhängigkeiten werden über NuGet wiederhergestellt:

```powershell
dotnet run --project tests/CopyCop.Cli.Tests -c Release
```

Sie prüfen CRC32, HID-Protokoll-Roundtrip und Fehlererkennung, Chunking,
Unicode-Normalisierung und -Ablehnung, deutsche QWERTZ-Zuordnung inklusive
Shift/AltGr/Dead Keys, exakte UTF-8-Grenzen, Kapazitätsbewertung,
firmwaretreue Tippdauer, Unicode-sichere Aufteilung und die vorgegebenen
Text-/Codebeispiele. Der Dateiimport wird mit UTF-8/16/32, BOMs, ungültigen
Bytes, binären Steuerzeichen, Größenlimits, Abbruch und kurzen Stream-Lesevorgängen
geprüft. Tests des GUI-ViewModels prüfen außerdem die Übernahme in den Editor,
die bestehende Zeichenprüfung, gesperrte Aktionen während des Imports und den
Erhalt des bisherigen Inhalts bei Abbruch oder Lesefehlern.

USB-Anmeldung, LEDs, Tasten und Flash-Stromausfälle benötigen den in
`docs/phases.md` beschriebenen Hardwaretest.
