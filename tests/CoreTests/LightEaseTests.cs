using System;
using DD2Estate.Core;

namespace DD2Estate.CoreTests
{
    /// <summary>
    /// How a change of the torchlight is drawn (Core/LightEase.cs): the colour grade goes to the light's band as
    /// DD1's does, the gauge slides at DD2's pace, and DD2's flame changes once on the way.
    /// </summary>
    public static class LightEaseTests
    {
        public static void TheGradeGoesToTheBandsUpperEdge()
        {
            // DD1: ceil(torch / 25) * 25: one look a band
            Check.Near(100, LightEase.GradeTarget(100), 1e-9, "full light");
            Check.Near(100, LightEase.GradeTarget(76), 1e-9, "76 is still the radiant band");
            Check.Near(75, LightEase.GradeTarget(75), 1e-9, "75 is the dim band's upper edge");
            Check.Near(75, LightEase.GradeTarget(51), 1e-9, "51");
            Check.Near(50, LightEase.GradeTarget(50), 1e-9, "50");
            Check.Near(25, LightEase.GradeTarget(1), 1e-9, "1 is dark, not black");
            Check.Near(0, LightEase.GradeTarget(0), 1e-9, "no light");
            Check.Near(100, LightEase.GradeTarget(140), 1e-9, "above the top");
            Check.Near(0, LightEase.GradeTarget(-3), 1e-9, "below nothing");
            // without bands the grade follows the light itself
            Check.Near(88, LightEase.GradeTarget(88, byBands: false), 1e-9, "every point");
        }

        public static void ATilesBurnInsideABandChangesNothing()
        {
            // 94 -> 88: the same band, so the same target: nothing drifts
            Check.Equal(LightEase.GradeTarget(94), LightEase.GradeTarget(88), "the same look");
            Check.True(LightEase.GradeTarget(76) != LightEase.GradeTarget(75), "the band's edge is where the look changes");
        }

        public static void TheApproachIsDd1s()
        {
            // g += (target - g) * dt / 0.5, sixty frames a second: e-folding in about half a second
            double g = 100, target = 75;
            var frames = 0;
            while (Math.Abs(g - target) > 25 * Math.Exp(-1) && frames < 1000)
            {
                g = LightEase.Approach(g, target, 1.0 / 60.0);
                frames++;
            }
            Check.True(frames >= 28 && frames <= 31, "63 % of the way after about half a second, got " + frames + " frames");
            for (var i = frames; i < 90; i++) g = LightEase.Approach(g, target, 1.0 / 60.0);
            Check.Near(target, g, 25 * 0.06, "95 % after a second and a half");
            for (var i = 0; i < 600; i++) g = LightEase.Approach(g, target, 1.0 / 60.0);
            Check.Equal(target, g, "and there in the end, exactly");
        }

        public static void ALongFrameDoesNotOvershoot()
        {
            Check.Equal(75.0, LightEase.Approach(100, 75, 2.0), "a frame longer than the time lands on the target");
            Check.Equal(100.0, LightEase.Approach(100, 75, 0.0), "no time, no step");
            Check.Equal(75.0, LightEase.Approach(100, 75, 0.016, time: 0), "no easing asked for: at once");
        }

        public static void TheGaugeSlidesAtASteadyPaceAndStops()
        {
            // a torch lit: 25 points at 60 a second
            double shown = 50, target = 75, t = 0;
            while (shown != target && t < 5)
            {
                var next = LightEase.Slide(shown, target, 1.0 / 144.0);
                Check.True(next > shown && next <= target, "upwards, never past");
                shown = next;
                t += 1.0 / 144.0;
            }
            Check.Near(25.0 / 60.0, t, 0.02, "25 points take 0.42 s at any frame rate");
            Check.Equal(69.0, LightEase.Slide(75, 69, 1.0), "a long frame lands on the light");
            Check.Near(74.0, LightEase.Slide(75, 69, 1.0 / 60.0), 1e-9, "downwards a point a sixtieth");
            Check.Equal(69.0, LightEase.Slide(75, 69, 0.016, pace: 0), "no pace: at once");
        }

        public static void TheTorchFlaresForWhatDd1sDoes()
        {
            Check.Equal(1, LightEase.Flare(50, 75), "a torch lit");
            Check.Equal(1, LightEase.Flare(94, 100), "a curio's light");
            Check.Equal(-1, LightEase.Flare(100, 75), "a torch put down");
            Check.Equal(-1, LightEase.Flare(100, 0), "an ambush");
            Check.Equal(0, LightEase.Flare(94, 88), "a tile's burn inside the band: the bar only");
            Check.Equal(0, LightEase.Flare(88, 87), "a lit tile's burn");
            Check.Equal(-1, LightEase.Flare(76, 70), "a tile's burn that takes the light into the next band");
            Check.Equal(-1, LightEase.Flare(1, 0), "the last of the light");
            Check.Equal(0, LightEase.Flare(60, 60), "no change");
        }

        public static void TheFlameChangesOnceOnTheWay()
        {
            // the gauge slides from 50 to 100 across DD2's thresholds: every level is entered once, none twice
            double shown = 50;
            var last = LightEase.FlameLevel(shown / 100);
            var changes = 0;
            for (var i = 0; i < 400 && shown != 100; i++)
            {
                shown = LightEase.Slide(shown, 100, 1.0 / 60.0);
                var level = LightEase.FlameLevel(shown / 100);
                Check.True(level >= last, "the flame never steps back on the way up");
                if (level != last) changes++;
                last = level;
            }
            Check.Equal(2, changes, "medium, then high");
            Check.Equal(1, LightEase.FlameLevel(0.5), "at the threshold itself the lower flame (DD2: above, not at)");
            Check.Equal(3, LightEase.FlameLevel(1.0), "full");
            Check.Equal(0, LightEase.FlameLevel(0.25), "a quarter is no light yet");
        }
    }
}
