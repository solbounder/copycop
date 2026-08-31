# Implementierungs- und Teststand

| Bereich | Stand |
|---|---|
| offizielles Pin-Mapping, Tasten, LEDs, Boot-Abtastung | implementiert; LED-Farbreihenfolge am Gerät angepasst |
| TARGET als ausschließliches Keyboard-HID | implementiert; unter Windows als einziges USB-Eingabegerät/HID-Tastatur bestätigt |
| LOAD als ausschließliches Generic-HID und Binärprotokoll | implementiert; HELLO/GET_INFO über den neuen Core am echten Gerät bestätigt |
| linkergetrennter Flash, CRC32, A/B-Commit, Speed-Journal | implementiert; Stromausfalltest am Gerät noch offen |
| gemeinsamer .NET-8-Core und plattformübergreifende CLI | implementiert; Windows-, Linux- und macOS-Builds erzeugt |
| Avalonia-GUI ohne Konsole | implementiert; Normal-, Unicode- und Überlaufansicht unter Windows visuell geprüft |
| deutsches QWERTZ, Unicode-Prüfung, Normalisierung, acht Geschwindigkeiten | implementiert und offline getestet; Geschwindigkeit kann auch während der normalen Ausgabe geändert werden |
| firmwaretreue Tippdauer für alle acht Geschwindigkeiten in der GUI | implementiert und offline getestet |
| gestaffeltes echtes AltGr im normalen Zielmodus | implementiert und kompiliert; Test am betroffenen Ziel noch offen |
| AFK-Bootmodus, zufällige Abstände, Endlos-/Einmalausgabe und Stopp | implementiert und kompiliert; Gerätetest noch offen |
| V-Kurzdruck für Pause/Weiter, V-Langdruck für Abbruch/Release-All | implementiert und kompiliert; Gerätetest noch offen |
| Update-Geste, Watchdog, Dokumentation | implementiert |

Automatisch geprüft werden CRC32, Protokoll-Serialisierung und CRC-Ablehnung,
40-Byte-Chunking, Normalisierung, Unicode-Ablehnung und -Ersetzung,
DE-QWERTZ/Shift/AltGr/Dead-Key-Zuordnung, exakte Ein-/Zwei-/Drei-Byte-Grenzen,
verlustfreie Unicode-Aufteilung sowie alle vorgegebenen Testtexte.

Nicht simulierbar sind die echten USB-Deskriptoren unter Windows, das elektrische
Tastenverhalten und ein Stromausfall mitten im Flash-Schreiben. Dafür gibt es
nach dem Flashen einen kurzen manuellen Test:

1. Normal einstecken: nur `CopyCop Keyboard`, grüne Startanzeige.
2. C beim Einstecken halten: nur `CopyCop Clipboard Loader`, blaue Anzeige.
3. Beispieltext laden, abziehen, normal einstecken und mit V in Notepad tippen.
4. Einen längeren Text starten, mit kurzem V pausieren und mit kurzem V an
   derselben Stelle fortsetzen; V anschließend 0,8 Sekunden halten und Abbruch prüfen.
5. Während eines längeren Texts mit Strg verlangsamen und mit C beschleunigen;
   Farbwechsel und geändertes Tipptempo prüfen.
6. Text erneut laden, abziehen, wieder einstecken und Persistenz prüfen.
7. Nur Strg beim Einstecken halten: violette AFK-Anzeige und weiterhin nur
   `CopyCop Keyboard` prüfen.
8. In AFK C für Wiederholung, V für Stopp und physisches Strg+C für genau einen
   zufällig getakteten Durchlauf prüfen.
9. Normal einstecken und den Testtext `@€\\|[]{}~` vollständig und ohne
   Grundzeichen ausgeben.
