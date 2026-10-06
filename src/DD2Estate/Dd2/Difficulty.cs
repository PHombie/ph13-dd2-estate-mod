using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Assets.Code.Actor;
using Assets.Code.Boss;
using Assets.Code.Buff;
using Assets.Code.Combat.BattleModifier;
using Assets.Code.Condition;
using Assets.Code.Library;
using Assets.Code.Run;
using Assets.Code.Utils;
using DD2Estate.Core;
using DD2Estate.Dungeon;
using UnityEngine;

namespace DD2Estate.Dd2
{
    /// <summary>
    /// Makes a fight as hard as the DD1 quest it belongs to (apprentice, veteran, champion, darkest).
    ///
    /// DD2 keeps one version of every monster and, hosted as a Kingdom without a map, none of its own ladders
    /// reaches the estate's fights: nothing ordains (no expedition run), the escalation stays at 1, there is no
    /// region index. So the tier is brought to the fight in four ways, all of them for the one fight that
    /// follows <see cref="Apply"/> and none of them written into the saved game:
    ///
    /// 1. Stats. Every enemy that joins the enemy team gets a stats container of its own (health, damage,
    ///    damage over time, speed, crit) with the shares of <c>tier_rules.&lt;tier&gt;.scaling.&lt;kind&gt;</c>.
    ///    The shares put DD1's growth of a fight from level 1 to 3 and 5 next to the growth of the estate's
    ///    heroes (docs/recon/difficulty.md, section 3). It hangs under the actor's battle data, which the game
    ///    empties when the actor leaves its team.
    /// 2. Ordainment. The dungeon's row for the tier is rolled per monster at the game's own chance, a lair
    ///    boss gets its row outright (BossCalculation.RollBossModifier answers nothing without an expedition).
    ///    What the row adds to health and damage is taken off the actor's share, so a blessed monster is as
    ///    strong as its neighbours and has the blessing's trick on top.
    /// 3. A gang boss fights with its gang's own battle modifier for the tier and its blessing buffs.
    /// 4. The game's conditions on the escalation value are answered with the tier's (1, 2, 3, 3): trinket
    ///    rarity in the loot, the gang factions' skills, which Death shows up. The run value itself stays 1.
    /// </summary>
    internal static class Difficulty
    {
        /// <summary>Team index of the enemies in every DD2 battle (heroes are 0).</summary>
        public const int EnemyTeam = 1;

        private const string StatsId = "estate_tier";
        // A plan made by the dungeon belongs to the fight started in the same breath; this many frames later,
        // back in the hub, it is over even if nobody said so.
        private const int GraceFrames = 5;

        internal class Plan
        {
            public string Dungeon, Tier, Boss, RunBoss;
            public EncounterKind Kind;
            public TierScaling Scaling;
            public int Escalation = 1;
            public BossModifierDefinition Ordain, BossOrdain;
            public float OrdainChance;
            public DataExternalBuffs BossBuffs;
            public HashSet<string> BossActors = new HashSet<string>();
            public BattleModifierDefinition BattleModifier;
            public int Frame;
            public bool Sticky;
            /// <summary>What each enemy was given: guid -> shares after the blessing's own were taken off.</summary>
            public readonly Dictionary<uint, TierScaling> Fitted = new Dictionary<uint, TierScaling>();
            public int Ordained;
        }

        private static Plan _plan;
        private static readonly System.Random Dice = new System.Random();
        private static bool _fitFailed;

        // ---- the two calls ------------------------------------------------------------------------------

        /// <summary>
        /// Call right before a fight is started (before the CombatScenarioData is made: its loot may be rolled
        /// there). <paramref name="tierName"/> is apprentice, veteran, champion or darkest; a dungeon that has
        /// one tier only (the Darkest Dungeon) uses it whatever is passed. <paramref name="bossId"/> is the
        /// boss of a boss fight (a dungeon's boss or a wanderer), null otherwise.
        /// </summary>
        public static void Apply(string dungeonId, string tierName, EncounterKind kind, string bossId)
        {
            Apply(dungeonId, tierName, kind, bossId, sticky: false);
        }

        /// <summary><paramref name="sticky"/>: stays until Clear or the next Apply (bridge tests start the fight with a later command).</summary>
        internal static void Apply(string dungeonId, string tierName, EncounterKind kind, string bossId, bool sticky)
        {
            try
            {
                var tier = DungeonContent.ResolveTier(dungeonId, tierName);
                // A boss slot nobody stands in is filled from the room pool (DungeonRun.StartFight): a room fight.
                if (kind == EncounterKind.Boss && string.IsNullOrEmpty(bossId)) kind = EncounterKind.Room;
                var plan = new Plan
                {
                    Dungeon = dungeonId, Tier = tier, Kind = kind, Boss = bossId,
                    Scaling = DungeonContent.Scaling(tier, kind),
                    Escalation = DungeonContent.Escalation(tier),
                    Frame = Time.frameCount, Sticky = sticky
                };
                DungeonContent.Ordain(dungeonId, tier, out var row, out var chance);
                plan.Ordain = Find<BossModifierDefinition>(row);
                plan.OrdainChance = plan.Ordain != null ? chance : 0f;
                var boss = kind == EncounterKind.Boss ? DungeonContent.Boss(dungeonId, bossId, tier) : null;
                if (boss != null)
                {
                    plan.RunBoss = boss.RunBoss;
                    plan.BossActors = boss.Actors;
                    plan.BossOrdain = Find<BossModifierDefinition>(boss.BossModifier);
                    // A row delivers its own buffs; the set is attached by hand only where no row fits the boss.
                    if (plan.BossOrdain == null) plan.BossBuffs = Find<DataExternalBuffs>(boss.ExternalBuffs);
                    plan.BattleModifier = Find<BattleModifierDefinition>(boss.BattleModifier);
                }
                _plan = plan;
                Plugin.Log.LogInfo("Difficulty: " + Summary(plan));
            }
            catch (Exception e)
            {
                _plan = null;
                Plugin.Log.LogError("Difficulty: no tier applied to this fight (" + dungeonId + ", " + tierName + ", " + kind + "): " + e);
            }
        }

        /// <summary>Call when control is back in the hub: the next fight starts from nothing.</summary>
        public static void Clear()
        {
            _plan = null;
        }

        /// <summary>The plan of the fight in progress, or null. Never outside an Estate session.</summary>
        internal static Plan Live
        {
            get
            {
                var plan = _plan;
                if (plan == null || !EstateSession.Active) return null;
                if (!plan.Sticky && EstateSession.InHub && Time.frameCount > plan.Frame + GraceFrames)
                {
                    _plan = null;
                    return null;
                }
                return plan;
            }
        }

        /// <summary>The plan as it stands, without the "is it still this fight" check (for the state report).</summary>
        internal static Plan Stored => _plan;

        private static T Find<T>(string id) where T : class, ILibraryElement<string>
        {
            if (string.IsNullOrEmpty(id)) return null;
            var library = SingletonMonoBehaviour<Library<string, T>>.Instance;
            if (library != null && library.GetHasLibraryKey(id)) return library.GetLibraryElement(id);
            Plugin.Log.LogWarning("Difficulty: this game build has no " + typeof(T).Name + " '" + id + "'");
            return null;
        }

        internal static string Summary(Plan plan)
        {
            var s = plan.Scaling;
            var text = new StringBuilder();
            text.Append(plan.Dungeon).Append(' ').Append(plan.Tier).Append(' ').Append(plan.Kind);
            if (plan.Boss != null) text.Append(" (").Append(plan.Boss).Append(')');
            text.Append(": health +").Append(Percent(s.Hp)).Append(", damage +").Append(Percent(s.Dmg)).Append(", dots +").Append(Percent(s.Dot))
                .Append(", speed +").Append(s.Speed.ToString("0.#", CultureInfo.InvariantCulture)).Append(", crit +").Append(Percent(s.Crit));
            if (s.Resist != 0f) text.Append(", resistances +").Append(Percent(s.Resist));
            text.Append("; escalation ").Append(plan.Escalation);
            if (plan.Ordain != null) text.Append("; ordains ").Append(plan.Ordain.m_Id).Append(" at ").Append(Percent(plan.OrdainChance));
            if (plan.BossOrdain != null) text.Append("; boss row ").Append(plan.BossOrdain.m_Id);
            if (plan.BossBuffs != null) text.Append("; boss buffs ").Append(plan.BossBuffs.Id);
            if (plan.BattleModifier != null) text.Append("; battle modifier ").Append(plan.BattleModifier.m_Id);
            if (plan.RunBoss != null) text.Append("; act ").Append(plan.RunBoss);
            return text.ToString();
        }

        private static string Percent(float share) => ((int)Math.Round(share * 100f)).ToString(CultureInfo.InvariantCulture) + "%";

        // ---- answers for the patches (DifficultyPatches.cs) -----------------------------------------------

        /// <summary>BossCalculation.RollBossModifier found nothing: the tier's row for this class, or null.</summary>
        internal static BossModifierDefinition RollOrdain(ActorDataClass actorClass)
        {
            var plan = Live;
            if (plan == null || actorClass == null) return null;
            BossModifierDefinition row = null;
            if (plan.BossOrdain != null && plan.BossOrdain.GetIsValidForActorClass(actorClass)) row = plan.BossOrdain;
            else if (plan.Ordain != null && plan.OrdainChance > 0f && plan.Ordain.GetIsValidForActorClass(actorClass) &&
                     Dice.NextDouble() < plan.OrdainChance) row = plan.Ordain;
            if (row != null) plan.Ordained++;
            return row;
        }

        /// <summary>The battle modifier the fight must have, or null to leave the game's roll alone.</summary>
        internal static BattleModifierDefinition ForcedBattleModifier() => Live?.BattleModifier;

        /// <summary>A game-wide condition was evaluated: the tier's answer replaces the session's where it has one.</summary>
        internal static void Answer(ConditionType conditionType, string conditionString, ref float value)
        {
            var plan = Live;
            if (plan == null) return;
            if (conditionType == ConditionType.RUN_VALUE)
            {
                if (conditionString == RunValueType.ESCALATION.GetName()) value = plan.Escalation;
            }
            else if (conditionType == ConditionType.BOSS)
            {
                // An act boss is fought as in its own act (in the estate no act is ever running, which the
                // data reads as "every act but this one": the Denial locks would get their re-run health).
                if (plan.RunBoss != null) value = conditionString == plan.RunBoss ? 1f : 0f;
            }
        }

        /// <summary>An enemy joined the enemy team: give it the tier's stats.</summary>
        internal static void Fit(ActorInstance actor)
        {
            var plan = Live;
            if (plan == null || actor == null) return;
            try
            {
                var battleData = actor.BattleActorData;
                var classId = actor.ActorDataClass != null ? actor.ActorDataClass.Id : actor.ActorDataId;
                var changed = false;

                if (plan.BossBuffs != null && plan.BossActors.Contains(classId) && !battleData.GetIsChild(plan.BossBuffs))
                {
                    battleData.AddChild(plan.BossBuffs);
                    changed = true;
                }

                // What the game's own blessing already gives this actor counts towards the tier's share.
                float blessedHp = 0f, blessedDmg = 0f;
                if (actor.BossModifier != null) Shares(actor.BossModifier.DataExternalBuffs, ref blessedHp, ref blessedDmg);
                if (plan.BossBuffs != null && battleData.GetIsChild(plan.BossBuffs)) Shares(plan.BossBuffs, ref blessedHp, ref blessedDmg);

                var given = plan.Scaling;
                given.Hp = Math.Max(0f, given.Hp - blessedHp);
                given.Dmg = Math.Max(0f, given.Dmg - blessedDmg);
                var text = StatsText(given);
                if (text.Length > 0)
                {
                    var stats = new ActorDataStats(StatsId, text);
                    stats.Init(StatsId + "_" + actor.ActorGuid);
                    battleData.AddChild(stats);
                    changed = true;
                }
                plan.Fitted[actor.ActorGuid] = given;
                // The game's own way to take a changed maximum: health keeps its share of it.
                if (changed) actor.RefreshStats();
            }
            catch (Exception e)
            {
                if (!_fitFailed) Plugin.Log.LogError("Difficulty: could not fit an enemy with the tier's stats: " + e);
                _fitFailed = true;
            }
        }

        private static void Shares(DataExternalBuffs buffs, ref float hp, ref float dmg)
        {
            if (buffs == null) return;
            foreach (var buff in buffs.GetBuffs())
            {
                var stats = buff != null ? buff.ActorDataStats : null;
                if (stats == null) continue;
                hp += stats.StatContainer.GetStatMultiplyValue(ActorStatType.HEALTH_MAX);
                dmg += stats.StatContainer.GetStatAddValue(ActorStatType.HEALTH_DAMAGE_DEALT_PERCENT);
            }
        }

        private static readonly string[] DotTypes = { "bleed", "blight", "burn" };
        private static readonly string[] ResistTypes = { "stun", "bleed", "blight", "burn", "debuff", "move" };

        // The shares as DD2 writes stats in its own tables (StatDataContainer.SetFromCsv).
        internal static string StatsText(TierScaling s)
        {
            var text = new StringBuilder();
            void Line(string kind, string stat, string sub, float value)
            {
                if (Math.Abs(value) < 0.0001f) return;
                text.Append(kind).Append(',').Append(stat).Append(',');
                if (sub != null) text.Append(sub).Append(',');
                text.Append(value.ToString("0.####", CultureInfo.InvariantCulture)).Append('\n');
            }
            Line("multiply_stat", ActorStatType.HEALTH_MAX.GetName(), null, s.Hp);
            Line("add_stat", ActorStatType.HEALTH_DAMAGE_DEALT_PERCENT.GetName(), null, s.Dmg);
            foreach (var dot in DotTypes) Line("sub_stat", ActorStatType.DOT_EFFECT_VALUE_DEALT_MULTIPLIER.GetName(), dot, s.Dot);
            Line("add_stat", ActorStatType.SPEED.GetName(), null, (float)Math.Round(s.Speed));
            Line("add_stat", ActorStatType.CRIT_CHANCE.GetName(), null, s.Crit);
            foreach (var resist in ResistTypes) Line("sub_stat", ActorStatType.RESISTANCE.GetName(), resist, s.Resist);
            return text.ToString();
        }
    }
}
