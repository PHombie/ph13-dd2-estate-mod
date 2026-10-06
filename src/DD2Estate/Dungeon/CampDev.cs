using System.Collections.Generic;
using DD2Estate.Core;
using DD2Estate.Dev;
using DD2Estate.Estate;
using Newtonsoft.Json.Linq;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// Test commands of the camp for the dev bridge (python tools/bridge.py run camp.state, ... run camp.start,
    /// ... run camp.eat meal=full, ... run camp.use guid=3 skill=encourage target=4, ... run camp.sleep). They
    /// do what the screen does, without the mouse.
    /// </summary>
    [EstateModule]
    internal static class CampDev
    {
        private static void Register()
        {
            // DD1's camping rules as read from the install; needs no expedition.
            AgentBridge.Register("camp.rules", o =>
            {
                var rules = CampContent.Rules;
                var meals = new List<object>();
                foreach (var meal in rules.Meals)
                    meals.Add(new { type = meal.Type, name = CampText.MealName(meal), rationsPerHero = meal.RationsPer, foodForFour = rules.FoodFor(meal, 4), healing = meal.Healing, dd1Stress = meal.Stress, effect = CampText.MealEffect(meal) });
                return new
                {
                    respitePoints = rules.StartPoints,
                    ambushChance = rules.AmbushChance,
                    ambushPartySurprise = rules.AmbushPartySurprise,
                    restoreTorch = rules.RestoreTorch,
                    ambushTorch = rules.AmbushTorch,
                    readyLimit = rules.ActiveLimit,
                    firewood = new { shortQuest = rules.Firewood(1), medium = rules.Firewood(2), longQuest = rules.Firewood(3) },
                    skills = rules.Skills.Count,
                    meals
                };
            });
            // The camping skills DD1 gives a class, in the Estate's words: {"cls":"hellion"}.
            AgentBridge.Register("camp.skills", o =>
            {
                var rules = CampContent.Rules;
                var cls = (string)o["cls"] ?? "crusader";
                var skills = new List<object>();
                foreach (var skill in rules.SkillsFor(cls))
                    skills.Add(new { id = skill.Id, name = CampText.Name(skill), shared = rules.IsShared(skill), respite = skill.Cost, uses = skill.UseLimit, dd1Price = skill.PriceGold, effect = string.Join(" | ", CampText.Lines(skill)) });
                return new { cls, religious = rules.IsReligious(cls), analogue = rules.UsesAnalogue(cls) ? CampingRules.Analogues[cls] : null, skills };
            });
            AgentBridge.Register("camp.state", o =>
            {
                var run = DungeonRun.Current;
                if (run == null) return new { expedition = false, view = CampView.State, dark = CampScreen.DarkHeld };
                return new
                {
                    expedition = true,
                    firewood = run.Firewood,
                    food = run.Food,
                    refusal = run.CampRefusal,
                    fightsLeft = run.CampLedger.FightsLeft,
                    view = CampView.State,
                    screen = CampScreen.IsOpen,
                    // the dark held over the scene between a camp's end and the wipe into its night ambush
                    dark = CampScreen.DarkHeld,
                    camp = run.Camp?.Describe()
                };
            });
            // Firewood (and food) for the bag: {"firewood":1,"food":8}.
            AgentBridge.Register("camp.give", o =>
            {
                var run = DungeonRun.Current;
                if (run == null) return "no expedition";
                var items = InventoryContent.Items;
                var wood = (int?)o["firewood"] ?? 1;
                var food = (int?)o["food"] ?? 0;
                var left = run.Give(items.Find("firewood"), wood) + (food > 0 ? run.Give(items.Food, food) : 0);
                return new { firewood = run.Firewood, food = run.Food, didNotFit = left };
            });
            // Makes camp by DD1's rules; {"force":true} camps wherever the party stands, firewood or not.
            AgentBridge.Register("camp.start", o =>
            {
                var run = DungeonRun.Current;
                if (run == null) return "no expedition";
                if ((bool?)o["force"] == true) return run.ForceCamp() ?? "camping (forced)";
                return run.MakeCamp(out var message) ? "camping" : message;
            });
            AgentBridge.Register("camp.eat", o => Camp.Current == null ? "no camp" : Camp.Current.Eat((string)o["meal"] ?? "full") ?? "eaten");
            AgentBridge.Register("camp.select", o =>
            {
                var camp = Camp.Current;
                if (camp == null) return "no camp";
                camp.Selected = (uint)o["guid"];
                camp.Armed = null;
                return "selected " + camp.Selected;
            });
            // {"guid":3,"skill":"encourage","target":4}; target only for a skill aimed at a companion.
            AgentBridge.Register("camp.use", o =>
            {
                var camp = Camp.Current;
                if (camp == null) return "no camp";
                return camp.Use((uint)o["guid"], (string)o["skill"], (uint?)o["target"] ?? 0u) ?? (object)camp.Describe();
            });
            // The night's roll; {"ambush":true} or {"ambush":false} decides it instead, {"surprised":false} with it.
            AgentBridge.Register("camp.sleep", o =>
            {
                var camp = Camp.Current;
                if (camp == null) return "no camp";
                return camp.Sleep((bool?)o["ambush"], (bool?)o["surprised"]) ?? "asleep";
            });
            // The party's camping buffs: the ledger and what each hero's stats container holds.
            AgentBridge.Register("camp.buffs", o =>
            {
                var run = DungeonRun.Current;
                if (run == null) return "no expedition";
                var entries = new List<object>();
                foreach (var entry in run.CampLedger.Entries)
                    entries.Add(new { hero = entry.Hero, skill = entry.SkillId, buff = entry.BuffId, amount = entry.Amount, kind = CampingBuffMap.KindOf(CampContent.Rules.Buff(entry.BuffId)).ToString() });
                var onHeroes = new JObject();
                foreach (var hero in Provisioning.Party()) onHeroes[hero.ActorGuid.ToString()] = CampBuffs.TextOn(hero.ActorGuid).Replace('\n', ';');
                return new
                {
                    fightsLeft = run.CampLedger.FightsLeft,
                    scouting = run.CampLedger.Total(CampContent.Rules, CampBuffKind.Scouting),
                    surpriseMonsters = run.CampLedger.Total(CampContent.Rules, CampBuffKind.MonstersSurprise),
                    scoutBonus = run.Exploration.ScoutBonus,
                    pendingSurprise = FightSurprise.Pending,
                    entries,
                    onHeroes
                };
            });
            // The rest stop's framing, live: {"drop":0.3,"pan":0,"fov":1,"arena":"combat_arena_kingdom_camp_ambush"}.
            AgentBridge.Register("camp.view", o =>
            {
                if (o["drop"] != null) CampView.CameraDrop = (float)o["drop"];
                if (o["pan"] != null) CampView.CameraPan = (float)o["pan"];
                if (o["fov"] != null) CampView.FovScale = (float)o["fov"];
                if (o["arena"] != null) CampView.AmbushArena = (string)o["arena"];
                CampView.ApplyCamera();
                return new { state = CampView.State, drop = CampView.CameraDrop, pan = CampView.CameraPan, fov = CampView.FovScale, arena = CampView.AmbushArena };
            });
        }
    }
}
