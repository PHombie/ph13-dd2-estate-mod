using System;
using System.Collections.Generic;
using System.IO;
using DD2Estate.Dd1;
using Newtonsoft.Json.Linq;

namespace DD2Estate.Estate
{
    /// <summary>
    /// What DD1 charges a hero at the Guild and the Blacksmith, read from the player's DD1 install:
    /// upgrades/heroes/&lt;class&gt;.upgrades.json lists, per skill, weapon and armour, the purchases in order with
    /// their gold price, the building step each one needs (guild.skill_levels, blacksmith.weapon/armour) and
    /// the resolve level it asks for. A DD2 class that DD1 also has uses its own file; any other uses the first
    /// file there is (DD1's prices are the same for all its classes).
    /// </summary>
    internal static class UpgradeHeroRules
    {
        private const string Folder = "upgrades/heroes";
        private const string Suffix = ".upgrades.json";

        public class Purchase
        {
            /// <summary>DD1 gold.</summary>
            public int Gold;
            /// <summary>DD1's hero level for it. The estate has no hero levels yet; kept for when it does.</summary>
            public int ResolveLevel;
            /// <summary>Building steps that must be built.</summary>
            public readonly List<UpgradeRules.Need> Needs = new List<UpgradeRules.Need>();
        }

        private class ClassRules
        {
            public readonly List<Purchase> Skill = new List<Purchase>();
            public readonly List<Purchase> Weapon = new List<Purchase>();
            public readonly List<Purchase> Armour = new List<Purchase>();
        }

        private static readonly Dictionary<string, ClassRules> Loaded = new Dictionary<string, ClassRules>();
        private static readonly JArray Empty = new JArray();
        private static string _anyFile;
        private static bool _searched;

        /// <summary>A combat skill: learning it first, then its levels in order.</summary>
        public static IReadOnlyList<Purchase> Skill(string classId) => For(classId).Skill;

        public static IReadOnlyList<Purchase> Weapon(string classId) => For(classId).Weapon;

        public static IReadOnlyList<Purchase> Armour(string classId) => For(classId).Armour;

        private static ClassRules For(string classId)
        {
            classId = classId ?? "";
            if (Loaded.TryGetValue(classId, out var rules)) return rules;
            rules = new ClassRules();
            try
            {
                var file = Folder + "/" + classId + Suffix;
                var text = classId.Length > 0 ? Dd1Install.ReadText(file) : null;
                if (text == null && AnyFile() != null) text = Dd1Install.ReadText(AnyFile());
                if (text != null) Read(JObject.Parse(text), rules);
                else Plugin.Log.LogWarning("DD1 hero upgrade files missing: " + Folder);
            }
            catch (Exception e) { Plugin.Log.LogWarning("DD1 hero upgrades could not be read for " + classId + ": " + e.Message); }
            Loaded[classId] = rules;
            return rules;
        }

        private static string AnyFile()
        {
            if (_searched) return _anyFile;
            _searched = true;
            try
            {
                var folder = Dd1Install.PathOf(Folder);
                if (folder != null && Directory.Exists(folder))
                {
                    var files = Directory.GetFiles(folder, "*" + Suffix);
                    Array.Sort(files, StringComparer.Ordinal);
                    if (files.Length > 0) _anyFile = Folder + "/" + Path.GetFileName(files[0]);
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("DD1 hero upgrade folder could not be listed: " + e.Message); }
            return _anyFile;
        }

        // The first tree of each kind stands for the kind: all seven skills of a class cost the same.
        private static void Read(JObject json, ClassRules rules)
        {
            foreach (var tree in json["trees"] as JArray ?? Empty)
            {
                var id = (string)tree["id"];
                List<Purchase> into = null;
                foreach (var tag in tree["tags"] as JArray ?? Empty)
                {
                    switch ((string)tag)
                    {
                        case "combat_skill": into = rules.Skill; break;
                        case "weapon": into = rules.Weapon; break;
                        case "armour": into = rules.Armour; break;
                    }
                }
                if (into == null || into.Count > 0) continue;
                foreach (var requirement in tree["requirements"] as JArray ?? Empty)
                {
                    var purchase = new Purchase { ResolveLevel = (int?)requirement["prerequisite_resolve_level"] ?? 0 };
                    foreach (var cost in requirement["currency_cost"] as JArray ?? Empty)
                        if ((string)cost["type"] == UpgradeRules.Gold) purchase.Gold += (int?)cost["amount"] ?? 0;
                    foreach (var need in requirement["prerequisite_requirements"] as JArray ?? Empty)
                    {
                        var other = (string)need["tree_id"];
                        var code = (string)need["requirement_code"];
                        // The hero's own earlier purchase is implied by the order; only building steps are kept.
                        if (other != null && code != null && other != id) purchase.Needs.Add(new UpgradeRules.Need { Tree = other, Code = code });
                    }
                    into.Add(purchase);
                }
            }
        }
    }
}
