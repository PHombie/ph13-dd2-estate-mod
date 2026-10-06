using System;

namespace DD2Estate.Core
{
    /// <summary>
    /// How a change of the torchlight comes to the eye. The light itself (Exploration.Light) changes at once: a
    /// fight, a curio and the scouting read the new value in the same moment. What is DRAWN follows it:
    ///
    /// <list type="bullet">
    /// <item><b>The dungeon's colours</b> (DD1's colour grade, five tables a dungeon). DD1 does not look the
    /// picture up at the torch's own value: it keeps a number of its own for the grade [exe 0xb04680 reads it at
    /// RaidDisplay+0x3588; 0xb044e0 moves it every frame] that goes towards the light's BAND, ceil(torch / 25)
    /// * 25, by <c>g += (target - g) * dt / 0.5</c> (a tween of 0.5 s that is made anew every frame while g is
    /// not there: an exponential approach, 63 % after half a second, 95 % after a second and a half). So at
    /// rest the dungeon has one of five looks (76..100, 51..75, 26..50, 1..25, 0), a tile's burn inside a band
    /// changes nothing on screen, and crossing into another band is a second and a half of drifting colour.
    /// </item>
    /// <item><b>The gauge.</b> DD1's own jumps [exe 0xb10ec0: the bars are built from torch / 100 every frame]
    /// under a one-second flare of the torch's art ("ignite", "extinguish", sparks) that the mod does not draw.
    /// DD2's widget, the torch on screen here, slides: a hundredth of its bar a frame (CombatTorchUiBhv.
    /// LateUpdate), which at sixty frames is sixty points of light a second. The mod's gauges slide at that
    /// pace by the clock, whatever the frame rate.</item>
    /// </list>
    /// </summary>
    public static class LightEase
    {
        /// <summary>DD1's bands of light are 25 points wide; the colour grade has a table at each band's upper edge.</summary>
        public const double BandWidth = 25.0;
        /// <summary>DD1's time for the colour grade's approach, seconds [exe: the float at 0x1123258].</summary>
        public const double GradeTime = 0.5;
        /// <summary>The gauge's pace, points of light a second (DD2: a hundredth of the bar a frame, at sixty frames).</summary>
        public const double GaugePace = 60.0;
        /// <summary>Nearer than this the value is there.</summary>
        public const double Close = 0.02;

        /// <summary>
        /// The light the colour grade goes towards: with <paramref name="byBands"/> the upper edge of the light's
        /// band (DD1), without it the light itself.
        /// </summary>
        public static double GradeTarget(double light, bool byBands = true, double max = RaidRules.MaxLight)
        {
            var clamped = Math.Max(0.0, Math.Min(max, light));
            return byBands ? Math.Min(max, Math.Ceiling(clamped / BandWidth - 1e-9) * BandWidth) : clamped;
        }

        /// <summary>One frame of DD1's approach: the shown value goes a share dt / time of what is left.</summary>
        public static double Approach(double shown, double target, double dt, double time = GradeTime)
        {
            if (dt <= 0.0) return shown;
            if (time <= 0.0) return target;
            var next = shown + (target - shown) * Math.Min(1.0, dt / time);
            return Math.Abs(target - next) < Close ? target : next;
        }

        /// <summary>One frame of the gauge: towards the light at a steady pace, never past it.</summary>
        public static double Slide(double shown, double target, double dt, double pace = GaugePace)
        {
            if (pace <= 0.0) return target;
            if (dt <= 0.0) return shown;
            var step = pace * dt;
            return Math.Abs(target - shown) <= step ? target : shown + Math.Sign(target - shown) * step;
        }

        /// <summary>The most a hallway's tile burns off the torch (shared/rules.json: tile_light_loss 6 in dim and dark tiles).</summary>
        public const double TileBurn = 6.0;

        /// <summary>
        /// Whether a change of the light is one the torch itself answers with a flare: +1 it flares up, -1 it
        /// gutters, 0 nothing but the gauge moves. DD1 [exe 0xb110bd..0xb11195, the listener 0xb136c0] plays its
        /// torch's "ignite" (or "extinguish") with a burst of sparks when the light's band rose (fell), and for
        /// every change that is not the walk's own burn: a torch lit, a torch put down, a skill, a camp, an
        /// ambush. The tile's burn inside a band only shortens the bar. Here the walk's burn is known by its
        /// size: the light fell, by no more than a tile burns.
        /// </summary>
        public static int Flare(double before, double after)
        {
            if (after > before + 1e-9) return 1;
            if (after < before - 1e-9 && (before - after > TileBurn + 1e-9 || GradeTarget(after) != GradeTarget(before))) return -1;
            return 0;
        }

        /// <summary>
        /// Which of DD2's four flames (0 no light, 1 low, 2 medium, 3 high) burns at a share of the light, by
        /// DD2's thresholds (CombatTorchUiBhv.UpdateBarMaterials: above high, above medium, above low).
        /// </summary>
        public static int FlameLevel(double share, double low = 0.25, double medium = 0.5, double high = 0.75)
        {
            return share > high ? 3 : share > medium ? 2 : share > low ? 1 : 0;
        }
    }
}
