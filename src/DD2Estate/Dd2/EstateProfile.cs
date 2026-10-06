using Assets.Code.Profile;
using Assets.Code.Utils;
using Assets.Code.Utils.Serialization;

namespace DD2Estate.Dd2
{
    /// <summary>
    /// The Estate plays in its own profile on the game's mod-profile list, so vanilla profiles and their saves
    /// are never written. Entering the Estate switches to that profile; leaving restores what was selected.
    /// The player's previous selection is kept in the plugin config, so it survives a crash inside the Estate.
    /// </summary>
    internal static class EstateProfile
    {
        private const string ProfileName = "Estate";
        private static bool _active;
        private static bool _previousInMods;

        public static bool Activate()
        {
            if (_active) return true;
            var profiles = SingletonMonoBehaviour<ProfileBhv>.Instance;
            _previousInMods = SaveUtils.inMods;
            ToList(profiles, true);

            var estateGuid = (uint)Plugin.ProfileGuid.Value;
            var current = profiles.GetNumberOfProfiles() > 0 ? profiles.GetCurrentProfileGuid() : 0u;
            if (current != 0 && current != estateGuid) Plugin.PreviousModProfile.Value = (int)current;

            var profile = estateGuid != 0 ? profiles.GetProfileByGUID(estateGuid) : null;
            if (profile == null || profile.GetName() != ProfileName)
            {
                if (profiles.GetNumberOfProfiles() >= profiles.m_MaxNumProfiles)
                {
                    Plugin.Log.LogError("The mod-profile list is full (" + profiles.m_MaxNumProfiles + "). Remove one mod profile so the Estate can create its own.");
                    Restore();
                    return false;
                }
                profile = profiles.CreateProfile(ProfileName);
                Plugin.ProfileGuid.Value = (int)profile.ProfileGuid;
                Plugin.Log.LogInfo("created the Estate mod profile #" + profile.ProfileGuid);
            }
            profiles.SetCurrentProfile(profile.ProfileGuid);
            _active = true;
            return true;
        }

        public static void Deactivate()
        {
            if (!_active) return;
            _active = false;
            Restore();
        }

        /// <summary>At the main menu outside a session: undo a profile switch left behind by a crash or kill.</summary>
        public static void RecoverIfNeeded()
        {
            if (_active || Plugin.PreviousModProfile.Value == 0) return;
            _previousInMods = SaveUtils.inMods;
            Restore();
        }

        private static void Restore()
        {
            var profiles = SingletonMonoBehaviour<ProfileBhv>.Instance;
            var previous = (uint)Plugin.PreviousModProfile.Value;
            if (previous != 0)
            {
                // Profile files are written per list, so the mod list has to be the active one while its
                // selection is put back (the main menu may already have switched to the regular list).
                ToList(profiles, true);
                if (profiles.GetProfileByGUID(previous) != null)
                {
                    profiles.SetCurrentProfile(previous);
                    Plugin.Log.LogInfo("restored mod profile #" + previous + " as the selected one");
                }
                Plugin.PreviousModProfile.Value = 0;
            }
            ToList(profiles, _previousInMods);
            // (RefreshCurrentProfile picks a profile only where the list's own choice is gone; a list that had
            // none chosen when the game came up is left as the game had it)
            if (profiles.GetNumberOfProfiles() > 0 && profiles.GetCurrentProfileGuid() != 0) profiles.RefreshCurrentProfile();
        }

        // The game tells its listeners of a list's chosen profile as it switches to that list, and its narration
        // manager falls over when the list has none chosen (the mod list came up so on 2026-10-05, current guid 0
        // with nine profiles in it: SaveUtils.LoadNarrationForProfile on a null profile). The switch itself is done
        // by then (the list and its guids are set before the listeners are told); whoever called goes on to
        // choose a profile, which tells the listeners properly.
        private static void ToList(ProfileBhv profiles, bool inMods)
        {
            try { profiles.OnProfileSourceChange(inMods); }
            catch (System.NullReferenceException)
            {
                Plugin.Log.LogWarning("The " + (inMods ? "mod" : "regular") + " profile list has no profile chosen: its listeners could not be told of the switch");
            }
        }
    }
}
