using System;
using System.Collections.Generic;
using System.Text;
using DD2Estate.Core;
using DD2Estate.Dd1;
using DD2Estate.Dd2;
using DD2Estate.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD2Estate.Estate
{
    /// <summary>
    /// DD1's "Trade Heirlooms" panel, on DD1's layout (campaign/town/heirloom_exchange/
    /// heirloom_exchange.layout.darkest, its art beside it): a small panel that slides up from behind the
    /// estate's bar, over the heirloom counts (town.layout.darkest heirloom_exchange_pos). On its left what is
    /// given: the heirloom, picked with DD1's arrows above and below its icon, and how many, with the arrows
    /// above and below the number. On its right what can be had for that, one framed row per rate in the order
    /// DD1's rules list them, each with its icon, its amount and DD1's confirm button; a bent arrow runs from
    /// the heirloom given to every row. A row that cannot be taken (the rate does not divide the amount, the
    /// estate cannot pay) wears DD1's invalid frame, whose art has the no-entry sign where the button would be.
    /// As in DD1 nothing here has a tooltip (its files have neither a place nor words for one), and the panel
    /// has DD1's three sounds: coming up, going down, a trade (/ui/town/heirloom_exchange_open, _close, _confirm).
    ///
    /// How the layout's numbers are read (the file does not say what they count from) is argued in
    /// docs/recon/dd1-town-panels.md. tools/preview_town_panels.py draws the same panel offline: change one,
    /// change the other.
    /// </summary>
    internal class HeirloomExchangePanel : MonoBehaviour
    {
        private const string Art = "campaign/town/heirloom_exchange/heirloom_exchange";
        private const string LayoutFile = Art + ".layout.darkest";
        private const string AnimFile = Art + ".anim.darkest";
        private const string TownLayout = "campaign/town/town.layout.darkest";
        private const string IconDir = "shared/estate/";

        // Sizes of DD1's art, for the boxes that stand in when a file is missing.
        private static readonly Vector2 PanelSize = new Vector2(429f, 268f);     // heirloom_exchange.background.png
        private static readonly Vector2 ArrowSize = new Vector2(32f, 30f);       // .arrow_up.png, .arrow_down.png
        private static readonly Vector2 GlowSize = new Vector2(52f, 52f);        // .selected_overlay.png
        private static readonly Vector2 IconSize = new Vector2(40f, 40f);        // currency.<heirloom>.icon.png
        private static readonly Vector2 FrameSize = new Vector2(189f, 56f);      // .frame.png, .frame_invalid.png
        private static readonly Vector2 ConfirmSize = new Vector2(48f, 24f);     // .confirm.png
        private static readonly Vector2 LinkSize = new Vector2(72f, 180f);       // .arrow_0.png .. .arrow_3.png
        private const int Links = 4;
        // The mod's own: the amount's box.
        private static readonly Vector2 AmountBox = new Vector2(44f, 30f);
        private static readonly Vector2 TakesBox = new Vector2(58f, 30f);

        private static HeirloomExchangePanel _instance;
        private static string _lastKind;

        private TownPanel _panel;
        private RectTransform _slide, _rows;
        private Image _kindIcon;
        private TextMeshProUGUI _amountLabel;
        private Button _amountUp, _amountDown;
        private Vector2 _rowStart, _rowPitch, _linkAt, _frameAt, _invalidAt, _iconAt, _takesAt, _confirmAt;
        private float _hidden, _slideIn, _slideOut, _pulseOut, _pulseIn;
        private Vector2 _pulseScale;
        private float _opened, _closing = -1f, _pulsed = -1f;
        private readonly List<Transform> _pulsing = new List<Transform>();
        private readonly Dictionary<string, int> _takes = new Dictionary<string, int>();
        private string _kind, _shown;
        private int _amount;
        private bool _closeHeard;

        public static bool IsOpen => _instance != null && TownPanel.Current != null && TownPanel.Current == _instance._panel;

        /// <summary>The bar's button: up, or back down behind the bar.</summary>
        public static void Toggle()
        {
            if (IsOpen) _instance.SlideAway();
            else Open();
        }

        /// <param name="kind">The heirloom to give; null: the one last traded, or the first the estate can trade.</param>
        public static bool Open(string kind = null)
        {
            var panel = TownPanel.Open(HeirloomExchange.Id, false);
            if (panel == null) return false;
            var view = panel.gameObject.AddComponent<HeirloomExchangePanel>();
            _instance = view;
            view._panel = panel;
            view.Build(kind);
            panel.Refresh = view.Refresh;
            panel.Closed = view.CloseSound;
            view.Refresh();
            EstateAudio.Ui("ui/town/heirloom_exchange_open");
            return true;
        }

        public static void Close()
        {
            if (IsOpen) TownPanel.Close();
        }

        private void OnEnable() => HeirloomExchange.Traded += OnTraded;

        private void OnDisable() => HeirloomExchange.Traded -= OnTraded;

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        // ---- construction --------------------------------------------------------------------------------

        private void Build(string kind)
        {
            // FALLBACK numbers: DD1's own values of these entries, used when a file cannot be read.
            var town = Dd1Ui.Layout(TownLayout);
            var layout = Dd1Ui.Layout(LayoutFile);
            var anim = Dd1Ui.Layout(AnimFile);
            const string main = "heirloom_exchange_layout", from = "heirloom_exchange_heirloom_from_layout", to = "heirloom_exchange_heirloom_to_layout";
            var at = Dd1Ui.Offset(town, "town_screen_layout", "heirloom_exchange_pos", 340f, 708f);
            _slideIn = Mathf.Max(0.01f, Dd1Ui.Number(anim, "heirloom_exchange_transition_animation", "transition_in_time", 0.4f));
            _slideOut = Mathf.Max(0.01f, Dd1Ui.Number(anim, "heirloom_exchange_transition_animation", "transition_out_time", 0.4f));
            // DD1 spells the block "pusle".
            const string pulse = "heirloom_exchange_to_amount_change_pusle";
            _pulseOut = Mathf.Max(0.01f, Dd1Ui.Number(anim, pulse, "pulse_out_duration", 0.25f));
            _pulseIn = Mathf.Max(0.01f, Dd1Ui.Number(anim, pulse, "pulse_in_duration", 0.125f));
            _pulseScale = Dd1Ui.Offset(anim, pulse, "pulse_scale", 1.25f, 1.25f);

            var part = _panel.AddPart("Exchange", at, PanelSize, TownPanel.Side.Left);
            // The panel comes up from behind the bar: it starts wholly below the bar's top edge.
            _hidden = Mathf.Max(0f, _panel.BarTop - at.y);
            _slide = UiKit.Rect("Slide", part).PlaceTopLeft(new Vector2(0f, _hidden), Dd1Ui.TopLeft, PanelSize);
            _opened = Time.unscaledTime;

            var back = Dd1Ui.Art("Back", _slide, Art + ".background.png", Vector2.zero, PanelSize, new Color(0.03f, 0.025f, 0.035f, 0.97f), true);
            TownPanel.RightClickCloses(back);
            _panel.BackOut = () =>
            {
                SlideAway();
                return true;
            };

            var title = Dd1Ui.Line("Title", _slide, "town_heirloom_exchange_title", Dd1Ui.Offset(layout, main, "title_pos", 215f, 24f), new Vector2(320f, 30f), TextAlignmentOptions.Top);
            title.text = HeirloomExchange.ScreenName;

            // What is given: two columns of DD1's "choice", the heirloom and the number. Its offsets count from
            // the middle of a column and from its top (the art's glow lies under the icon read so).
            var column = Dd1Ui.Offset(layout, main, "heirloom_from_pos", 0f, 0f) + Dd1Ui.Offset(layout, from, "choice_start_offset", 79f, 110f);
            var next = column + Dd1Ui.Offset(layout, from, "choice_spacing", 44f, 0f);
            var up = Dd1Ui.Offset(layout, from, "arrow_up_offset", 0f, -10f);
            var upGlow = Dd1Ui.Offset(layout, from, "arrow_up_selected_overlay_offset", -26f, -18f);
            var down = Dd1Ui.Offset(layout, from, "arrow_down_offset", 0f, 84f);
            var downGlow = Dd1Ui.Offset(layout, from, "arrow_down_selected_overlay_offset", -26f, 70f);
            var icon = Dd1Ui.Offset(layout, from, "icon_offset", 0f, 32f);
            var text = Dd1Ui.Offset(layout, from, "text_offset", 0f, 36f);

            Arrow("KindUp", column, up, upGlow, ".arrow_up.png", () => PickKind(-1));
            Arrow("KindDown", column, down, downGlow, ".arrow_down.png", () => PickKind(1));
            _kindIcon = Dd1Ui.Art("Kind", _slide, IconDir + "currency.bust.icon.png", column + icon - new Vector2(IconSize.x * 0.5f, 0f), IconSize, UiKit.Parchment);
            _amountUp = Arrow("AmountUp", next, up, upGlow, ".arrow_up.png", () => StepAmount(1));
            _amountDown = Arrow("AmountDown", next, down, downGlow, ".arrow_down.png", () => StepAmount(-1));
            _amountLabel = Dd1Ui.Line("Amount", _slide, "town_heirloom_exchange_from_amount", next + text, AmountBox, TextAlignmentOptions.Top);

            // What is taken: its offsets count from the middle of an icon and of the button (the frame's art
            // has its no-entry sign there), the amount from the middle of its top edge.
            var rows = Dd1Ui.Offset(layout, main, "heirloom_to_pos", 0f, 0f);
            _rowStart = rows + Dd1Ui.Offset(layout, to, "choice_start_offset", 256f, 75f);
            _rowPitch = Dd1Ui.Offset(layout, to, "choice_spacing", 0f, 44f);
            _linkAt = rows + Dd1Ui.Offset(layout, to, "arrow_offset", 148f, 80f);
            _frameAt = Dd1Ui.Offset(layout, to, "frame_offset", -32f, -8f);
            _invalidAt = Dd1Ui.Offset(layout, to, "frame_invalid_offset", -32f, -8f);
            _iconAt = Dd1Ui.Offset(layout, to, "heirloom_icon_offset", 0f, 20f);
            _takesAt = Dd1Ui.Offset(layout, to, "heirloom_amount_offset", 52f, 4f);
            _confirmAt = Dd1Ui.Offset(layout, to, "confirm_offset", 112f, 20f);
            _rows = UiKit.Stretch(UiKit.Rect("Rows", _slide));

            var kinds = HeirloomExchange.Rules.Kinds();
            _kind = kind != null && kinds.Contains(kind) ? kind : _lastKind != null && kinds.Contains(_lastKind) ? _lastKind : FirstPayable(kinds);
            _amount = HeirloomExchange.Rules.Clamp(_kind, 0, HeirloomExchange.Held(_kind));
        }

        // The first heirloom the estate holds enough of to trade; the first of all when it holds none.
        private static string FirstPayable(List<string> kinds)
        {
            foreach (var kind in kinds)
                if (HeirloomExchange.Held(kind) >= HeirloomExchange.Rules.Smallest(kind)) return kind;
            return kinds.Count > 0 ? kinds[0] : EstateState.HeirloomIds[0];
        }

        // One of DD1's arrows, drawn as it is painted, with its glow under the pointer. The glow lies behind the arrow.
        private Button Arrow(string name, Vector2 column, Vector2 offset, Vector2 glowOffset, string art, Action onClick)
        {
            var glow = Dd1Ui.Art(name + ".Glow", _slide, Art + ".selected_overlay.png", column + glowOffset, GlowSize);
            glow.gameObject.SetActive(false);
            var button = Dd1Ui.ArtButton(name, _slide, Art + art, column + offset - new Vector2(ArrowSize.x * 0.5f, 0f), ArrowSize, onClick, art.Contains("up") ? "+" : "-", false);
            UiKit.Hover(button.gameObject, inside => glow.gameObject.SetActive(inside && button.interactable));
            return button;
        }

        // ---- the player's hand ---------------------------------------------------------------------------

        private void PickKind(int direction)
        {
            var kinds = HeirloomExchange.Rules.Kinds();
            if (kinds.Count == 0) return;
            var index = (Mathf.Max(0, kinds.IndexOf(_kind)) + direction + kinds.Count) % kinds.Count;
            _kind = kinds[index];
            _lastKind = _kind;
            _amount = HeirloomExchange.Rules.Clamp(_kind, 0, HeirloomExchange.Held(_kind));
            // other rows, other amounts: nothing of the old kind "changed"
            _takes.Clear();
            Redraw();
        }

        private void StepAmount(int direction)
        {
            _amount = HeirloomExchange.Rules.Step(_kind, _amount, direction, HeirloomExchange.Held(_kind));
            Redraw();
        }

        private void Take(string to)
        {
            var trouble = HeirloomExchange.Trade(_kind, _amount, to);
            if (trouble == null)
            {
                _lastKind = _kind;
                EstateAudio.Ui("ui/town/heirloom_exchange_confirm");
            }
            // a row that can be taken has the button, so this is a trade the estate could not pay for a moment later
            else Plugin.Log.LogInfo("Heirloom exchange: no trade: " + trouble);
            Redraw();
        }

        private void OnTraded(HeirloomExchangeRules.Offer offer) => Redraw();

        private void Redraw()
        {
            _shown = null;
            Refresh();
        }

        // ---- state ---------------------------------------------------------------------------------------

        private void Refresh()
        {
            if (_slide == null) return;
            var rules = HeirloomExchange.Rules;
            var held = HeirloomExchange.Held(_kind);
            // The estate's heirlooms change behind the panel's back too (a building step bought with them).
            _amount = rules.Clamp(_kind, _amount, held);

            var key = new StringBuilder(_kind).Append('|').Append(_amount);
            foreach (var kind in EstateState.HeirloomIds) key.Append('|').Append(HeirloomExchange.Held(kind));
            if (key.ToString() == _shown) return;
            _shown = key.ToString();

            var icon = Dd1Ui.Sprite(IconDir + "currency." + _kind + ".icon.png");
            if (icon != null) _kindIcon.sprite = icon;
            // one colour, as DD1's style has it (town_heirloom_exchange_from_amount), also for more than the estate holds
            _amountLabel.text = _amount.ToString();
            _amountUp.interactable = rules.Step(_kind, _amount, 1, held) != _amount;
            _amountDown.interactable = rules.Step(_kind, _amount, -1, held) != _amount;

            UpgradeUi.Clear(_rows);
            _pulsing.Clear();
            var offers = rules.Offers(_kind, _amount, held);
            for (var i = 0; i < offers.Count; i++) AddRow(i, offers[i]);
            foreach (var offer in offers) _takes[offer.Rate.To] = offer.Takes;
            if (_pulsing.Count > 0) _pulsed = Time.unscaledTime;
        }

        private void AddRow(int index, HeirloomExchangeRules.Offer offer)
        {
            var at = _rowStart + _rowPitch * index;
            var to = offer.Rate.To;
            // DD1 has four bent arrows, one per row from the top; a fifth row would go without.
            if (index < Links) Dd1Ui.Art("Link" + index, _rows, Art + ".arrow_" + index + ".png", _linkAt, LinkSize);
            Dd1Ui.Art("Frame." + to, _rows, Art + (offer.Valid ? ".frame.png" : ".frame_invalid.png"), at + (offer.Valid ? _frameAt : _invalidAt), FrameSize,
                offer.Valid ? new Color(0.35f, 0.29f, 0.14f, 0.5f) : new Color(0.2f, 0.2f, 0.2f, 0.5f));
            var icon = Dd1Ui.Art("Icon." + to, _rows, IconDir + "currency." + to + ".icon.png", at + _iconAt - IconSize * 0.5f, IconSize, UiKit.Parchment);
            var takes = Dd1Ui.Line("Takes." + to, _rows, "town_heirloom_exchange_to_amount", at + _takesAt, TakesBox, TextAlignmentOptions.Top);
            takes.text = offer.Takes.ToString();
            // the scale of DD1's pulse turns about the number's middle
            var takesRect = (RectTransform)takes.transform;
            var corner = takesRect.anchoredPosition;
            takesRect.pivot = Dd1Ui.Middle;
            takesRect.anchoredPosition = corner + new Vector2(TakesBox.x * 0.5f, -TakesBox.y * 0.5f);
            if (!offer.Valid)
            {
                takes.color = UpgradeUi.Dim;
                icon.color = new Color(0.55f, 0.55f, 0.55f);
            }
            else if (_takes.TryGetValue(to, out var before) && before != offer.Takes) _pulsing.Add(takes.transform);
            if (!offer.Valid) return;

            Dd1Ui.ArtButton("Confirm." + to, _rows, Art + ".confirm.png", at + _confirmAt - ConfirmSize * 0.5f, ConfirmSize, () => Take(to), "OK");
        }

        // ---- motion --------------------------------------------------------------------------------------

        // DD1 lets the panel sink back behind the bar (transition_out_time) before it is gone.
        private void SlideAway()
        {
            if (_closing >= 0f) return;
            _closing = Time.unscaledTime;
            CloseSound();
        }

        // DD1's sound of the panel going down: once, whether it sinks behind the bar or another screen takes its place.
        private void CloseSound()
        {
            if (_closeHeard) return;
            _closeHeard = true;
            EstateAudio.Ui("ui/town/heirloom_exchange_close");
        }

        private void Update()
        {
            if (_slide == null) return;
            var now = Time.unscaledTime;
            float shown;
            if (_closing >= 0f)
            {
                // easeInSine
                var t = Mathf.Clamp01((now - _closing) / _slideOut);
                shown = Mathf.Cos(t * Mathf.PI * 0.5f);
                if (t >= 1f)
                {
                    if (TownPanel.Current == _panel) TownPanel.Close();
                    return;
                }
            }
            else shown = Mathf.Sin(Mathf.Clamp01((now - _opened) / _slideIn) * Mathf.PI * 0.5f);     // easeOutSine
            var y = -_hidden * (1f - shown);
            if (!Mathf.Approximately(_slide.anchoredPosition.y, y)) _slide.anchoredPosition = new Vector2(0f, y);

            if (_pulsed < 0f) return;
            var since = now - _pulsed;
            float swell;
            if (since < _pulseOut) swell = Mathf.Sin(since / _pulseOut * Mathf.PI * 0.5f);                    // easeOutSine
            else if (since < _pulseOut + _pulseIn) swell = Mathf.Cos((since - _pulseOut) / _pulseIn * Mathf.PI * 0.5f);     // easeInSine, back
            else
            {
                swell = 0f;
                _pulsed = -1f;
            }
            var scale = new Vector3(1f + (_pulseScale.x - 1f) * swell, 1f + (_pulseScale.y - 1f) * swell, 1f);
            foreach (var label in _pulsing)
                if (label != null) label.localScale = scale;
        }

        // ---- dev bridge ----------------------------------------------------------------------------------

        /// <summary>For tests: what the arrows do. kind: an heirloom id; steps: clicks on the amount's arrows (below 0: down).</summary>
        public static string Set(string kind, int steps)
        {
            if (!IsOpen) return "not open";
            var view = _instance;
            if (kind != null)
            {
                if (!HeirloomExchange.Rules.Kinds().Contains(kind)) return "no such heirloom";
                view._kind = kind;
                view._amount = HeirloomExchange.Rules.Clamp(kind, 0, HeirloomExchange.Held(kind));
                view._takes.Clear();
            }
            for (var i = 0; i < Math.Abs(steps); i++) view._amount = HeirloomExchange.Rules.Step(view._kind, view._amount, steps, HeirloomExchange.Held(view._kind));
            view.Redraw();
            return null;
        }

        /// <summary>For tests: a click on a row's confirm button.</summary>
        public static string Confirm(string to)
        {
            if (!IsOpen) return "not open";
            var offer = HeirloomExchange.Quote(_instance._kind, _instance._amount, to);
            if (offer == null || !offer.Valid) return HeirloomExchange.Reason(offer);
            _instance.Take(to);
            return null;
        }

        public static object Snapshot()
        {
            if (!IsOpen) return new { open = false };
            var view = _instance;
            var rows = new List<object>();
            foreach (var offer in HeirloomExchange.Rules.Offers(view._kind, view._amount, HeirloomExchange.Held(view._kind)))
                rows.Add(new { to = offer.Rate.To, takes = offer.Takes, valid = offer.Valid, reason = HeirloomExchange.Reason(offer) });
            return new
            {
                open = true, closing = view._closing >= 0f, kind = view._kind, amount = view._amount, held = HeirloomExchange.Held(view._kind),
                up = view._amountUp.interactable, down = view._amountDown.interactable, rows
            };
        }
    }
}
