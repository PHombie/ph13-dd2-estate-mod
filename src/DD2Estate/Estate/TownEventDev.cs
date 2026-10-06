using System.Collections.Generic;
using System.Linq;
using DD2Estate.Core;
using DD2Estate.Dev;

namespace DD2Estate.Estate
{
    /// <summary>
    /// Test commands of the town events for the dev bridge ({"cmd":"run","name":"events.force","id":"free_abbey"}):
    ///   events.state                      this week's event, the campaign's memory, which hooks were asked
    ///   events.list                       every DD1 event: weight now, whether it could happen, why it is left out
    ///   events.force id=..                makes an event this week's (no rules asked)
    ///   events.roll [dungeon= type= success=]   asks DD1's rules again for this week, as after that quest
    ///   events.clear                      ends this week's event
    ///   events.setting id=normal|plentiful|off   DD1's frequency option (saved with the estate)
    ///   events.open / events.close        the crier's notice
    ///   events.return [index=0]           "From Beyond": brings one of the offered dead back
    ///   events.hooks [activity= item= tag=]     what the hooks answer right now
    /// The week itself is advanced with roster.week (or by finishing an expedition).
    /// </summary>
    [EstateModule]
    internal static class TownEventDev
    {
        private static void Register()
        {
            AgentBridge.Register("events.state", o => new { events = TownEvents.Describe(), notice = TownEventPanel.Snapshot() });
            AgentBridge.Register("events.list", o =>
            {
                var week = EstateState.Current.Week;
                var situation = new TownEventSituation
                {
                    Week = week,
                    DeadHeroes = Graveyard.All.Count,
                    HeroLevels = RosterLifecycle.LivingGuids().Select(Resolve.Level).ToList(),
                    HasUpgrade = UpgradeRules.Has,
                    TrinketsInStorage = Trinkets.Stored().Count
                };
                var list = new List<object>();
                foreach (var pair in TownEvents.Support())
                {
                    var def = pair.Key;
                    list.Add(new
                    {
                        id = def.Id,
                        title = TownEventText.Title(def.Id),
                        tone = def.Tone,
                        weight = TownEventRules.Weight(def, TownEvents.State),
                        cooldown = def.Cooldown,
                        minimumWeek = def.MinimumWeek,
                        available = TownEventRules.Available(def, TownEvents.State, situation),
                        leftOut = pair.Value,
                        effects = def.Effects.Select(e => e.Type + "(" + e.Text + (e.Number != 0 ? "," + e.Number : "") + ")").ToList()
                    });
                }
                return new { week, setting = TownEvents.Setting, sources = TownEvents.Catalog.Sources, notRead = TownEvents.Catalog.SkippedSources, events = list };
            });
            AgentBridge.Register("events.force", o => TownEvents.Force((string)o["id"]) ?? (object)TownEvents.Describe());
            AgentBridge.Register("events.roll", o =>
            {
                var outcome = TownEvents.RollAgain((string)o["dungeon"], (string)o["type"], (bool?)o["success"] ?? true);
                return new
                {
                    picked = outcome.Event?.Id,
                    guaranteed = outcome.Guaranteed,
                    chance = outcome.Chance,
                    chancePassed = outcome.ChancePassed,
                    candidates = outcome.Candidates,
                    state = TownEvents.Describe()
                };
            });
            AgentBridge.Register("events.clear", o =>
            {
                TownEvents.Clear();
                return "cleared";
            });
            AgentBridge.Register("events.setting", o =>
            {
                var id = (string)o["id"];
                if (id != null && !TownEvents.Catalog.SettingIds.Contains(id)) return "DD1 has the settings " + string.Join(", ", TownEvents.Catalog.SettingIds);
                if (id != null) TownEvents.Setting = id;
                return new { setting = TownEvents.Setting, chances = TownEvents.Catalog.Chances(TownEvents.Setting) };
            });
            AgentBridge.Register("events.open", o => TownEventPanel.Open() ? TownEventPanel.Snapshot() : "nothing to show (no event this week, or the hamlet is not up)");
            AgentBridge.Register("events.close", o =>
            {
                TownEventPanel.Close();
                return "closed";
            });
            AgentBridge.Register("events.return", o =>
            {
                var active = TownEvents.Current;
                if (active == null || active.Returnable.Count == 0) return "nobody may return this week";
                var key = (string)o["key"] ?? active.Returnable[System.Math.Max(0, System.Math.Min((int?)o["index"] ?? 0, active.Returnable.Count - 1))];
                return TownEvents.Return(key) ?? "returned: " + key;
            });
            AgentBridge.Register("events.hooks", o =>
            {
                var activity = (string)o["activity"] ?? "bar";
                var item = (string)o["item"] ?? "supply";
                var tag = (string)o["tag"] ?? "weapon";
                return new
                {
                    activity,
                    closed = TownEventHooks.ActivityClosed(activity),
                    priceFactor = TownEventHooks.ActivityPriceFactor(activity),
                    priceOf20 = TownEventHooks.ActivityPrice(activity, 20),
                    reliefFactorOfLastWeek = TownEventHooks.ActivityReliefFactor(activity),
                    item,
                    provisionPriceFactor = TownEventHooks.ProvisionPriceFactor(item),
                    provisionStockOf12 = TownEventHooks.ProvisionStock(item, 12),
                    tag,
                    freeUpgrades = TownEventHooks.FreeUpgrades(tag),
                    levelRestrictionLifted = TownEventHooks.LevelRestrictionLifted,
                    asked = TownEventHooks.Asked
                };
            });
        }
    }
}
