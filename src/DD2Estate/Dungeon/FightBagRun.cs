using System;
using System.Collections.Generic;
using Assets.Code.Actor;
using Assets.Code.Combat;
using Assets.Code.Dot;
using Assets.Code.Game;
using Assets.Code.Run;
using Assets.Code.Source;
using Assets.Code.Token;
using Assets.Code.Utils;
using DD2Estate.Core;
using DD2Estate.Dd2;
using UnityEngine;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// The expedition's bag inside a DD2 fight: DD1's rules for it (<see cref="FightItemRules"/>) carried out on
    /// DD2's fighting actor. The hero whose turn it is uses an item on themselves, and it is no action of the
    /// turn. What an item does:
    ///   food              the hero eats: DD1's share of health, until full;
    ///   bandage           DD2's bleed is taken off the hero (DD1: cure_bleed);
    ///   antivenom         DD2's blight is taken off (DD1: cure_poison);
    ///   medicinal herbs   DD2's negative tokens are taken off (DD1: clear_debuff; a DD2 fight's debuffs are tokens);
    ///   laudanum          the dots that deal stress are taken off (DD1: clearDotStress, horror);
    ///   holy water        DD1's four resistance buffs for its three rounds, as DD2 stats (<see cref="FightBagBuffs"/>);
    ///   torch             DD2's flame rises by a torch's worth; the torchlight follows when the fight is over
    ///                     (DungeonRun.TakeLightBack);
    /// and beside it what the item does on a hero between fights (a share of health, a point of stress), see
    /// <see cref="FightItemRules.StandInsInAFight"/>. The fight sees it at once: the calls are the ones DD2's own
    /// effects make (ApplyHealthHeal, ApplyStressHeal, the containers' removals, RunValues.ChangeValue).
    /// </summary>
    internal partial class DungeonRun
    {
        private static readonly string[] BleedTypes = { "bleed" };
        private static readonly string[] BlightTypes = { "blight" };

        private FightItemRules _fightItems;

        internal FightItemRules FightItems => _fightItems ?? (_fightItems = FightItemRules.Load(new Dd1Files(), _supply, _raid));

        /// <summary>A fight of this expedition is on (from the moment it is called until the hub is back).</summary>
        public bool Fighting => _fighting;

        /// <summary>
        /// The hero whose turn it is in the fight on screen, when the turn is the player's to play (DD2's
        /// controller of the actor takes input); null otherwise: an enemy's turn, no fight, a hero acting out.
        /// </summary>
        internal static ActorInstance ActingHero()
        {
            try
            {
                if (GameModeMgr.CurrentMode != GameModeType.COMBAT || !SingletonMonoBehaviour<CombatBhv>.HasInstance()) return null;
                var combat = SingletonMonoBehaviour<CombatBhv>.Instance;
                if (!combat.GetHasCurrentActor()) return null;
                var actor = combat.GetCurrentActor();
                if (actor == null || !actor.IsLiving || actor.TeamIndex != 0 || actor.Controller == null || !actor.Controller.GetIsInputValid()) return null;
                return actor;
            }
            catch (Exception) { return null; }
        }

        // A dot of DD2's that deals stress as it ticks: what DD1 calls horror.
        private static bool IsHorror(DotInstance dot)
        {
            var effects = dot?.Definition?.m_Effects;
            if (effects == null) return false;
            foreach (var effect in effects)
                if (effect != null && (effect.m_StressDamage > 0f || effect.m_StressDamageUpTo > 0f)) return true;
            return false;
        }

        // A token DD2 counts against its bearer and shows: a fight's debuff.
        private static bool IsDebuff(TokenInstance token)
        {
            var definition = token?.Definition;
            return definition != null && definition.IsNegative && !definition.IsHidden;
        }

        internal FightHeroState FightStateOf(ActorInstance hero)
        {
            var state = new FightHeroState();
            if (hero == null) return state;
            state.Hurt = hero.HpRounded < hero.CurrentHpMax;
            state.Stressed = hero.Stress > 0f;
            state.Eaten = Eaten(hero.ActorGuid);
            try
            {
                state.Bleeding = hero.DotContainer.GetHasInstancesWithTypes(BleedTypes);
                state.Blighted = hero.DotContainer.GetHasInstancesWithTypes(BlightTypes);
                state.Horrified = hero.DotContainer.GetInstances((Predicate<DotInstance>)IsHorror).Count > 0;
                state.Debuffed = hero.TokenContainer.GetInstances((Predicate<TokenInstance>)IsDebuff).Count > 0;
            }
            catch (Exception e) { Plugin.Log.LogWarning("Fight bag: a hero's dots and tokens could not be read: " + e.Message); }
            return state;
        }

        internal FightScene FightSceneNow()
        {
            var flame = Torch();
            return new FightScene { Light = double.IsNaN(flame) ? _x.Light : flame, Ambush = _ambush, TorchBurns = _x.TorchBurns };
        }

        /// <summary>What the acting hero's use of a slot's stack would do now; null for an empty slot.</summary>
        internal FightItemUse PlanFightUse(int slot, ActorInstance hero)
        {
            var stack = _bag.Slot(slot);
            return stack == null ? null : FightItems.Plan(stack.Item, FightStateOf(hero), FightSceneNow());
        }

        /// <summary>Why a use is refused, in DD1's words where it has them (the tooltip's last line, the notice of a click).</summary>
        internal string FightRefusalText(ItemDef item, FightItemUse use, ActorInstance hero)
        {
            if (use == null || use.Possible) return null;
            var name = hero != null ? hero.ActorName : "The hero";
            switch (use.Refusal)
            {
                case FightItemRefusal.NotAFightItem: return RaidText.Get("str_user_information_cant_use_items_in_battle", "Can't use items during battle");
                case FightItemRefusal.TorchAtLimit: return RaidText.Get("str_cant_use_torch_at_limit", "It's no use...this is all the light we are getting for now.");
                case FightItemRefusal.TorchInAmbush: return RaidText.Get("str_cant_use_torch_during_ambush", "Can't use torches during an ambush!");
                case FightItemRefusal.Full: return name + " is full.";
                case FightItemRefusal.NotHurt: return name + " is not hurt.";
            }
            // a supply with an effect of DD1's: everything it could have acted on
            var lacks = new List<string>();
            var effect = FightItems.EffectOf(item);
            if (effect != null)
            {
                if (effect.CureBleed) lacks.Add("not bleeding");
                if (effect.CureBlight) lacks.Add("not blighted");
                if (effect.ClearDebuffs) lacks.Add("under no debuff");
                if (effect.ClearHorror) lacks.Add("not horrified");
            }
            if (FightItemRules.StandInsInAFight)
            {
                var between = _supply.UseOf(item);
                if (between.Kind == ItemUseKind.Heal) lacks.Add("not hurt");
                else if (between.Kind == ItemUseKind.StressHeal) lacks.Add("not stressed");
            }
            return lacks.Count == 0 ? "It would do nothing now." : name + " is " + string.Join(" and ", lacks) + ".";
        }

        /// <summary>
        /// The hero whose turn it is uses one unit of a slot's stack. False when nothing was used; the message
        /// says why (or what was done).
        /// </summary>
        public bool UseItemInFight(int slot, out string message)
        {
            message = null;
            var stack = _bag.Slot(slot);
            if (stack == null) return false;
            if (!_fighting || GameModeMgr.CurrentMode != GameModeType.COMBAT)
            {
                message = "Not in a fight.";
                return false;
            }
            var hero = ActingHero();
            if (hero == null || !FightBag.TurnIsOpen)
            {
                message = "Not now.";
                return false;
            }
            var item = stack.Item;
            var use = FightItems.Plan(item, FightStateOf(hero), FightSceneNow());
            if (!use.Possible)
            {
                message = FightRefusalText(item, use, hero);
                return false;
            }

            var source = SourceType.INVENTORY;
            var sourceId = "estate_" + (item.Id.Length > 0 ? item.Id : item.Type);
            var guid = hero.ActorGuid;
            try
            {
                if (use.CureBleed) hero.DotContainer.RemoveAllInstancesWithTypes(BleedTypes, false, source, sourceId, guid);
                if (use.CureBlight) hero.DotContainer.RemoveAllInstancesWithTypes(BlightTypes, false, source, sourceId, guid);
                if (use.ClearHorror) hero.DotContainer.RemoveAllInstances((Predicate<DotInstance>)IsHorror, source, sourceId, guid);
                if (use.ClearDebuffs) hero.TokenContainer.RemoveAllInstances((Predicate<TokenInstance>)IsDebuff, source, sourceId, guid);
                if (use.Buffs.Count > 0)
                {
                    var lines = new List<Dd2StatLine>();
                    foreach (var buff in use.Buffs) lines.AddRange(CampingBuffMap.Lines(buff.Buff, buff.Amount, -1));
                    FightBagBuffs.Add(hero, item.Id, CampingBuffMap.Text(lines), use.Rounds);
                }
                if (use.Heal > 0) hero.ApplyHealthHeal(Mathf.Max(1f, hero.CurrentHpMax * (float)use.Heal), false, source, false);
                if (use.StressHeal > 0) hero.ApplyStressHeal((float)use.StressHeal, source);
                if (use.Light > 0) Singleton<GameTypeMgr>.Instance.RunValues.ChangeValue(RunValueType.TORCH, (float)use.Light, source);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Fight bag: " + item.Key + " could not be used on " + hero.ActorName + ": " + e);
                message = "It cannot be used.";
                return false;
            }
            if (use.Kind == FightItemKind.Food) _eaten[guid] = Eaten(guid) + 1;
            _bag.RemoveAt(slot, 1);

            message = use.Kind == FightItemKind.Torch ? "A torch is lit."
                : use.Kind == FightItemKind.Food ? hero.ActorName + " eats."
                : use.Buffs.Count > 0 ? hero.ActorName + " is fortified for " + use.Rounds + " rounds: " + InventoryText.Lower(item) + "."
                : hero.ActorName + " is treated: " + InventoryText.Lower(item) + ".";
            Say(message);
            Plugin.Log.LogInfo("Fight bag: " + hero.ActorName + " uses " + item.Key + " (" + Did(use) + ")");
            return true;
        }

        /// <summary>
        /// Dev bridge: a hallway fight where the party stands, called the way the exploration calls one (the
        /// exploration itself is told nothing of it and takes its outcome for nothing). <paramref name="ambush"/>:
        /// as a camp's night ambush, in which DD1 lets no torch be lit.
        /// </summary>
        internal string DevFight(bool ambush)
        {
            if (Held || _restored || !EstateSession.InHub || _x.Status != RaidStatus.InProgress) return "the expedition is busy";
            _ambush = ambush;
            Enqueue(new List<ExplorationEvent> { new FightStarts { Slot = new EncounterSlot { Kind = EncounterKind.Hallway, Tier = _map.Tier } } });
            return ambush ? "a night ambush is called" : "a fight is called";
        }

        private static string Did(FightItemUse use)
        {
            var parts = new List<string>();
            if (use.CureBleed) parts.Add("bleed off");
            if (use.CureBlight) parts.Add("blight off");
            if (use.ClearDebuffs) parts.Add("debuffs off");
            if (use.ClearHorror) parts.Add("horror off");
            if (use.Buffs.Count > 0) parts.Add(use.Buffs.Count + " buff(s) for " + use.Rounds + " round(s)");
            if (use.Heal > 0) parts.Add("health +" + Mathf.RoundToInt((float)(use.Heal * 100)) + "%");
            if (use.StressHeal > 0) parts.Add("stress -" + use.StressHeal.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture));
            if (use.Light > 0) parts.Add("flame +" + use.Light.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture));
            return string.Join(", ", parts);
        }
    }
}
