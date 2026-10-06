using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Assets.Code.Actor;
using Assets.Code.Game;
using Assets.Code.Kingdom;
using Assets.Code.Kingdom.Events;
using Assets.Code.Library;
using Assets.Code.Platform;
using Assets.Code.Profile;
using Assets.Code.Serialization;
using Assets.Code.UI.Managers;
using Assets.Code.Utils;
using Assets.Code.Utils.Serialization;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DD2Estate.Dd2
{
    /// <summary>
    /// Saving and continuing an Estate. The game's own Kingdom snapshot (actors.json and kingdom.json in a
    /// numbered folder of the profile's kingdom slot) carries heroes, roster, inventory and the random streams;
    /// the mod adds one section of its own to kingdom.json. Snapshots are written only on the mod's request and
    /// only from the hub mode, so the newest one can always be resumed there.
    /// </summary>
    internal static class EstatePersistence
    {
        public const string SectionKey = "estate_mod";
        public const int Format = 1;

        // Becomes the folder name "Save_0001_Estate"; it is also what tells an Estate slot from a kingdom.
        private const string SnapshotLabel = "Estate";
        private const string SlotName = "Estate";

        /// <summary>The estate state to store with a snapshot. Without a hook the section that was loaded is kept.</summary>
        public static Func<JObject> SerializeEstate;

        /// <summary>Receives the stored section on a continue, once heroes, roster and inventory are back.</summary>
        public static Action<JObject> DeserializeEstate;

        /// <summary>True only while SaveNow is inside the game's save call; every vanilla save point stays blocked.</summary>
        public static bool Saving { get; private set; }

        /// <summary>The last Continue() restored a snapshot.</summary>
        public static bool Loaded { get; private set; }

        public static string LastError { get; private set; }

        /// <summary>A save is waiting for the previous one to finish. It is dropped if the hub is left first.</summary>
        public static bool Pending => _queued != null;

        public static bool InEstateProfile =>
            SaveUtils.inMods && Plugin.ProfileGuid.Value != 0 && SingletonMonoBehaviour<ProfileBhv>.HasInstance() &&
            SingletonMonoBehaviour<ProfileBhv>.Instance.GetCurrentProfileGuid() == (uint)Plugin.ProfileGuid.Value;

        /// <summary>Achievements are per Steam account, not per profile; nothing done in the Estate may earn one.</summary>
        public static bool BlocksAchievements => EstateSession.Active || EstateSession.Starting || InEstateProfile;

        private static JObject _section;
        private static string _queued;
        private static bool _flushing;
        private static readonly FieldInfo SaveNumber = AccessTools.Field(typeof(SaveUtils), "SaveNumber");

        // ---- finding the save -----------------------------------------------------------------------

        // The kingdom slots of the Estate profile, whichever profile is selected right now.
        private static string SlotsRoot
        {
            get
            {
                var guid = (uint)Plugin.ProfileGuid.Value;
                if (guid == 0) return null;
                return SaveUtils.PROFILES_SAVE_ROOT_PATH + "/" + SingletonMonoBehaviour<ProfileBhv>.Instance
                    .GetProfileRunSavesDirectoryNameByModClassification(guid, GameType.KINGDOM, isMod: true);
            }
        }

        /// <summary>Is there an Estate snapshot to continue from? Works at the main menu with any profile selected.</summary>
        public static bool HasSave()
        {
            try { return FindSnapshot(out _) != null; }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Estate: could not look for a save: " + e.Message);
                return false;
            }
        }

        /// <summary>For the Kingdoms list: is this slot of the selected profile an Estate rather than a kingdom?</summary>
        public static bool IsEstateSlot(string slot)
        {
            var dir = SaveUtils.GetRunSavesRootPath(createFolderIfMissing: false, GameType.KINGDOM) + "/" + slot;
            return Directory.Exists(dir) && Directory.GetDirectories(dir, "Save_*_" + SnapshotLabel).Length > 0;
        }

        private static string FindSnapshot(out string slot)
        {
            slot = null;
            var root = SlotsRoot;
            if (root == null || !Directory.Exists(root)) return null;
            var slots = Directory.GetDirectories(root).Where(SaveUtils.IsValidRunSaveDirectoryOrFile)
                .OrderByDescending(d => Path.GetFileName(d), StringComparer.Ordinal);
            foreach (var slotDir in slots)
            {
                foreach (var snapshot in Snapshots(slotDir).Reverse())
                {
                    if (!IsEstateSnapshot(snapshot)) continue;
                    slot = Path.GetFileName(slotDir);
                    return snapshot;
                }
            }
            return null;
        }

        private static IEnumerable<string> Snapshots(string slotDir) =>
            Directory.GetDirectories(slotDir, "Save_*").OrderBy(d => Path.GetFileName(d), StringComparer.Ordinal);

        private static bool IsEstateSnapshot(string dir)
        {
            if (!dir.EndsWith("_" + SnapshotLabel, StringComparison.Ordinal)) return false;
            var kingdom = dir + RunSaveFile.KINGDOM.m_FilePath;
            // The section is the last key of the file, so finding it also means the file was not cut short.
            return File.Exists(dir + RunSaveFile.ACTORS.m_FilePath) && File.Exists(kingdom) &&
                   File.ReadAllText(kingdom).Contains("\"" + SectionKey + "\"");
        }

        // ---- new game / continue --------------------------------------------------------------------

        /// <summary>Call before StartKingdom(isLoad: false): the first snapshot opens a new slot under this name.</summary>
        public static void StartNew()
        {
            _section = null;
            Loaded = false;
            LastError = null;
            SingletonMonoBehaviour<SaveLoadMgr>.Instance.SetKingdomNameForNewKingdom(SlotName);
        }

        /// <summary>
        /// Takes the place of StartKingdom(isLoad: false) in EstateSession.Enter() when HasSave() is true. The
        /// caller has switched to the Estate profile, set the Kingdom game type, installed the data libraries and
        /// set Active. On success (Loaded) the Kingdom-type session runs with heroes, roster statuses, inventory,
        /// random streams and the dormant day restored, and DeserializeEstate has run. Otherwise no kingdom is
        /// running (KingdomBhv.IsKingdomStarted is false) and LastError says why. The roster must not be refilled
        /// afterwards.
        /// </summary>
        public static IEnumerator Continue()
        {
            _section = null;
            Loaded = false;
            LastError = null;
            if (!Prepare(out var slot)) yield break;

            // Stepped by hand so that an exception in the game's load code ends here with a reason, instead of
            // killing the caller's coroutine with the session half-started.
            var kingdom = SingletonMonoBehaviour<KingdomBhv>.Instance;
            var start = kingdom.StartKingdom(EstateMode.Hub, null, null, isLoad: true, loadModeByCell: false);
            while (LastError == null)
            {
                object step;
                try
                {
                    if (!start.MoveNext()) break;
                    step = start.Current;
                }
                catch (Exception e)
                {
                    LastError = "the game failed while loading the Estate save: " + e;
                    break;
                }
                yield return step;
            }

            if (LastError == null) Restore(slot);
            if (Loaded) Plugin.Log.LogInfo("Estate continued from " + slot + "/" + Path.GetFileName(SaveUtils.GetRunLoadPath()));
            else Abort(kingdom);
        }

        // The same gate as the vanilla Continue button: the newest valid snapshot of the slot is picked, file
        // versions and DLC are checked, and only then is anything started.
        private static bool Prepare(out string slot)
        {
            slot = null;
            try
            {
                if (!InEstateProfile || FindSnapshot(out slot) == null)
                {
                    LastError = "there is no Estate save in the active profile";
                    return false;
                }
                Renumber(SlotsRoot + "/" + slot, inSession: false);

                var saves = SingletonMonoBehaviour<SaveLoadMgr>.Instance;
                SingletonMonoBehaviour<ProfileBhv>.Instance.GetCurrentProfile().SetCurrentKingdomSave(slot);
                saves.VerifySaveFiles();
                if (!Verified(slot))
                    LastError = "the Estate save is not valid for this game version (" + Validation(saves) + ")";
                else if (SingletonMonoBehaviour<MainMenuControllerBhv>.HasInstance() &&
                         SingletonMonoBehaviour<MainMenuControllerBhv>.Instance.ShowMissingDLCForProfileDialog(GameType.KINGDOM))
                    LastError = "a DLC this Estate was played with is not installed";
            }
            catch (Exception e) { LastError = "the Estate save could not be checked: " + e; }
            return LastError == null;
        }

        private static void Restore(string slot)
        {
            var saves = SingletonMonoBehaviour<SaveLoadMgr>.Instance;
            if (!SingletonMonoBehaviour<KingdomBhv>.Instance.IsKingdomStarted) LastError = "the game did not start its Kingdom-type session";
            else if (!Verified(slot) || _section == null) LastError = "the Estate save did not load (" + Validation(saves) + ")";
            else
            {
                try
                {
                    DeserializeEstate?.Invoke(_section);
                    Loaded = true;
                }
                catch (Exception e) { LastError = "the estate section could not be restored: " + e; }
            }
        }

        // A load that fails inside StartKingdom leaves a brand-new (or half-built) kingdom behind. Playing and
        // saving that one would bury the real save under it, so it is ended again.
        private static void Abort(KingdomBhv kingdom)
        {
            var state = AccessTools.Field(typeof(KingdomBhv), "m_KingdomInitState");
            if (state != null && Convert.ToInt32(state.GetValue(kingdom)) == 0) return;
            try { AccessTools.Method(typeof(KingdomBhv), "EndKingdom").Invoke(kingdom, null); }
            catch (Exception e)
            {
                // What EndKingdom does after destroying its managers, so that at least the menu works again.
                Plugin.Log.LogError("Estate: the half-loaded kingdom did not end cleanly, restart the game before playing on: " + (e.InnerException ?? e));
                SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance.RemoveAllLibraryElements();
                state?.SetValue(kingdom, Enum.ToObject(state.FieldType, 0));
                EventKingdomEnded.Trigger();
            }
        }

        private static bool Verified(string slot)
        {
            var path = SaveUtils.GetRunLoadPath();
            return SingletonMonoBehaviour<SaveLoadMgr>.Instance.GetValidationActionForSave() == SaveUtils.SaveValidationAction.SAVE_IS_VALID &&
                   !string.IsNullOrEmpty(path) && Path.GetFileName(Path.GetDirectoryName(path)) == slot;
        }

        private static string Validation(SaveLoadMgr saves) => saves.GetValidationActionForSave() + ", " + saves.GetFailureReasonForSave();

        /// <summary>Removes the Estate slot so the next entry starts over. Main menu only, Estate profile selected.</summary>
        public static bool DeleteSave()
        {
            if (EstateSession.Active || !InEstateProfile || FindSnapshot(out var slot) == null) return false;
            PlatformMgr.Instance.DeleteKingdomSave(slot);
            Plugin.Log.LogInfo("Estate save deleted: " + slot);
            return true;
        }

        // ---- saving ---------------------------------------------------------------------------------

        /// <summary>Writes a snapshot of the whole session, the mod's section included. Hub mode only.</summary>
        public static void SaveNow(string reason)
        {
            if (!EstateSession.InHub)
            {
                Plugin.Log.LogWarning("Estate save skipped (" + reason + "): not in the hub mode");
                return;
            }
            // The game parks a save that arrives while the previous one is still being finished and writes it a
            // few frames later, in whatever mode is current by then. It is held back here instead, so a snapshot
            // can never be taken half-way into a fight.
            if (SaveUtils.IsSaveInProgress)
            {
                _queued = reason;
                if (!_flushing) Plugin.Host.StartCoroutine(Flush());
                return;
            }
            Write(reason);
        }

        private static IEnumerator Flush()
        {
            _flushing = true;
            while (_queued != null && EstateSession.Active)
            {
                if (EstateSession.InHub && !SaveUtils.IsSaveInProgress)
                {
                    var reason = _queued;
                    _queued = null;
                    Write(reason);
                }
                yield return null;
            }
            _queued = null;
            _flushing = false;
        }

        private static void Write(string reason)
        {
            var before = SaveUtils.GetRunLoadPath();
            if (!string.IsNullOrEmpty(before) && SaveNumber != null && (int)SaveNumber.GetValue(null) >= RenumberFrom)
            {
                Renumber(Path.GetDirectoryName(before), inSession: true);
                before = SaveUtils.GetRunLoadPath();
            }

            Saving = true;
            try { SaveUtils.SaveCurrentGameMode(SnapshotLabel); }
            catch (Exception e) { Plugin.Log.LogError("Estate save (" + reason + ") failed: " + e); }
            finally { Saving = false; }

            // The game logs and swallows errors of the write itself; the load path only moves on after a full one.
            var snapshot = SaveUtils.GetRunLoadPath();
            if (snapshot != before && File.Exists(snapshot + RunSaveFile.KINGDOM.m_FilePath))
                Plugin.Log.LogInfo("Estate saved (" + reason + "): " + Path.GetFileName(snapshot));
            else
                Plugin.Log.LogError("Estate save (" + reason + ") wrote no snapshot. Either the SaveCurrentGameMode patch does not let " +
                                    "EstatePersistence.Saving through, or Player.log has 'Save sync operations failed'.");
        }

        private const int RenumberFrom = 9000;

        // Snapshot folders are "Save_" plus four digits, and the game orders and parses them as text: a fifth digit
        // would sort below the older folders and be trimmed away as the oldest. Long before that the few folders
        // that are left are counted from 1 again, oldest first, so a failure half-way keeps their order.
        private static void Renumber(string slotDir, bool inSession)
        {
            try
            {
                var names = Snapshots(slotDir).Select(d => Path.GetFileName(d)).Where(n => Number(n) > 0).ToList();
                if (names.Count == 0 || Number(names[names.Count - 1]) < RenumberFrom) return;
                var last = "";
                for (var i = 0; i < names.Count; i++)
                {
                    last = "Save_" + (i + 1).ToString("D4") + names[i].Substring(9);
                    if (last != names[i]) Directory.Move(Path.Combine(slotDir, names[i]), Path.Combine(slotDir, last));
                }
                if (inSession)
                {
                    SaveNumber.SetValue(null, names.Count);
                    SaveUtils.SetRunLoadPath(Path.Combine(slotDir, last));
                }
                Plugin.Log.LogInfo("Estate: snapshot folders renumbered, " + names.Count + " kept");
            }
            catch (Exception e) { Plugin.Log.LogWarning("Estate: snapshot folders were not renumbered: " + e.Message); }
        }

        private static int Number(string snapshotName) =>
            snapshotName.Length > 9 && int.TryParse(snapshotName.Substring(5, 4), out var number) ? number : 0;

        // ---- the mod's section of kingdom.json ------------------------------------------------------

        internal static void AddSection(JObject kingdomJson)
        {
            // An exception from the hook is left to abort the game's save: a snapshot without kingdom.json is
            // skipped on load, which is better than one whose estate part is stale or missing.
            var section = SerializeEstate?.Invoke() ?? (JObject)_section?.DeepClone() ?? new JObject();
            if (section["format"] == null) section["format"] = Format;
            kingdomJson[SectionKey] = section;
        }

        internal static void ReadSection(JObject kingdomJson) => _section = kingdomJson?[SectionKey] as JObject;

        public static object Describe()
        {
            string slot = null, snapshot = null;
            try { snapshot = FindSnapshot(out slot); }
            catch (Exception e) { LastError = e.Message; }
            return new
            {
                hasSave = snapshot != null,
                slot,
                snapshot = snapshot != null ? Path.GetFileName(snapshot) : null,
                saveNumber = SaveNumber?.GetValue(null),
                inProgress = SaveUtils.IsSaveInProgress,
                pending = _queued,
                loaded = Loaded,
                section = _section?.ToString(Formatting.None),
                lastError = LastError
            };
        }
    }
}
