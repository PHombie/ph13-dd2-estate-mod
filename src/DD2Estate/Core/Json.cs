using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace DD2Estate.Core
{
    /// <summary>Tolerant readers for save data: a missing or mistyped value gives the fallback instead of throwing.</summary>
    internal static class Json
    {
        public static int Int(JToken token, int fallback) => (int)Long(token, fallback);

        public static long Long(JToken token, long fallback)
        {
            if (token == null) return fallback;
            switch (token.Type)
            {
                case JTokenType.Integer: return (long)token;
                case JTokenType.Float: return (long)(double)token;
                case JTokenType.String: return long.TryParse((string)token, out var value) ? value : fallback;
                default: return fallback;
            }
        }

        public static double Number(JToken token, double fallback)
        {
            if (token == null) return fallback;
            switch (token.Type)
            {
                case JTokenType.Integer:
                case JTokenType.Float: return (double)token;
                default: return fallback;
            }
        }

        public static bool Bool(JToken token, bool fallback) => token != null && token.Type == JTokenType.Boolean ? (bool)token : fallback;

        public static T Enum<T>(JToken token, T fallback) where T : struct
        {
            return token != null && token.Type == JTokenType.String && System.Enum.TryParse((string)token, out T value) ? value : fallback;
        }

        public static IEnumerable<JToken> Array(JToken token) => token as JArray ?? (IEnumerable<JToken>)new JToken[0];

        /// <summary>DD1 JSON files may start with a byte order mark, which Newtonsoft rejects.</summary>
        public static JObject ParseFile(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            try { return JObject.Parse(text.TrimStart((char)0xFEFF)); }
            catch (Newtonsoft.Json.JsonException) { return null; }
        }
    }
}
