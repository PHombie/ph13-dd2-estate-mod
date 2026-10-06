using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Assets.Code.Actor;
using Assets.Code.Boss;
using Assets.Code.Combat.BattleModifier;
using Assets.Code.Condition;
using Assets.Code.Game;
using Assets.Code.Kingdom;
using Assets.Code.Library;
using Assets.Code.Run;
using Assets.Code.Source;
using Assets.Code.Utils;
using DD2Estate.Core;
using DD2Estate.Dev;
using DD2Estate.Dungeon;
using DD2Estate.Estate;
using HarmonyLib;
using UnityEngine;

namespace DD2Estate.Dd2
{
    /// <summary>
    /// Bridge commands for the tier scaling ({"cmd":"run","name":"difficulty.apply","dungeon":"crypts",
    /// "tier":"veteran","kind":"hallway"}): set a plan by hand, look at what the engine holds, and read the
    /// enemies of the battle in progress with the stats the scaling touches.
    /// </summary>
    [EstateModule]
    internal static class DifficultyDev
    {
        private static void Register()
        {
            AgentBridge.Register("difficulty.state", o => State());
            AgentBridge.Register("difficulty.apply", o =>
            {
                var kind = Enum.TryParse((string)o["kind"] ?? "room", true, out EncounterKind parsed) ? parsed : EncounterKind.Room;
                // Sticky: the fight is started by a later command, the plan must still be there then.
                Difficulty.Apply((string)o["dungeon"] ?? "crypts", (string)o["tier"] ?? "apprentice", kind, (string)o["boss"], sticky: true);
                return Difficulty.Stored != null ? Difficulty.Summary(Difficulty.Stored) : "not applied (see the log)";
            });
            AgentBridge.Register("difficulty.clear", o =>
            {
                Difficulty.Clear();
                return "cleared";
            });
            AgentBridge.Register("difficulty.enemies", o => Enemies());
        }

        private static object Scaling(TierScaling s) => new { hp = s.Hp, dmg = s.Dmg, dot = s.Dot, speed = s.Speed, crit = s.Crit, resist = s.Resist };

        // ---- difficulty.state ----------------------------------------------------------------------------

        private static object State()
        {
            var stored = Difficulty.Stored;
            var live = Difficulty.Live;
            object plan = null;
            if (stored != null)
            {
                plan = new
                {
                    dungeon = stored.Dungeon, tier = stored.Tier, kind = stored.Kind.ToString(), boss = stored.Boss,
                    live = ReferenceEquals(stored, live), sticky = stored.Sticky,
                    escalationForConditions = stored.Escalation,
                    scaling = Scaling(stored.Scaling),
                    ordain = stored.Ordain?.m_Id, ordainChance = stored.OrdainChance,
                    bossRow = stored.BossOrdain?.m_Id, bossBuffs = stored.BossBuffs?.Id,
                    battleModifier = stored.BattleModifier?.m_Id, actBoss = stored.RunBoss,
                    enemiesFitted = stored.Fitted.Count, enemiesOrdained = stored.Ordained
                };
            }
            return new { plan, engine = Engine(), patches = Patches() };
        }

        // What the game itself holds of every ladder the scaling could have used.
        private static object Engine()
        {
            var types = Singleton<GameTypeMgr>.Instance;
            if (types == null || !types.IsGameTypeStarted) return new { started = false };
            var values = types.RunValues;
            var levels = new List<string>();
            if (values?.CurrentRunValueLevelDefinitions != null)
                foreach (var level in values.CurrentRunValueLevelDefinitions) levels.Add(level.m_Id);
            var biome = types.BiomeManager != null ? types.BiomeManager.GetActiveBiome() : null;
            var kingdom = SingletonMonoBehaviour<KingdomBhv>.HasInstance() ? SingletonMonoBehaviour<KingdomBhv>.Instance.KingdomManager : null;
            var run = SingletonMonoBehaviour<RunBhv>.HasInstance() ? SingletonMonoBehaviour<RunBhv>.Instance.RunManager : null;
            return new
            {
                started = true,
                session = EstateSession.Active,
                mode = GameModeMgr.CurrentMode?.GetName(),
                gameType = types.CurrentGameType?.GetName(),
                // the saved run value (the scaling never writes it) and what conditions are told right now
                escalationRunValue = values != null ? values.GetValue(RunValueType.ESCALATION) : -1f,
                escalationForConditions = Difficulty.Live != null ? Difficulty.Live.Escalation : (values != null ? (int)values.GetValue(RunValueType.ESCALATION) : -1),
                runValueLevels = levels,
                torch = values != null ? values.GetValue(RunValueType.TORCH) : -1f,
                // DD1's torchlight beside it: the two are one thing (DD2's flame is the light as a fight begins,
                // and what the fight did to the flame is done to the light afterwards)
                expeditionLight = DD2Estate.Dungeon.DungeonRun.Current?.LightBooks(),
                battleModifierChance = types.RunDataManager != null ? types.RunDataManager.GetStatValue(RunStatType.BATTLE_MODIFIER_CHANCE) : -1f,
                gang = kingdom?.Gang?.m_Id,
                expeditionRunning = run != null,
                actBoss = run?.Boss?.m_Id,
                activeBiome = biome?.m_BiomeType?.GetName(),
                siegeStrength = biome != null ? biome.m_SiegeStrength : 0,
                typicalBiomesVisited = types.BiomeManager != null ? types.BiomeManager.BiomeTypicalCount : 0
            };
        }

        private static object Patches()
        {
            var targets = new (string name, MethodBase method)[]
            {
                ("stats: ActorInstance.OnAddedToTeam", AccessTools.Method(typeof(ActorInstance), nameof(ActorInstance.OnAddedToTeam))),
                ("ordain: BossCalculation.RollBossModifier", AccessTools.Method(typeof(BossCalculation), nameof(BossCalculation.RollBossModifier))),
                ("modifier: BattleModifierCalculation.RollBattleModifier", AccessTools.Method(typeof(BattleModifierCalculation), nameof(BattleModifierCalculation.RollBattleModifier))),
                ("conditions: ConditionCalculation.GetGameValueForConditionType", AccessTools.Method(typeof(ConditionCalculation), "GetGameValueForConditionType"))
            };
            var list = new List<object>();
            foreach (var target in targets)
            {
                var info = target.method != null ? Harmony.GetPatchInfo(target.method) : null;
                var applied = info != null && info.Postfixes.Any(p => p.owner == Plugin.Guid);
                list.Add(new { patch = target.name, found = target.method != null, applied });
            }
            return list;
        }

        // ---- difficulty.enemies --------------------------------------------------------------------------

        private static object Enemies()
        {
            var actors = SingletonMonoBehaviour<Library<uint, ActorInstance>>.HasInstance() ? SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance : null;
            if (actors == null) return "no actors";
            var plan = Difficulty.Stored;
            var enemies = new List<object>();
            string modifier = null;
            foreach (var actor in actors.GetLibraryElements(a => a != null && a.TeamIndex == Difficulty.EnemyTeam))
            {
                var classStats = actor.ActorDataClass != null ? actor.ActorDataClass.ActorDataStats : null;
                var baseHp = classStats != null ? classStats.StatContainer.GetStatAddValue(ActorStatType.HEALTH_MAX) : 0f;
                var maxHp = actor.CurrentHpMax;
                object tierShares = null;
                if (plan != null && plan.Fitted.TryGetValue(actor.ActorGuid, out var given)) tierShares = Scaling(given);
                enemies.Add(new
                {
                    guid = actor.ActorGuid,
                    cls = actor.ActorDataId,
                    living = actor.IsLiving,
                    baseHp,
                    maxHp = Mathf.RoundToInt(maxHp),
                    hp = Mathf.RoundToInt(actor.HpRaw),
                    hpMultiplier = baseHp > 0f ? Round(maxHp / baseHp) : 0f,
                    hpSharesBySource = BySource(() => actor.GetMultiplyStatValuesBySource(ActorStatType.HEALTH_MAX, false)),
                    damageMultiplier = Round(actor.GetClampedStatValue(ActorStatType.HEALTH_DAMAGE_DEALT_PERCENT)),
                    damageSharesBySource = BySource(() => actor.GetAddStatValuesBySource(ActorStatType.HEALTH_DAMAGE_DEALT_PERCENT, false)),
                    dotMultiplier = Round(1f + actor.GetClampedStatValue(ActorStatType.DOT_EFFECT_VALUE_DEALT_MULTIPLIER, "bleed")),
                    speed = actor.GetClampedStatValue(ActorStatType.SPEED),
                    crit = Round(actor.GetClampedStatValue(ActorStatType.CRIT_CHANCE)),
                    stunResist = Round(actor.GetClampedStatValue(ActorStatType.RESISTANCE, "stun")),
                    ordained = actor.BossModifier?.m_Id,
                    tierShares
                });
                // the fight's battle modifier, as this actor's team has it
                try { modifier = modifier ?? ((Assets.Code.Combat.Team)AccessTools.Field(typeof(ActorInstance), "m_Team").GetValue(actor))?.BattleModifier?.m_Id; }
                catch (Exception) { }
            }
            return new
            {
                plan = plan != null ? Difficulty.Summary(plan) : null,
                battleModifier = modifier,
                count = enemies.Count,
                enemies
            };
        }

        private static float Round(float value) => (float)Math.Round(value, 3);

        // A stats container without a source makes the game's own by-source sums throw; the totals above do not need them.
        private static object BySource(Func<IReadOnlyDictionary<SourceType, float>> read)
        {
            try
            {
                var shares = new Dictionary<string, float>();
                foreach (var pair in read()) shares[pair.Key != null ? pair.Key.GetName() : "?"] = Round(pair.Value);
                return shares;
            }
            catch (Exception e)
            {
                return "not available: " + e.GetType().Name;
            }
        }
    }
}
