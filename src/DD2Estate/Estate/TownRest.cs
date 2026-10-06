using Assets.Code.Actor;
using Assets.Code.Library;
using Assets.Code.Source;
using Assets.Code.Utils;
using DD2Estate.Dd2;

namespace DD2Estate.Estate
{
    /// <summary>
    /// DD1: wounds do not follow a hero home. Health is whole again once the party is back in the hamlet; only
    /// stress, quirks and diseases linger and need the town's buildings. That includes the Kingdom host's own
    /// "wounds" (a share of maximum health lost at death's door): they stand for DD1's mortality debuff, which
    /// lasts until the quest is over, so the hamlet mends them too.
    /// </summary>
    [EstateModule]
    internal static class TownRest
    {
        public static void Register()
        {
            EstateState.WeekAdvanced += week => HealRoster();
        }

        private static void HealRoster()
        {
            if (!EstateSession.Active) return;
            var library = SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance;
            foreach (var guid in EstateSession.RosterGuids())
            {
                var hero = library.GetLibraryElement(guid);
                if (hero == null || !hero.IsLiving) continue;
                if (hero.IsWounded) hero.ChangeWoundPercent(-hero.WoundPercent, SourceType.INN);
                var missing = hero.CurrentHpMax - hero.HpRounded;
                if (missing > 0f) hero.ApplyHealthHeal(missing, false, SourceType.INN, false);
            }
        }
    }
}
