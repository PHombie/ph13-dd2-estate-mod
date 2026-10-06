using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace DD2Estate.Dd1
{
    /// <summary>
    /// DD1's `.darkest` text format: `name:` blocks, each holding `.key value value ...` entries. Whitespace and
    /// line breaks are free-form (a block may sit on one line), values may be quoted, `//` and `#` start a
    /// comment. Block names can repeat (hero skills, effects), so blocks keep their file order.
    /// </summary>
    internal class DarkestFile
    {
        private static readonly string[] None = new string[0];

        public class Block
        {
            public readonly string Name;
            private readonly Dictionary<string, List<string>> _entries = new Dictionary<string, List<string>>();

            internal Block(string name) { Name = name; }

            public IEnumerable<string> Keys => _entries.Keys;

            public bool Has(string key) => _entries.ContainsKey(key);

            /// <summary>All values of an entry; empty when the key is missing.</summary>
            public IReadOnlyList<string> Values(string key)
            {
                return _entries.TryGetValue(key, out var list) ? (IReadOnlyList<string>)list : None;
            }

            public string String(string key, int index = 0, string fallback = null)
            {
                return _entries.TryGetValue(key, out var list) && index < list.Count ? list[index] : fallback;
            }

            /// <summary>Numeric value; a trailing % (as in `.atk 85%`) is ignored.</summary>
            public float Float(string key, int index = 0, float fallback = 0f)
            {
                var text = String(key, index);
                return text != null && TryNumber(text.TrimEnd('%'), out var value) ? value : fallback;
            }

            public int Int(string key, int index = 0, int fallback = 0)
            {
                return Mathf.RoundToInt(Float(key, index, fallback));
            }

            public Vector2 Vector2(string key)
            {
                return new Vector2(Float(key, 0), Float(key, 1));
            }

            public Vector3 Vector3(string key)
            {
                return new Vector3(Float(key, 0), Float(key, 1), Float(key, 2));
            }

            // A repeated key replaces the earlier entry.
            internal List<string> Start(string key)
            {
                var list = new List<string>();
                _entries[key] = list;
                return list;
            }
        }

        public readonly List<Block> Blocks = new List<Block>();

        /// <summary>First block with this name, or null.</summary>
        public Block Find(string name)
        {
            foreach (var block in Blocks)
                if (block.Name == name) return block;
            return null;
        }

        public IEnumerable<Block> All(string name)
        {
            foreach (var block in Blocks)
                if (block.Name == name) yield return block;
        }

        /// <summary>Reads a file from the DD1 install. Null if DD1 or the file is missing.</summary>
        public static DarkestFile Load(string relative)
        {
            var text = Dd1Install.ReadText(relative);
            if (text == null)
            {
                Plugin.Log.LogWarning("DD1 file missing: " + relative);
                return null;
            }
            return Parse(text);
        }

        public static DarkestFile Parse(string text)
        {
            var file = new DarkestFile();
            Block block = null;
            List<string> values = null;
            int i = 0, n = text.Length;
            while (i < n)
            {
                char c = text[i];
                if (char.IsWhiteSpace(c) || c == '﻿') { i++; continue; }
                if (c == '#' || (c == '/' && i + 1 < n && text[i + 1] == '/'))
                {
                    while (i < n && text[i] != '\n') i++;
                    continue;
                }
                if (c == '"')
                {
                    int end = i + 1;
                    while (end < n && text[end] != '"' && text[end] != '\n') end++;
                    values?.Add(text.Substring(i + 1, end - i - 1));
                    i = end < n && text[end] == '"' ? end + 1 : end;
                    continue;
                }

                int start = i;
                while (i < n && !char.IsWhiteSpace(text[i]) && text[i] != '"' && text[i] != '#'
                       && !(text[i] == '/' && i + 1 < n && text[i + 1] == '/')) i++;
                var token = text.Substring(start, i - start);

                if (token[0] != '.' && token[token.Length - 1] == ':')
                {
                    block = new Block(token.Substring(0, token.Length - 1));
                    file.Blocks.Add(block);
                    values = null;
                }
                else if (token[0] == '.' && token.Length > 1 && !TryNumber(token, out _))
                {
                    if (block == null)
                    {
                        block = new Block("");
                        file.Blocks.Add(block);
                    }
                    values = block.Start(token.Substring(1));
                }
                else
                {
                    values?.Add(token);
                }
            }
            return file;
        }

        // DD1 data always uses '.' decimals, whatever the player's locale.
        private static bool TryNumber(string text, out float value)
        {
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }
}
