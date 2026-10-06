using System;
using System.Collections.Generic;
using Assets.Code.Actor;
using Assets.Code.Library;
using Assets.Code.Utils;

namespace DD2Estate.Estate
{
    /// <summary>
    /// DD2's hero paths as the estate uses them. A class has its own path (the Wanderer) and the paths the game
    /// lets a player unlock for it, four in all for most classes. The estate holds one hero of a class per
    /// path, so a path is what tells two heroes of a class apart: a recruit steps off the coach with theirs
    /// and keeps it. Which paths the profile has unlocked does not matter: the estate has its own progression,
    /// the stage coach's Hero Paths tree (the mod's own; DD1 has no paths). Recruits of the class's own path
    /// come from the start, and each of the tree's three steps lets them arrive on one path more.
    /// </summary>
    [EstateModule]
    internal static class HeroPaths
    {
        public const string Tree = "stage_coach.hero_paths";
        // Both make room for more heroes: the tree's steps cost what these steps of DD1's Hero Barracks cost.
        private const string PricedLike = "stage_coach.rostersize";
        private static readonly int[] PricedLikeSteps = { 0, 2, 4 };
        private static readonly int[][] DeedsAndCrests = { new[] { 3, 4 }, new[] { 16, 15 }, new[] { 25, 26 } };

        private static readonly Dictionary<string, List<string>> _byClass = new Dictionary<string, List<string>>();

        private static void Register()
        {
            UpgradeRules.Define(BuildTree);
            UpgradeText.Names[Tree] = "Hero Paths";
            // A road and a signpost at dusk: DD1's art for the wagon's prices, the stage coach's own in another light.
            UpgradeText.Icons[Tree] = UpgradeUi.BuildingsDir + "nomad_wagon/nomad_wagon.cost.icon.png";
            UpgradeText.Describers[Tree] = Describe;
        }

        private static UpgradeRules.Tree BuildTree()
        {
            var tree = new UpgradeRules.Tree { Id = Tree, Building = StageCoach.BuildingId };
            var barracks = UpgradeRules.Find(PricedLike);
            for (var i = 0; i < PricedLikeSteps.Length; i++)
            {
                var step = new UpgradeRules.Step { Code = ((char)('a' + i)).ToString() };
                var like = barracks != null && PricedLikeSteps[i] < barracks.Steps.Count ? barracks.Steps[PricedLikeSteps[i]] : null;
                if (like != null)
                {
                    foreach (var cost in like.Costs) step.Costs.Add(new UpgradeRules.Cost { Currency = cost.Currency, Amount = cost.Amount });
                }
                else
                {
                    step.Costs.Add(new UpgradeRules.Cost { Currency = "deed", Amount = DeedsAndCrests[i][0] });
                    step.Costs.Add(new UpgradeRules.Cost { Currency = "crest", Amount = DeedsAndCrests[i][1] });
                }
                tree.Steps.Add(step);
            }
            return tree;
        }

        private static string Describe(UpgradeRules.Tree tree, int level)
        {
            var paths = level + 1;
            return "Recruits arrive on " + (level == 1 ? "a second" : level == 2 ? "a third" : "a fourth") + " path of their class; the estate may keep "
                   + (paths == 2 ? "two" : paths == 3 ? "three" : "four") + " heroes of a class, each on a path of their own";
        }

        /// <summary>How many paths of a class the coach brings recruits on: one, and one more per step built.</summary>
        public static int OpenCount => 1 + UpgradeRules.Level(Tree);

        /// <summary>The paths of a class recruits may arrive on with the tree as it stands, the class's own first.</summary>
        public static IReadOnlyList<string> Open(string classId)
        {
            var all = Of(classId);
            var open = new List<string>();
            for (var i = 0; i < all.Count && i < OpenCount; i++) open.Add(all[i]);
            return open;
        }

        private static Library<string, ActorDataPath> Paths => SingletonMonoBehaviour<Library<string, ActorDataPath>>.Instance;

        private static ActorDataClass Class(string classId)
        {
            return string.IsNullOrEmpty(classId) ? null : SingletonMonoBehaviour<Library<string, ActorDataClass>>.Instance.GetLibraryElement(classId);
        }

        public static ActorDataPath Data(string pathId) => string.IsNullOrEmpty(pathId) ? null : Paths.GetLibraryElement(pathId);

        /// <summary>The path a new hero of the class has when nobody chooses: "" for a class without paths.</summary>
        public static string Default(string classId)
        {
            var path = Class(classId)?.DefualtActorDataPath;
            return path != null ? path.Id : "";
        }

        /// <summary>
        /// The paths of a class in the game's own order: its default and every path with an unlock that is
        /// valid for it (ActorPathCalculation.GetActorDataPaths without the profile's say).
        /// </summary>
        public static IReadOnlyList<string> Of(string classId)
        {
            if (string.IsNullOrEmpty(classId)) return new List<string>();
            if (_byClass.TryGetValue(classId, out var known)) return known;
            var cls = Class(classId);
            var found = new List<ActorDataPath>();
            if (cls != null)
            {
                var paths = Paths;
                var count = paths.GetNumberOfLibraryElements();
                for (var i = 0; i < count; i++)
                {
                    var path = paths.GetLibraryElementAtIndex(i);
                    if (path == null || path == cls.DefualtActorDataPath || !path.GetHasUnlock() || !path.GetIsValidForActorDataClass(cls)) continue;
                    found.Add(path);
                }
                found.Sort((a, b) => a.m_OrderPriority != b.m_OrderPriority ? a.m_OrderPriority.CompareTo(b.m_OrderPriority) : string.CompareOrdinal(a.Id, b.Id));
                // The class's own path comes first whatever its priority: it is the one open from the start.
                if (cls.DefualtActorDataPath != null) found.Insert(0, cls.DefualtActorDataPath);
            }
            var ids = new List<string>();
            foreach (var path in found) ids.Add(path.Id);
            // A class without any path is still one hero: the empty path.
            if (ids.Count == 0) ids.Add("");
            if (cls != null) _byClass[classId] = ids;
            return ids;
        }

        public static string Of(ActorInstance actor)
        {
            return actor != null && actor.ActorDataPath != null ? actor.ActorDataPath.Id : "";
        }

        /// <summary>What a class and a path make together: the seat one hero of the estate takes.</summary>
        public static string Key(string classId, string pathId) => classId + "/" + (pathId ?? "");

        public static string Key(ActorInstance actor) => Key(actor.ActorDataId, Of(actor));

        /// <summary>The path's name as the game shows it; "" for no path.</summary>
        public static string Name(string pathId, string classId)
        {
            var path = Data(pathId);
            if (path == null) return "";
            try
            {
                var cls = Class(classId);
                return ActorPathDescription.GetNameString(path, cls != null ? cls.m_LocalizationGender : "", addColor: false);
            }
            catch (Exception)
            {
                return pathId;
            }
        }

        public static string Name(ActorInstance actor) => actor != null ? Name(Of(actor), actor.ActorDataId) : "";

        /// <summary>Puts a hero on a path of their class (skills swapped the native way) with health to match.</summary>
        public static bool Set(ActorInstance actor, string pathId)
        {
            var path = Data(pathId);
            if (actor == null || path == null || actor.ActorDataClass == null || !path.GetIsValidForActorDataClass(actor.ActorDataClass)) return false;
            actor.SetActorPath(path, refundUnlockedSkills: false);
            var missing = actor.CurrentHpMax - actor.HpRounded;
            if (missing > 0f) actor.ApplyHealthHeal(missing, false, Assets.Code.Source.SourceType.INN, false);
            return true;
        }
    }
}
