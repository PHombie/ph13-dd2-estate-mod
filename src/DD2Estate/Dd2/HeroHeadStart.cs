using System;
using System.Collections.Generic;
using Assets.Code.Actor;
using Assets.Code.Data;
using Assets.Code.Game;
using Assets.Code.Library;
using Assets.Code.Run;
using Assets.Code.Source;
using Assets.Code.Utils;
using DD2Estate.Dev;
using DD2Estate.Estate;
using HarmonyLib;
using UnityEngine;

namespace DD2Estate.Dd2
{
    /// <summary>
    /// The estate's heroes without Kingdoms' head start (the owner, 2026-10-06: "take the bonus off in the Estate
    /// so that the heroes have DD2's base numbers").
    ///
    /// WHAT IT IS. DD2's Kingdoms starts its "escalation" run value at 1, and its data hangs three buffs on
    /// every actor tagged "hero" while the value is 1 (kingdom_escalation_levels_data_export, the run value level
    /// <c>escalation_1_heroes</c>: gen_buff_dmg_20pct, gen_buff_max_hp_15pct, gen_buff_all_res_10pct): +20% damage
    /// dealt, +15% maximum health, +10% resistance to bleed, blight and burn. An Estate is hosted as a Kingdom
    /// whose escalation never leaves 1 (docs/recon/difficulty.md), so every estate hero carried the three for
    /// good.
    ///
    /// HOW THE GAME HANDS IT OUT. RunValues keeps one shared container per actor tag (GetActorData("hero")); every
    /// hero hangs that very container under its own data when it joins the run (ActorInstance.OnAddedToRun), and
    /// RunValues.UpdateRunValueLevels empties the shared containers and fills them again from the levels that
    /// hold now, every time a run value that has levels changes (the torch among them).
    ///
    /// WHAT THE MOD DOES. While an Estate session lasts, the head start's own container (the level's
    /// ActorDataContainer: the three buffs and nothing else) is taken out of the heroes' shared container each
    /// time the game has filled it (a postfix of UpdateRunValueLevels), and once a second for good measure. The
    /// run value itself is not touched and stays 1: the mod's difficulty rules lean on it, the level stays among
    /// the "current" ones, and its run stats (none) stay. Nothing is written to a save: the game builds the
    /// containers anew from its data. When the session ends the container is put back if the game has not
    /// rebuilt its levels by then, and outside a session the postfix does nothing: the regular game is as it was.
    ///
    /// WHAT A HERO SEES. A hero's stats are refreshed the game's own way (ActorInstance.RefreshStats): a changed
    /// maximum keeps the hero's share of it (46 of 46 becomes 40 of 40, 23 of 46 becomes 20 of 40). A hero read
    /// from a save while the container is already out has the save's health cut to the new maximum instead
    /// (full stays full; 30 of 46 is 30 of 40). Percent bonuses add up in DD2, so a blacksmith's level is worth
    /// its 10% of the class's base as before, without the 15 on top.
    ///
    /// [Rules] KingdomsHeadStart = true leaves the heroes as Kingdoms has them. Bridge: headstart.state,
    /// headstart.keep.
    /// </summary>
    [EstateModule]
    internal static class HeroHeadStart
    {
        /// <summary>DD2's tag of a hero's class, which the head start's level names.</summary>
        public const string HeroTag = "hero";

        /// <summary>[Rules] KingdomsHeadStart: true leaves Kingdoms' head start on the estate's heroes.</summary>
        public static bool Keep;

        private static int _taken, _putBack, _told;
        private static bool _refresh;
        private static float _nextLook;
        private static string _last = "", _first = "";

        private static void Register()
        {
            Keep = Plugin.Settings.Bind("Rules", "KingdomsHeadStart", false,
                "DD2's Kingdoms gives every hero +20% damage, +15% maximum health and +10% bleed / blight / burn resistance for as long as its escalation is 1, which in an Estate is always. Off (the default): the estate's heroes have DD2's base numbers. On: they keep Kingdoms' head start.").Value;
            NarrationMoments.Tick += OnTick;
            NarrationMoments.SessionEnded += OnSessionEnded;
            Bridge();
        }

        /// <summary>The head start is being kept off the heroes right now.</summary>
        public static bool Off => !Keep && (EstateSession.Active || EstateSession.Starting);

        private static RunValues Values
        {
            get
            {
                try { return Singleton<GameTypeMgr>.Instance.RunValues; }
                catch (Exception) { return null; }
            }
        }

        /// <summary>A level of the escalation that is the heroes': Kingdoms' head start (escalation_1_heroes).</summary>
        private static bool IsHeadStart(RunValueLevelDefinition level)
        {
            if (level == null || level.m_RunValueType != RunValueType.ESCALATION || level.m_Tags == null) return false;
            foreach (var tag in level.m_Tags)
                if (tag == HeroTag) return true;
            return false;
        }

        /// <summary>
        /// Brings the heroes' shared container in step: without the head start's container while it is kept
        /// off, with it otherwise (as the game filled it). True when something was changed.
        /// </summary>
        private static bool Sync(RunValues values, bool off)
        {
            var levels = values?.CurrentRunValueLevelDefinitions;
            if (levels == null) return false;
            DataContainer heroes;
            try { heroes = values.GetActorData(HeroTag); }
            catch (Exception) { return false; }
            if (heroes == null) return false;
            var changed = false;
            foreach (var level in levels)
            {
                if (!IsHeadStart(level) || level.ActorDataContainer == null) continue;
                var has = heroes.GetIsChild(level.ActorDataContainer);
                if (off && has)
                {
                    heroes.RemoveChild(level.ActorDataContainer);
                    _taken++;
                    changed = true;
                    _last = level.m_Id + " taken off the heroes (frame " + Time.frameCount + ", " + ActorsNow() + " actors in the game then" + (EstateSession.Starting ? ", the session still starting" : "") + ")";
                    if (_taken == 1) _first = _last;
                }
                else if (!off && !has)
                {
                    heroes.AddChild(level.ActorDataContainer);
                    _putBack++;
                    changed = true;
                    _last = level.m_Id + " put back on the heroes (frame " + Time.frameCount + ")";
                }
            }
            return changed;
        }

        // how many actors the game holds: none yet while a save is still being read
        private static int ActorsNow()
        {
            try { return SingletonMonoBehaviour<Library<uint, ActorInstance>>.HasInstance() ? SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance.GetNumberOfLibraryElements() : 0; }
            catch (Exception) { return -1; }
        }

        /// <summary>The game has just filled its shared containers again (the postfix below).</summary>
        internal static void AfterLevelsUpdated(RunValues values)
        {
            if (!Off) return;
            try
            {
                // the game refreshes every actor itself when the change came from a run value; a refresh of the
                // mod's own follows in the next frame of the session all the same (it is nothing where nothing changed)
                if (Sync(values, true)) _refresh = true;
            }
            catch (Exception e) { Plugin.Log.LogError("Hero head start: could not be taken off as the game rebuilt its levels: " + e); }
        }

        private static void OnTick()
        {
            if (!_refresh && Time.unscaledTime < _nextLook) return;
            _nextLook = Time.unscaledTime + 1f;
            try
            {
                if (Sync(Values, Off)) _refresh = true;
                if (_taken > _told)
                {
                    // once a session's worth: the game rebuilds its levels (and the mod takes the head start out again) with every change of the torch
                    if (_told == 0) Plugin.Log.LogInfo("Hero head start: Kingdoms' +20% damage, +15% health and +10% bleed / blight / burn resistance are off the estate's heroes (first: " + _first + ")");
                    _told = _taken;
                }
                if (!_refresh) return;
                _refresh = false;
                Refresh();
            }
            catch (Exception e) { Plugin.Log.LogError("Hero head start: " + e); }
        }

        // The session is over: the game's containers are as the game fills them (it rebuilds them for whatever
        // is played next; this is for what it may read before it does).
        private static void OnSessionEnded()
        {
            _refresh = false;
            _told = _taken = _putBack = 0;
            _first = "";
            try
            {
                if (Sync(Values, false)) Plugin.Log.LogInfo("Hero head start: the session is over, " + _last);
            }
            catch (Exception e) { Plugin.Log.LogWarning("Hero head start: could not be put back as the session ended: " + e.Message); }
        }

        // Every hero of the estate takes its numbers anew, the game's own way.
        private static void Refresh()
        {
            if (!EstateSession.Active || !Singleton<GameTypeMgr>.Instance.IsGameTypeStarted) return;
            var library = SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance;
            foreach (var guid in EstateSession.RosterGuids())
            {
                var hero = library.GetLibraryElement(guid);
                if (hero == null) continue;
                try { hero.RefreshStats(); }
                catch (Exception e) { Plugin.Log.LogWarning("Hero head start: " + hero.ActorName + " could not take its numbers anew: " + e.Message); }
            }
        }

        // ---- dev bridge ----------------------------------------------------------------------------------------

        private static Dictionary<string, float> BySource(IReadOnlyDictionary<SourceType, float> values)
        {
            var named = new Dictionary<string, float>();
            foreach (var pair in values) named[pair.Key != null ? pair.Key.GetName() : "none"] = pair.Value;
            return named;
        }

        private static object Describe()
        {
            var values = Values;
            var levels = new List<object>();
            bool? onHeroes = null;
            try
            {
                var heroes = values?.GetActorData(HeroTag);
                foreach (var level in values?.CurrentRunValueLevelDefinitions ?? new List<RunValueLevelDefinition>())
                {
                    var ours = IsHeadStart(level);
                    var has = ours && heroes != null && level.ActorDataContainer != null && heroes.GetIsChild(level.ActorDataContainer);
                    if (ours) onHeroes = (onHeroes ?? false) || has;
                    levels.Add(new { id = level.m_Id, runValue = level.m_RunValueType?.GetName(), tags = level.m_Tags, headStart = ours, onTheHeroes = ours ? has : (bool?)null });
                }
            }
            catch (Exception e) { levels.Add(new { error = e.Message }); }
            var roster = new List<object>();
            if (EstateSession.Active && Singleton<GameTypeMgr>.Instance.IsGameTypeStarted)
            {
                var library = SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance;
                foreach (var guid in EstateSession.RosterGuids())
                {
                    var hero = library.GetLibraryElement(guid);
                    if (hero == null) continue;
                    roster.Add(new
                    {
                        guid, name = hero.ActorName, cls = hero.ActorDataId, hp = hero.HpRaw, hpMax = hero.CurrentHpMax,
                        healthShares = BySource(hero.GetMultiplyStatValuesBySource(ActorStatType.HEALTH_MAX, false)),
                        damageAdds = BySource(hero.GetAddStatValuesBySource(ActorStatType.HEALTH_DAMAGE_DEALT_PERCENT, false)),
                        damage = hero.GetClampedStatValue(ActorStatType.HEALTH_DAMAGE_DEALT_PERCENT),
                        bleedResist = hero.GetClampedStatValue(ActorStatType.RESISTANCE, "bleed"),
                        blightResist = hero.GetClampedStatValue(ActorStatType.RESISTANCE, "blight"),
                        burnResist = hero.GetClampedStatValue(ActorStatType.RESISTANCE, "burn")
                    });
                }
            }
            return new
            {
                keep = Keep, off = Off, headStartOnTheHeroes = onHeroes,
                escalationRunValue = values != null && EstateSession.Active ? values.GetValue(RunValueType.ESCALATION) : (float?)null,
                taken = _taken, putBack = _putBack, first = _first, last = _last, levels, roster
            };
        }

        /// <summary>
        /// Test commands for the dev bridge:
        ///   headstart.state                the setting, whether Kingdoms' head start hangs on the heroes right now, the levels
        ///                                  the game holds current, and every hero's health and damage, source by source
        ///   headstart.keep on=true|false   for this session: leaves the head start on the heroes, or takes it off (the setting
        ///                                  [Rules] KingdomsHeadStart is read when the game starts); the heroes follow at once
        /// </summary>
        private static void Bridge()
        {
            AgentBridge.Register("headstart.state", o => Describe());
            AgentBridge.Register("headstart.keep", o =>
            {
                if (o["on"] != null) Keep = (bool)o["on"];
                if (EstateSession.Active)
                {
                    Sync(Values, Off);
                    Refresh();
                }
                return Describe();
            });
        }
    }

    /// <summary>
    /// The game has emptied its shared actor containers and filled them again from the run value levels that
    /// hold (on every change of a run value that has levels): in an Estate session the heroes' is left without
    /// Kingdoms' head start. Outside one, nothing.
    /// </summary>
    [HarmonyPatch(typeof(RunValues), "UpdateRunValueLevels")]
    internal static class HeroesGetNoHeadStart
    {
        private static void Postfix(RunValues __instance) => HeroHeadStart.AfterLevelsUpdated(__instance);
    }
}
