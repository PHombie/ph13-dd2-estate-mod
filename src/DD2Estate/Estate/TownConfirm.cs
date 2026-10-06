using System;
using Assets.Code.UI.Managers;
using Assets.Code.Utils;
using DD2Estate.Dungeon;
using DD2Estate.UI;
using UnityEngine;

namespace DD2Estate.Estate
{
    /// <summary>
    /// DD1's confirm dialog on the town's side: the one way the hamlet asks a question. The dialog itself is
    /// the raid's (<see cref="RaidConfirm"/>: shared/confirm_dialog, the question over its answers, each a line
    /// that lights up under the pointer); this stands it in the middle of the hamlet's canvas over everything
    /// else, with a shade that takes every click meant for what lies behind, and takes it away with the hamlet.
    /// One question at a time: a new one replaces the one that stands.
    ///
    ///     TownConfirm.Ask(question, "Yes", "No", () => Sell(id));               // "No" and a closed hamlet do nothing
    ///     TownConfirm.Ask(this, question, new[] { "Still Embark", "Cancel Embark" }, answer => ...);
    ///     TownConfirm.Tell("You need at least 1 hero to venture on. ...", "OK");
    ///
    /// The Estate Map's and the provision screen's questions, a trinket's sale, "unequip all", a treatment
    /// called off, a hero dismissed and the town crier's choice all come through here. Where the hamlet's
    /// canvas is not to be had the game's own dialog asks instead, so that no question is lost.
    /// </summary>
    internal static class TownConfirm
    {
        private const float ScreenWidth = 1920f, ScreenHeight = 1080f;

        private static RectTransform _root;
        private static RaidConfirm _dialog;
        private static object _owner;
        private static string _question;
        private static string[] _answers;
        private static Action<int> _answered;

        public static bool IsOpen => _root != null && _dialog != null && _dialog.IsOpen;

        /// <summary>Whether the question that stands is this owner's.</summary>
        public static bool IsOpenFor(object owner) => IsOpen && ReferenceEquals(_owner, owner);

        /// <summary>A question with two answers; <paramref name="onYes"/> runs on the first, <paramref name="onNo"/> (if any) on the second.</summary>
        public static void Ask(string question, string yes, string no, Action onYes, Action onNo = null)
        {
            Ask(null, question, new[] { yes, no }, answer =>
            {
                if (answer == 0) onYes?.Invoke();
                else onNo?.Invoke();
            });
        }

        /// <summary>Something DD1 says in its dialog with one answer to send it away (<paramref name="ok"/>: DD1's "OK" unless given).</summary>
        public static void Tell(string message, string ok = null, Action then = null)
        {
            Ask(null, message, new[] { ok ?? WindowText.Plain("confirm_ok") ?? "OK" }, answer => then?.Invoke());
        }

        /// <summary>
        /// A question with any answers; <paramref name="answered"/> gets the index of the one picked.
        /// <paramref name="owner"/>: who asks, for a screen that closes its own question when it goes.
        /// </summary>
        public static void Ask(object owner, string question, string[] answers, Action<int> answered)
        {
            Close();
            var canvas = RosterPanel.CanvasRoot;
            if (canvas == null || !HamletScreen.IsOpen)
            {
                AskTheGame(question, answers, answered);
                return;
            }
            try
            {
                _root = UiKit.Stretch(UiKit.Rect("TownConfirm", canvas));
                _root.gameObject.AddComponent<TownConfirmLayer>();
                // DD1's screen in the middle of the canvas, as the hamlet stands on a wider one
                var middle = new Vector2(0.5f, 0.5f);
                var frame = UiKit.Rect("Frame", _root).Place(middle, middle, Vector2.zero, new Vector2(ScreenWidth, ScreenHeight));
                _dialog = new RaidConfirm(frame);
                _owner = owner;
                _question = question;
                _answers = answers;
                _answered = answered;
                _dialog.Ask(question, answers, Finish);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Hamlet: DD1's dialog could not be shown, the game's own asks: " + e);
                Close();
                AskTheGame(question, answers, answered);
            }
        }

        /// <summary>Takes the question away unanswered.</summary>
        public static void Close()
        {
            var root = Forget();
            if (root == null) return;
            root.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(root.gameObject);
        }

        private static RectTransform Forget()
        {
            var root = _root;
            _root = null;
            _dialog = null;
            _owner = null;
            _answered = null;
            _question = null;
            _answers = null;
            return root;
        }

        /// <summary>Takes this owner's question away, if it is the one that stands.</summary>
        public static void Close(object owner)
        {
            if (IsOpenFor(owner)) Close();
        }

        /// <summary>For the test bridge: what a click on an answer does. False when no question stands or it has no such answer.</summary>
        public static bool Answer(int index)
        {
            if (!IsOpen || _answers == null || index < 0 || index >= _answers.Length) return false;
            Finish(index);
            return true;
        }

        public static object Snapshot()
        {
            return IsOpen ? new { open = true, question = _question, answers = _answers } : (object)new { open = false };
        }

        private static void Finish(int index)
        {
            var answered = _answered;
            Close();
            try { answered?.Invoke(index); }
            catch (Exception e) { Plugin.Log.LogError("Hamlet: an answer to DD1's dialog failed: " + e); }
        }

        // The hamlet's canvas is away (a question asked from a game screen): the game's own dialog, with the
        // first two answers on its two buttons.
        private static void AskTheGame(string question, string[] answers, Action<int> answered)
        {
            var ui = SingletonMonoBehaviour<CommonUiBhv>.Instance;
            if (ui == null || answers == null || answers.Length == 0) return;
            ui.ShowConfirmationDialog(CommonUiBhv.ConfirmationDialogType.HotkeyCloseable, "", question,
                () => answered?.Invoke(0), answers[0],
                () => { if (answers.Length > 1) answered?.Invoke(1); }, answers.Length > 1 ? answers[1] : answers[0]);
        }

        /// <summary>For the layer: the hamlet went away under the question.</summary>
        internal static void Gone(GameObject layer)
        {
            if (_root == null || _root.gameObject != layer) return;
            Forget();
            UnityEngine.Object.Destroy(layer);
        }
    }

    /// <summary>Keeps DD1's dialog over everything the hamlet draws, and takes it away when the hamlet is hidden.</summary>
    internal class TownConfirmLayer : MonoBehaviour
    {
        private void LateUpdate()
        {
            var parent = transform.parent;
            if (parent != null && transform.GetSiblingIndex() != parent.childCount - 1) transform.SetAsLastSibling();
        }

        // The hamlet is hidden by deactivating its canvas; a question does not wait for it to come back.
        private void OnDisable() => TownConfirm.Gone(gameObject);
    }
}
