using DD2Estate.Core;
using DD2Estate.Dev;
using DD2Estate.Estate;
using UnityEngine;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// The light as it is DRAWN, a frame at a time behind the light of the rules (<see cref="LightEase"/>): the
    /// value the dungeon's colour grade is looked up at, and the value the torch's gauge shows. The expedition's
    /// light itself (Exploration.Light) jumps, as it must: a fight, a curio and the scouting read it at once.
    ///
    /// Whoever draws asks here; the two values step once a frame however many ask. They are put straight onto
    /// the light, without a way there, when there was nothing to see the way on: a new expedition, the first
    /// frame after the dungeon's screen was away (a fight, in which a torch may have been lit), and a light set
    /// by the dev bridge for a picture (corridor.torch level=).
    /// </summary>
    [EstateModule]
    internal static class ShownLight
    {
        /// <summary>[Look] LightEase: seconds of DD1's approach of the colour grade; 0: the grade jumps, and the gauge with it.</summary>
        public static float GradeTime = (float)LightEase.GradeTime;
        /// <summary>[Look] LightBands: the dungeon's colours go by the light's band, as DD1's do. Off: by every point of light.</summary>
        public static bool ByBands = true;
        /// <summary>The gauge's pace, points of light a second (DD2's own widget: 60 at sixty frames).</summary>
        public static float GaugePace = (float)LightEase.GaugePace;

        /// <summary>Counts the changes of the light the torch answers with a flare (LightEase.Flare); <see cref="FlareUp"/> says which way the last went.</summary>
        public static int Flares { get; private set; }
        public static bool FlareUp { get; private set; }

        private static double _grade = RaidRules.MaxLight, _gauge = RaidRules.MaxLight, _light = RaidRules.MaxLight;
        private static int _frame = -10;
        private static DungeonRun _run;
        private static bool _overridden;
        private static int _snaps;

        private static void Register()
        {
            GradeTime = Mathf.Max(0f, Plugin.Settings.Bind("Look", "LightEase", (float)LightEase.GradeTime,
                "Seconds the dungeon's colours take to follow the torchlight into another band (Darkest Dungeon (1): 0.5, an approach that is nearly there after three times as long). The torch's gauge slides at the pace of DD2's own. 0: both jump.").Value);
            ByBands = Plugin.Settings.Bind("Look", "LightBands", true,
                "The dungeon's colours change when the torchlight crosses into another band (75, 50, 25, 0), as in Darkest Dungeon (1); inside a band they stay. Off: they follow every point of light.").Value;

            // What is drawn of the light: {} as it stands; {"time":0.5,"bands":true,"pace":60} the tunables;
            // {"snap":true} onto the light at once.
            AgentBridge.Register("light.shown", o =>
            {
                if (o["time"] != null) GradeTime = Mathf.Max(0f, (float)o["time"]);
                if (o["bands"] != null) ByBands = (bool)o["bands"];
                if (o["pace"] != null) GaugePace = Mathf.Max(0f, (float)o["pace"]);
                if ((bool?)o["snap"] == true) Snap();
                Step();
                var light = CorridorLight.Torch;
                return new
                {
                    light, gradeTarget = LightEase.GradeTarget(light, ByBands), grade = _grade, gauge = _gauge,
                    gradeMoving = Moving(_grade, LightEase.GradeTarget(light, ByBands)), gaugeMoving = Moving(_gauge, light),
                    time = GradeTime, bands = ByBands, pace = GaugePace, snaps = _snaps, flares = Flares, lastFlareUp = FlareUp,
                    table = CorridorPost.Level, blend = CorridorPost.Blend, post = CorridorPost.Status,
                    torch = DungeonHud.Torch?.DescribeShown()
                };
            });
        }

        /// <summary>The light the colour grade is looked up at, 0..100.</summary>
        public static float Grade
        {
            get
            {
                Step();
                return (float)_grade;
            }
        }

        /// <summary>The light the torch's gauge shows, 0..100.</summary>
        public static float Gauge
        {
            get
            {
                Step();
                return (float)_gauge;
            }
        }

        /// <summary>The gauge as a share of the full light, 0..1.</summary>
        public static float GaugeShare => Mathf.Clamp01(Gauge / (float)RaidRules.MaxLight);

        /// <summary>Both values onto the light as it is now.</summary>
        public static void Snap()
        {
            var light = CorridorLight.Torch;
            _grade = LightEase.GradeTarget(light, ByBands);
            _gauge = _light = light;
            _frame = Time.frameCount;
            _snaps++;
        }

        private static bool Moving(double shown, double target) => System.Math.Abs(shown - target) > 1e-6;

        private static void Step()
        {
            var now = Time.frameCount;
            if (now == _frame) return;
            var run = DungeonRun.Current;
            var overridden = CorridorLight.TorchOverride >= 0f;
            // nobody looked for a frame or more, another expedition, a light of the dev bridge's: there is no way to show
            if (now - _frame > 1 || run != _run || overridden || overridden != _overridden || GradeTime <= 0f)
            {
                _run = run;
                _overridden = overridden;
                Snap();
                return;
            }
            _frame = now;
            // a long frame (a scene loads) is not a long step
            var dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            var light = CorridorLight.Torch;
            var flare = LightEase.Flare(_light, light);
            if (flare != 0)
            {
                Flares++;
                FlareUp = flare > 0;
            }
            _light = light;
            _grade = LightEase.Approach(_grade, LightEase.GradeTarget(light, ByBands), dt, GradeTime);
            _gauge = LightEase.Slide(_gauge, light, dt, GaugePace);
        }
    }
}
