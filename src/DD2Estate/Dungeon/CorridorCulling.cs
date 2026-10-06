using System.Collections.Generic;
using DD2Estate.Dev;
using DD2Estate.Estate;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// Why heroes lost their weapons in the corridor (the owner, 2026-10-06: "some heroes sometimes lose the
    /// weapon from their hands"; the Plague Doctor's dagger, the Grave Robber's).
    ///
    /// The game draws its characters with a camera of their own, stacked on the main one ("Character Camera",
    /// an overlay camera of URP). The corridor gives the main camera the dungeon view's rectangle, the upper two
    /// thirds of the screen (<see cref="KeepViewRect"/>): a picture 8 wide to 3 high. URP draws an overlay camera
    /// into its base camera's rectangle and bends the overlay's projection to that shape, so the heroes come
    /// out where they belong. But what a camera draws is chosen before that, by the camera's own frustum, and
    /// the Character Camera's own rectangle was still the whole screen, 16 to 9: it looked for things to draw
    /// in the middle two thirds of the view's width only. A mesh that lay wholly in the outer sixth on either
    /// side was not drawn at all.
    ///
    /// A hero's body is one large mesh and reaches into the middle; a weapon is a small mesh of its own. The
    /// hero of the last rank stands at the left edge of that middle, in a room and in a hallway alike, and a
    /// weapon held behind the back hand (the Plague Doctor's dagger, the Grave Robber's) lay beyond it: gone
    /// for good where the idle holds it still, and going and coming every few seconds where the idle sways it
    /// across the line. At a hallway's start, where the camera stands and the party backs away from it, the
    /// same went for the last two ranks.
    ///
    /// The fix is the cause's: while the corridor holds the main camera's rectangle, every camera of its stack
    /// has the same one (the mod's own foreground camera always did), so each looks for what it draws in the
    /// picture it draws into. They get their own back with the main camera's.
    /// </summary>
    internal partial class CorridorView
    {
        /// <summary>The cameras stacked on the main one look where it does. Off: as the mod was (dev: to show the fault).</summary>
        public static bool StackRects = true;

        private class HeldCamera
        {
            public Camera Camera;
            public Rect Rect;
            public bool Aspect;     // its aspect was set by hand too (it did not follow its rectangle)
        }

        private readonly List<HeldCamera> _stack = new List<HeldCamera>();

        // main: the camera that has the view's rectangle now; null: nobody has it, everything is given back.
        private void KeepStackRects(Camera main)
        {
            if (!StackRects) main = null;
            List<Camera> stack = null;
            if (main != null)
            {
                var data = main.GetUniversalAdditionalCameraData();
                stack = data != null ? data.cameraStack : null;
            }
            for (var i = _stack.Count - 1; i >= 0; i--)
            {
                var held = _stack[i];
                if (held.Camera != null && stack != null && stack.Contains(held.Camera)) continue;
                if (held.Camera != null)
                {
                    held.Camera.rect = held.Rect;
                    if (held.Aspect) held.Camera.ResetAspect();
                }
                _stack.RemoveAt(i);
            }
            if (stack == null) return;
            var rect = main.rect;
            foreach (var camera in stack)
            {
                if (camera == null || camera == main) continue;
                HeldCamera held = null;
                foreach (var one in _stack)
                    if (one.Camera == camera) held = one;
                if (held == null)
                {
                    if (camera.rect == rect) continue;      // it looks there already (the mod's own foreground camera)
                    held = new HeldCamera { Camera = camera, Rect = camera.rect };
                    _stack.Add(held);
                }
                if (camera.rect != rect) camera.rect = rect;
                // a camera whose aspect was set by hand does not take its rectangle's
                if (Mathf.Abs(camera.aspect - main.aspect) > 0.001f)
                {
                    camera.aspect = main.aspect;
                    held.Aspect = true;
                }
            }
        }

        /// <summary>Dev bridge: the main camera and those stacked on it: where each looks for what it draws.</summary>
        internal object DescribeCameras()
        {
            var main = Camera.main;
            if (main == null) return "no main camera";
            var list = new List<object>();
            var data = main.GetUniversalAdditionalCameraData();
            if (data != null && data.cameraStack != null)
                foreach (var camera in data.cameraStack)
                {
                    if (camera == null) continue;
                    var held = false;
                    foreach (var one in _stack) held |= one.Camera == camera;
                    list.Add(new
                    {
                        name = camera.name, on = camera.isActiveAndEnabled, rect = new[] { camera.rect.x, camera.rect.y, camera.rect.width, camera.rect.height }, aspect = camera.aspect,
                        // the share of the picture's width it looks for things in (1: all of it)
                        looksAt = Mathf.Min(1f, camera.aspect / Mathf.Max(0.01f, main.aspect)), held, mask = camera.cullingMask
                    });
                }
            return new
            {
                stackRects = StackRects, viewRect = UseViewRect,
                main = new { name = main.name, rect = new[] { main.rect.x, main.rect.y, main.rect.width, main.rect.height }, aspect = main.aspect },
                stack = list
            };
        }
    }

    /// <summary>
    /// Dev bridge: corridor.cameras {} the main camera and those stacked on it; {"stackRects":false} the stacked
    /// cameras keep their own rectangles, as the mod had it (weapons at the view's sides are not drawn).
    /// </summary>
    [EstateModule]
    internal static class CorridorCullingDev
    {
        private static void Register()
        {
            AgentBridge.Register("corridor.cameras", o =>
            {
                if (o["stackRects"] != null) CorridorView.StackRects = (bool)o["stackRects"];
                return CorridorView.Instance != null ? CorridorView.Instance.DescribeCameras() : "no corridor";
            });
        }
    }
}
