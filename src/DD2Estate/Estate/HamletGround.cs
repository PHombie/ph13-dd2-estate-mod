using UnityEngine;
using UnityEngine.UI;

namespace DD2Estate.Estate
{
    /// <summary>
    /// The hamlet's ground: one texture on a quad that recedes from the viewer. uGUI has no perspective-correct
    /// quad, so the quad is drawn as a grid whose points the scene has already projected; the grid is fine
    /// enough that the straight texture interpolation inside each cell stays within a pixel of the real thing.
    /// </summary>
    internal class HamletGround : MaskableGraphic
    {
        private Texture _texture;
        private Vector2[] _points;      // (columns + 1) x (rows + 1), row by row from the near edge, in local space
        private int _columns, _rows;

        public override Texture mainTexture => _texture != null ? _texture : s_WhiteTexture;

        /// <summary>Texture u runs along a row (left to right), v from the first row (0) to the last (1).</summary>
        public void Set(Texture texture, Vector2[] points, int columns, int rows)
        {
            _texture = texture;
            _points = points;
            _columns = columns;
            _rows = rows;
            SetAllDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_points == null || _points.Length != (_columns + 1) * (_rows + 1)) return;

            Color32 tint = color;
            for (int row = 0; row <= _rows; row++)
                for (int column = 0; column <= _columns; column++)
                    vh.AddVert(_points[row * (_columns + 1) + column], tint, new Vector2((float)column / _columns, (float)row / _rows));

            for (int row = 0; row < _rows; row++)
            {
                for (int column = 0; column < _columns; column++)
                {
                    int a = row * (_columns + 1) + column;      // near-left of the cell
                    int b = a + _columns + 1;                   // far-left
                    vh.AddTriangle(a, b, b + 1);
                    vh.AddTriangle(a, b + 1, a + 1);
                }
            }
        }
    }
}
