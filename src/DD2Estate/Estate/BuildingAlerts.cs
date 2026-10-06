using System;
using System.Collections.Generic;
using System.Linq;
using DD2Estate.Dd2;
using DD2Estate.Dev;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DD2Estate.Estate
{
    /// <summary>
    /// Which buildings of the hamlet carry DD1's alert: the pulsing red mark over a building that has news for
    /// the player (fx/estate_exclamation, drawn by <see cref="HamletScene"/> at the building's alert_offset).
    /// DD1 keeps what the player has seen in a "novelty tracker" of its save (its exe names the file beside the
    /// mark's sprite); what counts as news is in none of its files.
    ///
    /// THE MOD'S READING, off a real frame of a DD1 hamlet in its second week: the tavern and the abbey (just
    /// opened), the graveyard and the statue carry the mark; the stage coach, which DD1's first week has the
    /// player enter, and the buildings still boarded up do not. So a building that stands open and has not been
    /// entered since it opened carries the mark, and going in takes it off. A system with news of its own (new
    /// recruits, new wares, a new grave) can put it back with <see cref="Raise"/>; none does yet.
    ///
    /// What has been entered is kept in the estate's save; an estate saved before this existed has seen its town.
    /// </summary>
    [EstateModule]
    internal static class BuildingAlerts
    {
        private const string SectionKey = "building_alerts";
        private const float CheckSeconds = 0.25f;

        private static readonly HashSet<string> Seen = new HashSet<string>();

        private static void Register()
        {
            EstateState.RegisterSection(SectionKey, () => new JObject { ["seen"] = new JArray(Seen.OrderBy(id => id)) }, Load, () => Seen.Clear());

            var go = new GameObject("DD2Estate.BuildingAlerts") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Watcher>();

            // Which buildings carry the mark. {"raise":"tavern"} or {"raise":"all"}: news there; {"see":"tavern"} or
            // {"see":"all"}: as if the player had been in.
            AgentBridge.Register("alerts.state", o =>
            {
                var raise = (string)o["raise"];
                if (raise == "all") Seen.Clear();
                else if (raise != null) Raise(raise);
                var see = (string)o["see"];
                if (see == "all") Seen.UnionWith(HamletScene.BuildingIds);
                else if (see != null) Seen.Add(see);
                return new { buildings = HamletScene.BuildingIds.Select(id => new { id, seen = Seen.Contains(id), locked = BuildingLocks.Locked(id), alert = On(id) }).ToArray() };
            });
        }

        private static void Load(JToken json)
        {
            Seen.Clear();
            // no section: an estate from before the alerts, whose player knows the town
            if (json == null) Seen.UnionWith(HamletScene.BuildingIds);
            else if (json["seen"] is JArray seen)
                foreach (var id in seen) Seen.Add((string)id);
        }

        /// <summary>The building carries the mark: it is open and has news the player has not gone in to see.</summary>
        public static bool On(string building) => !Seen.Contains(building) && !BuildingLocks.Locked(building);

        /// <summary>The building has news: it carries the mark until the player goes in.</summary>
        public static void Raise(string building) => Seen.Remove(building);

        private class Watcher : MonoBehaviour
        {
            private float _next;

            private void Update()
            {
                if (Time.unscaledTime < _next) return;
                _next = Time.unscaledTime + CheckSeconds;
                if (!EstateSession.InHub || EstateSession.View != EstateSession.Screen.Hamlet) return;
                // every building's screen is a window that carries the building's name
                var window = RosterWindow.Current;
                if (window != null && Array.IndexOf(HamletScene.BuildingIds, window.Id) >= 0) Seen.Add(window.Id);
            }
        }
    }
}
