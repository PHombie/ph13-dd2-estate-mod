using System.Collections.Generic;
using System.Linq;
using DD2Estate.Core;
using DD2Estate.Dev;
using DD2Estate.Dungeon;

namespace DD2Estate.Estate
{
    /// <summary>
    /// Test commands of the districts for the dev bridge ({"cmd":"run","name":"districts.build","id":"bank"});
    /// they do what the screen, the town crier and a won boss quest do, without the mouse and without the
    /// weeks. None of them asks whether DD1 would allow it now, except where it says so.
    /// </summary>
    [EstateModule]
    internal static class DistrictsDev
    {
        private static void Register()
        {
            // Everything: whether the install has districts, whether they are open, blueprints, what stands, every
            // building with its price here and in DD1, what it gives, whom, what is left out; the screen; the bar.
            AgentBridge.Register("districts.state", o => new { districts = Districts.Describe(), bar = TownPanelButtons.Describe() });
            // Opens the districts as "Cornerstones" does, without the event (no blueprint comes with it).
            AgentBridge.Register("districts.unlock", o =>
            {
                Districts.Unlock("dev bridge");
                return Districts.Describe();
            });
            // DD1's town event itself, this week: the crier's notice, the Ancestor's line, the districts open, one blueprint.
            AgentBridge.Register("districts.event", o => TownEvents.Force(Districts.UnlockEvent) ?? (object)new { events = TownEvents.Describe(), districts = Districts.Describe() });
            // Puts blueprints, gold and heirlooms in the estate's purse: {"blueprints":3,"gold":2000,"crest":800,"portrait":-5} adds and takes.
            AgentBridge.Register("districts.grant", o =>
            {
                if (o["blueprints"] != null) Districts.AddBlueprints((int)o["blueprints"], "dev bridge");
                if (o["gold"] != null) EstateState.AddGold((int)o["gold"]);
                foreach (var kind in EstateState.HeirloomIds)
                    if (o[kind] != null) EstateState.Current.AddHeirloom(kind, (int)o[kind]);
                return Districts.Describe();
            });
            // The screen, as the bar's button opens it; it also opens while the districts are locked, to be looked at.
            AgentBridge.Register("districts.open", o => DistrictsPanel.Open() ? DistrictsPanel.Snapshot() : (object)(Districts.Available ? TownPanel.BlockReason() ?? "not opened" : "this DD1 install has no districts"));
            AgentBridge.Register("districts.close", o =>
            {
                DistrictsPanel.Close();
                return "closed";
            });
            // Brings a building into the window: {"id":"library"}.
            AgentBridge.Register("districts.scroll", o => DistrictsPanel.ScrollTo((string)o["id"]) ?? DistrictsPanel.Snapshot());
            // A click on a building's button: DD1's "Construct this building?" comes up. {"id":"bank"}
            AgentBridge.Register("districts.press", o => DistrictsPanel.Press((string)o["id"]) ?? DistrictsPanel.Snapshot());
            // The answer to it: {"yes":true} pays and builds.
            AgentBridge.Register("districts.answer", o => DistrictsPanel.Answer((bool?)o["yes"] ?? true) ?? DistrictsPanel.Snapshot());
            // Pays a building's price and builds it, as the screen does after "Yes": {"id":"bank"}. Refused as the screen refuses.
            AgentBridge.Register("districts.build", o => Districts.Build((string)o["id"]) ?? Districts.Describe());
            // A building stands or does not, without a price: {"id":"granary","built":true}; {"all":true} builds everything.
            AgentBridge.Register("districts.set", o =>
            {
                var built = (bool?)o["built"] ?? true;
                if ((bool?)o["all"] == true)
                {
                    foreach (var building in Districts.Rules.Buildings) Districts.SetBuilt(building.Id, built);
                    return Districts.Describe();
                }
                return Districts.SetBuilt((string)o["id"], built) ?? Districts.Describe();
            });
            // The districts of a new estate again: locked, no blueprints, nothing built.
            AgentBridge.Register("districts.reset", o =>
            {
                Districts.ResetForTest();
                return Districts.Describe();
            });
            // The week's end of the Bank (interest) and the Puppet Theatre (idle heroes), without a week passing.
            AgentBridge.Register("districts.weekend", o =>
            {
                var gold = EstateState.Gold;
                Districts.WeekEndForTest();
                return new { goldBefore = gold, goldAfter = EstateState.Gold, report = QuestBoard.PendingNarration };
            });
            // What a won quest brings: {"quest":"plot_librarian_2"} looks the story quest's DD1 quest up and hands
            // over the blueprints its boss carries, as the end of an expedition does.
            AgentBridge.Register("districts.boss", o =>
            {
                var quest = (string)o["quest"];
                var story = Memoirs.PlotQuest(quest);
                if (story == null) return "no such story quest";
                var before = Districts.Blueprints;
                Districts.BossLoot(quest, true, null);
                return new { quest, dd1Quest = (string)story["dd1_quest_id"], blueprints = Districts.Blueprints - before, held = Districts.Blueprints };
            });
            // What the districts do for the party that is picked: each hero's stats container, the scouting share,
            // the respite points a camp would get, the Granary's food of this week, what a meal restores.
            AgentBridge.Register("districts.party", o =>
            {
                var heroes = new List<object>();
                foreach (var hero in Provisioning.Party())
                {
                    var cls = Districts.ClassId(hero);
                    var effect = Districts.Rules.HeroEffect(Districts.State, cls);
                    heroes.Add(new
                    {
                        hero = hero.ActorName, cls, tags = Districts.Rules.TagsOf(cls),
                        stats = CampingBuffMap.Text(effect.Lines).Trim().Replace("\n", " | "), scouting = effect.Scouting, given = effect.Given, leftOut = effect.LeftOut,
                        health = hero.HpRaw + " / " + hero.CurrentHpMax
                    });
                }
                var run = DungeonRun.Current;
                return new
                {
                    heroes, campPoints = Districts.CampPoints(), food = Districts.FreeFood(), eatFactor = Districts.EatFactor,
                    scoutBonus = run != null ? run.Exploration.ScoutBonus : 0, scoutChance = run != null ? run.Exploration.ScoutChance : 0,
                    scoutingByTorch = run != null ? run.Exploration.LightBand.Value("player_scouting_increase") : 0,
                    carried = DistrictBuffs.Describe()
                };
            });
        }
    }
}
