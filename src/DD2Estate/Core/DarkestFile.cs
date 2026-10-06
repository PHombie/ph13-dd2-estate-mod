using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DD2Estate.Core
{
    /// <summary>One <c>name: .field value value .field value</c> block of a DD1 <c>.darkest</c> file.</summary>
    public sealed class DarkestBlock
    {
        public string Name;
        public readonly Dictionary<string, List<string>> Fields = new Dictionary<string, List<string>>();

        public List<string> All(string field) => Fields.TryGetValue(field, out var values) ? values : new List<string>();

        public string Text(string field, int index = 0, string fallback = null)
        {
            return Fields.TryGetValue(field, out var values) && index < values.Count ? values[index] : fallback;
        }

        public double Number(string field, int index, double fallback)
        {
            var text = Text(field, index);
            return text != null && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;
        }

        public int Int(string field, int index, int fallback) => (int)Number(field, index, fallback);
    }

    public static class DarkestFile
    {
        /// <summary>Blocks in file order. A block may sit on one line or run over several.</summary>
        public static List<DarkestBlock> Parse(string text)
        {
            var blocks = new List<DarkestBlock>();
            if (string.IsNullOrEmpty(text)) return blocks;
            DarkestBlock block = null;
            List<string> values = null;
            foreach (var line in text.Split('\n'))
            {
                foreach (var token in Tokens(line))
                {
                    if (token.Quoted)
                    {
                        values?.Add(token.Text);
                    }
                    else if (token.Text.Length > 1 && token.Text[0] != '.' && token.Text[token.Text.Length - 1] == ':')
                    {
                        block = new DarkestBlock { Name = token.Text.Substring(0, token.Text.Length - 1) };
                        blocks.Add(block);
                        values = null;
                    }
                    else if (block != null && IsFieldName(token.Text))
                    {
                        var name = token.Text.Substring(1);
                        if (!block.Fields.TryGetValue(name, out values))
                            block.Fields[name] = values = new List<string>();
                    }
                    else
                    {
                        values?.Add(token.Text);
                    }
                }
            }
            return blocks;
        }

        // ".5" is a number, ".chance" is a field
        private static bool IsFieldName(string token) => token.Length > 1 && token[0] == '.' && (char.IsLetter(token[1]) || token[1] == '_');

        private struct Token
        {
            public string Text;
            public bool Quoted;
        }

        private static IEnumerable<Token> Tokens(string line)
        {
            var i = 0;
            while (i < line.Length)
            {
                var c = line[i];
                if (char.IsWhiteSpace(c) || c == (char)0xFEFF) { i++; continue; }
                if (c == '/' && i + 1 < line.Length && line[i + 1] == '/') yield break;
                if (c == '"')
                {
                    var end = line.IndexOf('"', i + 1);
                    if (end < 0) end = line.Length;
                    yield return new Token { Text = line.Substring(i + 1, end - i - 1), Quoted = true };
                    i = end + 1;
                    continue;
                }
                var sb = new StringBuilder();
                while (i < line.Length && !char.IsWhiteSpace(line[i])) sb.Append(line[i++]);
                yield return new Token { Text = sb.ToString() };
            }
        }
    }
}
