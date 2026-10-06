using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Assets.Code.Actor;
using Assets.Code.Library;
using Assets.Code.Skill;
using Assets.Code.Source;
using Assets.Code.Utils;
using DD2Estate.Core;
using DD2Estate.Dd2;
using HarmonyLib;
using Newtonsoft.Json.Linq;

namespace DD2Estate.Estate
{
    /// <summary>
    /// DD1's blacksmith for DD2 heroes. Every hero has a weapon level and an armour level, 1 to 5, bought one
    /// level at a time for gold. Everything about the purchase is DD1's (upgrades/heroes,
    /// <see cref="UpgradeHeroRules"/>): the price of each level, the Weaponsmithing / Armorsmithing step it
    /// needs, the resolve level the hero must have, and the Furnace tree's discount.
    ///
    /// What a level does is the owner's rule (<see cref="GearRules"/>), the same for every class: a weapon
    /// level over the first is a tenth more damage dealt and one in a hundred more crit chance, an armour level
    /// over the first a tenth more maximum health. (DD1 writes its own growth class by class, with speed and
    /// dodge in it; none of that is read any more.)
    ///
    /// DD2 keeps a hero's stats as the sum of ActorDataStats containers hanging off ActorInstance.ActorData
    /// (class, path, trinkets, quirks ...). The gear is one more such container, built from the same text DD2's
    /// tables use and added to the hero. The game does not save that tree, so the levels live in the estate's
    /// save section and the container is put back on every hero when a save is loaded.
    /// </summary>
    [EstateModule]
    internal static class Blacksmith
    {
        public const string BuildingId = "blacksmith";
        public const string CostTree = "blacksmith.cost";
        private const string SectionKey = "blacksmith";
        private const string StatsId = "estate_gear";

        public enum Gear { Weapon, Armour }

        /// <summary>Raised after a hero's gear changed.</summary>
        public static event Action Changed;

        private class Levels
        {
            public int Weapon = 1, Armour = 1;
            /// <summary>Health when the estate was saved: the game clamps it to the ungeared maximum on load.</summary>
            public float SavedHealth = -1f;
        }

        private class Attached
        {
            public ActorInstance Actor;
            public ActorDataStats Stats;
        }

        private static readonly Dictionary<uint, Levels> ByHero = new Dictionary<uint, Levels>();
        private static readonly Dictionary<uint, Attached> OnHero = new Dictionary<uint, Attached>();
        private static readonly FieldInfo HealthField = AccessTools.Field(typeof(ActorInstance), "m_Hp");

        private static void Register()
        {
            EstateState.RegisterSection(SectionKey, Save, Load, Reset);
            Buildings.Register(BuildingId, BlacksmithPanel.Open);
            UpgradeText.Describers[Tree(Gear.Weapon)] = DescribeForge;
            UpgradeText.Describers[Tree(Gear.Armour)] = DescribeForge;
        }

        public static string Tree(Gear gear) => gear == Gear.Weapon ? "blacksmith.weapon" : "blacksmith.armour";

        /// <summary>The kind of piece, as DD1 writes it ("Allows armor upgrades to rank %d").</summary>
        public static string Name(Gear gear) => gear == Gear.Weapon ? "Weapon" : "Armor";

        /// <summary>
        /// DD1's own name of the class's piece at a level ("Battered Longsword", "Longsword", "Questing Sword",
        /// "Greatsword", "The Long Crusade": &lt;class&gt;_weapon_&lt;0..4&gt;, &lt;class&gt;_armour_&lt;0..4&gt; of the heroes'
        /// string table); the kind of piece and its level for a class DD1 never had.
        /// </summary>
        public static string PieceName(ActorInstance actor, Gear gear, int level)
        {
            level = Math.Max(1, level);
            var name = actor != null ? HeroActionUi.Words(HeroActionUi.ClassOf(actor) + "_" + Tag(gear) + "_" + (level - 1).ToString(CultureInfo.InvariantCulture)) : null;
            return !string.IsNullOrEmpty(name) ? name : Name(gear) + " " + UpgradeText.Roman(level);
        }

        /// <summary>
        /// DD1's picture of the class's piece at a level: the tall card heroes/&lt;class&gt;/icons_equip/eqp_weapon_&lt;0..4&gt;.png
        /// (eqp_armour_...); null for a class DD1 never had.
        /// </summary>
        public static string Card(ActorInstance actor, Gear gear, int level)
        {
            return HeroActionUi.ClassFile(actor, "icons_equip/eqp_" + Tag(gear) + "_" + (Math.Max(1, level) - 1).ToString(CultureInfo.InvariantCulture) + ".png");
        }

        // ---- what a level gives ------------------------------------------------------------------------

        /// <summary>What a hero's weapon and armour give at their present levels (<see cref="GearRules"/>).</summary>
        public struct Bonus
        {
            /// <summary>Percent more damage dealt, percent more crit chance (the weapon); percent more maximum health (the armour).</summary>
            public int DamagePercent, CritPercent, HealthPercent;

            public bool Any => DamagePercent != 0 || CritPercent != 0 || HealthPercent != 0;
        }

        /// <summary>The three bonuses of a hero's gear, in whole percents; all nought for a hero with the gear they came with.</summary>
        public static Bonus BonusOf(uint guid)
        {
            int weapon = Level(guid, Gear.Weapon), armour = Level(guid, Gear.Armour);
            return new Bonus { DamagePercent = GearRules.DamagePercent(weapon), CritPercent = GearRules.CritPercent(weapon), HealthPercent = GearRules.HealthPercent(armour) };
        }

        /// <summary>The share more damage the hero deals for their weapon's level (0.4 at level 5).</summary>
        public static float DamageBonus(uint guid) => BonusOf(guid).DamagePercent / 100f;

        /// <summary>The crit chance the hero has more for their weapon's level, as a share (0.04 at level 5).</summary>
        public static float CritBonus(uint guid) => BonusOf(guid).CritPercent / 100f;

        /// <summary>The share more maximum health the hero has for their armour's level (0.4 at level 5).</summary>
        public static float HealthBonus(uint guid) => BonusOf(guid).HealthPercent / 100f;

        /// <summary>What a level of a piece gives over the gear a hero starts with, in the mod's own words.</summary>
        public static string Effect(Gear gear, int level)
        {
            return (gear == Gear.Weapon ? GearRules.WeaponWords(level) : GearRules.ArmourWords(level)) ?? "As issued";
        }

        // The gear as DD2 writes stats in its own tables (StatDataContainer.SetFromCsv): add_stat / multiply_stat.
        // Damage dealt and crit chance are added to (DD2 counts damage dealt from 1, so 0.4 is +40%); maximum
        // health is multiplied (DD2: the sum of the class's and every other container's, times 1 + the shares).
        private static string StatsText(int weapon, int armour)
        {
            var lines = GearRules.StatLines(weapon, armour, ActorStatType.HEALTH_DAMAGE_DEALT_PERCENT.GetName(), ActorStatType.CRIT_CHANCE.GetName(), ActorStatType.HEALTH_MAX.GetName());
            return lines.Count > 0 ? string.Join("\n", lines) + "\n" : "";
        }

        // ---- levels and DD1's terms --------------------------------------------------------------------

        private static string ClassOf(ActorInstance actor) => actor.ActorDataClass != null ? actor.ActorDataClass.Id : actor.ActorDataId;

        // DD1's purchases of a piece: the first buys level 2.
        private static IReadOnlyList<UpgradeHeroRules.Purchase> Purchases(ActorInstance actor, Gear gear)
        {
            return gear == Gear.Weapon ? UpgradeHeroRules.Weapon(ClassOf(actor)) : UpgradeHeroRules.Armour(ClassOf(actor));
        }

        public static int Level(uint guid, Gear gear)
        {
            if (!ByHero.TryGetValue(guid, out var levels)) return 1;
            return gear == Gear.Weapon ? levels.Weapon : levels.Armour;
        }

        public static int MaxLevel(ActorInstance actor, Gear gear) => Purchases(actor, gear).Count + 1;

        /// <summary>DD1's terms for the hero's next level of a piece; null when there is none.</summary>
        public static UpgradeHeroRules.Purchase Next(ActorInstance actor, Gear gear)
        {
            var purchases = Purchases(actor, gear);
            var index = Level(actor.ActorGuid, gear) - 1;
            return index >= 0 && index < purchases.Count ? purchases[index] : null;
        }

        /// <summary>DD1's terms for a given level of a piece (2 and up); null when DD1 has no such level.</summary>
        public static UpgradeHeroRules.Purchase Terms(ActorInstance actor, Gear gear, int level)
        {
            var purchases = Purchases(actor, gear);
            return level >= 2 && level - 2 < purchases.Count ? purchases[level - 2] : null;
        }

        public static int Price(UpgradeHeroRules.Purchase purchase) => UpgradeRules.Price(purchase.Gold, CostTree);

        /// <summary>DD1's upgrade tag of a piece, as its town events name it.</summary>
        public static string Tag(Gear gear) => gear == Gear.Weapon ? "weapon" : "armour";

        /// <summary>What the hero pays for a piece's next level this week: nothing while a town event's gift lasts.</summary>
        public static int Price(UpgradeHeroRules.Purchase purchase, Gear gear) => TownEventHooks.FreeUpgrades(Tag(gear)) > 0 ? 0 : Price(purchase);

        public static List<UpgradeRules.Need> Missing(UpgradeHeroRules.Purchase purchase)
        {
            var missing = new List<UpgradeRules.Need>();
            foreach (var need in purchase.Needs)
                if (!UpgradeRules.Has(need)) missing.Add(need);
            return missing;
        }

        /// <summary>
        /// What stands between the hero and a level of a piece, the gold aside, in DD1's words: the smithy's own
        /// level and the hero's resolve. Null when only the price is left.
        /// </summary>
        public static string Wants(ActorInstance actor, UpgradeHeroRules.Purchase purchase)
        {
            return HeroActionUi.Missing(purchase, actor.ActorGuid);
        }

        /// <summary>Why the hero's next level of a piece cannot be bought now; null when it can.</summary>
        public static string Block(ActorInstance actor, Gear gear)
        {
            var next = Next(actor, gear);
            if (next == null) return Purchases(actor, gear).Count == 0 ? "The smith's terms could not be read from DD1" : "Nothing better can be made";
            var wants = Wants(actor, next);
            if (wants != null) return wants;
            var price = Price(next, gear);
            if (price > EstateState.Gold) return "Needs " + UpgradeUi.Amount(price) + " gold";
            return EstateSession.InHub && EstateSession.View == EstateSession.Screen.Hamlet ? null : "Not while away from the hamlet";
        }

        // ---- fitting -----------------------------------------------------------------------------------

        private static ActorInstance Hero(uint guid)
        {
            var actor = SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance.GetLibraryElement(guid);
            return actor != null && actor.IsLiving ? actor : null;
        }

        /// <summary>The hero pays for the next level of a piece and has it at once. Returns null, or why not.</summary>
        public static string Fit(uint guid, Gear gear)
        {
            var actor = Hero(guid);
            if (actor == null) return "No such hero";
            var reason = Block(actor, gear);
            if (reason != null) return reason;
            var free = TownEventHooks.FreeUpgrades(Tag(gear)) > 0;
            var price = free ? 0 : Price(Next(actor, gear));
            Set(actor, gear, Level(guid, gear) + 1);
            if (free) TownEventHooks.UseFreeUpgrade(Tag(gear));
            else EstateState.AddGold(-price);
            EstateAudio.Ui(gear == Gear.Weapon ? "town/blacksmith_purchase_weapon" : "town/blacksmith_purchase_armor");
            Plugin.Log.LogInfo("Blacksmith: " + actor.ActorName + " gets " + Name(gear).ToLowerInvariant() + " level " + Level(guid, gear) + " for " + price + " gold");
            RaiseChanged();
            EstatePersistence.SaveNow("blacksmith");
            return null;
        }

        /// <summary>Gear a hero arrives with (a seasoned recruit): at least these levels, free, whatever the smithy's.</summary>
        public static void Grant(ActorInstance actor, int weapon, int armour)
        {
            var guid = actor.ActorGuid;
            var before = Level(guid, Gear.Weapon) + Level(guid, Gear.Armour);
            Set(actor, Gear.Weapon, Math.Max(Level(guid, Gear.Weapon), Math.Min(weapon, MaxLevel(actor, Gear.Weapon))));
            Set(actor, Gear.Armour, Math.Max(Level(guid, Gear.Armour), Math.Min(armour, MaxLevel(actor, Gear.Armour))));
            if (Level(guid, Gear.Weapon) + Level(guid, Gear.Armour) != before) RaiseChanged();
        }

        /// <summary>For tests: any level, no price, no conditions.</summary>
        public static string SetForTest(uint guid, Gear gear, int level)
        {
            var actor = Hero(guid);
            if (actor == null) return "No such hero";
            Set(actor, gear, Math.Max(1, Math.Min(level, MaxLevel(actor, gear))));
            RaiseChanged();
            return Name(gear) + " level " + Level(guid, gear);
        }

        private static void Set(ActorInstance actor, Gear gear, int level)
        {
            if (!ByHero.TryGetValue(actor.ActorGuid, out var levels)) ByHero[actor.ActorGuid] = levels = new Levels();
            if (gear == Gear.Weapon) levels.Weapon = level;
            else levels.Armour = level;
            // The game's own way to take a changed maximum: health keeps its share of it (ActorInstance.RefreshStats).
            Attach(actor);
            actor.RefreshStats();
            actor.UpdatePreviousHpMax();
        }

        // Swaps the hero's gear container for one that matches their levels. The source is CLASS: the character
        // sheet then lists the gain as a hero upgrade on top of the class's base, and the damage calculation
        // (SkillCalculation, GetAddStatValuesBySource) needs every stats container to have a source.
        private static void Attach(ActorInstance actor)
        {
            var guid = actor.ActorGuid;
            if (OnHero.TryGetValue(guid, out var old))
            {
                if (ReferenceEquals(old.Actor, actor)) actor.ActorData.RemoveChild(old.Stats);
                OnHero.Remove(guid);
            }
            var text = StatsText(Level(guid, Gear.Weapon), Level(guid, Gear.Armour));
            if (text.Length == 0) return;
            var stats = new ActorDataStats(StatsId, text);
            stats.Init(StatsId + "_" + guid);
            stats.SetSource(SourceType.CLASS, StatsId);
            actor.ActorData.AddChild(stats);
            OnHero[guid] = new Attached { Actor = actor, Stats = stats };
        }

        private static void RaiseChanged()
        {
            try { Changed?.Invoke(); }
            catch (Exception e) { Plugin.Log.LogError("Blacksmith: a Changed handler failed: " + e); }
        }

        // ---- save section ------------------------------------------------------------------------------

        private static JToken Save()
        {
            var json = new JObject();
            var actors = SingletonMonoBehaviour<Library<uint, ActorInstance>>.HasInstance() ? SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance : null;
            foreach (var pair in ByHero)
            {
                if (pair.Value.Weapon <= 1 && pair.Value.Armour <= 1) continue;
                var actor = actors != null ? actors.GetLibraryElement(pair.Key) : null;
                if (actor == null || !actor.IsLiving) continue;     // the fallen keep nothing
                json[pair.Key.ToString(CultureInfo.InvariantCulture)] = new JObject { ["w"] = pair.Value.Weapon, ["a"] = pair.Value.Armour, ["hp"] = actor.HpRaw };
            }
            return json;
        }

        // Runs when the game has already loaded the heroes (EstatePersistence.Continue: StartKingdom first, the
        // estate's section after it). By then each hero's health has been clamped to their maximum without
        // gear (ActorInstance.HandleEventGameTypeStarted, UnpauseRefreshStats), so the health they were saved
        // with is put back once the gear is on.
        private static void Load(JToken token)
        {
            ByHero.Clear();
            OnHero.Clear();
            if (!(token is JObject saved)) return;
            foreach (var property in saved.Properties())
            {
                if (!uint.TryParse(property.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var guid) || !(property.Value is JObject entry)) continue;
                ByHero[guid] = new Levels
                {
                    Weapon = Math.Max(1, (int?)entry["w"] ?? 1),
                    Armour = Math.Max(1, (int?)entry["a"] ?? 1),
                    SavedHealth = (float?)entry["hp"] ?? -1f
                };
            }
            if (!SingletonMonoBehaviour<Library<uint, ActorInstance>>.HasInstance()) return;
            foreach (var pair in ByHero)
            {
                try
                {
                    var actor = SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance.GetLibraryElement(pair.Key);
                    if (actor == null) continue;
                    Attach(actor);
                    if (pair.Value.SavedHealth > actor.HpRaw && HealthField != null)
                        HealthField.SetValue(actor, Math.Min(pair.Value.SavedHealth, actor.CurrentHpMax));
                    actor.UpdatePreviousHpMax();
                }
                catch (Exception e) { Plugin.Log.LogError("Blacksmith: gear of hero " + pair.Key + " could not be restored: " + e); }
            }
        }

        private static void Reset()
        {
            ByHero.Clear();
            OnHero.Clear();
        }

        // ---- text --------------------------------------------------------------------------------------

        // The Weaponsmithing / Armorsmithing lines of the upgrade screen: the level DD1 ties to the step.
        private static string DescribeForge(UpgradeRules.Tree tree, int level)
        {
            var gear = tree.Id == Tree(Gear.Weapon) ? Gear.Weapon : Gear.Armour;
            var code = tree.Steps[level - 1].Code;
            var purchases = gear == Gear.Weapon ? UpgradeHeroRules.Weapon(null) : UpgradeHeroRules.Armour(null);
            for (var i = 0; i < purchases.Count; i++)
            {
                foreach (var need in purchases[i].Needs)
                {
                    if (need.Tree != tree.Id || need.Code != code) continue;
                    // The first purchase makes level 2.
                    return Name(gear) + " level " + UpgradeText.Roman(i + 2) + " can be made for heroes of resolve level " + purchases[i].ResolveLevel
                           + ": " + Effect(gear, i + 2);
                }
            }
            return null;
        }

        // The hero's damaging skills as a fight counts a blow of each (SkillCalculation.CalculateResultDamage,
        // CalculateResutCrit): the skill's own range times the hero's damage dealt, the skill's own crit plus the
        // hero's. Before the target's side of it (its damage received, tokens).
        private static List<object> Blows(ActorInstance actor)
        {
            var blows = new List<object>();
            try
            {
                var skills = SingletonMonoBehaviour<Library<string, ActorDataSkill>>.Instance;
                var dealt = Math.Max(0f, actor.GetClampedStatValue(ActorStatType.HEALTH_DAMAGE_DEALT_PERCENT));
                var crit = actor.GetClampedStatValue(ActorStatType.CRIT_CHANCE);
                foreach (var id in actor.GetEquippedCharacterSheetCombatSkillIds())
                {
                    var skill = skills.GetLibraryElement(id);
                    var stats = skill != null && skill.GetIsDamaging() ? skill.GetDataContainerSum<ActorDataStats>() : null;
                    if (stats == null) continue;
                    var from = stats.StatContainer.GetStatTotal(ActorStatType.HEALTH_DAMAGE);
                    var to = from + stats.StatContainer.GetStatTotal(ActorStatType.HEALTH_DAMAGE_RANGE);
                    var own = stats.StatContainer.GetStatTotal(ActorStatType.CRIT_CHANCE);
                    blows.Add(new
                    {
                        skill = id, written = (int)from + "-" + (int)to,
                        dealt = (from * dealt).ToString("0.##", CultureInfo.InvariantCulture) + "-" + (to * dealt).ToString("0.##", CultureInfo.InvariantCulture),
                        critWritten = Math.Round(own * 100f, 2), crit = Math.Round((own + crit) * 100f, 2)
                    });
                }
            }
            catch (Exception e) { blows.Add("the skills could not be read: " + e.Message); }
            return blows;
        }

        public static object Describe(uint guid)
        {
            var actor = Hero(guid);
            if (actor == null) return "no such hero";
            var pieces = new List<object>();
            foreach (var gear in new[] { Gear.Weapon, Gear.Armour })
            {
                var next = Next(actor, gear);
                pieces.Add(new
                {
                    piece = Name(gear),
                    name = PieceName(actor, gear, Level(guid, gear)),
                    art = Card(actor, gear, Level(guid, gear)),
                    level = Level(guid, gear),
                    of = MaxLevel(actor, gear),
                    effect = Effect(gear, Level(guid, gear)),
                    nextPrice = next != null ? Price(next) : 0,
                    nextEffect = next != null ? Effect(gear, Level(guid, gear) + 1) : null,
                    blocked = Block(actor, gear)
                });
            }
            return new
            {
                hero = actor.ActorName,
                cls = actor.ActorDataId,
                gold = EstateState.Gold,
                resolve = Resolve.Level(guid),
                discount = UpgradeRules.Discount(CostTree),
                health = actor.HpRaw + " / " + actor.CurrentHpMax,
                // the rule's three bonuses of the hero (whole percents), then what DD2's own stats come to with them
                bonus = new { damagePercent = BonusOf(guid).DamagePercent, critPercent = BonusOf(guid).CritPercent, healthPercent = BonusOf(guid).HealthPercent },
                healthMax = actor.CurrentHpMax,
                damageDealt = actor.GetClampedStatValue(ActorStatType.HEALTH_DAMAGE_DEALT_PERCENT),
                damageTaken = actor.GetClampedStatValue(ActorStatType.HEALTH_DAMAGE_RECEIVED_PERCENT),
                crit = actor.GetClampedStatValue(ActorStatType.CRIT_CHANCE),
                speed = actor.GetClampedStatValue(ActorStatType.SPEED),
                stats = StatsText(Level(guid, Gear.Weapon), Level(guid, Gear.Armour)).Trim().Replace("\n", " | "),
                // where the game is (a fight: COMBAT) and what a blow of each damaging skill comes to there
                mode = Assets.Code.Game.GameModeMgr.CurrentMode?.GetName(),
                blows = Blows(actor),
                pieces
            };
        }
    }
}
