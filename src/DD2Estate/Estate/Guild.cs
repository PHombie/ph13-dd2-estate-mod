using System;
using System.Collections.Generic;
using Assets.Code.Actor;
using Assets.Code.Library;
using Assets.Code.Locale;
using Assets.Code.Skill;
using Assets.Code.Source;
using Assets.Code.Unlock;
using Assets.Code.Utils;
using DD2Estate.Dd2;
using UnityEngine;

namespace DD2Estate.Estate
{
    /// <summary>
    /// DD1's guild with DD2's skills. A DD2 hero has more skills than they start with and every skill has one
    /// better version; the mastery trainer of an inn teaches both, and so does the guild, for gold:
    ///
    /// - Learning a locked skill is DD1's "unlock a combat skill" (first purchase of a hero's skill tree). A
    ///   DD2 hero starts with five skills of eleven, as in Kingdoms; the first one learned costs DD1's price,
    ///   and every skill the hero knows beyond the five raises the next by half of it.
    /// - Mastering a skill is DD1's skill level: a hero's first mastery is level 1, the second level 2, and so
    ///   on, with DD1's price for that level, the Instructor Mastery step DD1 asks for it and the resolve level
    ///   DD1 asks of the hero (<see cref="Resolve"/>). Past DD1's last level nothing more is asked: with the
    ///   guild complete a hero of the top level's resolve may master everything, at the top price.
    ///
    /// Prices come from DD1's upgrades/heroes files (<see cref="UpgradeHeroRules"/>), scaled like every price
    /// and lowered by the guild's Training Regimen tree. The DD2 side is what the inn's trainer does
    /// (InnUpgradeSkillsBhv.ApplySkillToUnlock / ApplySkillToUpgrade) without the mastery points: the hero's
    /// SkillInstance is unlocked, or swapped for its upgraded version with ActorInstance.UnlockSkill. Both are
    /// part of the hero and are saved with them.
    /// </summary>
    [EstateModule]
    internal static class Guild
    {
        public const string BuildingId = "guild";
        public const string LevelsTree = "guild.skill_levels";
        public const string CostTree = "guild.cost";
        /// <summary>The combat skills a DD2 hero knows when they join.</summary>
        public const int StartingSkills = 5;
        /// <summary>The share of DD1's price added to the next skill for every one learned.</summary>
        public const float LearnPriceStep = 0.5f;

        public class Skill
        {
            /// <summary>The id the hero has it under now: the upgraded id once mastered.</summary>
            public string Id;
            public string Name;
            public bool Known;
            public bool Mastered;
            /// <summary>DD2 has a better version of it.</summary>
            public bool HasMastery;
            /// <summary>One of the skills the hero takes into a fight.</summary>
            public bool Equipped;
        }

        /// <summary>Raised after a hero learned or mastered something.</summary>
        public static event Action Changed;

        private static void Register()
        {
            Buildings.Register(BuildingId, GuildPanel.Open);
            UpgradeText.Describers[LevelsTree] = DescribeLevels;
        }

        private static Library<string, ActorDataSkill> SkillData => SingletonMonoBehaviour<Library<string, ActorDataSkill>>.Instance;

        // ---- a hero's skills ---------------------------------------------------------------------------

        /// <summary>
        /// What the hero could use in a fight, the skills they know first: the list the inn's trainer shows
        /// (SkillSelectionUtils, UPGRADE_COMBAT_SKILLS). A skill that exists once per form (the Abomination's)
        /// is listed once.
        /// </summary>
        public static List<Skill> SkillsOf(ActorInstance actor)
        {
            var skills = new List<Skill>();
            if (actor == null || actor.ActorDataClass == null || actor.ActorDataClass.IsHireClass) return skills;
            var twins = new HashSet<string>();
            foreach (var id in actor.GetUnlockedCharacterSheetCombatSkillIds()) Add(actor, id, true, skills, twins);
            foreach (var id in actor.GetLockedCombatSkillIds()) Add(actor, id, false, skills, twins);
            return skills;
        }

        private static void Add(ActorInstance actor, string id, bool known, List<Skill> skills, HashSet<string> twins)
        {
            var data = SkillData.GetLibraryElement(id);
            if (data == null || data.IsMoveSkill || data.IsPassSkill || twins.Contains(id)) return;
            if (data.ModeLinkedActorDataSkill != null) twins.Add(data.ModeLinkedActorDataSkill.Id);
            var instance = actor.GetCombatSkillInstance(id);
            if (instance == null) return;
            var mastered = instance.GetIsUpgraded();
            skills.Add(new Skill
            {
                Id = id,
                Name = SkillName(actor, instance),
                Known = known,
                Mastered = mastered,
                HasMastery = mastered || UpgradeOf(id) != null,
                Equipped = known && instance.GetIsEquipped()
            });
        }

        // The unlock whose requirement is this skill: its upgraded version (how the trainer finds it too).
        private static UnlockDefinition UpgradeOf(string skillId)
        {
            foreach (var unlock in LibraryUnlock.GetUnlocksFromRequirementId(skillId))
                if (SkillData.GetHasLibraryKey(unlock.m_Id)) return unlock;
            return null;
        }

        private static string SkillName(ActorInstance actor, SkillInstance instance)
        {
            var locale = Singleton<Localization>.Instance;
            var name = locale.TryGetString("skill_name_" + instance.SkillId);
            if (string.IsNullOrEmpty(name)) name = locale.TryGetString("skill_name_" + actor.GetCombatBaseSkillId(instance));
            if (string.IsNullOrEmpty(name)) return HeroNames.ClassName(instance.SkillId);
            // A mastered skill's name carries DD2's upgrade mark as a sprite tag; the guild draws its own.
            return System.Text.RegularExpressions.Regex.Replace(name, "<[^>]*>", "").Trim();
        }

        public static Sprite Icon(string skillId)
        {
            var resource = Singleton<ResourceDatabaseSkills>.Instance.GetResource(skillId, isErrorValid: false);
            return resource != null ? resource.m_SkillSprite : null;
        }

        public static int Mastered(ActorInstance actor)
        {
            var count = 0;
            foreach (var skill in SkillsOf(actor))
                if (skill.Mastered) count++;
            return count;
        }

        // ---- DD1's terms -------------------------------------------------------------------------------

        // DD1's purchases of one skill: learning it, then its levels.
        private static IReadOnlyList<UpgradeHeroRules.Purchase> Purchases(ActorInstance actor)
        {
            return UpgradeHeroRules.Skill(actor.ActorDataClass != null ? actor.ActorDataClass.Id : actor.ActorDataId);
        }

        // The DD1 skill level a hero's next mastery stands for; the last one again once DD1 runs out of levels.
        private static UpgradeHeroRules.Purchase MasteryPurchase(ActorInstance actor, int alreadyMastered)
        {
            var purchases = Purchases(actor);
            return purchases.Count < 2 ? null : purchases[Math.Min(alreadyMastered + 1, purchases.Count - 1)];
        }

        /// <summary>DD1's terms for learning a skill (the first purchase of a skill's tree); null when DD1's rules cannot be read.</summary>
        public static UpgradeHeroRules.Purchase LearnTerms(ActorInstance actor)
        {
            var purchases = Purchases(actor);
            return purchases.Count > 0 ? purchases[0] : null;
        }

        /// <summary>DD1's terms for the hero's next mastery (the skill level it stands for); null when DD1 has none.</summary>
        public static UpgradeHeroRules.Purchase MasteryTerms(ActorInstance actor) => MasteryPurchase(actor, Mastered(actor));

        /// <summary>Skills the hero knows beyond the five they came with (a veteran's extra ones count).</summary>
        public static int Learned(ActorInstance actor)
        {
            var known = 0;
            foreach (var skill in SkillsOf(actor))
                if (skill.Known) known++;
            return Math.Max(0, known - StartingSkills);
        }

        /// <summary>Estate gold for the hero's next skill; 0 when DD1's rules cannot be read.</summary>
        public static int LearnPrice(ActorInstance actor)
        {
            return LearnPrice(actor, Learned(actor));
        }

        /// <summary>Estate gold for a skill when the hero has learned so many already: DD1's price, half as much again each time.</summary>
        public static int LearnPrice(ActorInstance actor, int learned)
        {
            var purchases = Purchases(actor);
            if (purchases.Count == 0) return 0;
            return UpgradeRules.Price(Mathf.RoundToInt(purchases[0].Gold * (1f + LearnPriceStep * Math.Max(0, learned))), CostTree);
        }

        /// <summary>Estate gold for the hero's next mastery.</summary>
        public static int MasterPrice(ActorInstance actor)
        {
            var purchase = MasteryPurchase(actor, Mastered(actor));
            return purchase != null ? UpgradeRules.Price(purchase.Gold, CostTree) : 0;
        }

        /// <summary>
        /// How many skills this hero may have mastered with the guild and their resolve as they stand;
        /// int.MaxValue once every level DD1 knows is open to them.
        /// </summary>
        public static int MasteryLimit(ActorInstance actor)
        {
            return MasteryLimit(Purchases(actor), need => UpgradeRules.Has(need), Resolve.Level(actor.ActorGuid));
        }

        private static int MasteryLimit(IReadOnlyList<UpgradeHeroRules.Purchase> purchases, Func<UpgradeRules.Need, bool> built, int resolve)
        {
            var open = 0;
            for (var i = 1; i < purchases.Count; i++)
            {
                if (purchases[i].ResolveLevel > resolve) return open;
                foreach (var need in purchases[i].Needs)
                    if (!built(need)) return open;
                open++;
            }
            return purchases.Count < 2 ? 0 : int.MaxValue;
        }

        // The Instructor Mastery line of the upgrade screen: what the tree's step allows, by the same rule,
        // and the resolve DD1 asks of the hero for it.
        private static string DescribeLevels(UpgradeRules.Tree tree, int level)
        {
            var purchases = UpgradeHeroRules.Skill(null);
            var limit = MasteryLimit(purchases, need => need.Tree == tree.Id ? tree.IndexOf(need.Code) < level : UpgradeRules.Has(need), int.MaxValue);
            if (limit <= 0) return null;
            if (limit == int.MaxValue)
                return "Heroes of resolve level " + purchases[purchases.Count - 1].ResolveLevel + " may master every skill they know";
            return "A hero may master " + limit + (limit == 1 ? " skill" : " skills") + " (at resolve level " + purchases[limit].ResolveLevel + ")";
        }

        // ---- training ----------------------------------------------------------------------------------

        private static string SessionBlock()
        {
            return EstateSession.InHub && EstateSession.View == EstateSession.Screen.Hamlet ? null : "Not while away from the hamlet";
        }

        private static ActorInstance Hero(uint guid)
        {
            var actor = SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance.GetLibraryElement(guid);
            return actor != null && actor.IsLiving ? actor : null;
        }

        /// <summary>Why the hero cannot learn this skill now; null when they can.</summary>
        public static string LearnBlock(ActorInstance actor, string skillId)
        {
            var instance = actor.GetCombatSkillInstance(skillId);
            if (instance == null) return "Not one of their skills";
            if (instance.GetIsUnlocked()) return "Already known";
            var price = LearnPrice(actor);
            if (price <= 0) return "The guild's terms could not be read from DD1";
            var missing = HeroActionUi.Missing(Purchases(actor)[0], actor.ActorGuid);
            if (missing != null) return missing;
            return price > EstateState.Gold ? "Needs " + UpgradeUi.Amount(price) + " gold" : SessionBlock();
        }

        /// <summary>Why the hero cannot master this skill now; null when they can.</summary>
        public static string MasterBlock(ActorInstance actor, string skillId)
        {
            var instance = actor.GetCombatSkillInstance(skillId);
            if (instance == null) return "Not one of their skills";
            if (instance.GetIsUpgraded()) return "Already mastered";
            if (!instance.GetIsUnlocked()) return "Has to be learned first";
            if (UpgradeOf(skillId) == null) return "There is nothing more to this skill";
            var purchase = MasteryPurchase(actor, Mastered(actor));
            if (purchase == null) return "The guild's terms could not be read from DD1";
            var wants = MasteryWants(actor);
            if (wants != null) return wants;
            var price = UpgradeRules.Price(purchase.Gold, CostTree);
            return price > EstateState.Gold ? "Needs " + UpgradeUi.Amount(price) + " gold" : SessionBlock();
        }

        /// <summary>
        /// What stands between the hero and their next mastery, the gold aside, in DD1's words: the guild's own
        /// level and the hero's resolve (DD1 asks both). Null when only the price is left.
        /// </summary>
        public static string MasteryWants(ActorInstance actor)
        {
            return HeroActionUi.Missing(MasteryPurchase(actor, Mastered(actor)), actor.ActorGuid);
        }

        /// <summary>
        /// Takes a skill the hero knows into their fights, or out of them: DD1's Guild is also where a hero's
        /// skills are picked. The game's own switch (ActorInstance.SetCombatSkillEquipped, as its character
        /// sheet uses it), which keeps to the class's number of skills in use. Returns null, or why not.
        /// </summary>
        public static string ToggleEquipped(uint guid, string skillId)
        {
            var actor = Hero(guid);
            if (actor == null) return "No such hero";
            var instance = actor.GetCombatSkillInstance(skillId);
            if (instance == null) return "Not one of their skills";
            if (!instance.GetIsUnlocked()) return "Has to be learned first";
            var session = SessionBlock();
            if (session != null) return session;
            var was = actor.GetCombatSkillEquipped(skillId);
            actor.SetCombatSkillEquipped(skillId, !was, refreshStats: true);
            if (actor.GetCombatSkillEquipped(skillId) == was) return was ? "This skill cannot be put aside" : "No room for another skill: put one aside first";
            // Which skills are in use is the hero's own, saved with them when the estate is next written.
            RaiseChanged();
            return null;
        }

        /// <summary>The hero pays and learns a locked skill. Returns null, or why not.</summary>
        public static string Learn(uint guid, string skillId)
        {
            var actor = Hero(guid);
            if (actor == null) return "No such hero";
            var reason = LearnBlock(actor, skillId);
            if (reason != null) return reason;
            var price = LearnPrice(actor);
            Unlock(actor, skillId);
            EstateState.AddGold(-price);
            EstateAudio.Ui("town/trainer_purchase_skill");
            Plugin.Log.LogInfo("Guild: " + actor.ActorName + " learns " + skillId + " for " + price + " gold");
            Done("guild learn");
            return null;
        }

        /// <summary>The hero pays and masters a skill they know. Returns null, or why not.</summary>
        public static string Master(uint guid, string skillId)
        {
            var actor = Hero(guid);
            if (actor == null) return "No such hero";
            var reason = MasterBlock(actor, skillId);
            if (reason != null) return reason;
            var price = MasterPrice(actor);
            if (!Upgrade(actor, skillId)) return "The game has no better version of this skill";
            EstateState.AddGold(-price);
            EstateAudio.Ui("town/trainer_purchase_skill");
            Plugin.Log.LogInfo("Guild: " + actor.ActorName + " masters " + skillId + " for " + price + " gold");
            Done("guild master");
            return null;
        }

        /// <summary>
        /// Training a hero arrives with (a seasoned recruit): so many more skills known and so many mastered,
        /// picked by the dice, free and whatever the guild's level. Returns what was done, for the log.
        /// </summary>
        public static string Grant(ActorInstance actor, int learn, int master, RosterDice dice)
        {
            var learned = 0;
            var mastered = 0;
            for (; learned < learn; learned++)
            {
                var locked = SkillsOf(actor).FindAll(s => !s.Known);
                if (locked.Count == 0) break;
                Unlock(actor, locked[dice.Below(locked.Count)].Id);
            }
            for (; mastered < master; mastered++)
            {
                var open = SkillsOf(actor).FindAll(s => s.Known && !s.Mastered && s.HasMastery);
                if (open.Count == 0 || !Upgrade(actor, open[dice.Below(open.Count)].Id)) break;
            }
            if (learned + mastered > 0) RaiseChanged();
            return learned + " learned, " + mastered + " mastered";
        }

        // InnUpgradeSkillsBhv.ApplySkillToUnlock: the skill, the skill a path put in its place, its other form.
        private static void Unlock(ActorInstance actor, string skillId)
        {
            UnlockOne(actor, skillId);
            var twin = SkillData.GetLibraryElement(skillId)?.ModeLinkedActorDataSkill;
            if (twin != null) UnlockOne(actor, twin.Id);
        }

        private static void UnlockOne(ActorInstance actor, string skillId)
        {
            var instance = actor.GetCombatSkillInstance(skillId);
            if (instance == null || instance.GetIsUnlocked()) return;
            instance.SetIsUnlocked();
            actor.GetCombatReplacedSkill(skillId)?.SetIsUnlocked();
        }

        // InnUpgradeSkillsBhv.ApplySkillToUpgrade: the upgraded skill's unlock goes to the hero, who swaps the
        // skill for it (equipped state kept); the other form's twin follows.
        private static bool Upgrade(ActorInstance actor, string skillId)
        {
            var unlock = UpgradeOf(skillId);
            if (unlock == null) return false;
            actor.UnlockSkill(unlock, SourceType.INN);
            var twin = SkillData.GetLibraryElement(unlock.m_Id)?.ModeLinkedActorDataSkill;
            var twinUnlock = twin != null ? SkillUtils.GetUnlockFromSkillId(twin.Id) : null;
            if (twinUnlock != null && twinUnlock.RequirementIds.Count > 0) actor.UnlockSkill(twinUnlock, SourceType.INN);
            return true;
        }

        private static void Done(string reason)
        {
            RaiseChanged();
            EstatePersistence.SaveNow(reason);
        }

        private static void RaiseChanged()
        {
            try { Changed?.Invoke(); }
            catch (Exception e) { Plugin.Log.LogError("Guild: a Changed handler failed: " + e); }
        }

        public static object Describe(uint guid)
        {
            var actor = Hero(guid);
            if (actor == null) return "no such hero";
            var limit = MasteryLimit(actor);
            var skills = new List<object>();
            foreach (var skill in SkillsOf(actor))
            {
                skills.Add(new
                {
                    id = skill.Id,
                    name = skill.Name,
                    known = skill.Known,
                    mastered = skill.Mastered,
                    equipped = skill.Equipped,
                    blocked = skill.Mastered ? null : skill.Known ? MasterBlock(actor, skill.Id) : LearnBlock(actor, skill.Id)
                });
            }
            return new
            {
                hero = actor.ActorName,
                cls = actor.ActorDataId,
                gold = EstateState.Gold,
                resolve = Resolve.Level(guid),
                mastered = Mastered(actor),
                learned = Learned(actor),
                path = HeroPaths.Of(actor),
                masteryLimit = limit == int.MaxValue ? "none" : limit.ToString(),
                learnPrice = LearnPrice(actor),
                masterPrice = MasterPrice(actor),
                discount = UpgradeRules.Discount(CostTree),
                skills
            };
        }
    }
}
