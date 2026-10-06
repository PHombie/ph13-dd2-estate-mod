using System.Collections.Generic;
using Assets.Code.Actor;
using UnityEngine;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// The heroes' shadows on the corridor's floor (the owner, 2026-10-06: "the heroes' shadow comes out over the
    /// 3D model, not under it").
    ///
    /// The shadow is a part of DD2's hero model: a flat square on the ground under the hero
    /// (character/&lt;class&gt;/FakeGroundPlaneShadow, tagged "Shadow", 1.6 units a side, a millimetre above the
    /// floor), drawn after the model with a brushed mask (Red Hook/Unlit/Alpha Blend Mask, the transparent queue).
    /// Its material pulls it towards the camera in depth by a polygon offset (_OffsetFactor -30: thirty screen
    /// rows of the square's own slope), so that an arena's uneven ground does not swallow it. Seen as flat as a
    /// corridor or a fight shows the floor, thirty rows are more than half of the square: all of it that lies
    /// behind the hero's feet, where it should be hidden by the legs, comes out in front of them.
    ///
    /// A fight does not show the fault because it pulls its models forward too: DepthSorterBhv gives every
    /// actor a _DepthOffset by rank (0.6 a rank, the acting one foremost), which is farther than the shadows
    /// come; only the hindmost actor of a side has none. The corridor's models have none at all (they stand
    /// apart in real depth, <see cref="HeroDepthStep"/>), so every shadow, the hero's own and the neighbours',
    /// lay over the boots.
    ///
    /// The corridor's floor is a picture that writes no depth: nothing can swallow a shadow here, and the
    /// offset has no work to do. It is taken off the shadows of the corridor's models (their own copies of the
    /// material; the game's material is not touched, a fight's shadows stay as they are): at its true depth
    /// the square is hidden wherever a model stands in front of it, and seen on the floor everywhere else,
    /// standing, walking and changing rank alike.
    /// </summary>
    internal partial class CorridorView
    {
        /// <summary>The polygon offset the corridor gives the heroes' ground shadows (DD2's own: -30). The dev bridge can put DD2's back to compare.</summary>
        public static float ShadowOffsetFactor = 0f;

        private const string ShadowTag = "Shadow";      // CommonActorTags.TAG_SHADOW
        private static readonly int OffsetFactorId = Shader.PropertyToID("_OffsetFactor");
        private float _nextShadowCheck;
        private bool _shadowsFailed;

        // Models load after their actor is made and may be made again (a change of class): looked at twice a second.
        private void GroundShadows()
        {
            if (_shadowsFailed || Time.unscaledTime < _nextShadowCheck) return;
            _nextShadowCheck = Time.unscaledTime + 0.5f;
            try
            {
                foreach (var actor in _actors)
                {
                    if (actor == null) continue;
                    foreach (var renderer in actor.GetComponentsInChildren<Renderer>(false))
                    {
                        if (!renderer.CompareTag(ShadowTag)) continue;
                        var shared = renderer.sharedMaterial;
                        if (shared == null || !shared.HasProperty(OffsetFactorId) || Mathf.Approximately(shared.GetFloat(OffsetFactorId), ShadowOffsetFactor)) continue;
                        // the renderer's own copy: the game's material is the shadow of every actor of a fight
                        renderer.material.SetFloat(OffsetFactorId, ShadowOffsetFactor);
                    }
                }
            }
            catch (System.Exception e)
            {
                // cosmetic: never let it stop the view
                Plugin.Log.LogWarning("Corridor: the heroes' shadows could not be laid on the floor: " + e.Message);
                _shadowsFailed = true;
            }
        }

        /// <summary>Dev bridge: every hero's shadow as it is drawn now; <paramref name="factor"/> sets the offset first (DD2's own is -30).</summary>
        internal object DescribeShadows(float? factor)
        {
            if (factor.HasValue)
            {
                ShadowOffsetFactor = factor.Value;
                _shadowsFailed = false;
                _nextShadowCheck = 0f;
                GroundShadows();
            }
            var list = new List<object>();
            for (var i = 0; i < _actors.Count; i++)
            {
                var actor = _actors[i];
                if (actor == null) continue;
                foreach (var renderer in actor.GetComponentsInChildren<Renderer>(true))
                {
                    if (!renderer.CompareTag(ShadowTag)) continue;
                    var material = renderer.sharedMaterial;
                    list.Add(new
                    {
                        hero = actor.GetActorGuid(), rank = i < _actorRank.Count ? _actorRank[i] : -1, part = renderer.transform.parent != null ? renderer.transform.parent.name + "/" + renderer.name : renderer.name,
                        shown = renderer.gameObject.activeInHierarchy && renderer.enabled,
                        material = material != null ? material.name : null, queue = material != null ? material.renderQueue : -1,
                        offsetFactor = material != null && material.HasProperty(OffsetFactorId) ? material.GetFloat(OffsetFactorId) : float.NaN,
                        at = new[] { renderer.transform.position.x, renderer.transform.position.y, renderer.transform.position.z },
                        side = renderer.transform.lossyScale.x
                    });
                }
            }
            return new { wanted = ShadowOffsetFactor, failed = _shadowsFailed, shadows = list };
        }
    }
}
