using System.Text;
using CopyCop.Core;

var failures = new List<string>();

void Check(bool condition, string name)
{
    if (!condition) failures.Add(name);
}

Check(Crc32.Compute(Encoding.ASCII.GetBytes("123456789")) == 0xCBF43926u, "CRC32");

var frame = new ProtocolFrame((byte)ProtocolType.Data, 0, 42, 80, 0, [1, 2, 3]);
var parsedFrame = ProtocolFrame.Parse(frame.Serialize());
Check(parsedFrame.Type == frame.Type
      && parsedFrame.Status == frame.Status
      && parsedFrame.Sequence == frame.Sequence
      && parsedFrame.Argument0 == frame.Argument0
      && parsedFrame.Argument1 == frame.Argument1
      && parsedFrame.Payload.SequenceEqual(frame.Payload), "protocol round-trip");

var corruptedFrame = frame.Serialize();
corruptedFrame[20] ^= 0x01;
try
{
    _ = ProtocolFrame.Parse(corruptedFrame);
    Check(false, "protocol CRC rejection");
}
catch (InvalidDataException)
{
    Check(true, "protocol CRC rejection");
}

var chunks = TransferClient.Chunk(Enumerable.Range(0, 95).Select(i => (byte)i).ToArray(), 40).ToArray();
Check(chunks.Length == 3 && chunks[0].Length == 40 && chunks[2].Length == 15, "chunking");

var normalized = UnicodeAnalyzer.Analyze("„Hallo“\u00A0— ‘Welt’\r\n", false);
Check(normalized.Text == "\"Hallo\" - 'Welt'\n", "normalization");
Check(normalized.Unsupported.Count == 0, "normalized support");

var unsupported = UnicodeAnalyzer.Analyze("A😀→B", false);
Check(unsupported.Unsupported.Select(item => item.CodePoint).SequenceEqual([0x1F600, 0x2192]), "unsupported Unicode");

var replaced = UnicodeAnalyzer.Analyze("A😀B", true);
Check(replaced.Text == "A?B", "unsupported replacement");

Check(GermanLayout.TryMap(new Rune('z'), out var z) && z.Strokes[0].Keycode == 0x1C, "QWERTZ z");
Check(GermanLayout.TryMap(new Rune('@'), out var at) && at.Strokes[0].Modifiers == GermanLayout.AltGr, "AltGr @");
Check(GermanLayout.TryMap(new Rune('"'), out var quote) && quote.Strokes[0].Modifiers == GermanLayout.Shift, "Shift quote");
Check(GermanLayout.TryMap(new Rune('~'), out var tilde) && tilde.Strokes.Length == 2, "dead-key tilde");
Check(GermanLayout.TryMap(new Rune('ä'), out _), "umlaut");
Check(Encoding.UTF8.GetByteCount(new string('ä', 63_232)) == 126_464, "maximum UTF-8 byte boundary");

var simpleWorkload = TypingDurationEstimator.Analyze("a");
Check(simpleWorkload.StrokeCount == 1 && simpleWorkload.AltGrStrokeCount == 0,
    "typing workload regular key");
Check(simpleWorkload.Estimate(10) == TimeSpan.FromMilliseconds(11),
    "typing duration regular key");
var altGrWorkload = TypingDurationEstimator.Analyze("{");
Check(altGrWorkload.StrokeCount == 1 && altGrWorkload.AltGrStrokeCount == 1,
    "typing workload AltGr");
Check(altGrWorkload.Estimate(10) == TimeSpan.FromMilliseconds(95),
    "typing duration staged AltGr");
var deadAltGrWorkload = TypingDurationEstimator.Analyze("~");
Check(deadAltGrWorkload.StrokeCount == 2 && deadAltGrWorkload.AltGrStrokeCount == 1
      && deadAltGrWorkload.Estimate(10) == TimeSpan.FromMilliseconds(106),
    "typing duration AltGr dead key");
Check(TypingDurationEstimator.SpeedLevelsMilliseconds.SequenceEqual(
        new[] { 10, 25, 50, 100, 250, 500, 750, 1000 }),
    "typing duration speed levels");

var asciiAtLimit = TextCapacity.Assess(new string('x', TextCapacity.FirmwareMaximumBytes), false);
Check(asciiAtLimit.CanTransfer && asciiAtLimit.RequiredParts == 1, "ASCII exact capacity");

var asciiOverLimit = TextCapacity.Assess(new string('x', TextCapacity.FirmwareMaximumBytes + 1), false);
Check(!asciiOverLimit.FitsCapacity && asciiOverLimit.RequiredParts == 2, "ASCII over capacity");

var umlautsAtLimit = TextCapacity.Assess(new string('ä', 63_232), false);
Check(umlautsAtLimit.CanTransfer && umlautsAtLimit.Utf8Bytes == 126_464, "umlaut capacity");

var eurosAtLimit = TextCapacity.Assess(new string('€', 42_154), false);
var eurosOverLimit = TextCapacity.Assess(new string('€', 42_155), false);
Check(eurosAtLimit.FitsCapacity && !eurosOverLimit.FitsCapacity, "three-byte capacity");

Check(!TextCapacity.Assess("A😀B", false).CanTransfer, "unsupported blocks transfer");
Check(TextCapacity.Assess("A😀B", true).CanTransfer, "replacement enables transfer");

var longText = new string('a', TextCapacity.FirmwareMaximumBytes - 20)
               + "\n" + new string('b', 200);
var split = TextSplitter.Split(longText, TextCapacity.FirmwareMaximumBytes);
Check(split.Count == 2, "split part count");
Check(string.Concat(split.Select(part => part.Text)) == longText, "split preserves text");
Check(split.All(part => part.Utf8Bytes <= TextCapacity.FirmwareMaximumBytes), "split byte bounds");
Check(split[0].Text.EndsWith('\n'), "split prefers line boundary");

var unicodeSplitText = string.Concat(Enumerable.Repeat("ä😀", 12));
var unicodeSplit = TextSplitter.Split(unicodeSplitText, 11);
Check(string.Concat(unicodeSplit.Select(part => part.Text)) == unicodeSplitText,
    "Unicode split preserves runes");
Check(unicodeSplit.All(part => Encoding.UTF8.GetByteCount(part.Text) <= 11),
    "Unicode split byte bounds");

var fixtures = new[]
{
    "Hallo Welt!",
    "public static void main(String[] args) {\n    System.out.println(\"Hallo Welt!\");\n}",
    "String json = \"{\\\"name\\\":\\\"Finn\\\",\\\"enabled\\\":true}\";",
    "äöüÄÖÜß @ € \\ | { } [ ] ~ # ' \"",
    "Zeile 1\n\tZeile 2\n",
};
foreach (var fixture in fixtures)
    Check(UnicodeAnalyzer.Analyze(fixture, false).Unsupported.Count == 0, $"fixture: {fixture[..Math.Min(12, fixture.Length)]}");

if (failures.Count > 0)
{
    Console.Error.WriteLine("Fehlgeschlagen: " + string.Join(", ", failures));
    return 1;
}

Console.WriteLine("Alle copycop-cli-Tests erfolgreich.");
return 0;
