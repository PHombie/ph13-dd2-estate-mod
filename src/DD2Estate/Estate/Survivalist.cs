using System;
using System.Collections.Generic;
using System.Globalization;
using Assets.Code.Actor;
using Assets.Code.Library;
using Assets.Code.Utils;
using DD2Estate.Core;
using DD2Estate.Dd2;
using DD2Estate.Dungeon;
using Newtonsoft.Json.Linq;

namespace DD2Estate.Estate
{
    /// <summary>
    /// DD1's Survivalist for DD2 heroes. Every hero has a book of camping skills (<see cref="CampSkillBook"/>):
    /// a recruit arrives with DD1's roll of them (two of the class's own, one shared), the Survivalist teaches
    /// the others for gold at DD1's price (the skill's own upgrade_requirements in
    /// raid/camping/*.camping_skills.json, 1750 gold each in the stock game) less the Bonfire tree's discount
    /// (camping_trainer.cost), and only so many are ready for an expedition at a time, the hero's choice.
    ///
    /// DD2 has no camping skills, so nothing of this is on the ActorInstance: the books live in the estate's
    /// save section, by hero guid. Which DD1 class a DD2 hero camps as is the class id both games share; the
    /// two DD2 heroes DD1 only has through its last DLC fall back on an analogue without it
    /// (<see cref="CampingRules.Analogues"/>).
    /// </summary>
    [EstateModule]
    internal static class Survivalist
    {
        public const string BuildingId = "camping_trainer";
        public const string CostTree = "camping_trainer.cost";
        private const string SectionKey = "survivalist";

        private static readonly Dictionary<uint, CampSkillBook> Books = new Dictionary<uint, CampSkillBook>();

        /// <summary>Raised after a hero learned a skill or changed which are ready.</summary>
        public static event Action Changed;

        private static void Register()
        {
            EstateState.RegisterSection(SectionKey, Save, Load, Reset);
            Claim();
        }

        // UpgradeWindow registers a stand-in screen for this building at start-up (its upgrades alone, from
        // before the Survivalist had a screen); of the two registrations the later one wins, and which that is
        // depends on the order the modules are found in. So the screen is claimed again whenever an estate is
        // loaded or begun, which is always after start-up.
        private static void Claim() => Buildings.Register(BuildingId, SurvivalistPanel.Open);

        private static CampingRules Rules => CampContent.Rules;

        // ---- a hero's book ---------------------------------------------------------------------------

        /// <summary>
        /// The DD1 class a DD2 hero camps as: the hero's class id, which the two games share for every hero
        /// both have. (ActorDataId can name a form of the class; the class definition's id is tried first.)
        /// </summary>
        public static string ClassOf(ActorInstance actor)
        {
            if (actor == null) return "";
            var byClass = actor.ActorDataClass != null ? actor.ActorDataClass.Id : null;
            if (!string.IsNullOrEmpty(byClass) && (Rules.ClassSkills(byClass).Count > 0 || Rules.ClassSkills(actor.ActorDataId).Count == 0)) return byClass;
            return actor.ActorDataId ?? "";
        }

        /// <summary>
        /// The hero's camping skills. A hero without a book gets one on first asking: DD1's roll for a recruit,
        /// from a seed of the hero's own so that an unsaved book comes out the same again.
        /// </summary>
        public static CampSkillBook Book(ActorInstance actor)
        {
            if (actor == null) return new CampSkillBook();
            var guid = actor.ActorGuid;
            var cls = ClassOf(actor);
            if (Books.TryGetValue(guid, out var book) && book.ClassId == cls) return book;
            // no book, or the guid is another hero's now
            book = CampSkillBook.Roll(Rules, cls, new Rng(Seed(guid, cls)));
            Books[guid] = book;
            Plugin.Log.LogInfo("Survivalist: " + actor.ActorName + " (" + cls + ") arrives knowing " + string.Join(", ", book.Known));
            return book;
        }

        public static CampSkillBook Book(uint guid) => Book(Hero(guid));

        private static long Seed(uint guid, string cls)
        {
            // FNV-1a: the same on every runtime, which string.GetHashCode is not
            unchecked
            {
                var hash = 1469598103934665603UL;
                foreach (var c in cls) hash = (hash ^ c) * 1099511628211UL;
                return (long)(hash ^ ((ulong)guid * 0x9E3779B97F4A7C15UL));
            }
        }

        private static ActorInstance Hero(uint guid)
        {
            var actor = SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance.GetLibraryElement(guid);
            return actor != null && actor.IsLiving ? actor : null;
        }

        // ---- DD1's terms -----------------------------------------------------------------------------

        /// <summary>Estate gold for a skill: DD1's price, scaled like every price, less the Bonfire's discount.</summary>
        public static int Price(CampSkill skill) => skill != null ? UpgradeRules.Price(skill.PriceGold, CostTree) : 0;

        private static string SessionBlock()
        {
            return EstateSession.InHub && EstateSession.View == EstateSession.Screen.Hamlet ? null : "Not while away from the hamlet";
        }

        /// <summary>Why the hero cannot learn this skill now; null when they can.</summary>
        public static string LearnBlock(ActorInstance actor, string skillId)
        {
            var refusal = Book(actor).LearnRefusal(Rules, skillId);
            if (refusal != null) return refusal;
            var price = Price(Rules.Skill(skillId));
            if (price <= 0) return "The Survivalist's terms could not be read from DD1";
            return price > EstateState.Gold ? "Needs " + UpgradeUi.Amount(price) + " gold" : SessionBlock();
        }

        /// <summary>The hero pays and learns a camping skill. Returns null, or why not.</summary>
        public static string Learn(uint guid, string skillId)
        {
            var actor = Hero(guid);
            if (actor == null) return "No such hero";
            var reason = LearnBlock(actor, skillId);
            if (reason != null) return reason;
            var price = Price(Rules.Skill(skillId));
            reason = Book(actor).Learn(Rules, skillId);
            if (reason != null) return reason;
            EstateState.AddGold(-price);
            EstateAudio.Ui("town/trainer_purchase_skill");
            Plugin.Log.LogInfo("Survivalist: " + actor.ActorName + " learns " + skillId + " for " + price + " gold");
            Done("survivalist");
            return null;
        }

        /// <summary>Makes a known skill ready for the next expedition, or puts a ready one aside. Returns null, or why not.</summary>
        public static string Toggle(uint guid, string skillId)
        {
            var actor = Hero(guid);
            if (actor == null) return "No such hero";
            var reason = SessionBlock() ?? Book(actor).Toggle(Rules, skillId);
            if (reason != null) return reason;
            Done("camping skills");
            return null;
        }

        /// <summary>For tests: every skill of the class, no price.</summary>
        public static string GrantAll(uint guid)
        {
            var actor = Hero(guid);
            if (actor == null) return "No such hero";
            var book = Book(actor);
            foreach (var skill in Rules.SkillsFor(book.ClassId)) book.Learn(Rules, skill.Id);
            RaiseChanged();
            return book.Known.Count + " skills known";
        }

        private static void Done(string reason)
        {
            RaiseChanged();
            EstatePersistence.SaveNow(reason);
        }

        private static void RaiseChanged()
        {
            try { Changed?.Invoke(); }
            catch (Exception e) { Plugin.Log.LogError("Survivalist: a Changed handler failed: " + e); }
        }

        // ---- save section ----------------------------------------------------------------------------

        private static JToken Save()
        {
            var json = new JObject();
            var actors = SingletonMonoBehaviour<Library<uint, ActorInstance>>.HasInstance() ? SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance : null;
            foreach (var pair in Books)
            {
                var actor = actors != null ? actors.GetLibraryElement(pair.Key) : null;
                if (actor == null || !actor.IsLiving) continue;     // the fallen keep nothing
                json[pair.Key.ToString(CultureInfo.InvariantCulture)] = pair.Value.ToJson();
            }
            return json;
        }

        private static void Load(JToken token)
        {
            Books.Clear();
            Claim();
            if (!(token is JObject saved)) return;
            foreach (var property in saved.Properties())
            {
                if (!uint.TryParse(property.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var guid)) continue;
                var book = CampSkillBook.FromJson(property.Value);
                try { book.Repair(Rules); }
                catch (Exception e) { Plugin.Log.LogWarning("Survivalist: a saved book could not be checked against DD1: " + e.Message); }
                Books[guid] = book;
            }
        }

        private static void Reset()
        {
            Books.Clear();
            Claim();
        }

        // ---- dev bridge ------------------------------------------------------------------------------

        public static object Describe(uint guid)
        {
            var actor = Hero(guid);
            if (actor == null) return "no such hero";
            var book = Book(actor);
            var skills = new List<object>();
            foreach (var skill in Rules.SkillsFor(book.ClassId))
            {
                skills.Add(new
                {
                    id = skill.Id,
                    name = CampText.Name(skill),
                    shared = Rules.IsShared(skill),
                    respite = skill.Cost,
                    uses = skill.UseLimit,
                    known = book.Knows(skill.Id),
                    ready = book.IsActive(skill.Id),
                    price = Price(skill),
                    dd1Price = skill.PriceGold,
                    blocked = book.Knows(skill.Id) ? null : LearnBlock(actor, skill.Id),
                    effect = string.Join(" | ", CampText.Lines(skill))
                });
            }
            return new
            {
                hero = actor.ActorName,
                cls = book.ClassId,
                analogue = Rules.UsesAnalogue(book.ClassId) ? CampingRules.Analogues[book.ClassId] : null,
                gold = EstateState.Gold,
                discount = UpgradeRules.Discount(CostTree),
                readyLimit = Rules.ActiveLimit,
                skills
            };
        }
    }
}
