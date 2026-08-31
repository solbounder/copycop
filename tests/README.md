# Tests

Die Tests benötigen .NET 8; die Abhängigkeiten werden über NuGet wiederhergestellt:

```powershell
dotnet run --project tests/CopyCop.Cli.Tests -c Release
```

Sie prüfen CRC32, HID-Protokoll-Roundtrip und Fehlererkennung, Chunking,
Unicode-Normalisierung und -Ablehnung, deutsche QWERTZ-Zuordnung inklusive
Shift/AltGr/Dead Keys, exakte UTF-8-Grenzen, Kapazitätsbewertung,
Unicode-sichere Aufteilung und die vorgegebenen Text-/Codebeispiele.

USB-Anmeldung, LEDs, Tasten und Flash-Stromausfälle benötigen den in
`docs/phases.md` beschriebenen Hardwaretest.
