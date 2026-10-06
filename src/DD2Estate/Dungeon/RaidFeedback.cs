using System;
using Assets.Code.Actor;
using Assets.Code.Actor.Events;
using Assets.Code.Combat.Events;
using Assets.Code.Events;
using Assets.Code.Library;
using Assets.Code.Quirk;
using Assets.Code.Quirk.Events;
using Assets.Code.Utils;
using DD2Estate.Dd2;
using DD2Estate.Estate;
using UnityEngine;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// What happens to the party's heroes while the dungeon's screen is up, told the DD1 way: a number or a
    /// word that pops up over the hero (<see cref="RaidPopText"/>), and for a few moments DD1's banner
    /// (<see cref="RaidAnnouncement"/>).
    ///
    /// The game says so itself: every change of a hero's health or stress, every quirk that comes or goes and
    /// the step onto death's door raises an event of DD2's, whoever caused it. So a trap, hunger, a curio, a
    /// camping skill, a meal and an item from the bag all show without a line of their own, and so will
    /// whatever the expedition learns to do next. An expedition listens from its start to its end
    /// (<see cref="DungeonRun"/>); nothing is shown while the hub is not the game's mode (a fight is DD2's own
    /// screen with its own numbers) or for anybody who has no tray on the dungeon's screen.
    ///
    /// The banners with a moment of their own (a trap, a disarming, an ambush, a surprise) are asked for where
    /// the expedition decides them.
    /// </summary>
    internal static class RaidFeedback
    {
        private static bool _listening;

        public static bool Listening => _listening;

        public static void Listen()
        {
            if (_listening) return;
            _listening = true;
            EventManager.AddListener<EventActorHealthDamage>(OnDamage);
            EventManager.AddListener<EventActorHealthHeal>(OnHeal);
            EventManager.AddListener<EventStressDamage>(OnStress);
            EventManager.AddListener<EventStressHeal>(OnStressHeal);
            EventManager.AddListener<EventQuirkAdded>(OnQuirkAdded);
            EventManager.AddListener<EventQuirkRemoved>(OnQuirkRemoved);
            EventManager.AddListener<EventActorStatusChanged>(OnStatus);
            EventManager.AddListener<EventActorSurviveDeathsDoor>(OnSurvived);
            EventManager.AddListener<EventActorDeath>(OnDeath);
        }

        public static void Stop()
        {
            if (!_listening) return;
            _listening = false;
            EventManager.RemoveListener<EventActorHealthDamage>(OnDamage);
            EventManager.RemoveListener<EventActorHealthHeal>(OnHeal);
            EventManager.RemoveListener<EventStressDamage>(OnStress);
            EventManager.RemoveListener<EventStressHeal>(OnStressHeal);
            EventManager.RemoveListener<EventQuirkAdded>(OnQuirkAdded);
            EventManager.RemoveListener<EventQuirkRemoved>(OnQuirkRemoved);
            EventManager.RemoveListener<EventActorStatusChanged>(OnStatus);
            EventManager.RemoveListener<EventActorSurviveDeathsDoor>(OnSurvived);
            EventManager.RemoveListener<EventActorDeath>(OnDeath);
        }

        // The dungeon's screen is up and this is one of the heroes on it. The cheap questions come first: in a
        // fight every blow raises these events.
        private static bool Watching(uint guid)
        {
            return DungeonRun.Current != null && EstateSession.InHub && EstateSession.View == EstateSession.Screen.Dungeon && DungeonHud.Shows(guid);
        }

        // DD1 writes a whole number, without a sign: the colour says what it is.
        private static string Number(float amount) => Mathf.Max(1, Mathf.RoundToInt(amount)).ToString();

        // A listener must never throw into the game's own event: it is the game that raised it.
        private static void Pop(uint guid, RaidPop kind, string text = null)
        {
            try { DungeonHud.Pop(guid, kind, text); }
            catch (Exception e) { Plugin.Log.LogWarning("Dungeon HUD: no pop text (" + kind.Kind + "): " + e.Message); }
        }

        private static void Announce(RaidAnnounce what, string subject)
        {
            try { DungeonHud.Announce(what, subject); }
            catch (Exception e) { Plugin.Log.LogWarning("Dungeon HUD: no banner (" + what.Key + "): " + e.Message); }
        }

        private static void OnDamage(EventActorHealthDamage e)
        {
            if (!Watching(e.m_ActorGuid)) return;
            // a blow to a hero at death's door takes no health: DD1 writes how it ended instead (OnSurvived, OnDeath)
            if (e.m_HealthDamageCalculation != null && e.m_HealthDamageCalculation.m_IsAtDeathsDoor) return;
            Pop(e.m_ActorGuid, RaidPop.Damage, Number(e.m_HealthDamage));
        }

        private static void OnHeal(EventActorHealthHeal e)
        {
            if (e.m_HealthChange < 0.5f || !Watching(e.m_ActorGuid)) return;
            Pop(e.m_ActorGuid, RaidPop.Heal, Number(e.m_HealthChange));
        }

        // DD2's stress runs 0..10 and the trays show it so: the number is DD2's points.
        private static void OnStress(EventStressDamage e)
        {
            if (e.m_StressDamageAmount < 0.5f || !Watching(e.m_ActorGuid)) return;
            Pop(e.m_ActorGuid, RaidPop.Stress, Number(e.m_StressDamageAmount));
        }

        private static void OnStressHeal(EventStressHeal e)
        {
            if (e.m_StressHealAmount < 0.5f || !Watching(e.m_ActorGuid)) return;
            Pop(e.m_ActorGuid, RaidPop.StressHeal, Number(e.m_StressHealAmount));
        }

        // The quirk's definition and its name without the game's colours; false when the game cannot say.
        private static bool Quirk(uint guid, string id, out QuirkDefinition quirk, out string name)
        {
            quirk = null;
            name = null;
            try
            {
                var library = SingletonMonoBehaviour<Library<string, QuirkDefinition>>.Instance;
                if (library == null || string.IsNullOrEmpty(id) || !library.TryGetLibraryElement(id, out quirk) || quirk == null) return false;
                var hero = SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance.GetLibraryElement(guid);
                name = TrayIcons.Bare(SanitariumRules.QuirkName(quirk, hero));
                return name.Length > 0;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Dungeon HUD: the quirk " + id + " has no name to announce: " + e.Message);
                return false;
            }
        }

        // DD1: "New Quirk:" and its name, in the colour of a good or a bad one.
        private static void OnQuirkAdded(EventQuirkAdded e)
        {
            if (!Watching(e.m_ActorGuid) || !Quirk(e.m_ActorGuid, e.m_QuirkId, out var quirk, out var name)) return;
            Announce(quirk.IsNegative || quirk.IsDisease || quirk.IsCurse ? RaidAnnounce.NewBadQuirk : RaidAnnounce.NewQuirk, name);
        }

        // DD1: "%s Quirk Removed!" in its banner for a quirk a curio takes; a disease that is cured is written
        // over the hero ("%s Cured!", str_ui_disease_cured: the camp's cure).
        private static void OnQuirkRemoved(EventQuirkRemoved e)
        {
            if (!Watching(e.m_ActorGuid) || !Quirk(e.m_ActorGuid, e.m_QuirkId, out var quirk, out var name)) return;
            if (quirk.IsDisease && !quirk.IsCurse) Pop(e.m_ActorGuid, RaidPop.Cured, RaidText.Format(RaidText.Get("str_ui_disease_cured", "%s Cured!"), name));
            else Announce(RaidAnnounce.QuirkRemoved, name);
        }

        // DD1: "%s is at Death's Door!"
        private static void OnStatus(EventActorStatusChanged e)
        {
            if (!e.m_IsActive || e.m_ActorStatusType != ActorStatusType.DEATHS_DOOR || !Watching(e.m_ActorGuid)) return;
            var hero = SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance.GetLibraryElement(e.m_ActorGuid);
            if (hero != null) Announce(RaidAnnounce.DeathsDoor, hero.ActorName);
        }

        // DD1 writes "Death's Door!" over a hero who takes a blow there and lives, "DEATHBLOW!" over one who does not.
        private static void OnSurvived(EventActorSurviveDeathsDoor e)
        {
            if (Watching(e.m_ActorGuid)) Pop(e.m_ActorGuid, RaidPop.DeathAvoided);
        }

        private static void OnDeath(EventActorDeath e)
        {
            if (Watching(e.m_DyingActorGuid)) Pop(e.m_DyingActorGuid, RaidPop.Deathblow);
        }
    }
}
