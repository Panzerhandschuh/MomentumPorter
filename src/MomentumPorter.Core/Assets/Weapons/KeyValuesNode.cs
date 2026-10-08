namespace MomentumPorter.Assets.Weapons;

using System.Text;

/// <summary>
/// A KeyValues (KV1) node as used by weapon scripts and soundscripts: either a key with a string value, or a key
/// with child nodes. Keys match case-insensitively, like the engine's. Comments aren't kept.
/// </summary>
public sealed class KeyValuesNode
{
    public KeyValuesNode(string key, string? value = null)
    {
        Key = key;
        Value = value;
    }

    public string Key { get; set; }

    /// <summary>The value of a leaf, or null for a block.</summary>
    public string? Value { get; set; }

    /// <summary>A platform conditional such as <c>[$X360]</c> that followed the node, kept as written.</summary>
    public string? Condition { get; set; }

    public List<KeyValuesNode> Children { get; } = [];

    public bool IsBlock => Value is null;

    public KeyValuesNode? Find(string key) => Children.FirstOrDefault(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    public IEnumerable<KeyValuesNode> FindAll(string key) => Children.Where(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    /// <summary>The value of a child leaf, or null.</summary>
    public string? GetString(string key) => Find(key) is { IsBlock: false } node ? node.Value : null;

    /// <summary>
    /// Sets a child leaf's value, adding it at the end (or after <paramref name="after"/>) if there's none.
    /// </summary>
    public KeyValuesNode Set(string key, string value, string? after = null)
    {
        KeyValuesNode? node = Find(key);
        if (node is not null && !node.IsBlock)
        {
            node.Value = value;
            return node;
        }

        if (node is not null)
            Children.Remove(node);

        node = new KeyValuesNode(key, value);
        int index = after is null ? -1 : Children.FindIndex(x => x.Key.Equals(after, StringComparison.OrdinalIgnoreCase));
        Children.Insert(index >= 0 ? index + 1 : Children.Count, node);
        return node;
    }

    /// <summary>
    /// The child block with this key, added (empty) if there's none.
    /// </summary>
    public KeyValuesNode GetOrAddBlock(string key)
    {
        KeyValuesNode? node = Find(key);
        if (node is { IsBlock: true })
            return node;

        if (node is not null)
            Children.Remove(node);

        node = new KeyValuesNode(key);
        Children.Add(node);
        return node;
    }

    public bool Remove(string key) => Children.RemoveAll(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase)) > 0;

    public KeyValuesNode Clone(string? key = null)
    {
        var clone = new KeyValuesNode(key ?? Key, Value) { Condition = Condition };
        clone.Children.AddRange(Children.Select(x => x.Clone()));
        return clone;
    }

    /// <summary>
    /// Parses a file's top level nodes, returned as the children of a node with an empty key.
    /// </summary>
    /// <exception cref="FormatException">The text isn't balanced KeyValues</exception>
    public static KeyValuesNode ParseDocument(string text, string? fileName = null)
    {
        var root = new KeyValuesNode("");
        var tokens = new Tokenizer(text);
        ParseChildren(tokens, root, fileName ?? "KeyValues", topLevel: true);
        return root;
    }

    /// <summary>
    /// Parses a file holding a single top level block, such as a weapon script's <c>WeaponData</c>.
    /// </summary>
    public static KeyValuesNode Parse(string text, string? fileName = null)
    {
        KeyValuesNode root = ParseDocument(text, fileName);
        return root.Children.FirstOrDefault(x => x.IsBlock) ?? throw new FormatException($"{fileName ?? "KeyValues"} has no block");
    }

    private static void ParseChildren(Tokenizer tokens, KeyValuesNode parent, string fileName, bool topLevel)
    {
        while (true)
        {
            Token? key = tokens.Next();
            if (key is null)
            {
                if (topLevel)
                    return;

                throw new FormatException($"{fileName}: missing '}}' to close {parent.Key}");
            }

            if (key.Value.IsBrace)
            {
                if (key.Value.Text == "}" && !topLevel)
                    return;

                throw new FormatException($"{fileName}: unexpected '{key.Value.Text}' in {parent.Key}");
            }

            // Conditionals may sit between a key and its block too
            tokens.SkipConditional();
            Token value = tokens.Next() ?? throw new FormatException($"{fileName}: {key.Value.Text} has no value");
            var node = new KeyValuesNode(key.Value.Text);
            if (value.IsBrace)
            {
                if (value.Text != "{")
                    throw new FormatException($"{fileName}: {key.Value.Text} has no value");

                ParseChildren(tokens, node, fileName, topLevel: false);
            }
            else
            {
                node.Value = value.Text;
            }

            node.Condition = tokens.ReadConditional();
            parent.Children.Add(node);
        }
    }

    /// <summary>
    /// Writes the node (or, for a document root with an empty key, its children) as KeyValues text.
    /// </summary>
    public string Write()
    {
        var builder = new StringBuilder();
        if (Key.Length == 0 && IsBlock)
        {
            foreach (KeyValuesNode child in Children)
            {
                child.Write(builder, 0);
                if (child.IsBlock)
                    builder.Append('\n');
            }
        }
        else
        {
            Write(builder, 0);
        }

        return builder.ToString();
    }

    private void Write(StringBuilder builder, int depth)
    {
        string indent = new('\t', depth);
        string condition = Condition is null ? "" : " " + Condition;
        if (!IsBlock)
        {
            builder.Append(indent).Append(Quote(Key)).Append('\t').Append(Quote(Value!)).Append(condition).Append('\n');
            return;
        }

        builder.Append(indent).Append(Quote(Key)).Append(condition).Append('\n');
        builder.Append(indent).Append("{\n");
        foreach (KeyValuesNode child in Children)
            child.Write(builder, depth + 1);
        builder.Append(indent).Append("}\n");
    }

    private static string Quote(string text) => "\"" + text + "\"";

    public override string ToString() => IsBlock ? $"{Key} {{{Children.Count}}}" : $"{Key} = {Value}";

    private readonly record struct Token(string Text, bool IsBrace);

    private sealed class Tokenizer(string text)
    {
        private int _position;

        public Token? Next()
        {
            SkipWhitespaceAndComments();
            if (_position >= text.Length)
                return null;

            char c = text[_position];
            if (c is '{' or '}')
            {
                _position++;
                return new Token(c.ToString(), true);
            }

            if (c == '"')
            {
                int close = text.IndexOf('"', _position + 1);
                if (close < 0)
                    close = text.Length;

                string value = text[(_position + 1)..close];
                _position = Math.Min(close + 1, text.Length);
                return new Token(value, false);
            }

            int start = _position;
            while (_position < text.Length && !char.IsWhiteSpace(text[_position]) && text[_position] is not ('{' or '}' or '"'))
            {
                if (text[_position] == '/' && _position + 1 < text.Length && text[_position + 1] == '/')
                    break;

                _position++;
            }

            return new Token(text[start.._position], false);
        }

        public void SkipConditional() => ReadConditional();

        /// <summary>
        /// Reads a following <c>[$PLATFORM]</c> conditional, if there's one on the same line.
        /// </summary>
        public string? ReadConditional()
        {
            int position = _position;
            while (position < text.Length && text[position] is ' ' or '\t')
                position++;

            if (position >= text.Length || text[position] != '[')
                return null;

            int close = text.IndexOf(']', position);
            if (close < 0)
                return null;

            _position = close + 1;
            return text[position.._position];
        }

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
                else if (text[_position] == '/' && _position + 1 < text.Length && text[_position + 1] == '*')
                {
                    int end = text.IndexOf("*/", _position + 2, StringComparison.Ordinal);
                    _position = end < 0 ? text.Length : end + 2;
                }
                else
                {
                    break;
                }
            }
        }
    }
}
