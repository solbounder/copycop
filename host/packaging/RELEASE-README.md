# CopyCop Hostprogramme

**Text komprimieren – Empfang über HTML** ist in Desktop und Android optional
für eingefügten Text verfügbar. Der Originaltext bleibt im Editor; Paketgröße,
Ersparnis, Tippdauer und Aufteilung beziehen sich auf die übertragene Fassung.
Am Ziel in `copycop.html` empfangen; der Text wird angezeigt und kann kopiert
oder gespeichert werden (Dateiname aus den ersten Wörtern). Für direktes Tippen
und zum Übertragen der selbstentpackenden Empfangs-HTML den Haken ausschalten.

- `CopyCop` / `CopyCop.exe`: grafische Oberfläche ohne Konsolenfenster
- `copycop-cli` / `copycop-cli.exe`: optionale Kommandozeile

Das Quellgerät muss im blauen LOAD-Modus angeschlossen sein: CopyCop abziehen,
mittlere C-Taste halten, USB einstecken und bei blauem Licht loslassen.

Linux: Beide ausführbaren Dateien gegebenenfalls mit `chmod +x` ausführbar
machen. Für HID-Zugriff `99-copycop.rules` als root nach
`/etc/udev/rules.d/` kopieren, die udev-Regeln neu laden und CopyCop neu
einstecken. Die CLI benötigt zusätzlich `xsel`; die GUI nicht.

macOS: Beim ersten Start kann für den nicht signierten lokalen Build
Rechtsklick → Öffnen nötig sein. Die CLI liegt neben `CopyCop.app`.

Jeder gespeicherte Teil darf maximal 126.464 UTF-8-Bytes groß sein. Die GUI
zeigt die genaue Belegung, kann längere Texte verlustfrei aufteilen und
berechnet die Tippdauer für alle acht Geschwindigkeiten.

Der Reiter `Bedienung & LEDs` erklärt alle physischen Tastenkombinationen,
Startmodi, Geschwindigkeitsfarben und weiteren LED-Signale direkt in der GUI.
Im Normalmodus ändern Strg und C das Tempo auch während CopyCop gerade tippt.
V pausiert beziehungsweise setzt mit kurzem Druck fort; etwa 0,8 Sekunden
Haltedauer brechen die Ausgabe vollständig ab.

## .copycop-Dateipakete

Mit „Dateien hinzufügen …“ oder „Ordner hinzufügen …“ die Dateiliste Schritt
für Schritt ergänzen. „Paket erstellen“ bündelt die aktuelle Auswahl mit
optionaler Kompression; „Als .copycop speichern …“ legt sie ab. Auf dem Zielrechner die
beiliegende `copycop.html` öffnen, alle Teile im Empfangsfeld tippen lassen und
„Prüfen & entpacken“ wählen; vollständige Pakete werden auch automatisch
entpackt. Dateien lassen sich anzeigen, kopieren oder speichern. Mit dem
Schalter „Maske“ zeigt die Seite statt des getippten Textes nur einen
Ladebildschirm mit Knopf und Fortschrittsbalken; Esc schaltet sie aus. Die HTML-Datei ist ausschließlich der Empfänger
und funktioniert lokal ohne Upload. Dieselbe Dateiverwaltung bietet die Android-App.
Die CLI kann mit `pack` und `unpack` ebenfalls Pakete erstellen und entpacken.
