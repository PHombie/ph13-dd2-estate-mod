using System;
using System.Collections.Generic;
using Assets.Code.Game;
using Assets.Code.Item;
using Assets.Code.Rules;
using Assets.Code.Utils;
using Newtonsoft.Json.Linq;

namespace DD2Estate.Estate
{
    /// <summary>
    /// The estate layer of a playthrough: everything DD1 tracks between expeditions that DD2 has no notion
    /// of (week, gold, heirlooms, building levels, quest history). The purse is a number of the estate's own
    /// (format 2). It used to be DD2's gold item in the player inventory, whose slots it shared with every other
    /// item: gold past the free slots was dropped without a word (1320 with twelve trinkets in the stores).
    /// From format 3 on the estate keeps everything it owns itself ("one inventory",
    /// docs/recon/inventory-unification.md): the unworn trinkets are a list of the estate's own
    /// (<see cref="Trinkets"/>), and DD2's player inventory holds nothing. An older save is brought over by the
    /// pass that follows every load (<see cref="Dd2.Dd2Sweep"/>): it goes by what lies in DD2's inventory, not
    /// by the number, so a save of any format loads. An older build reads a format-3 save as an estate with no
    /// unworn trinkets.
    /// From format 4 on gold is counted in DD1's own numbers (3000 to begin with, 75 for a torch, 1000 for a
    /// skill). Before, the estate had a scale of its own, one for fifty of DD1's, from when the purse was stacks
    /// of DD2's gold. A save of an older format has every amount of gold it holds multiplied as it is read
    /// (<see cref="DD2Estate.Core.EstateSaveRules"/>); an older build would read a format-4 save as an estate
    /// fifty times as rich.
    /// Saved as the "estate_mod" section of the playthrough. Systems with their own data (activities, roster
    /// history, quests) add a named section with <see cref="RegisterSection"/> instead of fields here.
    /// </summary>
    internal class EstateState
    {
        public const int Format = DD2Estate.Core.EstateSaveRules.GoldInDd1Numbers;

        /// <summary>
        /// What one of DD2's own gold is worth in the purse, should DD2's inventory ever hold any (the purse of
        /// a save from before it was a number; nothing of DD2's pays gold in the Estate): the rate the estate
        /// counted at while its gold was DD2's.
        /// </summary>
        public const int Dd2GoldWorth = DD2Estate.Core.EstateSaveRules.OldGoldScale;

        /// <summary>DD1's plot quest that is its beginning: what it pays is what an estate has when its hamlet is first seen.</summary>
        private const string Dd1FirstQuest = "plot_tutorial_crypts";
        // FALLBACK, used only when DD1's plot quests cannot be read: DD1's own number.
        private const int StockStartingGold = 3000;

        /// <summary>
        /// The gold a new estate begins with. DD1's wallet starts empty (scripts/starting_save) and its first
        /// hamlet is seen with the pay of the Old Road in it (campaign/quest: plot_tutorial_crypts, 3000).
        /// </summary>
        public static int StartingGold
        {
            get
            {
                try
                {
                    var gold = QuestBoard.Rewards.Plot(Dd1FirstQuest)?.Reward.Gold ?? 0;
                    if (gold > 0) return gold;
                }
                catch (Exception e) { Plugin.Log.LogWarning("Estate: DD1's first quest could not be read for the starting gold: " + e.Message); }
                return StockStartingGold;
            }
        }

        public static readonly string[] HeirloomIds = { "crest", "deed", "bust", "portrait" };

        public static EstateState Current { get; private set; } = new EstateState();

        /// <summary>Raised after the week number went up (argument: the new week).</summary>
        public static event Action<int> WeekAdvanced;

        public int Week = 1;
        public int Purse;
        public readonly Dictionary<string, int> Heirlooms = new Dictionary<string, int>();
        public readonly Dictionary<string, int> Buildings = new Dictionary<string, int>();
        public readonly List<string> CompletedQuests = new List<string>();
        public int ExpeditionsWon;
        public int ExpeditionsLost;

        // ---- sections owned by other systems -----------------------------------------------------------

        private class Section
        {
            public Func<JToken> Save;
            public Action<JToken> Load;   // receives null when the save has no such section
            public Action Reset;          // new game
        }

        private static readonly Dictionary<string, Section> Sections = new Dictionary<string, Section>();

        /// <summary>Registers extra per-playthrough data. Call once at plugin start-up.</summary>
        public static void RegisterSection(string key, Func<JToken> save, Action<JToken> load, Action reset)
        {
            Sections[key] = new Section { Save = save, Load = load, Reset = reset };
        }

        public static void StartNew()
        {
            Current = new EstateState();
            foreach (var section in Sections.Values) Guarded("reset", section.Reset);
            Dd2.Dd2Sweep.NewEstate();
        }

        /// <summary>What an expedition came to, for systems that react to it (resolve experience, town events).</summary>
        public class Expedition
        {
            public string Dungeon, QuestId;
            /// <summary>DD1 difficulty number: 1, 3, 5 or 6.</summary>
            public int Difficulty;
            /// <summary>1 short, 2 medium, 3 long.</summary>
            public int Length;
            public bool Success;
            /// <summary>Resolve experience the quest states for its survivors (DD1's plot quests); 0: DD1's table by difficulty and length.</summary>
            public int ResolveXp;
            public IReadOnlyList<uint> Survivors = new List<uint>();
            /// <summary>
            /// What the expedition's end paid the estate, the quest and the bag apart: the one record the results
            /// screen, the Activity Log and the purse are all read against. Null from a caller that has none.
            /// </summary>
            public DD2Estate.Dungeon.ExpeditionBooks Books;
            /// <summary>Everyone who set out, the fallen too.</summary>
            public IReadOnlyList<uint> Party = new List<uint>();
            /// <summary>Their resolve levels as they set out, in the same order.</summary>
            public IReadOnlyList<int> PartyLevels = new List<int>();
            /// <summary>What DD1 says about the plot quest a story quest stands for (the Darkest Dungeon's rules); null otherwise.</summary>
            public DD2Estate.Core.PlotQuestInfo Dd1Plot;
        }

        /// <summary>Raised when an expedition ends, before the week advances.</summary>
        public static event Action<Expedition> ExpeditionEnded;

        public static void RaiseExpeditionEnded(Expedition expedition)
        {
            try { ExpeditionEnded?.Invoke(expedition); }
            catch (Exception e) { Plugin.Log.LogError("ExpeditionEnded handler failed: " + e); }
        }

        /// <summary>DD1: every expedition costs a week, whatever its outcome.</summary>
        public void AdvanceWeek()
        {
            Week++;
            try { WeekAdvanced?.Invoke(Week); }
            catch (Exception e) { Plugin.Log.LogError("WeekAdvanced handler failed: " + e); }
        }

        public int Heirloom(string id) => Heirlooms.TryGetValue(id, out var n) ? n : 0;

        public void AddHeirloom(string id, int amount)
        {
            Heirlooms[id] = Math.Max(0, Heirloom(id) + amount);
        }

        public int BuildingLevel(string id) => Buildings.TryGetValue(id, out var n) ? n : 0;

        // ---- gold -----------------------------------------------------------------------------------

        public static int Gold => Current.Purse;

        /// <summary>Gold into the purse, or out of it. The purse does not go under nought.</summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public static void AddGold(int amount)
        {
            AddGold(amount, Caller());
        }

        /// <summary>The same, with a word on what for (the purse's journal; the caller's class when left out).</summary>
        public static void AddGold(int amount, string why)
        {
            var before = Current.Purse;
            Current.Purse = Math.Max(0, before + amount);
            Journal(Current.Purse - before, why);
        }

        // ---- the purse's journal -----------------------------------------------------------------------
        // Every change of the purse since the game started, for the dev bridge (ledger.purse) and
        // tools/ledger_check.py: that the purse moves by nothing else, and that nothing pays into it in the
        // middle of an expedition. Not saved.

        public sealed class PurseEntry
        {
            /// <summary>The entry's number, counted from the game's start.</summary>
            public int N;
            public int Week, Amount, After;
            /// <summary>What for: a word of the caller's, or the class that asked.</summary>
            public string Why;
            /// <summary>
            /// "expedition" while a party is out and its expedition has not ended; else "hamlet": in town, and at
            /// an expedition's end (the quest's pay, the haul, the week that turns with it).
            /// </summary>
            public string Where;
        }

        private const int JournalKept = 400;
        private static readonly List<PurseEntry> Entries = new List<PurseEntry>();
        private static int _journalNext;

        /// <summary>The purse's last changes, oldest first.</summary>
        public static IReadOnlyList<PurseEntry> PurseJournal => Entries;

        /// <summary>The number the next entry will get: an earlier value of it says where to read on from.</summary>
        public static int PurseJournalNext => _journalNext;

        private static void Journal(int change, string why)
        {
            if (change == 0) return;
            Entries.Add(new PurseEntry
            {
                N = _journalNext++, Week = Current.Week, Amount = change, After = Current.Purse, Why = string.IsNullOrEmpty(why) ? "?" : why,
                Where = DD2Estate.Dungeon.DungeonRun.InTheField ? "expedition" : "hamlet"
            });
            if (Entries.Count > JournalKept) Entries.RemoveRange(0, Entries.Count - JournalKept);
        }

        // The class that called AddGold(int): two frames up. A lambda's or an iterator's own class is named by
        // the class it was written in.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static string Caller()
        {
            try
            {
                var type = new System.Diagnostics.StackFrame(2, false).GetMethod()?.DeclaringType;
                while (type != null && type.Name.StartsWith("<", StringComparison.Ordinal) && type.DeclaringType != null) type = type.DeclaringType;
                return type?.Name;
            }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// Gold lying in DD2's own player inventory goes into the purse at what it is worth in DD1's
        /// (<see cref="Dd2GoldWorth"/>), and the stacks are taken out of the inventory: the purse of a save from
        /// before it was a number, and whatever DD2's code may still put there (nothing of DD2's pays gold in
        /// the Estate any more: Dd2Loot). Returns what the purse got.
        /// </summary>
        public static int TakeDd2Gold()
        {
            var inventory = Singleton<GameTypeMgr>.Instance.PlayerInventory;
            var gold = RulesManager.GetRules<InventoryRules>().GOLD;
            if (inventory == null || gold == null) return 0;
            var found = inventory.GetGoldQty();
            if (found <= 0) return 0;
            inventory.RemoveItem(gold, found);
            var worth = (int)Math.Min(int.MaxValue - Current.Purse, (long)found * Dd2GoldWorth);
            Current.Purse += worth;
            Journal(worth, "dd2 gold");
            return worth;
        }

        // ---- save section ----------------------------------------------------------------------------

        public JObject ToJson()
        {
            var json = new JObject
            {
                ["format"] = Format,
                ["week"] = Week,
                ["gold"] = Purse,
                ["heirlooms"] = JObject.FromObject(Heirlooms),
                ["buildings"] = JObject.FromObject(Buildings),
                ["completed_quests"] = new JArray(CompletedQuests),
                ["expeditions_won"] = ExpeditionsWon,
                ["expeditions_lost"] = ExpeditionsLost
            };
            var sections = new JObject();
            foreach (var pair in Sections)
            {
                try { sections[pair.Key] = pair.Value.Save?.Invoke(); }
                catch (Exception e) { Plugin.Log.LogError("estate section '" + pair.Key + "' failed to save: " + e); }
            }
            json["sections"] = sections;
            return json;
        }

        /// <summary>What reading the save loaded last did to bring it over; empty when it needed nothing.</summary>
        public static readonly List<string> LoadNotes = new List<string>();

        public static void LoadFrom(JObject json)
        {
            // the format the file was written in: the sweep after the load goes by it
            var savedFormat = (int?)json?["format"] ?? 0;
            LoadNotes.Clear();
            // an older save's gold in DD1's numbers, on a copy: every section below reads the amounts as its own
            try { json = DD2Estate.Core.EstateSaveRules.BroughtOver(json, LoadNotes); }
            catch (Exception e) { Plugin.Log.LogError("Estate: the save's gold could not be brought over to DD1's numbers: " + e); }
            foreach (var note in LoadNotes) Plugin.Log.LogInfo("Estate: " + note);
            var state = new EstateState();
            if (json != null)
            {
                state.Week = (int?)json["week"] ?? 1;
                state.Purse = (int?)json["gold"] ?? 0;
                if (json["heirlooms"] is JObject heirlooms)
                    foreach (var p in heirlooms.Properties()) state.Heirlooms[p.Name] = (int)p.Value;
                if (json["buildings"] is JObject buildings)
                    foreach (var p in buildings.Properties()) state.Buildings[p.Name] = (int)p.Value;
                if (json["completed_quests"] is JArray quests)
                    foreach (var q in quests) state.CompletedQuests.Add((string)q);
                state.ExpeditionsWon = (int?)json["expeditions_won"] ?? 0;
                state.ExpeditionsLost = (int?)json["expeditions_lost"] ?? 0;
            }
            Current = state;
            var sections = json?["sections"] as JObject;
            foreach (var pair in Sections)
            {
                var token = sections?[pair.Key];
                Guarded("load " + pair.Key, () => pair.Value.Load?.Invoke(token));
            }
            // The game has put heroes, roster and its own player inventory back by now. What an older save
            // kept there (format 1: the purse as stacks of DD2's gold; up to format 2: the unworn trinkets)
            // becomes the estate's own.
            Guarded("sweep after load", () => Dd2.Dd2Sweep.AfterLoad(savedFormat));
            // one line for the player of an older estate, whose every sum reads fifty times what it did
            if (json != null && savedFormat < Format)
                Guarded("note on the gold", () => ActivityLog.Add(ActivityLog.Kinds.Note,
                    "The estate keeps its books in the old coin again: every sum of gold, the purse and every price alike, is counted fifty to one of before."));
        }

        private static void Guarded(string what, Action action)
        {
            try { action?.Invoke(); }
            catch (Exception e) { Plugin.Log.LogError("estate section " + what + " failed: " + e); }
        }
    }
}
