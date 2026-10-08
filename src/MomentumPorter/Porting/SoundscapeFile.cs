namespace MomentumPorter.Porting;

/// <summary>
/// The soundscapes defined in a soundscape script (scripts/soundscapes_*.txt), keeping each definition's original text.
/// </summary>
internal sealed class SoundscapeFile
{
    private SoundscapeFile(string path, List<Soundscape> soundscapes)
    {
        Path = path;
        Soundscapes = soundscapes;
    }

    public string Path { get; }

    public List<Soundscape> Soundscapes { get; }

    public static SoundscapeFile Parse(string path, string text)
    {
        var tokens = new Tokenizer(text);
        var soundscapes = new List<Soundscape>();
        while (tokens.Next() is { } name)
        {
            if (name.IsBrace)
                throw new FormatException($"{path}: unexpected '{name.Value}' at offset {name.Start}");

            Token open = tokens.Next() ?? throw new FormatException($"{path}: {name.Value} has no body");
            if (open.Value != "{" || !open.IsBrace)
                throw new FormatException($"{path}: expected '{{' after {name.Value}");

            var references = new List<string>();
            int end = ParseBlock(tokens, path, references);
            soundscapes.Add(new Soundscape(name.Value, text[name.Start..end], references));
        }

        return new SoundscapeFile(path, soundscapes);
    }

    // Returns the offset after the block's closing brace. Collects the soundscapes its playsoundscape rules play.
    private static int ParseBlock(Tokenizer tokens, string path, List<string> references, bool inPlaySoundscape = false)
    {
        while (true)
        {
            Token key = tokens.Next() ?? throw new FormatException($"{path}: missing '}}'");
            if (key.IsBrace && key.Value == "}")
                return key.End;

            Token value = tokens.Next() ?? throw new FormatException($"{path}: {key.Value} has no value");
            if (value.IsBrace && value.Value == "{")
            {
                ParseBlock(tokens, path, references, key.Value.Equals("playsoundscape", StringComparison.OrdinalIgnoreCase));
                continue;
            }

            if (value.IsBrace)
                throw new FormatException($"{path}: {key.Value} has no value");

            if (inPlaySoundscape && key.Value.Equals("name", StringComparison.OrdinalIgnoreCase))
                references.Add(value.Value);

            // Platform conditionals, e.g. "key" "value" [$X360]
            tokens.SkipConditional();
        }
    }

    public sealed record Soundscape(string Name, string Text, List<string> PlaysSoundscapes);

    private readonly record struct Token(string Value, int Start, int End, bool IsBrace);

    private sealed class Tokenizer(string text)
    {
        private int _position;

        public Token? Next()
        {
            SkipWhitespaceAndComments();
            if (_position >= text.Length)
                return null;

            int start = _position;
            char c = text[_position];
            if (c is '{' or '}')
            {
                _position++;
                return new Token(c.ToString(), start, _position, true);
            }

            if (c == '"')
            {
                int close = text.IndexOf('"', _position + 1);
                if (close < 0)
                    close = text.Length;

                _position = Math.Min(close + 1, text.Length);
                return new Token(text[(start + 1)..close], start, _position, false);
            }

            while (_position < text.Length && !char.IsWhiteSpace(text[_position]) && text[_position] is not ('{' or '}' or '"'))
                _position++;

            return new Token(text[start.._position], start, _position, false);
        }

        public void SkipConditional()
        {
            int position = _position;
            SkipWhitespaceAndComments();
            if (_position < text.Length && text[_position] == '[')
            {
                int close = text.IndexOf(']', _position);
                _position = close < 0 ? text.Length : close + 1;
            }
            else
            {
                _position = position;
            }
        }

        private void SkipWhitespaceAndComments()
        {
            while (_position < text.Length)
            {
                if (char.IsWhiteSpace(text[_position]))
                {
                    _position++;
                }
                else if (text[_position] == '/' && _position + 1 < text.Length && text[_position + 1] == '/')
                {
                    int newline = text.IndexOf('\n', _position);
                    _position = newline < 0 ? text.Length : newline + 1;
                }
                else
                {
                    break;
                }
            }
        }
    }
}
