using System.Collections.Generic;
using DD2Estate.Dd2;

namespace DD2Estate.Estate
{
    /// <summary>
    /// What happens when a hamlet building is clicked: the screen its own system registered for it (every
    /// building's is the mod's, built from DD1's rules and art).
    /// </summary>
    internal static class Buildings
    {
        private static readonly Dictionary<string, System.Action> Handlers = new Dictionary<string, System.Action>();

        /// <summary>Gives a building its screen.</summary>
        public static void Register(string id, System.Action open)
        {
            Handlers[id] = open;
        }

        public static bool HasScreen(string id) => Handlers.ContainsKey(id);

        public static void Open(string id)
        {
            if (!EstateSession.InHub) return;
            // DD1: a building the estate has not earned yet stands boarded up and does not open
            if (BuildingLocks.Locked(id))
            {
                Plugin.Log.LogInfo("Hamlet: the " + id + " is not open yet");
                return;
            }
            if (Handlers.TryGetValue(id, out var handler)) handler();
            else Plugin.Log.LogInfo("Hamlet: " + id + " has no screen yet");
        }
    }
}
