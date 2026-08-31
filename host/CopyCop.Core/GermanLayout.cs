using System.Text;

namespace CopyCop.Core;

public readonly record struct KeyStroke(byte Modifiers, byte Keycode);
public sealed record KeySequence(params KeyStroke[] Strokes);

public static class GermanLayout
{
    public const byte Shift = 0x02;
    public const byte AltGr = 0x40;

    private const byte A = 0x04;
    private const byte Digit1 = 0x1E;
    private const byte Digit0 = 0x27;
    private const byte Enter = 0x28;
    private const byte Tab = 0x2B;
    private const byte Space = 0x2C;
    private const byte Minus = 0x2D;
    private const byte Equal = 0x2E;
    private const byte BracketLeft = 0x2F;
    private const byte BracketRight = 0x30;
    private const byte Backslash = 0x31;
    private const byte Semicolon = 0x33;
    private const byte Apostrophe = 0x34;
    private const byte Comma = 0x36;
    private const byte Period = 0x37;
    private const byte Slash = 0x38;
    private const byte Europe2 = 0x64;

    private static KeySequence One(byte modifiers, byte key) =>
        new(new KeyStroke(modifiers, key));
    private static KeySequence Dead(byte modifiers, byte key) =>
        new(new KeyStroke(modifiers, key), new KeyStroke(0, Space));

    public static bool TryMap(Rune rune, out KeySequence sequence)
    {
        var value = rune.Value;
        if (value is >= 'a' and <= 'z')
        {
            var key = (byte)(A + value - 'a');
            if (value == 'y') key = (byte)(A + 'z' - 'a');
            if (value == 'z') key = (byte)(A + 'y' - 'a');
            sequence = One(0, key);
            return true;
        }
        if (value is >= 'A' and <= 'Z')
        {
            var key = (byte)(A + value - 'A');
            if (value == 'Y') key = (byte)(A + 'Z' - 'A');
            if (value == 'Z') key = (byte)(A + 'Y' - 'A');
            sequence = One(Shift, key);
            return true;
        }
        if (value is >= '1' and <= '9')
        {
            sequence = One(0, (byte)(Digit1 + value - '1'));
            return true;
        }

        sequence = value switch
        {
            '0' => One(0, Digit0),
            ' ' => One(0, Space),
            '\t' => One(0, Tab),
            '\n' => One(0, Enter),
            '!' => One(Shift, Digit1),
            '"' => One(Shift, Digit1 + 1),
            '§' => One(Shift, Digit1 + 2),
            '$' => One(Shift, Digit1 + 3),
            '%' => One(Shift, Digit1 + 4),
            '&' => One(Shift, Digit1 + 5),
            '/' => One(Shift, Digit1 + 6),
            '(' => One(Shift, Digit1 + 7),
            ')' => One(Shift, Digit1 + 8),
            '=' => One(Shift, Digit0),
            '?' => One(Shift, Minus),
            '`' => Dead(Shift, Equal),
            '´' => Dead(0, Equal),
            '+' => One(0, BracketRight),
            '*' => One(Shift, BracketRight),
            '~' => Dead(AltGr, BracketRight),
            '#' => One(0, Backslash),
            '\'' => One(Shift, Backslash),
            '-' => One(0, Slash),
            '_' => One(Shift, Slash),
            '.' => One(0, Period),
            ',' => One(0, Comma),
            ':' => One(Shift, Period),
            ';' => One(Shift, Comma),
            '<' => One(0, Europe2),
            '>' => One(Shift, Europe2),
            '|' => One(AltGr, Europe2),
            '@' => One(AltGr, A + 'q' - 'a'),
            '€' => One(AltGr, A + 'e' - 'a'),
            '[' => One(AltGr, Digit1 + 7),
            ']' => One(AltGr, Digit1 + 8),
            '{' => One(AltGr, Digit1 + 6),
            '}' => One(AltGr, Digit0),
            '\\' => One(AltGr, Minus),
            'ä' => One(0, Apostrophe),
            'ö' => One(0, Semicolon),
            'ü' => One(0, BracketLeft),
            'Ä' => One(Shift, Apostrophe),
            'Ö' => One(Shift, Semicolon),
            'Ü' => One(Shift, BracketLeft),
            'ß' => One(0, Minus),
            _ => null!,
        };
        return sequence is not null;
    }
}
