using System;
using System.Collections.Generic;
using DD2Estate.Core;

namespace DD2Estate.Estate
{
    /// <summary>
    /// What the town crier's notice says. Titles and descriptions are DD1's (town_event_title_&lt;id&gt;,
    /// town_event_description_&lt;id&gt;); the lines that say what an event does are built with DD1's own formats
    /// (town_event_info_format_&lt;effect&gt;, or DD1's single line town_event_info_&lt;id&gt; where an event has
    /// one), read from the English section of the player's DD1 string tables. Where the mod remaps an effect
    /// the line says what happens here, in the mod's words; those places are marked.
    /// </summary>
    internal static class TownEventText
    {
        /// <summary>DD1's name of the notice board: "Hear Ye, Hear Ye!" (the id's capital E is DD1's).</summary>
        public static string Heading => Dd1Strings.Get("town_name_town_Event") ?? "Town Event";

        public static string Title(string eventId)
        {
            var title = Dd1Strings.Get("town_event_title_" + eventId);
            return title != null ? Dd1Strings.Plain(title) : Words(eventId);
        }

        public static string Description(string eventId)
        {
            return Dd1Strings.Plain(Dd1Strings.Get("town_event_description_" + eventId));
        }

        /// <summary>What the event does this week, a line per effect; what its arrival did comes last.</summary>
        public static List<string> Info(TownEvents.Active active)
        {
            var lines = new List<string>();
            var def = active?.Def;
            if (def == null) return lines;
            var own = Dd1Strings.Get("town_event_info_" + def.Id);
            if (own != null) lines.Add(Dd1Strings.Format(own));
            else
            {
                foreach (var effect in def.Effects)
                {
                    string line = null;
                    try { line = Line(def, effect, active); }
                    catch (Exception e) { Plugin.Log.LogWarning("Town event text for " + def.Id + " failed: " + e.Message); }
                    // The two ends of DD1's damage range read the same.
                    if (!string.IsNullOrEmpty(line) && !lines.Contains(line)) lines.Add(line);
                }
            }
            foreach (var note in active.Notes)
                if (!lines.Contains(note)) lines.Add(note);
            return lines;
        }

        private static string Line(TownEventDef def, TownEventEffect effect, TownEvents.Active active)
        {
            var percent = (int)Math.Round(effect.Number * 100);
            switch (effect.Type)
            {
                case "free_activity":
                    return Dd1Strings.Format(Dd1Strings.Get("town_event_info_format_free_activity") ?? "%s is Free", ActivityText.Activity(effect.Text));
                case "activity_lock":
                    return Dd1Strings.Format(Dd1Strings.Get("town_event_info_format_activity_lock") ?? "%s is Locked", ActivityText.Activity(effect.Text));
                case "activity_cost_change":
                    return Dd1Strings.Format(Dd1Strings.Get("town_event_info_format_activity_cost_change") ?? "%s costs %d%%", ActivityText.Activity(effect.Text), percent);
                case "provision_item_type_cost_change":
                    return Dd1Strings.Format(Dd1Strings.Get("town_event_info_format_provision_item_type_cost_change") ?? "%s costs %d%%", ItemType(effect.Text), percent);
                case "provision_item_type_amount_change":
                    return Dd1Strings.Format(Dd1Strings.Get("town_event_info_format_provision_item_type_amount_change") ?? "%s quantities change by %d%%", ItemType(effect.Text), percent);
                case "upgrade_tag_free":
                {
                    var granted = Math.Max(1, (int)Math.Round(effect.Number));
                    var line = Dd1Strings.Format(Dd1Strings.Get("town_event_info_format_upgrade_tag_free") ?? "Free Upgrade: %s x %d",
                        Dd1Strings.Get("upgrade_tag_name_" + effect.Text) ?? Words(effect.Text), granted);
                    // The mod's own addition: the notice stays up all week, so it says when the gift is spent.
                    return active.Applied && (!active.FreeUpgrades.TryGetValue(effect.Text, out var left) || left <= 0) ? line + " (used)" : line;
                }
                case "upgrade_tag_discount":
                    return Dd1Strings.Format(Dd1Strings.Get("town_event_info_format_upgrade_tag_discount") ?? "%s cost %d%%",
                        Dd1Strings.Get("upgrade_tag_name_" + effect.Text) ?? Words(effect.Text), percent);
                case "embark_party_buff":
                    return Dd1Strings.Format(Dd1Strings.Get("town_event_info_format_embark_party_buff") ?? "%s on Next Quest", Buff(effect.Text));
                case "idle_buff":
                    return Dd1Strings.Format(Dd1Strings.Get("town_event_info_format_idle_buff") ?? "All Idle Heroes %s", Buff(effect.Text));
                case "idle_resolve_level":
                    // A class DD2 does not have gains nothing: no line (the archery tournament's musketeers).
                    if (!HeroExists(effect.Text)) return null;
                    // DD1's format hangs an "s" on the class name ("Idle %ss gain ..."); DD2's Highwayman and
                    // Man-at-Arms want a plural of their own, so the name goes in already plural.
                    return Dd1Strings.Format((Dd1Strings.Get("town_event_info_format_idle_resolve_level") ?? "Idle %ss gain %d Resolve Level").Replace("%ss", "%s"),
                        Plural(HeroNames.ClassName(effect.Text)), Math.Max(1, (int)Math.Round(effect.Number)));
                case "stage_coach_bonus_recruits":
                    // After the arrival the note says how many really came.
                    return active.Applied ? null : Dd1Strings.Format(Dd1Strings.Get("town_event_info_format_stage_coach_bonus_recruits") ?? "More recruits than usual");
                case "bonus_recruit":
                    return active.Applied ? null : HeroNames.ClassName(effect.Text) + " recruits arrive";
                case "plot_quest":
                {
                    // DD1 [exe 0x9e5020]: "Plot Quest: <the quest's name>"
                    var plot = PlotQuests.Rules.Find(effect.Text);
                    return plot == null ? null : Dd1Strings.Format(Dd1Strings.Get("town_event_info_format_plot_quest") ?? "Plot Quest: %s", PlotQuests.Name(plot));
                }
                case "trinket_retention_add_from_storage":
                    // DD1 [exe 0x9e50f4]: the event's own number, "The Shrieker has stolen 8 of your trinkets!"
                    return Dd1Strings.Format(Dd1Strings.Get("town_event_info_format_trinket_retention_add_from_storage") ?? "The Shrieker has stolen %d of your trinkets!",
                        Math.Max(1, (int)Math.Round(effect.Number)));
                case "dead_recruit":
                    // The mod's words: DD1 shows the three graves and lets its description speak.
                    return active.Returned || active.Returnable.Count == 0 ? null : "One of the fallen may return to the living";
                default:
                    return null;
            }
        }

        // A DD1 buff as its tooltip says it: "+33% Resolve XP in Ruins".
        private static string Buff(string buffId)
        {
            var buff = TownEvents.Catalog.Buff(buffId);
            if (buff == null) return Words(buffId);
            var percent = (int)Math.Round(buff.Amount * 100);
            string text;
            if (buff.Stat == "resolve_check_percent")
            {
                // The mod's words: DD1 says "Virtue Chance"; on a DD2 hero the same roll is the chance to come
                // out of a stress test resolute.
                text = (percent >= 0 ? "+" : "") + percent + "% chance to be Resolute";
            }
            else
            {
                var format = Dd1Strings.Get("buff_stat_tooltip_" + buff.Stat + (buff.SubStat.Length > 0 ? "_" + buff.SubStat : ""));
                text = format != null ? Dd1Strings.Format(format, percent) : (percent >= 0 ? "+" : "") + percent + "% " + Words(buff.Stat);
            }
            if (buff.Rule == "in_dungeon")
                text = Dd1Strings.Format(Dd1Strings.Get("buff_rule_tooltip_in_dungeon") ?? "%s in %s", text, Dd1Strings.Get("dungeon_name_" + buff.RuleText) ?? Words(buff.RuleText));
            return text;
        }

        private static string Plural(string className)
        {
            if (className.StartsWith("Man", StringComparison.Ordinal) && className.EndsWith("Arms", StringComparison.Ordinal)) return "Men" + className.Substring(3);
            if (className.EndsWith("man", StringComparison.Ordinal)) return className.Substring(0, className.Length - 3) + "men";
            return className + "s";
        }

        private static string ItemType(string type)
        {
            return Dd1Strings.Get("str_inventory_type_name_" + type) ?? Words(type);
        }

        private static bool HeroExists(string classId)
        {
            var classes = RosterLifecycle.AvailableClasses();
            for (var i = 0; i < classes.Count; i++)
                if (classes[i] == classId) return true;
            return false;
        }

        /// <summary>"lock_bar_discount_tavern" to "Lock Bar Discount Tavern": the last resort for a text DD1 does not have.</summary>
        internal static string Words(string id)
        {
            var words = (id ?? "").Split('_');
            for (var i = 0; i < words.Length; i++)
                if (words[i].Length > 0) words[i] = char.ToUpperInvariant(words[i][0]) + words[i].Substring(1);
            return string.Join(" ", words);
        }
    }
}
