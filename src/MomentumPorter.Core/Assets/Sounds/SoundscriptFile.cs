namespace MomentumPorter.Assets.Sounds;

using System.Text;

/// <summary>
/// A key with a string value or a block of child keys, as in Valve's KeyValues text format.
/// </summary>
public sealed class KeyValueNode
{
    public KeyValueNode(string key, string value)
    {
        Key = key;
        Value = value;
    }

    public KeyValueNode(string key, IEnumerable<KeyValueNode> children)
    {
        Key = key;
        Children = children.ToList();
    }

    public string Key { get; set; }

    /// <summary>The value, or null for a block.</summary>
    public string? Value { get; set; }

    /// <summary>The child keys, or null for a plain value.</summary>
    public List<KeyValueNode>? Children { get; }

    /// <summary>A platform conditional after the value, e.g. <c>[$X360]</c>, kept as written.</summary>
    public string? Conditional { get; set; }

    public bool IsBlock => Children is not null;

    /// <summary>The first child with this key (case-insensitive, as the engine compares them).</summary>
    public KeyValueNode? Find(string key) => Children?.FirstOrDefault(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    public string? GetValue(string key) => Find(key)?.Value;

    /// <summary>Sets the first child with this key to a plain value, adding it at the end if there isn't one.</summary>
    public void SetValue(string key, string value)
    {
        if (Children is null)
            throw new InvalidOperationException($"{Key} isn't a block");

        int index = Children.FindIndex(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            Children.Add(new KeyValueNode(key, value));
        else
            Children[index] = new KeyValueNode(Children[index].Key, value);
    }

    public int RemoveAll(string key) => Children?.RemoveAll(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase)) ?? 0;

    internal void Write(StringBuilder text, int indent, string newline)
    {
        text.Append('\t', indent).Append(Quote(Key));
        if (Children is null)
        {
            text.Append('\t').Append(Quote(Value ?? ""));
            if (Conditional is not null)
                text.Append(' ').Append(Conditional);
            text.Append(newline);
            return;
        }

        if (Conditional is not null)
            text.Append(' ').Append(Conditional);
        text.Append(newline).Append('\t', indent).Append('{').Append(newline);
        foreach (KeyValueNode child in Children)
            child.Write(text, indent + 1, newline);
        text.Append('\t', indent).Append('}').Append(newline);
    }

    private static string Quote(string value) => $"\"{value}\"";
}

/// <summary>
/// One sound in a soundscript (game_sounds_*.txt), e.g. <c>"MomPlayer.Jump" { "channel" ... "wave" "player/jump1.wav" }</c>.
/// </summary>
public sealed class SoundscriptEntry
{
    internal SoundscriptEntry(KeyValueNode node, int start, int end, string? original)
    {
        Node = node;
        Start = start;
        End = end;
        OriginalFormat = original;
    }

    public string Name => Node.Key;

    public KeyValueNode Node { get; }

    public string? Channel
    {
        get => Node.GetValue("channel");
        set => SetOrRemove("channel", value);
    }

    public string? Volume
    {
        get => Node.GetValue("volume");
        set => SetOrRemove("volume", value);
    }

    public string? SoundLevel
    {
        get => Node.GetValue("soundlevel");
        set => SetOrRemove("soundlevel", value);
    }

    public string? Pitch
    {
        get => Node.GetValue("pitch");
        set => SetOrRemove("pitch", value);
    }

    /// <summary>The sound files it plays (<c>wave</c>, or the <c>wave</c>s of an <c>rndwave</c> block), relative to sound/.</summary>
    public IReadOnlyList<string> Waves
    {
        get
        {
            var waves = new List<string>();
            foreach (KeyValueNode child in Node.Children!)
            {
                if (child.Key.Equals("wave", StringComparison.OrdinalIgnoreCase) && child.Value is not null)
                    waves.Add(child.Value);
                else if (child.Key.Equals("rndwave", StringComparison.OrdinalIgnoreCase) && child.Children is not null)
                    waves.AddRange(child.Children.Where(x => x.Value is not null && x.Key.Equals("wave", StringComparison.OrdinalIgnoreCase)).Select(x => x.Value!));
            }

            return waves;
        }
    }

    /// <summary>
    /// Replaces what it plays: one file becomes a <c>wave</c>, several an <c>rndwave</c> (the engine picks one at
    /// random each time). Comments in the entry are lost when it's written.
    /// </summary>
    public void SetWaves(IReadOnlyList<string> waves)
    {
        if (waves.Count == 0)
            throw new ArgumentException("A sound needs at least one wave", nameof(waves));

        List<KeyValueNode> children = Node.Children!;
        int index = children.FindIndex(x => x.Key.Equals("wave", StringComparison.OrdinalIgnoreCase) || x.Key.Equals("rndwave", StringComparison.OrdinalIgnoreCase));
        children.RemoveAll(x => x.Key.Equals("wave", StringComparison.OrdinalIgnoreCase) || x.Key.Equals("rndwave", StringComparison.OrdinalIgnoreCase));
        KeyValueNode node = waves.Count == 1
            ? new KeyValueNode("wave", waves[0])
            : new KeyValueNode("rndwave", waves.Select(x => new KeyValueNode("wave", x)));
        children.Insert(index < 0 ? children.Count : Math.Min(index, children.Count), node);
    }

    // Where the entry was in the parsed text ([Start, End)), or -1 for added entries
    internal int Start { get; }

    internal int End { get; }

    // The entry as Format wrote it when parsed, to tell whether it has changed since
    internal string? OriginalFormat { get; }

    internal bool IsModified => OriginalFormat is null || Format("\n") != OriginalFormat;

    internal string Format(string newline) => Format(Node, newline);

    internal static string Format(KeyValueNode node, string newline)
    {
        var text = new StringBuilder();
        node.Write(text, 0, newline);
        return text.ToString();
    }

    private void SetOrRemove(string key, string? value)
    {
        if (value is null)
            Node.RemoveAll(key);
        else
            Node.SetValue(key, value);
    }
}

/// <summary>
/// A soundscript (scripts/game_sounds_*.txt). Writing it back keeps the original text, comments and formatting of
/// everything that wasn't changed; changed entries are rewritten in place and new ones are appended.
/// </summary>
public sealed class SoundscriptFile
{
    private readonly string _text;
    private readonly List<SoundscriptEntry> _entries;
    private readonly HashSet<SoundscriptEntry> _removed = [];

    private SoundscriptFile(string text, List<SoundscriptEntry> entries)
    {
        _text = text;
        _entries = entries;
        NewLine = text.Contains("\r\n") ? "\r\n" : "\n";
    }

    public IReadOnlyList<SoundscriptEntry> Entries => _entries;

    public string NewLine { get; }

    public static SoundscriptFile Load(string path) => Parse(File.ReadAllText(path), path);

    public static SoundscriptFile Parse(string text, string? path = null)
    {
        string where = path ?? "soundscript";
        var tokens = new Tokenizer(text);
        var entries = new List<SoundscriptEntry>();
        while (tokens.Next() is { } name)
        {
            if (name.IsBrace)
                throw new FormatException($"{where}: unexpected '{name.Value}' at line {tokens.LineOf(name.Start)}");

            string? conditional = tokens.TryConditional();
            Token value = tokens.Next() ?? throw new FormatException($"{where}: {name.Value} has no value");
            if (!value.IsBrace)
            {
                // A top-level key/value such as #include: kept as text, not an entry
                tokens.TryConditional();
                continue;
            }

            if (value.Value != "{")
                throw new FormatException($"{where}: unexpected '}}' at line {tokens.LineOf(value.Start)}");

            (List<KeyValueNode> children, int end) = ParseBlock(tokens, where);
            var node = new KeyValueNode(name.Value, children) { Conditional = conditional };
            entries.Add(new SoundscriptEntry(node, name.Start, end, SoundscriptEntry.Format(node, "\n")));
        }

        return new SoundscriptFile(text, entries);
    }

    // Returns the children and the offset after the block's closing brace
    private static (List<KeyValueNode> Children, int End) ParseBlock(Tokenizer tokens, string where)
    {
        var children = new List<KeyValueNode>();
        while (true)
        {
            Token key = tokens.Next() ?? throw new FormatException($"{where}: missing '}}' at the end");
            if (key.IsBrace)
            {
                if (key.Value == "}")
                    return (children, key.End);

                throw new FormatException($"{where}: unexpected '{{' at line {tokens.LineOf(key.Start)}");
            }

            string? blockConditional = tokens.TryConditional();
            Token value = tokens.Next() ?? throw new FormatException($"{where}: {key.Value} has no value");
            if (value.IsBrace && value.Value == "{")
            {
                (List<KeyValueNode> grandChildren, _) = ParseBlock(tokens, where);
                children.Add(new KeyValueNode(key.Value, grandChildren) { Conditional = blockConditional });
                continue;
            }

            if (value.IsBrace)
                throw new FormatException($"{where}: {key.Value} has no value at line {tokens.LineOf(key.Start)}");

            children.Add(new KeyValueNode(key.Value, value.Value) { Conditional = tokens.TryConditional() });
        }
    }

    /// <summary>The first entry with this name (the one the engine uses; it ignores later duplicates).</summary>
    public SoundscriptEntry? Find(string name)
    {
        return _entries.FirstOrDefault(x => !_removed.Contains(x) && x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Appends a new, empty entry.</summary>
    public SoundscriptEntry Add(string name)
    {
        var entry = new SoundscriptEntry(new KeyValueNode(name, []), -1, -1, null);
        _entries.Add(entry);
        return entry;
    }

    /// <summary>Adds a copy of another entry's keys under a new name (e.g. to define a sound like an existing one).</summary>
    public SoundscriptEntry AddCopy(string name, SoundscriptEntry template)
    {
        SoundscriptEntry entry = Add(name);
        entry.Node.Children!.AddRange(template.Node.Children!.Select(Clone));
        return entry;
    }

    public bool Remove(string name)
    {
        SoundscriptEntry? entry = Find(name);
        if (entry is null)
            return false;

        if (entry.Start < 0)
            _entries.Remove(entry);
        else
            _removed.Add(entry);
        return true;
    }

    public string ToText()
    {
        var text = new StringBuilder(_text.Length + 1024);
        int position = 0;
        foreach (SoundscriptEntry entry in _entries.Where(x => x.Start >= 0).OrderBy(x => x.Start))
        {
            text.Append(_text, position, entry.Start - position);
            position = entry.End;
            if (_removed.Contains(entry))
                continue;

            if (entry.IsModified)
                text.Append(entry.Format(NewLine).TrimEnd('\r', '\n'));
            else
                text.Append(_text, entry.Start, entry.End - entry.Start);
        }

        text.Append(_text, position, _text.Length - position);

        foreach (SoundscriptEntry entry in _entries.Where(x => x.Start < 0))
        {
            if (text.Length > 0 && !EndsWithNewLine(text))
                text.Append(NewLine);
            text.Append(NewLine).Append(entry.Format(NewLine));
        }

        return text.ToString();
    }

    public void Save(string path) => File.WriteAllText(path, ToText(), new UTF8Encoding(false));

    private static bool EndsWithNewLine(StringBuilder text) => text[^1] == '\n';

    private static KeyValueNode Clone(KeyValueNode node)
    {
        return node.Children is null
            ? new KeyValueNode(node.Key, node.Value ?? "") { Conditional = node.Conditional }
            : new KeyValueNode(node.Key, node.Children.Select(Clone)) { Conditional = node.Conditional };
    }

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
            {
                if (text[_position] == '/' && _position + 1 < text.Length && text[_position + 1] == '/')
                    break;
                _position++;
            }

            return new Token(text[start.._position], start, _position, false);
        }

        /// <summary>Consumes a platform conditional such as [$X360] if one comes next.</summary>
        public string? TryConditional()
        {
            int position = _position;
            while (_position < text.Length && text[_position] is ' ' or '\t')
                _position++;

            if (_position < text.Length && text[_position] == '[')
            {
                int close = text.IndexOf(']', _position);
                int end = close < 0 ? text.Length : close + 1;
                string conditional = text[_position..end];
                _position = end;
                return conditional;
            }

            _position = position;
            return null;
        }

        public int LineOf(int offset) => text.AsSpan(0, Math.Min(offset, text.Length)).Count('\n') + 1;

        private void SkipWhitespaceAndComments()
        {
            while (_position < text.Length)
            {
                if (char.IsWhiteSpace(text[_position]) || text[_position] == '﻿')
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
