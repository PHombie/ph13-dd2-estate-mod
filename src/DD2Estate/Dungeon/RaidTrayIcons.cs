using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Assets.Code.Actor;
using DD2Estate.Core;
using DD2Estate.Dd1;
using DD2Estate.Estate;
using DD2Estate.UI;
using UnityEngine;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// One of the status icons DD1 keeps over a hero's health bar (overlays/tray_*.png, 24 px): what it is the
    /// sign of, which of the tray's two rows it stands in, and what its tooltip says.
    /// </summary>
    internal sealed class TrayIcon
    {
        public readonly string Key, Art;
        /// <summary>The row that grows to the left (tray_icon_left_offset); else the one that grows to the right.</summary>
        public readonly bool Left;
        public readonly Func<ActorInstance, DungeonRun, bool> Shows;
        /// <summary>The tooltip, as rich text; asked when the pointer comes.</summary>
        public readonly Func<ActorInstance, DungeonRun, string> Words;

        // ---- as a mark in DD2's row under a hero's bars (Dd2Bars) ----
        /// <summary>DD2's fight has a mark of its own for this: "deathsdoor" or "recovery" (the two the fight keeps in its row of tokens).</summary>
        public string Dd2Own;
        /// <summary>
        /// Pictures of DD2's to draw the mark with, the first the game hands over: an address of the game's,
        /// "sprite:NAME" for a picture of DD2's icon atlas (it has no address), or "buff" / "debuff" for the
        /// arrows a fight shows beside a health bar. None, or none to be had: DD1's own little picture.
        /// </summary>
        public string[] Dd2Art;
        /// <summary>The size of DD2's picture in the row's 32 px (DD2 draws its own bare signs at 26), and whether it lies on the token's black plate.</summary>
        public Vector2 Dd2Size = new Vector2(26f, 26f);
        public bool Dd2Plate;

        public TrayIcon(string key, string art, bool left, Func<ActorInstance, DungeonRun, bool> shows, Func<ActorInstance, DungeonRun, string> words)
        {
            Key = key;
            Art = art;
            Left = left;
            Shows = shows;
            Words = words;
        }
    }

    /// <summary>
    /// The lasting states of a hero that mean something outside a fight in the Estate, as DD1's tray icons:
    ///
    /// - death's door (tray_deathsdoor.png): DD1's own tooltip, tray_icon_tooltip_deathsdoor_title over
    ///   tray_icon_tooltip_deathsdoor;
    /// - what death's door leaves behind (tray_deathsdoor_effects.png, DD1's "mortality debuffs"): in the Estate
    ///   DD2's wound, a share of the hero's health that is gone until it is mended;
    /// - disease (tray_disease.png): the hero's disease quirks by name;
    /// - camping buffs (tray_buff_plus.png): a line a buff in DD1's own words (buff_stat_tooltip_&lt;stat&gt;
    ///   inside buff_rule_tooltip_&lt;rule&gt;) and DD1's note of how long it lasts
    ///   (tray_icon_tooltip_buff_duration_combat_end_format "%s (%d Battles)", or
    ///   tray_icon_tooltip_buff_until_camp_format for a buff without a count of fights);
    /// - the week's town event, when it gives the party something for this expedition (tray_town_event.png).
    ///
    /// The words are DD1's; what a buff does to a DD2 hero is the mod's mapping (CampingBuffMap), as the camp
    /// says when the skill is used. Not in DD1's files, and so the mod's reading (docs/recon/dd1-raid-ui.md, 9):
    /// which icon stands in which row (what harms on the left, what helps on the right), that a camping buff
    /// wears the gold arrow (tray_buff_plus.png) rather than the blue one of a fight's buffs, and that the town
    /// event's bell stands in the row at all (status_bars has an "icon" of its own, icon_offset 50 30 with
    /// tooltips for a town event and a resolve XP bonus, that may be its place: 30 px under the bar's top is the
    /// panel's seam on this screen). Bleed, blight, stun, marks, guard, riposte, stealth and the rest are a
    /// fight's, and a fight is DD2's own screen; DD2 has no lasting affliction or virtue.
    /// </summary>
    internal static class TrayIcons
    {
        private static readonly Regex Tags = new Regex("<[^>]+>");

        public static readonly TrayIcon DeathsDoor = new TrayIcon("deathsdoor", "overlays/tray_deathsdoor.png", true,
            (actor, run) => actor.GetIsStatusActive(ActorStatusType.DEATHS_DOOR),
            (actor, run) => Negative(RaidText.Get("tray_icon_tooltip_deathsdoor_title", "Death's Door") + "\n" + RaidText.Get("tray_icon_tooltip_deathsdoor", "The next blow could be fatal!")))
        {
            Dd2Own = "deathsdoor"
        };

        public static readonly TrayIcon Mortality = new TrayIcon("mortality", "overlays/tray_deathsdoor_effects.png", true,
            (actor, run) => actor.IsWounded,
            (actor, run) => RaidText.Marks(RaidText.Get("buff_bsrc_deathsdoor_recovery", "Death's Door Recovery")) + "\n"
                            + Negative(RaidText.Format(RaidText.Get("buff_stat_tooltip_combat_stat_multiply_max_hp", "%+d%% MAX HP"), -actor.WoundPercent * 100f)))
        {
            Dd2Own = "recovery"
        };

        public static readonly TrayIcon Disease = new TrayIcon("disease", "overlays/tray_disease.png", true,
            (actor, run) => Diseases(actor).Count > 0,
            (actor, run) =>
            {
                var names = Diseases(actor);
                return Negative(names.Count > 0 ? string.Join("\n", names) : RaidText.Get("str_diseases", "Diseases"));
            })
        {
            // DD2's own sign for a disease: the green one of its icon atlas, drawn like the fight's arrows and its
            // skull for death's door (icon_buff, icon_debuff_outline, icon_death_outline); else the grey one of
            // its inns' trees, which has an address
            Dd2Art = new[] { "sprite:icon_disease_outline", "Assets/Art/UI/Icons/SkillTreeSprites/icon_disease_grey.png" }
        };

        public static readonly TrayIcon CampBuffs = new TrayIcon("camp_buff", "overlays/tray_buff_plus.png", false,
            (actor, run) => CampLines(actor, run, null),
            (actor, run) =>
            {
                var lines = new List<string>();
                CampLines(actor, run, lines);
                return lines.Count > 0 ? string.Join("\n", lines) : Positive(RaidText.Format(RaidText.Get("tray_icon_tooltip_buff_until_camp_format", "%s (Until Camp)"), "-"));
            })
        {
            // the arrow a fight shows beside the health bar of an actor with buffs
            Dd2Art = new[] { "buff" }
        };

        public static readonly TrayIcon TownEvent = new TrayIcon("town_event", "overlays/tray_town_event.png", false,
            (actor, run) => EventLines(run).Count > 0,
            (actor, run) =>
            {
                var lines = EventLines(run);
                var text = RaidTooltip.Titled(_eventTitle ?? "Town Event", null);
                foreach (var line in lines) text += "\n" + line;
                return text;
            });

        /// <summary>
        /// A hero's chance to disarm, shown on every hero while the pointer rests on a trap the party has spotted
        /// (seen in DD1's own frame, _lab/dd1_ref/raid/raid_trap_spotted_hover.png: overlays/tray_trap_disarm.png
        /// over each health bar, after whatever else the hero wears); the tooltip is DD1's
        /// resistance_trap_disarm_format, "Trap Disarm: %.0f%%".
        /// </summary>
        public static readonly TrayIcon TrapChance = new TrayIcon("trap_disarm", "overlays/tray_trap_disarm.png", false,
            (actor, run) => run != null && CorridorView.Instance != null && CorridorView.Instance.isActiveAndEnabled && CorridorView.Instance.Props.TrapUnderPointer,
            (actor, run) => TrapDisarm.Words(actor, run));

        /// <summary>The icons in the order they take their places over a bar, from its middle outwards.</summary>
        public static readonly TrayIcon[] All = { DeathsDoor, Mortality, Disease, CampBuffs, TownEvent, TrapChance };

        public static TrayIcon Find(string key)
        {
            foreach (var icon in All)
                if (icon.Key == key) return icon;
            return null;
        }

        private static string Positive(string text) => "<color=" + RaidText.Hex(Dd1Fonts.Colour("tray_icon_tooltip_positive", UiKit.Notable)) + ">" + text + "</color>";

        private static string Negative(string text) => "<color=" + RaidText.Hex(Dd1Fonts.Colour("tray_icon_tooltip_negative", UiKit.Harmful)) + ">" + text + "</color>";

        /// <summary>A text of the game's own without its colour and icon tags (a quirk's name comes in DD2's colours).</summary>
        public static string Bare(string text) => string.IsNullOrEmpty(text) ? "" : Tags.Replace(text, "").Trim();

        // ---- diseases ----------------------------------------------------------------------------------

        private static List<string> Diseases(ActorInstance actor)
        {
            var names = new List<string>();
            var quirks = actor.QuirkContainer;
            if (quirks == null) return names;
            for (var i = 0; i < quirks.GetNumberOfInstances(); i++)
            {
                var quirk = quirks.GetInstanceAtIndex(i);
                if (quirk?.Definition != null && quirk.Definition.IsDisease && !quirk.Definition.IsCurse) names.Add(Bare(SanitariumRules.QuirkName(quirk.Definition, actor)));
            }
            return names;
        }

        // ---- buffs in DD1's words ----------------------------------------------------------------------

        /// <summary>
        /// A buff as DD1's tooltips word it: the stat's format (buff_stat_tooltip_&lt;stat&gt;[_&lt;sub&gt;]: "%+d%% DMG")
        /// inside the rule's (buff_rule_tooltip_&lt;rule&gt;: "%s if in position %d"). Null when DD1 has no format
        /// for the stat.
        /// </summary>
        public static string BuffWords(string stat, string sub, double amount, string rule, bool ruleFalse, double ruleNumber, string ruleText)
        {
            var format = RaidText.Get("buff_stat_tooltip_" + stat + (string.IsNullOrEmpty(sub) ? "" : "_" + sub));
            if (format == null) return null;
            // DD1 keeps a share as a fraction and writes it in hundredths; speed and health points are whole
            var whole = stat == "combat_stat_add" && (sub == "speed_rating" || sub == "max_hp");
            var text = RaidText.Format(format, whole ? amount : amount * 100.0);
            if (string.IsNullOrEmpty(rule) || rule == "always") return text;
            var within = RaidText.Get("buff_rule_tooltip_" + rule + (ruleFalse ? "_false" : ""));
            if (within == null) return text;
            // a rule's second place is a number (DD1 counts ranks from 0 and writes them from 1) or a name
            if (within.IndexOf("%d", StringComparison.Ordinal) >= 0) return RaidText.Format(within, text, rule == "in_rank" ? ruleNumber + 1 : ruleNumber);
            return RaidText.Format(within, text, ruleText ?? "");
        }

        // More of these is worse for the hero who has it.
        private static bool LessIsBetter(string stat)
        {
            return stat == "stress_dmg_received_percent" || stat == "damage_received_percent" || stat == "party_surprise_chance";
        }

        /// <summary>False for a camping buff that is one in name only: an amount that works against the hero.</summary>
        public static bool Helps(CampBuffDef buff, double amount)
        {
            return buff == null || (LessIsBetter(buff.StatType) ? amount <= 0 : amount >= 0);
        }

        // The hero's camping buffs, a line each, in DD1's words; true when there is any. The two ends of DD1's
        // damage range read the same and are written once.
        private static bool CampLines(ActorInstance actor, DungeonRun run, List<string> lines)
        {
            var ledger = run?.CampLedger;
            if (ledger == null || !ledger.Any) return false;
            var any = false;
            foreach (var entry in ledger.Entries)
            {
                if (entry.Hero != actor.ActorGuid) continue;
                any = true;
                if (lines == null) return true;
                var buff = CampContent.Rules.Buff(entry.BuffId);
                var words = buff != null ? BuffWords(buff.StatType, buff.SubType, entry.Amount, buff.RuleType, buff.RuleFalse, buff.RuleNumber, buff.RuleText) : null;
                if (words == null) words = TownEventText.Words(entry.BuffId);
                words = buff != null && buff.DurationType == "combat_end"
                    ? RaidText.Format(RaidText.Get("tray_icon_tooltip_buff_duration_combat_end_format", "%s (%d Battles)"), words, ledger.FightsLeft)
                    : RaidText.Format(RaidText.Get("tray_icon_tooltip_buff_until_camp_format", "%s (Until Camp)"), words);
                var line = Helps(buff, entry.Amount) ? Positive(words) : Negative(words);
                if (!lines.Contains(line)) lines.Add(line);
            }
            return any;
        }

        // ---- the week's town event -----------------------------------------------------------------------

        private const float EventEvery = 5f;        // seconds between looks at the week's event
        private static DungeonRun _eventFor;
        private static float _eventNext;
        private static string _eventTitle;
        private static bool _eventFailed;
        private static readonly List<string> _eventLines = new List<string>();

        /// <summary>
        /// What the week's town event gives the party on this expedition, a line a buff: the buffs of its
        /// embark_party_buff effects that hold in this dungeon and that the Estate plays (damage, the chance to
        /// come out of a stress test resolute, resolve experience: Estate/TownEventEffects.cs). The week does not
        /// change while an expedition is out: the lines are made for it once and looked over now and then.
        /// </summary>
        private static List<string> EventLines(DungeonRun run)
        {
            if (run == null)
            {
                _eventFor = null;
                _eventLines.Clear();
                return _eventLines;
            }
            if (ReferenceEquals(_eventFor, run) && Time.unscaledTime < _eventNext) return _eventLines;
            _eventFor = run;
            _eventNext = Time.unscaledTime + EventEvery;
            try
            {
                var active = TownEvents.Current;
                _eventTitle = null;
                _eventLines.Clear();
                if (active?.Def == null) return _eventLines;
                _eventTitle = TownEventText.Title(active.Id);
                var dungeon = run.Exploration.Map.DungeonId;
                foreach (var effect in TownEvents.Effects(active, "embark_party_buff"))
                {
                    var buff = TownEvents.Catalog.Buff(effect.Text);
                    if (buff == null || !(buff.Rule == "always" || (buff.Rule == "in_dungeon" && buff.RuleText == dungeon))) continue;
                    string words;
                    var percent = Mathf.RoundToInt((float)(buff.Amount * 100));
                    if (buff.Stat == "resolve_check_percent")
                    {
                        // the mod's words, as on the town's notice: DD1 says "Virtue Chance"; on a DD2 hero the same
                        // roll is the chance to come out of a stress test resolute
                        words = (percent >= 0 ? "+" : "") + percent + "% chance to be Resolute";
                    }
                    else if (buff.Stat == "resolve_xp_bonus_percent" || (buff.Stat == "combat_stat_multiply" && (buff.SubStat == "damage_low" || buff.SubStat == "damage_high")))
                    {
                        words = BuffWords(buff.Stat, buff.SubStat, buff.Amount, null, false, 0, null) ?? ((percent >= 0 ? "+" : "") + percent + "% " + TownEventText.Words(buff.Stat));
                    }
                    else continue;
                    if (buff.Rule == "in_dungeon")
                        words = RaidText.Format(RaidText.Get("buff_rule_tooltip_in_dungeon", "%s in %s"), words, RaidText.Get("dungeon_name_" + dungeon, DungeonContent.DisplayName(dungeon)));
                    words = RaidText.Format(RaidText.Get("tray_icon_tooltip_buff_duration_quest_end_format", "%s (Quest)"), words);
                    var line = buff.Amount >= 0 ? Positive(words) : Negative(words);
                    if (!_eventLines.Contains(line)) _eventLines.Add(line);
                }
            }
            catch (Exception e)
            {
                if (!_eventFailed) Plugin.Log.LogWarning("Dungeon HUD: the week's town event could not be read for the heroes' trays: " + e.Message);
                _eventFailed = true;
                _eventLines.Clear();
            }
            return _eventLines;
        }
    }
}
