using System;
using System.Collections;
using Assets.Code.UI.Managers;
using Assets.Code.Utils;
using DD2Estate.Dd2;

namespace DD2Estate.Menu
{
    /// <summary>
    /// What the main menu's "Estate" entry does. DD1 keeps one estate per save slot and lets the player start
    /// another; the mod keeps one estate in its own profile, so with an estate already founded the entry asks:
    /// continue it, or abandon it and found a new one. Abandoning is asked twice, and a dialog closed with its
    /// hotkey never abandons anything.
    ///
    /// Before any of that: without a Darkest Dungeon (1) to read from there is no Estate to enter, and the
    /// entry asks for its folder (<see cref="Dd1Prompt"/>). With a right folder it goes on as if pressed again.
    /// </summary>
    internal static class EstateMenu
    {
        public static void Clicked()
        {
            if (EstateSession.Active || EstateSession.Starting) return;
            if (!Dd1Prompt.Have(Proceed)) return;
            Proceed();
        }

        private static void Proceed()
        {
            if (EstateSession.Active || EstateSession.Starting) return;
            bool founded;
            try { founded = HasEstate(); }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Estate menu: could not look for a saved estate: " + e.Message);
                founded = false;
            }
            if (!founded)
            {
                Enter();
                return;
            }
            Ask("The Estate",
                "Your estate awaits its heir. Return to it, or abandon it and found a new one?",
                Enter, "Continue",
                () => Later(AskAbandon), "New Estate");
        }

        private static void AskAbandon()
        {
            Ask("Abandon the estate?",
                "The estate, its heroes and all they brought home are lost for good.",
                () =>
                {
                    Abandon();
                    Enter();
                }, "Abandon it",
                () => { }, "Keep it");
        }

        // The saved estate lives in the mod's own profile: look there, then hand the player's profile back.
        private static bool HasEstate()
        {
            if (!EstateProfile.Activate()) return false;
            try { return EstatePersistence.HasSave(); }
            finally { EstateProfile.Deactivate(); }
        }

        private static void Abandon()
        {
            if (!EstateProfile.Activate()) return;
            try
            {
                Plugin.Log.LogInfo("Estate menu: the estate is abandoned (" + (EstatePersistence.DeleteSave() ? "save deleted" : "there was no save") + ")");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Estate menu: the estate could not be abandoned: " + e);
            }
            finally { EstateProfile.Deactivate(); }
        }

        private static void Enter() => Plugin.Host.StartCoroutine(EstateSession.Enter());

        private static void Ask(string title, string body, Action yes, string yesText, Action no, string noText)
        {
            SingletonMonoBehaviour<CommonUiBhv>.Instance.ShowConfirmationDialog(
                CommonUiBhv.ConfirmationDialogType.HotkeyCloseable, title, body, yes, yesText, no, noText);
        }

        // A dialog cannot open from inside the closing of another: wait for the frame after.
        internal static void Later(Action action) => Plugin.Host.StartCoroutine(NextFrame(action));

        private static IEnumerator NextFrame(Action action)
        {
            yield return null;
            yield return null;
            action();
        }
    }
}
