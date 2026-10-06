using DD2Estate.Dev;

namespace DD2Estate.Estate
{
    /// <summary>
    /// Test commands of the heirloom exchange for the dev bridge ({"cmd":"run","name":"heirlooms.trade",
    /// "from":"bust","amount":6,"to":"crest"}); they do what the panel does, without the mouse.
    /// </summary>
    [EstateModule]
    internal static class HeirloomExchangeDev
    {
        private static void Register()
        {
            // DD1's rates per heirloom, what the estate holds, the smallest and largest amount the arrows reach; the panel.
            AgentBridge.Register("heirlooms.state", o => new { exchange = HeirloomExchange.Describe(), screen = HeirloomExchangePanel.Snapshot(), bar = TownPanelButtons.Describe() });
            // What an amount buys right now: {"from":"bust","amount":6} lists every offer; with "to" only that one.
            AgentBridge.Register("heirlooms.quote", o =>
            {
                var from = (string)o["from"] ?? "bust";
                var amount = (int?)o["amount"] ?? HeirloomExchange.Rules.Smallest(from);
                var offers = new System.Collections.Generic.List<object>();
                foreach (var offer in HeirloomExchange.Rules.Offers(from, amount, HeirloomExchange.Held(from)))
                    if (o["to"] == null || (string)o["to"] == offer.Rate.To)
                        offers.Add(new { to = offer.Rate.To, gives = offer.Gives, takes = offer.Takes, valid = offer.Valid, reason = HeirloomExchange.Reason(offer) });
                return new { from, amount, held = HeirloomExchange.Held(from), offers };
            });
            // Makes a trade at DD1's rate and saves: {"from":"bust","amount":6,"to":"crest"}.
            AgentBridge.Register("heirlooms.trade", o =>
                HeirloomExchange.Trade((string)o["from"], (int?)o["amount"] ?? 0, (string)o["to"]) ?? (object)HeirloomExchange.Describe());
            // Puts heirlooms in the estate's purse for a test: {"bust":12,"crest":-3} adds and takes.
            AgentBridge.Register("heirlooms.give", o =>
            {
                foreach (var kind in EstateState.HeirloomIds)
                    if (o[kind] != null) EstateState.Current.AddHeirloom(kind, (int)o[kind]);
                return HeirloomExchange.Describe();
            });
            // Opens DD1's panel as the bar's button does: {"kind":"deed"} with that heirloom picked.
            AgentBridge.Register("heirlooms.open", o => HeirloomExchangePanel.Open((string)o["kind"]) ? HeirloomExchangePanel.Snapshot() : (object)(TownPanel.BlockReason() ?? "not opened"));
            AgentBridge.Register("heirlooms.close", o =>
            {
                HeirloomExchangePanel.Close();
                return "closed";
            });
            // The panel's arrows: {"kind":"crest"} picks the heirloom, {"steps":2} clicks the amount up twice ({"steps":-1}: down).
            AgentBridge.Register("heirlooms.pick", o => HeirloomExchangePanel.Set((string)o["kind"], (int?)o["steps"] ?? 0) ?? HeirloomExchangePanel.Snapshot());
            // A click on a row's confirm button: {"to":"portrait"}.
            AgentBridge.Register("heirlooms.confirm", o => HeirloomExchangePanel.Confirm((string)o["to"]) ?? HeirloomExchangePanel.Snapshot());
        }
    }
}
