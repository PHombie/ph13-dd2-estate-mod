using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace DD2Estate.Dev
{
    /// <summary>
    /// Tiny reflection path evaluator for the dev bridge: "Namespace.Type.Member.Member[3].Method()".
    /// Reads only: fields, properties, indexers with an int/string literal, and parameterless methods.
    /// </summary>
    internal static class Reflect
    {
        private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.FlattenHierarchy;

        public static object Eval(string expr)
        {
            if (string.IsNullOrWhiteSpace(expr)) throw new ArgumentException("empty expr");
            expr = expr.Trim();
            object root = null;
            if (expr[0] == '$' || expr[0] == '@')
            {
                // $TypeName = first live instance of that component type; @Path/To/Object:Component = that component.
                int end;
                if (expr[0] == '$')
                {
                    end = expr.IndexOf('.');
                    if (end < 0) end = expr.Length;
                    root = Instance(expr.Substring(1, end - 1));
                }
                else
                {
                    var colon = expr.IndexOf(':');
                    if (colon < 0) throw new ArgumentException("use @Path/To/Object:Component");
                    end = expr.IndexOf('.', colon);
                    if (end < 0) end = expr.Length;
                    var go = AgentBridge.FindObject(expr.Substring(1, colon - 1));
                    if (go == null) throw new ArgumentException("object not found: " + expr.Substring(1, colon - 1));
                    var compName = expr.Substring(colon + 1, end - colon - 1);
                    root = compName == "GameObject" ? (object)go : go.GetComponents<UnityEngine.Component>().FirstOrDefault(c => c != null && c.GetType().Name == compName);
                    if (root == null) throw new ArgumentException("no component " + compName + " on " + go.name);
                }
                if (end >= expr.Length) return root;
                expr = "__root__" + expr.Substring(end);
            }
            var tokens = Split(expr);

            // The longest dotted prefix that names a loaded type is the root.
            Type type = null;
            var used = 0;
            if (root != null)
            {
                type = root.GetType();
                used = 1;
            }
            for (var n = tokens.Count; n >= 1 && type == null; n--)
            {
                if (tokens.Take(n).Any(t => t.EndsWith(")") || t.Contains("["))) continue;
                type = FindType(string.Join(".", tokens.Take(n)));
                used = n;
            }
            if (type == null) throw new ArgumentException("no loaded type matches the start of '" + expr + "'");

            object current = root;
            var currentType = type;
            for (var i = used; i < tokens.Count; i++)
            {
                var token = tokens[i];
                string index = null;
                var bracket = token.IndexOf('[');
                if (bracket >= 0)
                {
                    index = token.Substring(bracket + 1, token.LastIndexOf(']') - bracket - 1);
                    token = token.Substring(0, bracket);
                }
                if (token.Length > 0)
                {
                    current = GetMember(currentType, current, token);
                    if (current == null && (i < tokens.Count - 1 || index != null))
                        throw new NullReferenceException("'" + token + "' is null in '" + expr + "'");
                }
                if (index != null) current = Index(current, index);
                currentType = current?.GetType();
            }
            return used == tokens.Count && root == null ? type : current;
        }

        /// <summary>First live instance of a component type given by short or full name (active objects first).</summary>
        public static object Instance(string typeName)
        {
            var type = FindType(typeName) ?? FindTypeByShortName(typeName);
            if (type == null) throw new ArgumentException("no loaded type named " + typeName);
            var live = UnityEngine.Object.FindObjectOfType(type);
            if (live != null) return live;
            foreach (var o in UnityEngine.Resources.FindObjectsOfTypeAll(type))
                if (o is UnityEngine.Component c && c.gameObject.scene.IsValid()) return o;
            throw new ArgumentException("no instance of " + type.FullName + " in the loaded scenes");
        }

        private static readonly Dictionary<string, Type> ShortNames = new Dictionary<string, Type>();

        public static Type FindTypeByShortName(string name)
        {
            if (ShortNames.TryGetValue(name, out var cached)) return cached;
            Type found = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types; }
                foreach (var t in types)
                {
                    if (t == null || t.Name != name) continue;
                    found = t;
                    break;
                }
                if (found != null) break;
            }
            ShortNames[name] = found;
            return found;
        }

        /// <summary>Calls a method by name on <paramref name="target"/>; JSON args are converted to the parameter types.</summary>
        public static object Invoke(object target, string method, JArray args)
        {
            if (target == null) throw new NullReferenceException("invoke target is null");
            var isStatic = target is Type;
            var type = target as Type ?? target.GetType();
            var count = args?.Count ?? 0;
            for (var t = type; t != null; t = t.BaseType)
            {
                foreach (var m in t.GetMethods(All | BindingFlags.DeclaredOnly))
                {
                    if (m.Name != method || m.IsGenericMethodDefinition) continue;
                    var ps = m.GetParameters();
                    if (ps.Length != count) continue;
                    if (isStatic && !m.IsStatic) continue;
                    object[] values;
                    try { values = ps.Select((p, i) => FromJson(args[i], p.ParameterType)).ToArray(); }
                    catch { continue; }
                    return m.Invoke(m.IsStatic ? null : target, values);
                }
            }
            throw new MissingMethodException(type.FullName, method + "/" + count);
        }

        public static void Set(object target, string member, JToken value)
        {
            if (target == null) throw new NullReferenceException("set target is null");
            var type = target as Type ?? target.GetType();
            for (var t = type; t != null; t = t.BaseType)
            {
                var f = t.GetField(member, All | BindingFlags.DeclaredOnly);
                if (f != null)
                {
                    f.SetValue(f.IsStatic ? null : target, FromJson(value, f.FieldType));
                    return;
                }
                var p = t.GetProperty(member, All | BindingFlags.DeclaredOnly);
                if (p != null && p.CanWrite)
                {
                    p.SetValue(p.GetSetMethod(true).IsStatic ? null : target, FromJson(value, p.PropertyType), null);
                    return;
                }
            }
            throw new MissingMemberException(type.FullName, member);
        }

        private static object FromJson(JToken token, Type type)
        {
            if (token == null || token.Type == JTokenType.Null) return null;
            if (type.IsEnum) return token.Type == JTokenType.Integer ? Enum.ToObject(type, (int)token) : Enum.Parse(type, (string)token, true);
            // "=expr" passes the value of another reflection path (an object reference) as the argument
            if (token.Type == JTokenType.String && ((string)token).StartsWith("=")) return Eval(((string)token).Substring(1));
            return token.ToObject(type);
        }

        public static object Members(object value, string filter)
        {
            if (value == null) return null;
            var type = value as Type ?? value.GetType();
            var lines = new List<string>();
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (var m in t.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    string line;
                    switch (m)
                    {
                        case FieldInfo f: line = (f.IsStatic ? "static " : "") + "field " + Name(f.FieldType) + " " + f.Name; break;
                        case PropertyInfo p: line = "prop " + Name(p.PropertyType) + " " + p.Name; break;
                        case MethodInfo mi when !mi.IsSpecialName:
                            line = (mi.IsStatic ? "static " : "") + "method " + Name(mi.ReturnType) + " " + mi.Name + "(" + string.Join(", ", mi.GetParameters().Select(x => Name(x.ParameterType) + " " + x.Name)) + ")";
                            break;
                        default: continue;
                    }
                    if (string.IsNullOrEmpty(filter) || line.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) lines.Add(t.Name + ": " + line);
                    if (lines.Count >= 400) return lines;
                }
            }
            return lines;
        }

        public static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = null;
                try { t = asm.GetType(fullName, false); } catch { }
                if (t != null) return t;
            }
            return null;
        }

        private static string Name(Type t)
        {
            if (!t.IsGenericType) return t.Name;
            return t.Name.Split('`')[0] + "<" + string.Join(",", t.GetGenericArguments().Select(Name)) + ">";
        }

        private static object GetMember(Type type, object target, string name)
        {
            if (name.EndsWith("()"))
            {
                name = name.Substring(0, name.Length - 2);
                for (var t = type; t != null; t = t.BaseType)
                {
                    var m = t.GetMethod(name, All | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                    if (m != null) return m.Invoke(m.IsStatic ? null : target, null);
                }
                throw new MissingMethodException(type.FullName, name + "()");
            }
            for (var t = type; t != null; t = t.BaseType)
            {
                var f = t.GetField(name, All | BindingFlags.DeclaredOnly);
                if (f != null) return f.GetValue(f.IsStatic ? null : target);
                var p = t.GetProperty(name, All | BindingFlags.DeclaredOnly);
                if (p != null && p.GetIndexParameters().Length == 0) return p.GetValue(p.GetGetMethod(true).IsStatic ? null : target, null);
            }
            var nested = type.GetNestedType(name, BindingFlags.Public | BindingFlags.NonPublic);
            if (nested != null) return nested;
            throw new MissingMemberException(type.FullName, name);
        }

        private static object Index(object target, string index)
        {
            if (target == null) throw new NullReferenceException("indexing null");
            index = index.Trim().Trim('"', '\'');
            if (target is IList list && int.TryParse(index, out var i)) return list[i];
            if (target is IDictionary dict)
            {
                foreach (DictionaryEntry e in dict)
                    if (Convert.ToString(e.Key) == index) return e.Value;
                throw new KeyNotFoundException(index);
            }
            if (target is IEnumerable en && int.TryParse(index, out var n))
            {
                foreach (var item in en)
                    if (n-- == 0) return item;
                throw new IndexOutOfRangeException(index);
            }
            throw new ArgumentException("cannot index " + target.GetType().Name);
        }

        private static List<string> Split(string expr)
        {
            var tokens = new List<string>();
            var depth = 0;
            var start = 0;
            for (var i = 0; i < expr.Length; i++)
            {
                var c = expr[i];
                if (c == '[') depth++;
                else if (c == ']') depth--;
                else if (c == '.' && depth == 0)
                {
                    tokens.Add(expr.Substring(start, i - start));
                    start = i + 1;
                }
            }
            tokens.Add(expr.Substring(start));
            return tokens;
        }
    }
}
