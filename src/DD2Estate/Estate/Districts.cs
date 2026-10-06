using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Assets.Code.Actor;
using Assets.Code.Source;
using DD2Estate.Core;
using DD2Estate.Dd2;
using DD2Estate.Dungeon;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DD2Estate.Estate
{
    /// <summary>
    /// DD1's Districts for the estate: eleven buildings outside the hamlet, each bought once with gold,
    /// heirlooms and a blueprint, each a standing bonus. What a building costs and gives is DD1's, read from
    /// the player's install (<see cref="DistrictRules"/>); this class keeps the campaign's side of it (whether
    /// the districts are open, the blueprints, what stands), pays for a building, and carries the bonuses to
    /// where the estate has a place for them:
    ///
    ///   the week's end       the Bank's interest on the purse, the Puppet Theatre's relief for whoever stayed home;
    ///   the provision shop   the Granary's food, in the bag before anything is bought (Provisioning.NewShop);
    ///   an expedition        the Cartographer's torchlight table and the Granary's share of every meal
    ///                        (<see cref="Adjust"/>, Camp.Eat), the Outsiders' respite points (Camp.Begin), the
    ///                        Athenaeum's calm at a curio of knowledge (<see cref="OnCurio"/>);
    ///   the heroes           the class buffs, as a stats container on every hero (<see cref="DistrictBuffs"/>).
    ///
    /// The districts open with DD1's town event "Cornerstones" (TownEvents: week 10 or later, with one
    /// blueprint); the other blueprints are DD1's boss loot, paid when the story quest that stands for the
    /// boss's DD1 quest is won (<see cref="BlueprintSources"/>). Gold, the heirlooms and the blueprint are DD1's
    /// own amounts. Saved as the "districts" section of the estate.
    /// Without the districts' files in the install nothing here shows or does anything.
    /// </summary>
    [EstateModule]
    internal static class Districts
    {
        /// <summary>DD1's id of the screen (town_name_district).</summary>
        public const string Id = "district";
        public const string UnlockEvent = "cc_districts_unlock";
        private const string SectionKey = "districts";
        // DD1's estate bar shows the plans of fx/estate_districts (a Spine sprite): rolled up under a ruler, and spread out while open.
        private const string IconSheet = "fx/estate_districts/estate_districts.sprite.png";

        private static DistrictRules _rules;
        private static BlueprintSources _sources;
        private static DistrictState _state = new DistrictState();
        private static Rng _rng = NewRng();
        // The Granary's food is rolled once a week, however often the shop is opened.
        private static int _foodWeek = -1, _food;
        // Heroes who sat the last expedition out, taken when it ended (the week-end handlers of the buildings
        // run in no particular order and empty their slots).
        private static List<uint> _idle;
        private static bool _buttonAdded;

        /// <summary>Raised when the districts opened, a blueprint came or went, or a building was built.</summary>
        public static event Action Changed;

        private static void Register()
        {
            EstateState.RegisterSection(SectionKey, Save, Load, Reset);
            EstateState.ExpeditionEnded += expedition => Guard("expedition end", () => OnExpeditionEnded(expedition));
            EstateState.WeekAdvanced += week => Guard("week", () => OnWeekAdvanced(week));
            NarrationMoments.HamletOpened += () => Guard("bar button", AddButton);
            // a party's scouting share is in before its first step, not a second later
            NarrationMoments.ExpeditionSeen += run => Guard("scouting", DistrictBuffs.Sync);
            NarrationMoments.Tick += () => Guard("hero buffs", DistrictBuffs.Tick);
            NarrationMoments.SessionEnded += DistrictBuffs.Forget;
        }

        // The systems listened to raise their events without a net: nothing thrown here may reach them.
        private static void Guard(string what, Action action)
        {
            try { action(); }
            catch (Exception e) { Plugin.Log.LogError("Districts: " + what + " failed: " + e); }
        }

        // DD1 keeps the districts' button on the estate's bar, left of the log's. It is put there the first
        // time a hamlet comes up, and only when the install has districts to show.
        private static void AddButton()
        {
            if (_buttonAdded || !Available) return;
            _buttonAdded = true;
            // The bar plays the sprite itself (its "idle": the rolled plans under the ruler; "selected" while the
            // screen is open: the plans spread out on the ink). What is named here is the pointer's box, the
            // ruler's, and what is drawn if the sprite cannot be read: the places are the skeleton's own
            // (estate_districts.sprite.skel, y turned down), every picture at 0.6 of its sheet size.
            TownPanelButtons.Add(new TownPanelButtons.Entry
            {
                // after the crier's bell, which has the row's fifth place in a week with an event (DD1's own screen
                // shows the bell at 1360); a button that is away leaves no gap. Before the districts are open DD1's
                // row has no button for them at all.
                Id = Id, Art = IconSheet + "#ruler", Scale = 0.6f, Offset = new Vector2(-3.9f, 0.2f), Size = new Vector2(62f, 74f), Place = 5,
                Layers = new[]
                {
                    new TownPanelButtons.Layer { Art = new[] { IconSheet + "#blueprint_closed" }, Offset = new Vector2(14.1f, -8f), Scale = 0.6f },
                    new TownPanelButtons.Layer { Art = new[] { IconSheet + "#ruler" }, Offset = new Vector2(-3.9f, 0.2f), Scale = 0.6f }
                },
                Name = () => DistrictText.ScreenName, ShownWhile = () => Unlocked,
                Toggle = DistrictsPanel.Toggle, IsOpen = () => DistrictsPanel.IsOpen
            });
        }

        // ---- DD1's data ----------------------------------------------------------------------------------

        /// <summary>DD1's buildings; none when the install has no districts.</summary>
        public static DistrictRules Rules
        {
            get
            {
                if (_rules != null) return _rules;
                try
                {
                    _rules = DistrictRules.Load(new Dd1Files());
                    if (_rules.Missing) Plugin.Log.LogInfo("Districts: " + DistrictRules.File + " is not in this DD1 install; the estate has no districts");
                    else
                        Plugin.Log.LogInfo("Districts (DD1): " + string.Join(", ", _rules.Buildings.Select(b => b.Id + " " + string.Join("+", DistrictRules.Price(b).Select(c => c.ToString()))))
                                           + (_rules.StockBuffsInUse ? "; " + DistrictRules.BuffsFile + " not readable, DD1's stock buffs in use" : ""));
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("Districts could not be read from DD1: " + e.Message);
                    _rules = DistrictRules.Parse(null, null);
                }
                return _rules;
            }
        }

        /// <summary>The DD1 quests whose bosses carry a blueprint.</summary>
        public static BlueprintSources Sources
        {
            get
            {
                if (_sources != null) return _sources;
                _sources = BlueprintSources.Load(new Dd1Files());
                Plugin.Log.LogInfo("Districts: blueprints from " + _sources.ByQuest.Count + " DD1 quests" + (_sources.Missing ? " (DD1's quests not readable, the stock list in use)" : ""));
                return _sources;
            }
        }

        /// <summary>False on an install without the districts' files: no button, no screen, no bonus.</summary>
        public static bool Available => !Rules.Missing;

        public static DistrictState State => _state;

        /// <summary>DD1: the screen is locked until the town event "Cornerstones".</summary>
        public static bool Unlocked => Available && _state.Unlocked;

        public static int Blueprints => _state.Blueprints;

        public static bool IsBuilt(string id) => Available && _state.Has(id);

        // ---- opening, blueprints -------------------------------------------------------------------------

        /// <summary>Opens the districts for building (DD1's town event effect districts_unlocked). False when they were open already.</summary>
        public static bool Unlock(string why)
        {
            if (!Available || _state.Unlocked) return false;
            _state.Unlocked = true;
            Plugin.Log.LogInfo("Districts: open for building (" + why + ")");
            ActivityLog.Add(ActivityLog.Kinds.Note, ActivityLogText.Building(DistrictText.ScreenName) + " " + ActivityLogText.Good("Craftsmen return: new buildings may be raised outside the hamlet."));
            RaiseChanged();
            return true;
        }

        /// <summary>Blueprints come (a boss's loot, the town event) or go (a test).</summary>
        public static void AddBlueprints(int amount, string why)
        {
            if (amount == 0) return;
            _state.Blueprints = Math.Max(0, _state.Blueprints + amount);
            Plugin.Log.LogInfo("Districts: " + (amount > 0 ? "+" : "") + amount + " blueprint (" + why + "), " + _state.Blueprints + " held");
            RaiseChanged();
        }

        // ---- building ------------------------------------------------------------------------------------

        private static Dictionary<string, int> Purse()
        {
            var purse = new Dictionary<string, int> { { DistrictRules.Gold, EstateState.Gold }, { DistrictRules.Blueprint, _state.Blueprints } };
            foreach (var kind in EstateState.HeirloomIds) purse[kind] = EstateState.Current.Heirloom(kind);
            return purse;
        }

        /// <summary>How many of a currency of a building's price the estate holds.</summary>
        public static int Held(string currency)
        {
            return Purse().TryGetValue(currency, out var held) ? held : 0;
        }

        /// <summary>A building's price and whether it can be built now.</summary>
        public static DistrictOffer Quote(string id) => Rules.Quote(_state, id, Purse());

        /// <summary>Why an offer cannot be taken, in words; null for one that can.</summary>
        public static string Reason(DistrictOffer offer)
        {
            if (offer == null) return "No such building";
            switch (offer.Refusal)
            {
                case DistrictRefusal.NoSuchBuilding: return "No such building";
                case DistrictRefusal.Locked: return DistrictText.LockedName;
                case DistrictRefusal.AlreadyBuilt: return "Already built";
                case DistrictRefusal.CannotPay: return "Not enough " + DistrictText.Currency(offer.ShortOf, 2).ToLowerInvariant();
                default: return null;
            }
        }

        /// <summary>Pays a building's price and builds it. Returns null, or why nothing was built.</summary>
        public static string Build(string id)
        {
            if (!EstateSession.InHub || EstateSession.View != EstateSession.Screen.Hamlet) return "Not while away from the hamlet";
            var offer = Rules.Build(_state, id, Purse());
            var reason = Reason(offer);
            if (reason != null) return reason;

            var estate = EstateState.Current;
            foreach (var cost in offer.Price)
            {
                if (cost.Currency == DistrictRules.Gold) EstateState.AddGold(-cost.Amount);
                else if (cost.Currency == DistrictRules.Blueprint) _state.Blueprints = Math.Max(0, _state.Blueprints - cost.Amount);
                else estate.AddHeirloom(cost.Currency, -cost.Amount);
            }
            // a Granary built in the middle of a week hands out this week's food too
            _foodWeek = -1;
            EstateAudio.Ui("town/build_district");
            var paid = string.Join(", ", offer.Price.Select(c => UpgradeUi.Amount(c.Amount) + " " + DistrictText.Currency(c.Currency, c.Amount).ToLowerInvariant()));
            Plugin.Log.LogInfo("Districts: " + id + " built for " + paid);
            ActivityLog.Add(ActivityLog.Kinds.Note, ActivityLogText.Building(DistrictText.ScreenName) + " " + ActivityLogText.Good(DistrictText.Title(offer.Building)) + " is built: " + paid + ".");
            DistrictBuffs.Sync();
            RaiseChanged();
            EstatePersistence.SaveNow("district");
            return null;
        }

        private static void RaiseChanged()
        {
            try { Changed?.Invoke(); }
            catch (Exception e) { Plugin.Log.LogError("Districts: a Changed handler failed: " + e); }
        }

        // ---- the week turns ------------------------------------------------------------------------------

        private static string ClassOf(ActorInstance actor) => actor.ActorDataClass != null ? actor.ActorDataClass.Id : actor.ActorDataId;

        /// <summary>The DD1 class a DD2 hero stands for at the districts: the class id, which the two games share for every hero both have.</summary>
        public static string ClassId(ActorInstance actor) => actor != null ? ClassOf(actor) ?? "" : "";

        // DD1's "idle": at home and not busy. The party is away, and whoever cannot join it has a place in a
        // building (or is missing). The same test the town events use.
        private static List<uint> IdleHeroes()
        {
            var idle = new List<uint>();
            foreach (var guid in RosterLifecycle.LivingGuids())
                if (!EstateSession.IsInParty(guid) && EstateSession.PartyBlockReason(guid) == null) idle.Add(guid);
            return idle;
        }

        private static void OnExpeditionEnded(EstateState.Expedition expedition)
        {
            if (!EstateSession.Active || !Available) return;
            _idle = IdleHeroes();
        }

        // DD1 lists a blueprint as an heirloom item of the bag (inventory_item: .type "heirloom" .id "blueprint"); its
        // card is the feature's own picture.
        private static readonly ItemDef BlueprintItem = new ItemDef { Type = ItemTypes.Heirloom, Id = DistrictRules.Blueprint, IconRoot = DistrictRules.Feature };

        /// <summary>
        /// DD1's boss loot at an expedition's end (DungeonRun.Finish, once the bag is brought home): a story quest
        /// stands for a DD1 plot quest, whose monsters carry a blueprint or do not, and the quest is won when they
        /// are dead. The blueprints go to the estate and, as one more stack of the haul, onto the results screen's
        /// row of heirlooms.
        /// </summary>
        public static void BossLoot(string questId, bool success, RaidHaul haul)
        {
            try
            {
                if (!success || !EstateSession.Active || !Available) return;
                var story = Memoirs.PlotQuest(questId);
                var found = story != null ? Sources.For((string)story["dd1_quest_id"]) : 0;
                if (found <= 0) return;
                AddBlueprints(found, "boss loot of " + questId);
                haul?.Stacks.Add(new HaulStack { Item = BlueprintItem, Amount = found });
                ActivityLog.Add(ActivityLog.Kinds.Note, ActivityLogText.Building(DistrictText.ScreenName) + " " + ActivityLogText.Good("+" + found + " " + DistrictText.Currency(DistrictRules.Blueprint, found)) + ".");
            }
            catch (Exception e) { Plugin.Log.LogError("Districts: the boss's blueprint could not be handed over: " + e); }
        }

        private static void OnWeekAdvanced(int week)
        {
            var idle = _idle;
            _idle = null;
            if (!EstateSession.Active || !Available) return;

            // DD1's Bank (weekly_gold_interest): a share of the gold the estate holds when the party is home.
            // The share is seldom whole: the fraction is the chance of one more, as with the Tavern's relief.
            var rate = Rules.Interest(_state);
            if (rate > 0)
            {
                var interest = Whole(EstateState.Gold * rate);
                if (interest > 0)
                {
                    EstateState.AddGold(interest);
                    var line = "The Bank pays interest on the estate's gold: " + UpgradeUi.Amount(interest) + " gold.";
                    Plugin.Log.LogInfo("Districts, week end: " + line);
                    QuestBoard.Report(line);
                    ActivityLog.Add(ActivityLog.Kinds.Note, ActivityLogText.Building(BankName) + " " + ActivityLogText.Good("+" + UpgradeUi.Amount(interest) + " gold") + " of interest.");
                }
            }

            // DD1's Puppet Theatre (stress_relief): stress every idle hero sheds in a week, on DD1's scale of
            // 200. On DD2's scale that is half a point: the fraction is the chance of a point.
            var relief = Rules.IdleRelief(_state);
            if (relief > 0)
            {
                var eased = 0;
                foreach (var guid in idle ?? IdleHeroes())
                {
                    var actor = SanitariumLedger.Actor(guid);
                    if (actor == null || !actor.IsLiving || actor.Stress <= 0f) continue;
                    var points = Whole(relief * actor.StressMax / ActivityRules.Dd1StressMax);
                    if (points <= 0) continue;
                    actor.ApplyStressHeal(points, SourceType.INN);
                    eased++;
                }
                if (eased > 0)
                {
                    var line = "The puppets played all week: " + eased + (eased == 1 ? " hero" : " heroes") + " at home shed some stress.";
                    Plugin.Log.LogInfo("Districts, week end: " + line);
                    QuestBoard.Report(line);
                }
            }
        }

        private static string BankName
        {
            get
            {
                foreach (var building in Rules.Buildings)
                    if (_state.Has(building.Id) && building.EffectsOf(DistrictEffectKind.Interest).Any()) return DistrictText.Title(building);
                return DistrictText.ScreenName;
            }
        }

        // A whole number with the same average: the fraction is the chance of one more.
        private static int Whole(double value)
        {
            var whole = Math.Floor(value);
            return (int)whole + (_rng.NextDouble() < value - whole ? 1 : 0);
        }

        // ---- what other systems ask ----------------------------------------------------------------------

        /// <summary>
        /// An expedition's rules as the estate's districts change them: the Cartographer's Camp's torchlight
        /// table and loot of the dark in the place of DD1's own, and the Granary's share on top of what eating
        /// restores. Once per run, when it is put up: a new one as it begins, one out of a save when the hub
        /// is in again (by then every section of the save is read, in whatever order they came).
        /// </summary>
        public static void Adjust(RaidRules raid, LootTables loot)
        {
            try
            {
                var eat = Available ? Rules.EatBonus(_state) : 0;
                // the bag's rules are shared by every expedition of the session: set, never multiplied
                InventoryContent.Rules.EatBonus = eat;
                if (!Available) return;
                if (raid != null) raid.HungerHeal *= 1 + eat;
                var light = Rules.Light(_state);
                if (light == null) return;
                var bands = raid != null && light.Darkness != null && raid.UseDarkness(light.Darkness);
                if (loot != null && light.DarknessLoot != null) loot.UseDarknessBonuses(light.DarknessLoot);
                Plugin.Log.LogInfo("Districts: the expedition walks by the Cartographer's torchlight" + (bands ? "" : " (its table could not be read: DD1's own stays)"));
            }
            catch (Exception e) { Plugin.Log.LogError("Districts: the expedition's rules could not be adjusted: " + e); }
        }

        /// <summary>What a meal restores, as a multiple of DD1's own: 1.15 with the Granary.</summary>
        public static double EatFactor => 1 + (Available ? Rules.EatBonus(_state) : 0);

        /// <summary>The Granary's food of this week (DD1: 4 to 10), for the bag of the provision shop. 0 without one.</summary>
        public static int FreeFood()
        {
            if (!Available) return 0;
            var week = EstateState.Current.Week;
            if (_foodWeek != week)
            {
                _foodWeek = week;
                _food = Rules.RollSupply(_state, ItemTypes.Provision, "", _rng);
                if (_food > 0) Plugin.Log.LogInfo("Districts: the Granary hands out " + _food + " food in week " + week);
            }
            return _food;
        }

        /// <summary>Respite points a camp of this party gets on top of DD1's own (the Outsiders Bonfire: 2 with one of its heroes at the fire).</summary>
        public static int CampPoints()
        {
            if (!Available) return 0;
            return Rules.CampPoints(_state, Provisioning.Party().Select(ClassId));
        }

        /// <summary>
        /// A hero has used a curio. DD1's Athenaeum: one of its classes sheds stress at a curio of knowledge
        /// (15 of DD1's 200; on DD2's scale under a point, so the fraction is the chance of one).
        /// </summary>
        public static void OnCurio(CurioDef curio, uint heroGuid, Action<string> say)
        {
            try
            {
                if (!Available || curio == null || heroGuid == 0u) return;
                var hero = SanitariumLedger.Actor(heroGuid);
                if (hero == null || !hero.IsLiving) return;
                var dd1 = Rules.CurioStressHeal(_state, ClassId(hero), curio.Tags);
                if (dd1 <= 0 || hero.Stress <= 0f) return;
                var points = Whole(dd1 * hero.StressMax / ActivityRules.Dd1StressMax);
                if (points <= 0) return;
                hero.ApplyStressHeal(points, SourceType.STORY);
                say?.Invoke(hero.ActorName + " finds comfort in what is written here.");
            }
            catch (Exception e) { Plugin.Log.LogError("Districts: the curio's comfort could not be given: " + e); }
        }

        // ---- save ----------------------------------------------------------------------------------------

        private static Rng NewRng() => new Rng(DateTime.Now.Ticks ^ 0x44697374L);

        private static JToken Save()
        {
            var json = _state.ToJson();
            json["rng"] = _rng.State.ToString(CultureInfo.InvariantCulture);
            json["food_week"] = _foodWeek;
            json["food"] = _food;
            json["heroes"] = DistrictBuffs.Save();
            return json;
        }

        // Runs when the game has already loaded the heroes (EstatePersistence.Continue): their buffs go back on.
        private static void Load(JToken token)
        {
            Reset();
            if (!(token is JObject json)) return;
            _state = DistrictState.FromJson(json);
            if (ulong.TryParse((string)json["rng"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var state)) _rng = Rng.FromState(state);
            _foodWeek = (int?)json["food_week"] ?? -1;
            _food = Math.Max(0, (int?)json["food"] ?? 0);
            DistrictBuffs.Load(json["heroes"]);
        }

        private static void Reset()
        {
            _state = new DistrictState();
            _rng = NewRng();
            _foodWeek = -1;
            _food = 0;
            _idle = null;
            DistrictBuffs.Forget();
        }

        // ---- dev bridge ----------------------------------------------------------------------------------

        /// <summary>For tests: the districts of a new estate again.</summary>
        public static void ResetForTest()
        {
            DistrictBuffs.TakeOff();
            Reset();
            DistrictBuffs.Sync();
            RaiseChanged();
        }

        /// <summary>For tests: a building stands or does not, without a price.</summary>
        public static string SetBuilt(string id, bool built)
        {
            if (Rules.Find(id) == null) return "no such building";
            if (built && !_state.Has(id)) _state.Built.Add(id);
            if (!built) _state.Built.Remove(id);
            _foodWeek = -1;
            DistrictBuffs.Sync();
            RaiseChanged();
            return null;
        }

        /// <summary>For tests: the week's ends of the Bank and the Puppet Theatre, without a week passing.</summary>
        public static void WeekEndForTest() => OnWeekAdvanced(EstateState.Current.Week);

        public static object Describe()
        {
            var rules = Rules;
            var buildings = new List<object>();
            foreach (var building in rules.Buildings)
            {
                var offer = Quote(building.Id);
                var leftOut = new List<string>();
                foreach (var effect in building.EffectsOf(DistrictEffectKind.HeroBuff))
                    foreach (var id in effect.BuffIds)
                        if (rules.BuffEffect(id, effect.Tags.Count == 0).LeftOut.Count > 0) leftOut.Add(id);
                buildings.Add(new
                {
                    id = building.Id,
                    title = DistrictText.Title(building),
                    built = _state.Has(building.Id),
                    price = offer.Price.Select(c => new { currency = c.Currency, amount = c.Amount, held = Held(c.Currency) }).ToList(),
                    dd1Price = string.Join(", ", building.Costs.Select(c => c.ToString())),
                    blocked = Reason(offer),
                    gives = DistrictText.Lines(building).Select(ActivityLogText.Plain).ToList(),
                    heroes = DistrictText.Classes(building),
                    leftOut,
                    effects = building.Effects.Select(e => e.Type + (e.Kind == DistrictEffectKind.Unknown ? " (no reading)" : "")).ToList()
                });
            }
            return new
            {
                available = Available,
                unlocked = _state.Unlocked,
                blueprints = _state.Blueprints,
                built = _state.Built,
                week = EstateState.Current.Week,
                gold = EstateSession.Active ? EstateState.Gold : 0,
                heirlooms = EstateState.HeirloomIds.ToDictionary(kind => kind, kind => EstateState.Current.Heirloom(kind)),
                stockBuffs = rules.StockBuffsInUse,
                interest = rules.Interest(_state),
                idleRelief = rules.IdleRelief(_state),
                eatFactor = EatFactor,
                food = new { week = _foodWeek, amount = _food },
                blueprintQuests = Available ? Sources.ByQuest : null,
                screen = DistrictsPanel.Snapshot(),
                buffs = DistrictBuffs.Describe(),
                buildings
            };
        }
    }
}
