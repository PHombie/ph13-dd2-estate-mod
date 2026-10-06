using System;
using System.Collections.Generic;
using System.Linq;
using Assets.Code.Actor;
using Assets.Code.Library;
using Assets.Code.Quirk;
using Assets.Code.Utils;
using DD2Estate.Core;
using DD2Estate.Dd2;
using DD2Estate.Dev;
using DD2Estate.Estate;
using Newtonsoft.Json.Linq;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// The results screens over the dev bridge, without an expedition behind them:
    ///
    ///   python tools/bridge.py run results.show outcome=victory        victory | escape | defeat, with a made-up record
    ///   ... run results.show outcome=defeat dead=4                     nobody came home: an empty bag, four skulls
    ///   ... run results.show outcome=escape heirlooms=false stacks=3   no heirlooms, a short treasure row
    ///   ... run results.show outcome=victory stacks=16 quirks=3        the squeezed row; three lines behind every mask
    ///   ... run results.show outcome=victory page=2 finish=true        straight to the heroes, everything revealed
    ///   ... run results.show last=true                                 the last real expedition of this session again
    ///   ... run results.next                                           the red button, as a click on it
    ///   ... run results.mask hero=0                                    a hero's mask, as a click on it
    ///   ... run results.state                                          what is on the screen, and the record behind it
    ///   ... run results.close
    ///   ... run results.layout                                         the numbers as read from DD1's files
    ///   ... run results.chances completed=true                         the party's chances at a quest's end, unrolled
    ///
    /// The made-up record goes the real way where it can: the bag is a real bag settled by DD1's keep rates,
    /// the outcome is DD1's rule, heroes and quirks are the estate's own when a session is up. Nothing of it
    /// touches the estate: no gold, no experience, no quirk is given, and "Return to Town" only closes.
    /// More options of results.show: heroes (1..4), dead, levelup (true/false), quirks (0..3 a hero),
    /// trinkets (0..2), dungeon (crypts, weald, warrens, cove, darkestdungeon), quest (the name under the title).
    /// </summary>
    [EstateModule]
    internal static class RaidResultsDev
    {
        private static readonly string[] Names = { "Reynauld", "Dismas", "Paracelsus", "Junia" };
        private static readonly string[] Classes = { "man_at_arms", "highwayman", "plague_doctor", "vestal" };
        // experience before and after: a level gained (6 to 10 passes 8), none, none, the first level (0 to 4 passes 2)
        private static readonly int[] Before = { 6, 2, 14, 0 }, Gains = { 4, 4, 4, 4 }, Steady = { 8, 2, 14, 2 };
        private static readonly int[] Stress = { 5, 6, 4, 8 };

        // the bag of a long day: two purses, gems, what is left of the provisions (DD1 units)
        private static readonly string[] Loot =
        {
            "gold:1750", "ruby:1", "torch:3", "provision:4", "gold:600", "jade:2", "shovel:1", "emerald:1", "bandage:2", "holy_water:1", "citrine:3", "onyx:1",
            "sapphire:1", "antivenom:1", "medicinal_herbs:1", "skeleton_key:2"
        };
        private static readonly string[] Heirlooms = { "crest:5", "deed:2", "bust:1", "portrait:1" };

        private static readonly string[][] MadeUpQuirks =
        {
            new[] { "Positive", "Steady" }, new[] { "Negative", "Nervous" }, new[] { "Disease", "Creeping Cough" },
            new[] { "Positive", "Quick Reflexes" }, new[] { "Negative", "Clumsy" }, new[] { "Disease", "The Red Plague" }
        };

        private static void Register()
        {
            AgentBridge.Register("results.show", o =>
            {
                var results = (bool?)o["last"] == true ? RaidResultsScreen.Last : MakeUp(o);
                if (results == null) return "no expedition has ended in this session";
                if (!RaidResultsScreen.Show(results, null)) return "the screens could not be built (see the log)";
                if ((int?)o["page"] == 2)
                {
                    RaidResultsScreen.DevPress();       // the first page's reveal, cut short
                    RaidResultsScreen.DevPress();       // "Next"
                }
                if ((bool?)o["finish"] == true) RaidResultsScreen.DevPress();
                return RaidResultsScreen.Describe();
            });
            AgentBridge.Register("results.next", o => new { did = RaidResultsScreen.DevPress(), state = RaidResultsScreen.Describe() });
            AgentBridge.Register("results.mask", o => new { did = RaidResultsScreen.DevMask((int?)o["hero"] ?? 0), state = RaidResultsScreen.Describe() });
            AgentBridge.Register("results.state", o => RaidResultsScreen.Describe());
            AgentBridge.Register("results.close", o =>
            {
                RaidResultsScreen.Close();
                return "closed";
            });
            AgentBridge.Register("results.layout", o => Layout());
            AgentBridge.Register("results.chances", o =>
            {
                if (!EstateSession.Active) return "no estate session";
                var completed = (bool?)o["completed"] ?? true;
                return Provisioning.Party().Select(hero => QuestAftermath.Chances(hero, completed)).ToList();
            });
        }

        private static RaidResults MakeUp(JObject o)
        {
            var name = ((string)o["outcome"] ?? "victory").ToLowerInvariant();
            var completed = name.StartsWith("v", StringComparison.Ordinal);
            var escape = name.StartsWith("e", StringComparison.Ordinal);
            var heroes = Math.Max(1, Math.Min(4, (int?)o["heroes"] ?? 4));
            var rules = QuestAftermath.Rules;
            if (escape) heroes = Math.Max(heroes, rules.MinHeroesForEscape);
            // the dead are what makes the outcome asked for, by DD1's own rule: an escape needs three alive, a defeat fewer
            var dead = (int?)o["dead"] ?? (completed ? 0 : escape ? Math.Min(1, heroes - rules.MinHeroesForEscape) : heroes - 1);
            dead = Math.Max(0, Math.Min(heroes, dead));
            if (escape) dead = Math.Min(dead, heroes - rules.MinHeroesForEscape);
            else if (!completed) dead = Math.Max(dead, heroes - rules.MinHeroesForEscape + 1);
            if (completed) dead = Math.Min(dead, heroes - 1);
            var living = heroes - dead;

            var dungeon = (string)o["dungeon"] ?? "crypts";
            var results = new RaidResults
            {
                Outcome = rules.Outcome(completed, living),
                QuestName = (string)o["quest"] ?? (completed ? "Cleanse" : "Scout"),
                Dungeon = dungeon
            };

            // the quest's offer, given or withheld
            results.Rewards.Add(new QuestPayment { Kind = QuestPayment.Gold, Amount = 3000, Given = completed });
            results.Rewards.Add(new QuestPayment { Kind = QuestPayment.Heirloom, Id = "crest", Amount = 4, Given = completed });
            results.Rewards.Add(new QuestPayment { Kind = QuestPayment.Heirloom, Id = "deed", Amount = 2, Given = completed });
            var trinkets = Math.Max(0, Math.Min(2, (int?)o["trinkets"] ?? 1));
            var rarities = new[] { "uncommon", "very_rare" };
            for (var i = 0; i < trinkets; i++)
                results.Rewards.Add(new QuestPayment { Kind = QuestPayment.Trinket, Id = SomeTrinket(rarities[i], i), Rarity = rarities[i], Given = completed });

            // the bag, settled as an expedition settles it: everything after a victory, DD1's shares after a
            // retreat, nothing when nobody came home
            // (a bag twice the size of DD1's, so that sixteen stacks of treasure leave room for the heirlooms)
            var bag = new Core.Inventory(InventoryContent.Rules.RaidSlots * 2, InventoryContent.Rules.RaidStackLimits);
            var stacks = Math.Max(0, Math.Min(InventoryContent.Rules.RaidSlots, (int?)o["stacks"] ?? 7));
            var heirlooms = (bool?)o["heirlooms"] ?? true;
            for (var i = 0; i < stacks; i++) Put(bag, Loot[i % Loot.Length]);
            if (heirlooms)
                foreach (var stack in Heirlooms) Put(bag, "heirloom:" + stack);
            var status = completed ? RaidStatus.Succeeded : living > 0 ? RaidStatus.Abandoned : RaidStatus.Failed;
            results.SetHaul(bag.Settle(type => InventoryContent.Rules.KeepRate(type, status)));

            // the party: the estate's own heroes when there is an estate, else four of the Ancestor's old guard
            var party = Party(heroes);
            var levelUp = (bool?)o["levelup"] ?? true;
            var quirks = Math.Max(0, Math.Min(RaidResultRules.QuirksPerHeroLimit, (int?)o["quirks"] ?? -1));
            var quirkPlan = (int?)o["quirks"] == null ? new[] { 3, 1, 0, 2 } : new[] { quirks, quirks, quirks, quirks };
            var made = 0;
            for (var i = 0; i < heroes; i++)
            {
                var actor = i < party.Count ? party[i] : null;
                // the dead are the last of the line, as a fight leaves them
                var isDead = i >= living;
                var before = levelUp ? Before[i] : Steady[i];
                var row = new RaidResults.Hero
                {
                    Guid = actor != null ? actor.ActorGuid : 0u,
                    Name = actor != null ? actor.ActorName : Names[i],
                    ClassId = actor != null ? actor.ActorDataId : Classes[i],
                    Dead = isDead,
                    XpBefore = before,
                    XpAfter = completed && !isDead ? before + Gains[i] : before,
                    Stress = isDead ? 0 : Stress[i],
                    Diseased = !isDead && i == 0 && quirkPlan[0] >= 3
                };
                for (var j = 0; !isDead && j < quirkPlan[i]; j++) row.NewQuirks.Add(SomeQuirk(actor, made++, j));
                results.Heroes.Add(row);
            }
            return results;
        }

        private static void Put(Core.Inventory bag, string entry)
        {
            var colon = entry.LastIndexOf(':');
            var item = InventoryContent.Items.Find(entry.Substring(0, colon));
            if (item != null && int.TryParse(entry.Substring(colon + 1), out var amount)) bag.Add(item, amount);
        }

        private static List<ActorInstance> Party(int heroes)
        {
            var party = new List<ActorInstance>();
            if (!EstateSession.Active) return party;
            try
            {
                party.AddRange(Provisioning.Party());
                var library = SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance;
                foreach (var guid in RosterLifecycle.LivingGuids())
                {
                    if (party.Count >= heroes) break;
                    var actor = library.GetLibraryElement(guid);
                    if (actor != null && !party.Contains(actor)) party.Add(actor);
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("results.show: the estate's heroes could not be read: " + e.Message); }
            return party;
        }

        // A trinket of DD2's to show on DD1's rarity card; null (a card without an icon) when the game has none to name.
        private static string SomeTrinket(string dd1Rarity, int index)
        {
            try
            {
                var pool = Trinkets.Pool(Trinkets.SubTypeFor(dd1Rarity));
                return pool.Count > 0 ? pool[index % pool.Count] : null;
            }
            catch (Exception) { return null; }
        }

        // One of DD2's quirks by kind (the first lines: one of each kind, as a hero full of news has them), in the
        // game's own words when it can be asked; else a made-up name.
        private static QuestAftermath.Gain SomeQuirk(ActorInstance actor, int made, int line)
        {
            var kind = line == 0 ? QuestAftermath.Kind.Positive : line == 1 ? QuestAftermath.Kind.Negative : QuestAftermath.Kind.Disease;
            if (line == 0 && made % 2 == 1) kind = QuestAftermath.Kind.Negative;
            try
            {
                if (actor != null && SingletonMonoBehaviour<Library<string, QuirkDefinition>>.HasInstance())
                {
                    var tag = kind == QuestAftermath.Kind.Positive ? QuirkDefinition.QUIRK_POSITIVE_TAG : kind == QuestAftermath.Kind.Negative ? QuirkDefinition.QUIRK_NEGATIVE_TAG : QuirkDefinition.QUIRK_DISEASE_TAG;
                    var pool = SingletonMonoBehaviour<Library<string, QuirkDefinition>>.Instance.GetLibraryElements(q => q.Tags.Contains(tag) && !q.IsCurse && (kind == QuestAftermath.Kind.Disease || !q.IsDisease));
                    if (pool.Count > 0) return QuestAftermath.Describe(pool[(made * 7 + 3) % pool.Count], actor);
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("results.show: the game's quirks could not be read: " + e.Message); }
            var index = MadeUpQuirks.ToList().FindIndex(q => q[0] == kind.ToString());
            var pick = MadeUpQuirks[(index + (made % 2) * 3) % MadeUpQuirks.Length];
            return new QuestAftermath.Gain { Kind = kind, Id = pick[1].ToLowerInvariant().Replace(' ', '_'), Name = pick[1], Description = "A made-up quirk, to look at the screen." };
        }

        private static object Layout()
        {
            var l = RaidResultsLayout.Current;
            return new
            {
                panel = new[] { l.Panel.x, l.Panel.y }, result = new[] { l.QuestResult.x, l.QuestResult.y }, questTitle = new[] { l.QuestTitle.x, l.QuestTitle.y },
                state = new[] { l.State.x, l.State.y }, bar = new[] { l.Bar.x, l.Bar.y }, forward = new[] { l.Forward.x, l.Forward.y },
                itemsFrame = new[] { l.ItemsFrame.x, l.ItemsFrame.y }, rewardsTitle = new[] { l.RewardsTitle.x, l.RewardsTitle.y },
                rewardsGrid = new[] { l.RewardsGrid.x, l.RewardsGrid.y, l.Rewards.Start.x, l.Rewards.Pitch.x, l.Rewards.Columns },
                treasureGrid = new[] { l.TreasureGrid.x, l.TreasureGrid.y, l.Treasure.Start.x, l.Treasure.Pitch.x, l.Treasure.Columns },
                heirloomGrid = new[] { l.HeirloomGrid.x, l.HeirloomGrid.y, l.Heirlooms.Start.x, l.Heirlooms.Pitch.x, l.Heirlooms.Columns },
                treasureTotal = new[] { l.TreasureTotal.x, l.TreasureTotal.y }, heirloomTotal = new[] { l.HeirloomTotal.x, l.HeirloomTotal.y }, heirloomOrder = l.HeirloomOrder,
                heroesFrame = new[] { l.HeroesFrame.x, l.HeroesFrame.y }, heroes = Enumerable.Range(0, 4).Select(i => new[] { l.HeroOrigin(i).x, l.HeroOrigin(i).y }).ToList(),
                portrait = new[] { l.Portrait.x, l.Portrait.y }, name = new[] { l.HeroName.x, l.HeroName.y }, status = new[] { l.Status.x, l.Status.y }, experience = new[] { l.XpText.x, l.XpText.y },
                quirks = new[] { l.QuirksCentre.x + l.QuirksStart.x, l.QuirksCentre.y + l.QuirksStart.y, l.QuirksSpacing.y }, masks = new[] { l.Masks.x, l.Masks.y },
                resolveBar = new[] { l.ResolveBar.x, l.ResolveBar.y, l.BarHolder.x, l.BarHolder.y, l.BarFill.x, l.BarFill.y, l.BarFillSize.x, l.BarFillSize.y },
                stress = new[] { l.Stress.x, l.Stress.y, l.StressSpacing.x },
                seconds = new { card = l.CardWait, popup = l.PopupSeconds, popupRise = l.PopupRise, goldPulse = l.GoldPulse, heirloomPulse = l.HeirloomPulse, experience = l.XpSlideSeconds, bar = l.XpBarSeconds, quirkWait = l.QuirkWait, quirkFade = l.QuirkFade },
                rules = new { escapeNeeds = QuestAftermath.Rules.MinHeroesForEscape, quirksPerHero = RaidResultRules.QuirksPerHeroLimit }
            };
        }
    }
}
