using System.Collections.Generic;
using DD2Estate.Core;
using DD2Estate.Dev;
using DD2Estate.Estate;
using UnityEngine;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// Test commands for the dungeon view itself, for the dev bridge: what the camera, the scene, the light and
    /// the party are right now, in the same terms as tools/dd1_corridor_reference.py prints DD1's
    /// (python tools/bridge.py run corridor.state), and ways to set them without the mouse. The numbers
    /// themselves are static fields (CorridorNumbers, CorridorView, CorridorScene, CorridorLight, CorridorProps):
    /// set one through the bridge, then run corridor.rebuild.
    /// </summary>
    [EstateModule]
    internal static class CorridorDev
    {
        private static void Register()
        {
            // Everything about the view on screen: camera, layers, light, party, props.
            AgentBridge.Register("corridor.state", o =>
            {
                var view = CorridorView.Instance;
                if (view == null || !view.gameObject.activeInHierarchy) return "no corridor";
                return State(view);
            });
            // The torch the view is lit by: {"level":40}; {"level":-1} gives it back to the expedition.
            AgentBridge.Register("corridor.torch", o =>
            {
                if (o["level"] != null) CorridorLight.TorchOverride = (float)o["level"];
                return new { torch = CorridorLight.Torch, overridden = CorridorLight.TorchOverride >= 0f, expedition = DungeonRun.Current != null ? (double?)DungeonRun.Current.Exploration.Light : null };
            });
            // The party somewhere in the hallway, no tile entered: {"tile":3} (its position in the tile's middle)
            // or {"x":2715} (the leader's x in DD1 units: tile i is centred on 720 i).
            AgentBridge.Register("corridor.place", o =>
            {
                var view = CorridorView.Instance;
                if (view == null) return "no corridor";
                if (o["x"] != null) view.PlaceAt((float)o["x"]);
                else if (o["tile"] != null) view.PlaceInTile((int)o["tile"]);
                return new { leadX = view.LeadX, tile = view.CurrentTile, tileOfLeader = view.TileOf(view.LeadX), length = view.Length, room = view.IsRoom };
            });
            // The light's terms, each on or off, for A/B screenshots: {"ramp":false} walls, floor and props unlit;
            // {"heroes":false} every hero at full brightness; {"grade":false} no colour grade; {"grain":false};
            // {"flicker":0.3} the mod's own visible flicker (0 is DD1); {"fade":0.5} DD1's scene fade (1 = lit);
            // {"gamma":1.45,"heroGamma":2,"heroBrightness":1.35,"contribution":1,"grainIntensity":0.011} the tunables;
            // {"all":false} / {"all":true} every term at once. Without arguments: what is on.
            AgentBridge.Register("corridor.light", o =>
            {
                if (o["all"] != null)
                {
                    var on = (bool)o["all"];
                    CorridorLight.Enabled = CorridorLight.HeroLight = CorridorPost.Grade = CorridorPost.GrainOn = on;
                    if (on) CorridorLight.Fade = 1f;
                }
                if (o["ramp"] != null) CorridorLight.Enabled = (bool)o["ramp"];
                if (o["heroes"] != null) CorridorLight.HeroLight = (bool)o["heroes"];
                if (o["grade"] != null) CorridorPost.Grade = (bool)o["grade"];
                if (o["grain"] != null) CorridorPost.GrainOn = (bool)o["grain"];
                if (o["flicker"] != null) CorridorLight.FlickerVisible = (float)o["flicker"];
                if (o["fade"] != null) CorridorLight.Fade = (float)o["fade"];
                if (o["gamma"] != null) CorridorLight.Gamma = (float)o["gamma"];
                if (o["heroGamma"] != null) CorridorLight.HeroGamma = (float)o["heroGamma"];
                if (o["heroBrightness"] != null) CorridorLight.HeroBrightness = (float)o["heroBrightness"];
                if (o["contribution"] != null) CorridorPost.GradeContribution = (float)o["contribution"];
                if (o["grainIntensity"] != null) CorridorPost.GrainIntensity = (float)o["grainIntensity"];
                return Light(CorridorView.Instance);
            });
            // DD1's way from one area into the next, on the area on screen (it is shown again in the same place):
            // {} the fades; {"walk":true} in a hallway the party walks into the door ahead first.
            // Settings, with {"run":false} to set only: {"timescale":20} twenty times faster (0: no sequence at all,
            // for test scripts), {"enabled":false}, {"walkin":false} fades only, {"depth":0} heroes stay on their line,
            // {"cameraBlend":false}.
            AgentBridge.Register("corridor.transition", o =>
            {
                var view = CorridorView.Instance;
                if (view == null) return "no corridor";
                if (o["timescale"] != null) CorridorTransition.TimeScale = (float)o["timescale"];
                if (o["enabled"] != null) CorridorTransition.Enabled = (bool)o["enabled"];
                if (o["walkin"] != null) CorridorTransition.WalkIn = (bool)o["walkin"];
                if (o["depth"] != null) CorridorTransition.WalkDepthShare = (float)o["depth"];
                if (o["cameraBlend"] != null) CorridorTransition.CameraBlend = (bool)o["cameraBlend"];
                string started = null;
                if ((bool?)o["run"] ?? true) started = view.DevTransition((bool?)o["walk"] ?? false);
                return new { started, state = Transition(view) };
            });
            // The stand-ins for what DD2's models lack: {"walk":0..3} the walk (0 glide, 1 bob, 2 bob with sway and lean,
            // 3 the game's own move animation), {"keepFacing":true}, {"gradient":true,"opacity":0.3,"offset":..,
            // "falloff":..,"ceiling":..,"spread":..} the shader's vertical gradient on the models (below 0: the material's own).
            AgentBridge.Register("corridor.style", o =>
            {
                if (o["walk"] != null) CorridorView.WalkStyle = (int)o["walk"];
                if (o["keepFacing"] != null) CorridorView.KeepFacing = (bool)o["keepFacing"];
                if (o["sway"] != null) CorridorView.SwayDegrees = (float)o["sway"];
                if (o["lean"] != null) CorridorView.LeanDegrees = (float)o["lean"];
                if (o["bob"] != null) CorridorView.BobHeight = (float)o["bob"];
                if (o["gradient"] != null) CorridorView.HeroGradient = (bool)o["gradient"];
                if (o["opacity"] != null) CorridorView.HeroGradientOpacity = (float)o["opacity"];
                if (o["offset"] != null) CorridorView.HeroGradientOffset = (float)o["offset"];
                if (o["falloff"] != null) CorridorView.HeroGradientFalloff = (float)o["falloff"];
                if (o["ceiling"] != null) CorridorView.HeroGradientCeiling = (float)o["ceiling"];
                if (o["spread"] != null) CorridorView.HeroGradientSpread = (float)o["spread"];
                if (o["yaw"] != null) CorridorView.HeroYaw = (float)o["yaw"];
                return new
                {
                    walk = CorridorView.WalkStyle, keepFacing = CorridorView.KeepFacing, yaw = CorridorView.HeroYaw, sway = CorridorView.SwayDegrees, lean = CorridorView.LeanDegrees, bob = CorridorView.BobHeight,
                    gradient = CorridorView.HeroGradient,
                    gradientValues = new[] { CorridorView.HeroGradientOpacity, CorridorView.HeroGradientOffset, CorridorView.HeroGradientFalloff, CorridorView.HeroGradientCeiling, CorridorView.HeroGradientSpread }
                };
            });
            // Any hallway or room on screen, to look at it (the view only: the expedition knows nothing of it and
            // shows its own area again at its next step): {"dungeon":"weald","tiles":["corridor_door.basic",
            // "corridor_wall.01","corridor_wall.01","corridor_door.basic"]}, {"room":true,"tiles":["room_wall.clearing"]}.
            // Settings of the ends: {"clamp":false} the camera follows the leader everywhere, {"extend":false} the
            // endhall picture as a wall tile beside the doors (the mod's first reading), {"caps":false} no end caps,
            // {"capDrop":80}; {"foregroundWalking":true} the strips while walking only.
            AgentBridge.Register("corridor.area", o =>
            {
                var view = CorridorView.Instance;
                if (view == null) return "no corridor";
                if (o["clamp"] != null) CorridorView.ClampCamera = (bool)o["clamp"];
                if (o["extend"] != null) CorridorScene.ExtendEnds = (bool)o["extend"];
                if (o["caps"] != null) CorridorScene.ShowEndCaps = (bool)o["caps"];
                if (o["capDrop"] != null) CorridorScene.EndCapDrop = (float)o["capDrop"];
                if (o["foregroundWalking"] != null) CorridorScene.ForegroundWhileWalking = (bool)o["foregroundWalking"];
                if (o["tiles"] is Newtonsoft.Json.Linq.JArray tiles)
                {
                    var names = new List<string>();
                    foreach (var tile in tiles) names.Add((string)tile);
                    var was = CorridorTransition.Enabled;
                    CorridorTransition.Enabled = false;
                    view.ShowArea((string)o["dungeon"] ?? "crypts", names, (bool?)o["room"] ?? false, (int?)o["start"] ?? 0);
                    CorridorTransition.Enabled = was;
                }
                else view.Rebuild();
                return new { clamp = CorridorView.ClampCamera, extend = CorridorScene.ExtendEnds, caps = CorridorScene.ShowEndCaps, capDrop = CorridorScene.EndCapDrop, foregroundWalking = CorridorScene.ForegroundWhileWalking, length = view.Length, x = view.LeadX };
            });
            // DD1's scripted moments, without playing: {"hero":2} the hero of rank 2 turns to the curio of the party's tile
            // (or room) as scripts/timescript/investigate_intro.times has it; {"kind":"obstacle"}; {"end":true} plays
            // investigate_extro. Settings with {"run":false}: {"enabled":false}, {"blur":false}, {"presentation":false},
            // {"offset":154}.
            AgentBridge.Register("corridor.investigate", o =>
            {
                var view = CorridorView.Instance;
                if (view == null) return "no corridor";
                if (o["enabled"] != null) CorridorScript.Enabled = (bool)o["enabled"];
                if (o["blur"] != null) CorridorScript.Blur = (bool)o["blur"];
                if (o["presentation"] != null) CorridorScript.Presentation = (bool)o["presentation"];
                if (o["offset"] != null) CorridorScript.Offset = (float)o["offset"];
                if ((bool?)o["run"] ?? true)
                {
                    if ((bool?)o["end"] == true) view.StartCoroutine(CorridorView.EndInteraction());
                    else
                    {
                        if (!System.Enum.TryParse((string)o["kind"] ?? "curio", true, out PropKind kind)) kind = PropKind.Curio;
                        view.StartCoroutine(CorridorView.Investigate(kind, view.GuidOfSlot(((int?)o["hero"] ?? 1) - 1)));
                    }
                }
                return Scripted(view);
            });
            // A trap going off under a hero (trap.times): {"hero":1}; {"disarm":true} the disarming instead
            // (disarm_intro, a second, disarm_extro).
            AgentBridge.Register("corridor.trap", o =>
            {
                var view = CorridorView.Instance;
                if (view == null) return "no corridor";
                var guid = view.GuidOfSlot(((int?)o["hero"] ?? 1) - 1);
                view.StartCoroutine((bool?)o["disarm"] == true ? CorridorView.Disarm(guid) : CorridorView.TrapSprung(guid));
                return Scripted(view);
            });
            // DD1's steady cam: {"enabled":false}, {"strength":2} (1 is DD1). Without arguments: where it is.
            AgentBridge.Register("corridor.drift", o =>
            {
                var view = CorridorView.Instance;
                if (o["enabled"] != null) CorridorScript.Drift = (bool)o["enabled"];
                if (o["strength"] != null) CorridorScript.DriftStrength = (float)o["strength"];
                return new
                {
                    enabled = CorridorScript.Drift,
                    strength = CorridorScript.DriftStrength,
                    offset = view != null ? V(view.DriftOffset) : null,
                    roll = view != null ? view.DriftRoll : 0f,
                    limits = V(CorridorScript.MaxDistance),
                    timeRange = V(CorridorScript.TimeRange),
                    driftSpeed = V(CorridorScript.DriftSpeed),
                    turnRate = V(CorridorScript.TurnRate),
                    drag = CorridorScript.Drag,
                    positionScalar = CorridorScript.PositionScalar
                };
            });
            // The walking-back camera held at a blend: {"back":1}; {"back":-1} lets it follow the walking again.
            AgentBridge.Register("corridor.camera", o =>
            {
                var view = CorridorView.Instance;
                if (view == null) return "no corridor";
                if (o["back"] != null) CorridorView.BackOverride = (float)o["back"];
                return new { back = view.BackBlend, held = CorridorView.BackOverride >= 0f, at = V(view.CameraAt), zoom = view.CameraZoom };
            });
        }

        private static object State(CorridorView view)
        {
            var main = Camera.main;
            var at = view.CameraAt;
            var zoom = view.CameraZoom;
            var heroes = new List<object>();
            for (var i = 0; i < view.Actors.Count; i++)
            {
                var actor = view.Actors[i];
                if (actor == null) continue;
                // the body's mesh, without weapons: the tallest of the actor's skinned meshes whose name has no "_wpn"
                Bounds? body = null;
                foreach (var mesh in actor.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    if (mesh.name.Contains("_wpn") || !mesh.enabled) continue;
                    if (body == null || mesh.bounds.size.y > body.Value.size.y) body = mesh.bounds;
                }
                var animator = actor.GetComponentInChildren<Animator>();
                var state = animator != null && animator.isActiveAndEnabled && animator.layerCount > 0 ? animator.GetCurrentAnimatorStateInfo(0) : default(AnimatorStateInfo);
                heroes.Add(new
                {
                    rank = i + 1,
                    name = actor.name,
                    // what turns a model round shows here: the actor's own rotation and scale, and the animator's state
                    rotation = V(actor.transform.eulerAngles),
                    localRotation = V(actor.transform.localEulerAngles),
                    scale = V(actor.transform.lossyScale),
                    idle = animator != null && state.IsName("idle_neutral"),
                    animatorState = state.shortNameHash,
                    animatorTime = state.normalizedTime,
                    x = view.LeadX - i * view.Spacing,
                    world = V(actor.transform.position),
                    bodyHeightWorld = body?.size.y,
                    bodyHeightDd1 = body != null ? (float?)(body.Value.size.y / CorridorNumbers.UnitScale / Mathf.Max(0.01f, CorridorView.HeroScale)) : null,
                    box1080 = body != null && main != null ? Box1080(main, body.Value.min, body.Value.max) : null
                });
            }
            var perUnit = new Dictionary<string, float>();
            foreach (var z in new[] { CorridorNumbers.ForegroundZ, 0f, CorridorNumbers.TrapZ, CorridorNumbers.CurioZ, CorridorNumbers.WallZ })
                perUnit["z" + z] = zoom * CorridorNumbers.Focal / (z - at.z);
            return new
            {
                area = view.IsRoom ? "room" : "hallway",
                unitScale = CorridorNumbers.UnitScale,
                leadX = view.LeadX,
                tile = view.CurrentTile,
                length = view.Length,
                speed = view.Speed,
                camera = new
                {
                    dd1 = V(at),
                    zoom,
                    back = view.BackBlend,
                    verticalFov = CorridorNumbers.VerticalFov(zoom),
                    // DD1 pixels (1920-wide view) to a unit at each depth; the reference renderer prints the same
                    pxPerUnit = perUnit,
                    main = main != null ? new { position = V(main.transform.position), fov = main.fieldOfView, aspect = main.aspect, rect = new[] { main.rect.x, main.rect.y, main.rect.width, main.rect.height }, pixels = new[] { main.pixelWidth, main.pixelHeight } } : null,
                    viewRect = CorridorView.UseViewRect,
                    viewPixels = view.ViewPixels
                },
                light = Light(view),
                transition = Transition(view),
                script = Scripted(view),
                drift = new { enabled = CorridorScript.Drift, offset = V(view.DriftOffset), roll = view.DriftRoll },
                style = new { walk = CorridorView.WalkStyle, keepFacing = CorridorView.KeepFacing, yaw = CorridorView.HeroYaw, gradient = CorridorView.HeroGradient },
                foreground = new
                {
                    mode = CorridorScene.ForegroundMode,
                    layer = CorridorScene.ForegroundLayer,
                    alpha = CorridorScene.ForegroundAlpha,
                    camera = CorridorScene.ForegroundCamera.Status
                },
                layers = view.Scene.Describe(at, zoom),
                heroes,
                props = view.Props.All.Count
            };
        }

        private static object Scripted(CorridorView view)
        {
            return new
            {
                enabled = CorridorScript.Enabled,
                presenting = view.Presenting,
                held = view.ScriptHeld,
                actorSlot = view.ScriptActor,
                // what DD1's scripts have set: zoom, focus, the scene's saturation / intensity / blur, the heroes' boost...
                values = view.ScriptValues,
                scene = CorridorPost.SceneStatus,
                hudAlpha = CorridorView.HudAlpha,
                foregroundAlpha = CorridorScene.ForegroundAlpha,
                camera = new { at = V(view.CameraAt), zoom = view.CameraZoom }
            };
        }

        private static object Transition(CorridorView view)
        {
            return new
            {
                phase = view.TransitionPhase,
                running = CorridorView.TransitionRunning,
                areaPending = view.AreaPending,
                fade = CorridorLight.Fade,
                enabled = CorridorTransition.Enabled,
                timescale = CorridorTransition.TimeScale,
                walkin = CorridorTransition.WalkIn,
                depth = CorridorTransition.WalkDepthShare,
                cameraBlend = CorridorTransition.CameraBlend,
                // DD1's times: the slow darkening near the door, fade out, fade in; the last stretch a hero darkens over
                times = new[] { CorridorNumbers.DoorCameraBlendTime, CorridorNumbers.FadeOutTime, CorridorNumbers.FadeInTime },
                darkenOver = CorridorNumbers.DoorDarken + CorridorNumbers.DoorVisibility,
                heroLight = view.HeroLightScalar
            };
        }

        private static object Light(CorridorView view)
        {
            var zoom = view != null ? view.CameraZoom : 1f;
            return new
            {
                ramp = CorridorLight.Enabled,
                heroes = CorridorLight.HeroLight,
                grade = CorridorPost.Grade,
                grain = CorridorPost.GrainOn,
                torch = CorridorLight.Torch,
                torchOverridden = CorridorLight.TorchOverride >= 0f,
                fade = CorridorLight.Fade,
                curtain = CorridorLight.Curtain,
                flicker = CorridorLight.Flicker,
                flickerVisible = CorridorLight.FlickerVisible,
                @base = C(CorridorLight.Base),
                half = C(CorridorLight.Half),
                edge = C(CorridorLight.Edge),
                // the ramp's stops in DD1 units from the camera's axis; DD1's multiplier there and the vertex colour that gives it
                stops = new[] { 0f, CorridorNumbers.RampWidth * 0.5f / zoom, CorridorNumbers.RampWidth / zoom },
                dd1AtStops = new[] { CorridorLight.RampGamma(0f).r, CorridorLight.RampGamma(CorridorNumbers.RampWidth * 0.5f).r, CorridorLight.RampGamma(CorridorNumbers.RampWidth).r },
                vertexAtStops = new[] { CorridorLight.Ramp(0f).r, CorridorLight.Ramp(CorridorNumbers.RampWidth * 0.5f).r, CorridorLight.Ramp(CorridorNumbers.RampWidth).r },
                gamma = CorridorLight.Gamma,
                heroGamma = CorridorLight.HeroGamma,
                heroBrightness = CorridorLight.HeroBrightness,
                // each model's _Brightness now, leader first, and DD1's light on it (0..1)
                heroBrightnessNow = view != null ? view.HeroBrightnessNow() : null,
                post = new
                {
                    status = CorridorPost.Status,
                    level = CorridorPost.Level,
                    blend = CorridorPost.Blend,
                    contribution = CorridorPost.GradeContribution,
                    grainIntensity = CorridorPost.GrainIntensity,
                    camera = CorridorScene.ForegroundCamera.Status,
                    // who runs post effects and on which volumes: only the mod's camera should take the mod's volume
                    cameras = CorridorScene.ForegroundCamera.Stack(Camera.main),
                    lutSize = UnityEngine.Rendering.Universal.UniversalRenderPipeline.asset != null ? UnityEngine.Rendering.Universal.UniversalRenderPipeline.asset.colorGradingLutSize : 0,
                    // what the grade makes of a mid grey and of white (display values)
                    grey136 = CorridorPost.Probe(136, 136, 136),
                    white = CorridorPost.Probe(255, 255, 255)
                },
                colourSpace = QualitySettings.activeColorSpace.ToString()
            };
        }

        /// <summary>A world box as DD1's reference renderer prints it: left, top, right, bottom on a 1920x1080 screen, y from the top.</summary>
        internal static float[] Box1080(Camera camera, Vector3 worldMin, Vector3 worldMax)
        {
            var a = camera.WorldToScreenPoint(worldMin);
            var b = camera.WorldToScreenPoint(new Vector3(worldMax.x, worldMax.y, worldMin.z));
            float sx = 1920f / Mathf.Max(1, Screen.width), sy = 1080f / Mathf.Max(1, Screen.height);
            return new[]
            {
                Mathf.Round(Mathf.Min(a.x, b.x) * sx * 10f) / 10f, Mathf.Round((Screen.height - Mathf.Max(a.y, b.y)) * sy * 10f) / 10f,
                Mathf.Round(Mathf.Max(a.x, b.x) * sx * 10f) / 10f, Mathf.Round((Screen.height - Mathf.Min(a.y, b.y)) * sy * 10f) / 10f
            };
        }

        private static float[] V(Vector3 v) => new[] { v.x, v.y, v.z };

        private static float[] C(Color c) => new[] { Mathf.Round(c.r * 1000f) / 1000f, Mathf.Round(c.g * 1000f) / 1000f, Mathf.Round(c.b * 1000f) / 1000f };
    }
}
