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

Die Pakettests prüfen bytegenaue Wiederherstellung von Text- und Binärdateien,
Kompression, portable Dateinamen, Nummerierung und Gerätegrenzen, vertauschte
und doppelte Teile, fehlende Teile, beschädigte Daten, sichere Pfade,
Entpacklimits und die neuen GUI-Aktionen einschließlich Fehler und Abbruch.

Zusätzlich testet Node.js (22+ mit Compression Streams) dieselbe Codec-Implementierung,
die in der lokalen Empfangsseite läuft, sowie native Pakete aus der .NET-CLI:

```powershell
dotnet build host/copycop-cli/copycop-cli.csproj -c Release
node tests/bundle-web.test.mjs
```

Temporäre Fixtures bleiben im ignorierten `build/bundle-tests`-Ordner; ein
anderer Scratch-Ordner kann als erstes Argument übergeben werden.

Die gemeinsame Dateiliste und Desktop-ViewModel-Tests prüfen zusätzlich
nacheinander ausgewählte Einzeldateien, ergänzte Ordner, identische Duplikate,
Namenskonflikte, Abbruch, Entfernen, Leeren und das Ergänzen wieder geöffneter
Pakete. Veraltete Pakete sind nach Änderungen der Dateiliste nicht mehr sendbar.
Die Browserprüfung stellt sicher, dass keine Paket-Erstellung im HTML enthalten ist.

Die verteilte `web/copycop.html` ist eine komprimierte, selbstentpackende HTML-Datei.
Änderungen erfolgen in `web/copycop.source.html`; anschließend mit
`node web/build-receiver.mjs` neu erzeugen. Die Tests vergleichen den entpackten
Inhalt mit der Quelldatei und prüfen die tatsächlich verteilte HTML mit CopyCops
Zeichenprüfung, Normalisierung und Gerätegrenze. So bleibt auch die Empfangsseite
selbst ohne Zeichenersetzung in einem Geräteteil übertragbar.
