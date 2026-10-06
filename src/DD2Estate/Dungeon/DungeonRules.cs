using System;
using System.Collections.Generic;
using System.Linq;
using Assets.Code.Actor;
using Assets.Code.Library;
using Assets.Code.Utils;
using DD2Estate.Core;
using DD2Estate.Dd2;
using DD2Estate.Estate;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// DD1's rules of an expedition that are decided while it is walked, with DD1's numbers (each from the
    /// file its rule class names):
    ///
    ///   a fight begins          who is caught off guard (<see cref="SurpriseRules"/>): each side's base chance
    ///                           by hallway or room, scouted or not, the torchlight's share, the camp's buffs;
    ///                           nobody in the Darkest Dungeon (is_surprise_enabled) and nobody against a boss;
    ///   a hallway fight begins  a wanderer may take its place (<see cref="WanderingRules"/>): the Collector
    ///                           while the bag is nearly full, the Shambler in the dark, once an expedition each;
    ///   a torch at its altar    calls the Shambler up (the curio's own "Summon" result);
    ///   a curio is come to      a hero whose quirk draws them to its kind may reach for it unasked, and a
    ///                           kleptomaniac keeps what they find (<see cref="CompulsionRules"/>);
    ///   moments of the walk     the Ancestor's remarks (audio/narration.json, by <see cref="Narration"/>);
    ///   the quest begins        the under-levelled start it stressed (<see cref="RosterUpkeep"/>), and so do
    ///                           those who swore never to come back (<see cref="DarkestDungeon"/>).
    ///
    /// The fights themselves are DD2's: only when one starts, against whom and with what advantage is decided here.
    /// </summary>
    internal partial class DungeonRun
    {
        // ---- the plot quest's own rules --------------------------------------------------------------

        /// <summary>DD1's <c>is_surprise_enabled</c> of the plot quest a story quest stands for: false in the Darkest Dungeon.</summary>
        public bool SurpriseEnabled => _surpriseOverride ?? QuestBoard.Dd1Plot(_quest)?.SurpriseEnabled ?? true;

        // Dev bridge: the Darkest Dungeon's "no surprise" on any expedition (or off in a descent), to look at it.
        private bool? _surpriseOverride;

        /// <summary>
        /// Dev bridge: puts the plot quest's three switches on this expedition whatever its quest says (null
        /// leaves one as it is): no surprise, no scouting on entering a room, a torch that nothing moves.
        /// </summary>
        internal void OverrideRules(bool? surprise, bool? scouting, bool? torchBurns)
        {
            if (surprise != null) _surpriseOverride = surprise;
            if (scouting != null) _x.ScoutingEnabled = scouting.Value;
            if (torchBurns != null) _x.TorchBurns = torchBurns.Value;
        }

        /// <summary>DD1's difficulty number of the expedition (1, 3, 5, and 6 for the Darkest Dungeon).</summary>
        public int QuestDifficulty => _quest != null ? _quest.Difficulty : _map.Tier;

        /// <summary>Dev bridge: the rules this expedition runs under, and where each of them stands.</summary>
        internal object DescribeRules()
        {
            var plot = QuestBoard.Dd1Plot(_quest);
            return new
            {
                quest = _quest?.Id, dungeon = _map.DungeonId, difficulty = QuestDifficulty,
                dd1Plot = plot == null ? null : new
                {
                    id = plot.Id, surprise = plot.SurpriseEnabled, scouting = plot.ScoutingEnabled, torchSetting = plot.TorchSetting ?? "default",
                    canRetreat = plot.CanRetreat, retreatDeaths = plot.RetreatDeaths, rosterBuffsOnFailure = plot.RosterBuffsOnFailure, rosterBuffMinPartyLevel = plot.RosterBuffMinPartyLevel
                },
                scoutingEnabled = _x.ScoutingEnabled, scoutChance = _x.ScoutChance, torchBurns = _x.TorchBurns, light = _x.Light,
                surprise = DescribeSurprise(), wanderers = DescribeWanderers(), compulsions = DescribeCompulsions()
            };
        }

        // ---- surprise --------------------------------------------------------------------------------

        private static SurpriseRules _surpriseRules;

        internal static SurpriseRules Surprise => _surpriseRules ?? (_surpriseRules = SurpriseRules.Load(new Dd1Files()));

        /// <summary>Dev bridge: the side caught off guard in the next fight, whatever the dice would say.</summary>
        internal static SurpriseSide? ForcedSurprise;

        private string _lastSurprise = "";

        // What DD1's monsters of this fight say about surprise: its bosses can neither surprise nor be
        // surprised, a wanderer says so itself (the Shambler always surprises), anybody else is ordinary.
        private MonsterBattleModifier ModifierOf(FightStarts fight, string wanderer)
        {
            if (wanderer != null) return WandererModifier(wanderer);
            // (a boss's room nobody stands in is filled from the room pool: an ordinary fight)
            return fight.Slot.Kind == EncounterKind.Boss && _quest?.Boss != null ? MonsterBattleModifier.Boss : MonsterBattleModifier.Ordinary;
        }

        private SurpriseSide RollSurprise(FightStarts fight, string wanderer)
        {
            var room = _x.RoomId >= 0;
            var modifier = ModifierOf(fight, wanderer);
            var partyBuff = _campBuffs.Total(CampContent.Rules, CampBuffKind.PartySurprise);
            var monstersBuff = _campBuffs.Total(CampContent.Rules, CampBuffKind.MonstersSurprise);
            var band = _x.LightBand;
            var heroes = LivingParty().Count;
            var odds = Surprise.Odds(room, fight.Scouted, band, partyBuff, monstersBuff, heroes);
            SurpriseSide side;
            string how;
            if (ForcedSurprise != null)
            {
                side = ForcedSurprise.Value;
                ForcedSurprise = null;
                how = "forced";
            }
            else
            {
                // DD1's order: a quest without surprise rolls nothing, and the fight's monsters still have their
                // word (one that always surprises does so there too)
                var rolled = SurpriseEnabled ? Surprise.Roll(room, fight.Scouted, band, partyBuff, monstersBuff, heroes, _rng) : SurpriseSide.None;
                side = SurpriseRules.Settle(rolled, modifier);
                how = !SurpriseEnabled ? "the quest has no surprise" : side != rolled ? "rolled " + rolled + ", the monsters say otherwise" : "rolled";
            }
            _lastSurprise = (room ? "room" : "hallway") + (fight.Scouted ? ", known" : ", unknown") + ", light " + Mathf.RoundToInt((float)_x.Light) + ", " + heroes + " heroes"
                            + ": party " + odds.PartyShare.ToString("0.###") + ", monsters " + odds.MonstersShare.ToString("0.###") + " -> " + side + " (" + how + ")";
            Plugin.Log.LogInfo("Dungeon: surprise: " + _lastSurprise);
            return side;
        }

        /// <summary>Dev bridge: DD1's numbers, and the chances as the party stands now.</summary>
        internal object DescribeSurprise()
        {
            var rules = Surprise;
            var band = _x.LightBand;
            var partyBuff = _campBuffs.Total(CampContent.Rules, CampBuffKind.PartySurprise);
            var monstersBuff = _campBuffs.Total(CampContent.Rules, CampBuffKind.MonstersSurprise);
            var heroes = LivingParty().Count;
            object At(bool room, bool known)
            {
                var odds = rules.Odds(room, known, band, partyBuff, monstersBuff, heroes);
                return new { party = odds.PartyShare, monsters = odds.MonstersShare, weights = new { nobody = odds.None, party = odds.Party, monsters = odds.Monsters } };
            }
            return new
            {
                enabled = SurpriseEnabled,
                forced = ForcedSurprise?.ToString(),
                light = _x.Light, heroes,
                band = new { heroes_surprised_increase = band.Value("heroes_surprised_increase"), monsters_surprised_increase = band.Value("monsters_surprised_increase") },
                campBuffs = new { party = partyBuff, monsters = monstersBuff },
                now = new { hallway = At(false, false), hallwayKnown = At(false, true), room = At(true, false), roomKnown = At(true, true) },
                boss = "never (DD1's bosses: can_surprise False, can_be_surprised False)",
                last = _lastSurprise
            };
        }

        internal static object DescribeSurpriseRules()
        {
            var rules = Surprise;
            return new
            {
                corridor = new { party = rules.CorridorParty, monsters = rules.CorridorMonsters },
                room = new { party = rules.RoomParty, monsters = rules.RoomMonsters },
                knownCorridor = new { party = rules.KnownCorridorParty, monsters = rules.KnownCorridorMonsters },
                knownRoom = new { party = rules.KnownRoomParty, monsters = rules.KnownRoomMonsters },
                max = new { party = rules.MaxParty, monsters = rules.MaxMonsters },
                leastWeightOfNobody = SurpriseRules.LeastNobody
            };
        }

        // ---- wanderers -------------------------------------------------------------------------------

        // How often each DD1 wanderer has come on this expedition (DD1's .limit).
        private readonly Dictionary<string, int> _wanderersMet = new Dictionary<string, int>();
        // The wanderer DD1's roll put on a hallway tile ("hallway:segment"), until it is beaten there.
        private readonly Dictionary<string, string> _wandererAt = new Dictionary<string, string>();
        // The tile of the fight in progress, when that fight is a wanderer's of the roll.
        private string _fightTile;
        private WanderingRules _wandering;
        private string _lastWanderer = "";

        /// <summary>Dev bridge: the DD1 monster ("collector", "shambler") that takes the next hallway fight's place whatever the dice say.</summary>
        internal static string ForcedWanderer;

        /// <summary>
        /// DD1's conditional mash of this dungeon at this level; the data's own copy of its numbers
        /// (Data/dungeons.json) when the install has no such file to read.
        /// </summary>
        internal WanderingRules Wandering
        {
            get
            {
                if (_wandering != null) return _wandering;
                _wandering = WanderingRules.Load(new Dd1Files(), _map.DungeonId, _map.Tier);
                if (_wandering.Source == null)
                    _wandering.Lines.AddRange(DungeonContent.WandererFallback(_map.DungeonId, DungeonContent.TierName(_map.DungeonId, _map.Tier)));
                return _wandering;
            }
        }

        /// <summary>The share of the bag's slots that hold something (DD1's inventory_valid_item_percent).</summary>
        internal double BagShare => _bag.SlotCount > 0 ? (_bag.SlotCount - _bag.FreeSlots) / (double)_bag.SlotCount : 0;

        // DD1's letter of a monster's level: collector_A in an apprentice dungeon, _B veteran, _C champion.
        private string WandererClass(string id)
        {
            var monster = DungeonContent.WandererDd1(id) ?? id;
            return monster + (_map.Tier >= 5 ? "_C" : _map.Tier >= 3 ? "_B" : "_A");
        }

        private MonsterBattleModifier WandererModifier(string id) => MonsterBattleModifier.Load(new Dd1Files(), WandererClass(id));

        /// <summary>
        /// The wanderer of this fight, with its DD2 fight: the one the party called up (the event names it),
        /// or the one DD1's dice put in a hallway fight's place. Null: the fight is the tile's own.
        /// </summary>
        private string PickWanderer(FightStarts fight, string tier, out string config, out string arena)
        {
            config = arena = null;
            _fightTile = null;
            var monster = fight.Wanderer;
            var how = "called up";
            var counts = false;
            if (monster == null)
            {
                // DD1's conditional mashes are all "hall:" lines: a room's fight is never a wanderer's
                if (_x.RoomId >= 0 || _x.HallwayId < 0 || fight.Slot.Kind != EncounterKind.Hallway) return null;
                var tile = _x.HallwayId + ":" + _x.Segment;
                if (_wandererAt.TryGetValue(tile, out monster)) how = "still where the party left it";
                else if (ForcedWanderer != null)
                {
                    monster = ForcedWanderer;
                    ForcedWanderer = null;
                    how = "forced";
                }
                else
                {
                    monster = Wandering.Roll(_x.Light, BagShare, _wanderersMet, _rng)?.Monster;
                    how = "rolled";
                    counts = true;
                }
                if (monster == null) return null;
                _fightTile = tile;
            }
            var id = DungeonContent.Wanderer(monster, _map.DungeonId);
            if (id != null) DungeonContent.PickWanderer(_map.DungeonId, id, tier, _x.RoomId >= 0 ? EncounterKind.Room : EncounterKind.Hallway, _rng, out config, out arena);
            if (config == null)
            {
                Plugin.Log.LogWarning("Dungeon: the wanderer '" + monster + "' has no fight in this dungeon or game build; the fight is the tile's own");
                _fightTile = null;
                return null;
            }
            if (_fightTile != null)
            {
                // DD1 keeps the roll's answer on the tile: a party that flees finds the same wanderer there, and
                // the line's limit counts it once
                _wandererAt[_fightTile] = monster;
                if (counts)
                {
                    _wanderersMet.TryGetValue(monster, out var times);
                    _wanderersMet[monster] = times + 1;
                }
            }
            _lastWanderer = monster + " (" + how + ", light " + Mathf.RoundToInt((float)_x.Light) + ", bag " + BagShare.ToString("0.##") + ")";
            Plugin.Log.LogInfo("Dungeon: wanderer: " + _lastWanderer + " -> " + config + " @ " + arena);
            Say(DungeonContent.WandererName(id) + " bars the way.");
            // DD1 (the monster's torchlight_modifier): the Shambler's fight is held in the dark, and the torch stays out
            var torch = WandererModifier(id).Torchlight;
            if (torch != null && _x.Light > torch[1]) ChangeLight(torch[1] - _x.Light);
            else if (torch != null && _x.Light < torch[0]) ChangeLight(torch[0] - _x.Light);
            return id;
        }

        // DD1's "Summon" result of a curio: "summon_mash_shambler" for a torch set to the Shambler's altar.
        private void Summon(string value)
        {
            const string prefix = "summon_mash_";
            var monster = value != null && value.StartsWith(prefix, StringComparison.Ordinal) ? value.Substring(prefix.Length) : value;
            if (monster == null || DungeonContent.Wanderer(monster, _map.DungeonId) == null)
            {
                Say("Something stirs, and is still again.");
                Plugin.Log.LogWarning("Dungeon: a curio summons '" + value + "', which the data has no fight for");
                return;
            }
            Say("The flame gutters. Something answers.");
            Add(_x.StartFight(monster));
        }

        /// <summary>Dev bridge: a wanderer here and now, as if called up at its altar. Returns why not, or null.</summary>
        internal string SummonForTest(string monster)
        {
            if (Busy || !EstateSession.InHub || _x.Status != RaidStatus.InProgress) return "Not now.";
            if (DungeonContent.Wanderer(monster, _map.DungeonId) == null) return "The data has no wanderer '" + monster + "' in this dungeon.";
            var events = _x.StartFight(monster);
            if (events.Count == 0) return "The party is not free to fight.";
            Enqueue(events);
            return null;
        }

        internal object DescribeWanderers()
        {
            var rules = Wandering;
            var lines = new List<object>();
            foreach (var line in rules.Lines)
            {
                var id = DungeonContent.Wanderer(line.Monster, _map.DungeonId);
                var modifier = id != null ? WandererModifier(id) : null;
                lines.Add(new
                {
                    monster = line.Monster, types = line.Types, chance = line.Chance, light = line.Light, bagShare = line.BagShare, limit = line.Limit, canBeAmbush = line.CanBeAmbush,
                    holdsNow = line.Holds(_x.Light, BagShare), open = WanderingRules.Open(line, _wanderersMet),
                    fight = id, name = id != null ? DungeonContent.WandererName(id) : null,
                    dd1Class = id != null ? WandererClass(id) : null,
                    battleModifier = modifier == null ? null : new { modifier.CanSurprise, modifier.CanBeSurprised, modifier.AlwaysSurprise, modifier.AlwaysBeSurprised, torchlight = modifier.Torchlight }
                });
            }
            return new
            {
                source = rules.Source ?? "Data/dungeons.json (DD1's file not found)",
                dungeon = _map.DungeonId, level = _map.Tier,
                light = _x.Light, bagShare = BagShare, bagSlots = _bag.SlotCount, bagFree = _bag.FreeSlots,
                chanceAtNextHallwayFight = rules.ChanceNow(_x.Light, BagShare, _wanderersMet),
                met = _wanderersMet, forced = ForcedWanderer, last = _lastWanderer, lines
            };
        }

        // ---- a quirk's pull at a curio -----------------------------------------------------------------

        // The hero who reached for the curio at hand unasked, until what they found has been dealt with.
        private CompulsionCandidate _compelled;
        private string _lastCompulsion = "";

        /// <summary>Dev bridge: this hero reaches for the next curio whatever the dice say ("keep": and keeps its loot).</summary>
        internal static uint ForcedCompulsion;
        internal static bool ForcedKeepLoot;

        /// <summary>
        /// DD1: the party steps onto a hallway tile with a curio, and a hero whose quirk draws them to its kind
        /// may use it at once, with bare hands, before the player is asked (a room's curio never rolls). True
        /// when somebody did (the curio's result follows).
        /// </summary>
        private bool Compelled(CurioFound curio)
        {
            _compelled = null;
            CompulsionCandidate pick;
            var tags = _curios.Get(curio.CurioId)?.Tags ?? new List<string>();
            var drawn = new List<CompulsionCandidate>();
            if (ForcedCompulsion != 0u)
            {
                // (the bridge's forced hero reaches for a room's curio too, to be looked at anywhere)
                pick = new CompulsionCandidate { Hero = ForcedCompulsion, Compulsion = new Compulsion { Quirk = "forced", Tag = "All", Chance = 1, KeepLoot = ForcedKeepLoot } };
                ForcedCompulsion = 0u;
            }
            else if (_x.RoomId >= 0) return false;
            else
            {
                drawn = CompulsionRules.Drawn(CurioCompulsions.Of(LivingParty()), tags, _rng);
                pick = CompulsionRules.Pick(drawn, _rng);
            }
            _lastCompulsion = curio.CurioId + " [" + string.Join(", ", tags) + "]: " + drawn.Count + " drawn" + (pick != null ? ", #" + pick.Hero + " reaches for it (" + pick.Compulsion + ")" : ", nobody reaches for it");
            if (drawn.Count > 0 || pick != null) Plugin.Log.LogInfo("Dungeon: compulsion: " + _lastCompulsion);
            var hero = pick != null ? SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance.GetLibraryElement(pick.Hero) : null;
            if (hero == null || !hero.IsLiving) return false;
            var events = _x.UseCurio();
            if (events.Count == 0) return false;
            _compelled = pick;
            // the hero who acts is the one DD1's scripts walk up to the curio
            DungeonHud.Select(pick.Hero);
            Say(hero.ActorName + " cannot leave " + CurioName(curio.CurioId) + " alone" + (pick.Compulsion.Quirk != "forced" ? " (" + CurioCompulsions.QuirkName(hero, pick.Compulsion) + ")." : "."));
            Add(events);
            return true;
        }

        /// <summary>
        /// DD1's keep_loot: the loot of a curio a kleptomaniac opened by their own compulsion is theirs. True
        /// when the drops are gone that way (DD1's own words for it are the hero's).
        /// </summary>
        private bool KeepsLoot(List<LootDrop> drops)
        {
            var compelled = _compelled;
            if (compelled == null || !compelled.Compulsion.KeepLoot || drops == null || drops.Count == 0) return false;
            var hero = SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance.GetLibraryElement(compelled.Hero);
            var words = Dd1Strings.Get("str_keep_loot_string") ?? "I'll be keeping this for myself. Reward, hard earned...";
            Say((hero != null ? hero.ActorName : "The hero") + ": \"" + words + "\"");
            // the hero says so on the screen, in DD1's speech balloon
            DungeonHud.Bark(compelled.Hero, words);
            Plugin.Log.LogInfo("Dungeon: compulsion: #" + compelled.Hero + " keeps " + drops.Count + " drop(s) of loot");
            return true;
        }

        internal object DescribeCompulsions()
        {
            var curio = _x.CurioHere;
            var def = curio != null ? _curios.Get(curio) : null;
            var compulsions = CurioCompulsions.Of(LivingParty());
            var party = new List<object>();
            foreach (var hero in LivingParty())
            {
                var own = compulsions.Find(c => c.Hero == hero.ActorGuid);
                var pulls = new List<object>();
                foreach (var compulsion in own?.Compulsions ?? new List<Compulsion>())
                    pulls.Add(new { quirk = compulsion.Quirk, tag = compulsion.Tag, chance = compulsion.Chance, keepLoot = compulsion.KeepLoot, drawnByCurioHere = def != null && CompulsionRules.Draws(compulsion, def.Tags) });
                party.Add(new { guid = hero.ActorGuid, name = hero.ActorName, quirks = hero.QuirkContainer?.GetIds(), compulsions = pulls });
            }
            return new
            {
                curioHere = curio, curioTags = def?.Tags, inHallway = _x.RoomId < 0,
                forcedHero = ForcedCompulsion, forcedKeepLoot = ForcedKeepLoot, last = _lastCompulsion, party,
                rules = CurioCompulsions.Describe()
            };
        }

        // ---- the Ancestor on the way ---------------------------------------------------------------------

        // The party's stress added up when it was last looked at; negative: not looked at yet.
        private float _stressSeen = -1f;

        /// <summary>A moment of the walk by DD1's trigger id; the tags are the dungeon and what the moment is about.</summary>
        private void Narrate(string triggerId, params string[] about)
        {
            Narration.Trigger(triggerId, Narration.Scope.Dungeon, RaidNarration.Tags(_map.DungeonId, about));
        }

        // DD1's "torchlight_full" and "torchlight_out": the torch has come to its top, or has gone out, by
        // whatever changed it.
        private void NarrateLight(LightChanged light)
        {
            if (RaidNarration.TorchFull(light.Before, light.After)) Narrate("torchlight_full");
            else if (RaidNarration.TorchOut(light.Before, light.After)) Narrate("torchlight_out");
        }

        // DD1's "half_health_half_stress": looked for whenever a hero gains stress. Here: whenever the party's
        // stress is found higher than it was (after anything the walk did to it, and after a fight).
        private void NarrateParty()
        {
            try
            {
                var party = LivingParty();
                var stress = 0f;
                foreach (var hero in party) stress += hero.Stress;
                var rose = _stressSeen >= 0f && stress > _stressSeen + 0.001f;
                _stressSeen = stress;
                if (rose && RaidNarration.PartyIsLow(party)) Narrate("half_health_half_stress");
            }
            catch (Exception e) { Plugin.Log.LogWarning("Dungeon: the party's state could not be read for the narration: " + e.Message); }
        }

        // ---- the quest begins ------------------------------------------------------------------------

        // DD1 at the dungeon's door: a hero below the quest's level starts it stressed, and one who has seen
        // the Darkest Dungeon and is made to go again starts it close to breaking.
        private void EnterAsDd1Has()
        {
            try
            {
                var party = LivingParty();
                foreach (var line in RosterUpkeep.OnEmbark(_map.Seed, QuestDifficulty, party)) Say(line);
                foreach (var line in DarkestDungeon.OnEmbark(_map.Seed, _quest, party)) Say(line);
            }
            catch (Exception e) { Plugin.Log.LogError("Dungeon: DD1's rules of setting out could not be applied: " + e); }
        }

        /// <summary>
        /// The stress the expedition itself deals a hero (hallways, hunger, traps, obstacles), with DD1's share
        /// more for one who is below the quest's level (effectiveDifficultyStressDmgModifiers). DD2 stress is
        /// whole points: the fraction of the larger amount is the chance of one more.
        /// </summary>
        private float StressFor(ActorInstance hero, float points)
        {
            var more = RosterUpkeep.StressModifier(hero.ActorGuid);
            return more > 0 ? RosterUpkeepRules.Whole(points * (1 + more), _rng) : points;
        }

        // ---- save ------------------------------------------------------------------------------------

        private JObject RulesToJson()
        {
            return new JObject { ["wanderers"] = JObject.FromObject(_wanderersMet), ["wandererAt"] = JObject.FromObject(_wandererAt) };
        }

        private void RulesFromJson(JToken token)
        {
            if (!(token is JObject json)) return;
            if (json["wanderers"] is JObject met)
                foreach (var property in met.Properties()) _wanderersMet[property.Name] = (int?)property.Value ?? 0;
            if (json["wandererAt"] is JObject at)
                foreach (var property in at.Properties())
                    if ((string)property.Value != null) _wandererAt[property.Name] = (string)property.Value;
        }

        // The fight is over: a wanderer beaten on its tile is gone from it.
        private void AfterFight(FightOutcome outcome)
        {
            if (_fightTile != null && outcome == FightOutcome.Won) _wandererAt.Remove(_fightTile);
            _fightTile = null;
        }
    }
}
