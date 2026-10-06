using System.Collections.Generic;
using DD2Estate.Dev;
using Newtonsoft.Json.Linq;

namespace DD2Estate.Estate
{
    /// <summary>
    /// Test commands of the building upgrades, the Guild and the Blacksmith for the dev bridge
    /// (python tools/bridge.py run upgrade.list, ... run upgrade.buy tree=tavern.bar, ... run guild.master
    /// guid=3 skill=highwayman_pistol_shot, ... run smith.fit guid=3 piece=armour). They do what the screens
    /// do, without the mouse. Resolve levels for the gates: the estate's own resolve commands.
    /// </summary>
    [EstateModule]
    internal static class UpgradeDev
    {
        private static void Register()
        {
            AgentBridge.Register("upgrade.list", o => List((string)o["building"]));
            AgentBridge.Register("upgrade.buy", o =>
            {
                var tree = UpgradeRules.Find((string)o["tree"]);
                if (tree == null) return "no such tree";
                return UpgradeRules.Buy(tree) ?? "built: " + tree.Id + " is at " + UpgradeRules.Level(tree) + " of " + tree.Steps.Count;
            });
            // Sets a tree's level without paying, down as well as up: {"tree":"guild.skill_levels","level":2}.
            AgentBridge.Register("upgrade.set", o =>
            {
                var id = (string)o["tree"];
                if (string.IsNullOrEmpty(id)) return "tree?";
                EstateState.Current.Buildings[id] = (int?)o["level"] ?? 0;
                UpgradeRules.RaiseChanged();
                return id + " = " + UpgradeRules.Level(id);
            });
            // Heirlooms (and gold) for the purse: {"crest":50,"deed":20,"bust":20,"portrait":20,"gold":500}; no
            // amounts at all gives 100 of each heirloom.
            AgentBridge.Register("upgrade.grant", o =>
            {
                var any = false;
                foreach (var id in EstateState.HeirloomIds)
                {
                    if (o[id] == null) continue;
                    EstateState.Current.AddHeirloom(id, (int)o[id]);
                    any = true;
                }
                if (o["gold"] != null)
                {
                    EstateState.AddGold((int)o["gold"]);
                    any = true;
                }
                if (!any)
                    foreach (var id in EstateState.HeirloomIds) EstateState.Current.AddHeirloom(id, 100);
                UpgradeRules.RaiseChanged();
                return Purse();
            });
            // The building's screen with its upgrades on show: {"building":"tavern"}.
            AgentBridge.Register("upgrade.open", o =>
            {
                var building = (string)o["building"] ?? "tavern";
                if (UpgradeRules.TreesOf(building).Count == 0) return "the " + building + " has no upgrade trees";
                if (System.Array.IndexOf(ActivityRules.BuildingIds, building) >= 0)
                {
                    BuildingPanel.Open(building);
                    return BuildingPanel.ShowUpgrades(true) ? "open" : "not opened";
                }
                if (building == SanitariumRules.Building)
                {
                    SanitariumPanel.Open();
                    return SanitariumPanel.ShowUpgrades(true) ? "open" : "not opened";
                }
                Buildings.Open(building);
                var pane = RosterWindow.Current != null ? RosterWindow.Current.Upgrades : null;
                if (pane == null) return "not opened";
                pane.SetOpen(true);
                return pane.Snapshot();
            });
            AgentBridge.Register("upgrade.close", o =>
            {
                BuildingPanel.Close();
                RosterWindow.Close();
                return "closed";
            });

            AgentBridge.Register("guild.open", o =>
            {
                GuildPanel.Open();
                return RosterWindow.IsOpen ? "open" : "not open";
            });
            AgentBridge.Register("guild.state", o => Guild.Describe((uint)o["guid"]));
            AgentBridge.Register("guild.learn", o => Guild.Learn((uint)o["guid"], (string)o["skill"]) ?? "learned");
            AgentBridge.Register("guild.master", o => Guild.Master((uint)o["guid"], (string)o["skill"]) ?? "mastered");
            // A skill into the hero's loadout or out of it, as a click on a known skill of the Guild does: {"guid":1,"skill":"..."}.
            AgentBridge.Register("guild.equip", o => Guild.ToggleEquipped((uint)o["guid"], (string)o["skill"]) ?? (object)Guild.Describe((uint)o["guid"]));

            AgentBridge.Register("smith.open", o =>
            {
                BlacksmithPanel.Open();
                return RosterWindow.IsOpen ? "open" : "not open";
            });
            AgentBridge.Register("smith.state", o => Blacksmith.Describe((uint)o["guid"]));
            // Buys the next level of a piece: {"guid":3,"piece":"weapon"} (or "armour").
            AgentBridge.Register("smith.fit", o => Blacksmith.Fit((uint)o["guid"], Piece(o)) ?? Blacksmith.Describe((uint)o["guid"]));
            // Sets a level without paying or asking: {"guid":3,"piece":"armour","level":5}.
            AgentBridge.Register("smith.set", o => Blacksmith.SetForTest((uint)o["guid"], Piece(o), (int?)o["level"] ?? 1));

            // A seasoned recruit on this week's coach: {"cls":"jester","tier":2}; hire with roster.hire.
            AgentBridge.Register("upgrade.veteran", o => StageCoach.OfferForTest((string)o["cls"], (int?)o["tier"] ?? 1));
            // Resolve for the Guild's and the Blacksmith's gates: {"guid":3,"level":2} raises the hero to that
            // level, {"guid":3,"xp":4} adds experience.
            AgentBridge.Register("upgrade.resolve", o =>
            {
                var guid = (uint)o["guid"];
                if (o["xp"] != null) Resolve.Grant(guid, (int)o["xp"]);
                var target = (int?)o["level"] ?? 0;
                for (var level = Resolve.Level(guid); level < target; level = Resolve.Level(guid))
                {
                    var missing = Resolve.ToNextLevel(guid);
                    if (missing <= 0) break;
                    Resolve.Grant(guid, missing);
                    if (Resolve.Level(guid) <= level) break;
                }
                UpgradeRules.RaiseChanged();
                return new { guid, level = Resolve.Level(guid), xp = Resolve.Experience(guid) };
            });
        }

        private static Blacksmith.Gear Piece(JObject o)
        {
            return ((string)o["piece"] ?? "weapon").StartsWith("a") ? Blacksmith.Gear.Armour : Blacksmith.Gear.Weapon;
        }

        private static object Purse()
        {
            var purse = new JObject { ["gold"] = EstateState.Gold };
            foreach (var id in EstateState.HeirloomIds) purse[id] = EstateState.Current.Heirloom(id);
            return purse;
        }

        private static object List(string building)
        {
            var trees = new List<object>();
            foreach (var tree in UpgradeRules.All)
            {
                if (building != null && tree.Building != building) continue;
                var level = UpgradeRules.Level(tree);
                var steps = new List<object>();
                for (var i = 0; i < tree.Steps.Count; i++)
                {
                    var step = tree.Steps[i];
                    var cost = new List<string>();
                    foreach (var c in step.Costs) cost.Add(UpgradeRules.Amount(c) + " " + c.Currency);
                    steps.Add(new
                    {
                        code = step.Code,
                        built = i < level,
                        cost = string.Join(", ", cost),
                        needs = UpgradeText.Needs(step.Needs),
                        effect = string.Join(" | ", UpgradeText.StepLines(tree, i + 1))
                    });
                }
                trees.Add(new
                {
                    tree = tree.Id,
                    name = UpgradeText.TreeName(tree.Id),
                    level,
                    of = tree.Steps.Count,
                    blocked = UpgradeRules.BlockReason(tree),
                    steps
                });
            }
            return new { purse = Purse(), artLevels = ArtLevels(), trees };
        }

        private static object ArtLevels()
        {
            var levels = new JObject();
            foreach (var building in HamletScene.BuildingIds) levels[building] = UpgradeWindow.ArtLevel(building);
            return levels;
        }
    }
}
