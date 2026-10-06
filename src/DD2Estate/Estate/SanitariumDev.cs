using System.Collections.Generic;
using DD2Estate.Dev;

namespace DD2Estate.Estate
{
    /// <summary>
    /// Test commands of the Sanitarium for the dev bridge ({"cmd":"run","name":"sanitarium.place","guid":3,
    /// "ward":"treatment","quirk":"quirk_clumsy_neg","slot":0}); they do what the screen does, without the
    /// mouse. Wards: "treatment" (Treatment Ward), "disease_treatment" (Medical Ward).
    /// </summary>
    [EstateModule]
    internal static class SanitariumDev
    {
        private static void Register()
        {
            AgentBridge.Register("sanitarium.state", o => new { ledger = SanitariumLedger.Describe(), screen = SanitariumPanel.Snapshot() });
            AgentBridge.Register("sanitarium.rules", o =>
            {
                var rules = new List<object>();
                foreach (var ward in SanitariumRules.Wards)
                {
                    rules.Add(new
                    {
                        ward = ward.Id,
                        name = ActivityText.Activity(ward.Id),
                        slots = SanitariumRules.Slots(ward),
                        maxSlots = ward.MaxSlots,
                        remove = SanitariumRules.Price(ward, SanitariumRules.Treatment.Remove, false, 0),
                        removeLockedIn = SanitariumRules.Price(ward, SanitariumRules.Treatment.Remove, true, 0),
                        lockIn = SanitariumRules.Price(ward, SanitariumRules.Treatment.Lock, false, 0),
                        cure = SanitariumRules.Price(ward, SanitariumRules.Treatment.Cure, false, 0),
                        cureAllChance = SanitariumRules.CureAllChance(ward),
                        successChance = SanitariumRules.SuccessChance(ward)
                    });
                }
                return new { maxLockedIn = SanitariumRules.MaxLockedPositive, wards = rules };
            });
            // What the wards can do for a hero, with the prices at that hero's resolve level: {"guid":3}.
            AgentBridge.Register("sanitarium.options", o =>
            {
                var guid = (uint)o["guid"];
                var actor = SanitariumLedger.Actor(guid);
                if (actor == null) return "no such hero";
                var wards = new List<object>();
                foreach (var ward in SanitariumRules.Wards)
                {
                    var options = new List<object>();
                    foreach (var option in SanitariumRules.Options(actor, ward))
                        options.Add(new { quirk = option.Id, treatment = option.Kind.ToString().ToLowerInvariant(), cost = option.Cost, lockedIn = option.Quirk.IsLocked(), blocked = option.Blocked });
                    wards.Add(new { ward = ward.Id, refusal = SanitariumLedger.Refusal(guid, ward), options });
                }
                return new { guid, resolve = Resolve.Level(guid), wards };
            });
            AgentBridge.Register("sanitarium.open", o =>
            {
                SanitariumPanel.Open();
                if (o["upgrades"] != null) SanitariumPanel.ShowUpgrades((bool)o["upgrades"]);
                return SanitariumPanel.IsOpen ? "open" : "not opened";
            });
            AgentBridge.Register("sanitarium.close", o =>
            {
                SanitariumPanel.Close();
                return "closed";
            });
            // The screen's own steps without paying, for screenshots: {"ward":"treatment","slot":0,"guid":3,"quirk":"..."}
            // stands the hero at the cell, as a drag from the roster does, and picks the quirk; leave the quirk
            // out to stop before it.
            AgentBridge.Register("sanitarium.pick", o =>
                SanitariumPanel.Pick((string)o["ward"] ?? SanitariumRules.TreatmentWard, (int?)o["slot"] ?? 0, (uint?)o["guid"] ?? 0u, (string)o["quirk"]) ?? "picked");
            AgentBridge.Register("sanitarium.place", o =>
            {
                var ward = SanitariumRules.Find((string)o["ward"] ?? SanitariumRules.TreatmentWard);
                if (ward == null) return "no such ward";
                return SanitariumLedger.Place((uint)o["guid"], ward, (int?)o["slot"] ?? 0, (string)o["quirk"]) ?? "placed";
            });
            AgentBridge.Register("sanitarium.cancel", o =>
            {
                var stay = SanitariumLedger.StayOf((uint)o["guid"]);
                return stay == null ? "not in the sanitarium" : SanitariumLedger.Cancel(stay) ?? "cancelled";
            });
        }
    }
}
