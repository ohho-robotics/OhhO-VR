using System;
using UnityEngine;

namespace OmniBot.VR.Video
{
    /// <summary>
    /// Cylindrical screen. The viewer sits on the -Z side. Edges bend toward
    /// the viewer (smaller Z) so the panel reads as a curved monitor. Units are
    /// whatever the caller uses; the sim screen passes canvas pixels.
    /// </summary>
    public static class CurvedScreenGeometry
    {
        public static int VertexCount(int columns)
        {
            columns = ClampColumns(columns);
            return (columns + 1) * 2;
        }

        public static int TriangleIndexCount(int columns)
        {
            columns = ClampColumns(columns);
            return columns * 6;
        }

        public static bool TryWrite(float width, float height, float radius, int columns,
            float[] xyz, float[] uv, int[] tris)
        {
            if (width <= 0f || height <= 0f) return false;
            if (float.IsNaN(width) || float.IsNaN(height) || float.IsNaN(radius)) return false;
            columns = ClampColumns(columns);
            if (radius < 1e-3f) radius = 1e6f;
            int verts = (columns + 1) * 2;
            if (xyz == null || xyz.Length < verts * 3) return false;
            if (uv == null || uv.Length < verts * 2) return false;
            if (tris == null || tris.Length < columns * 6) return false;

            float halfW = width * 0.5f;
            float halfH = height * 0.5f;
            int n = columns + 1;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)columns;
                float s = -halfW + width * t;
                float theta = s / radius;
                float x = (float)(Math.Sin(theta) * radius);
                float z = (float)(radius * (Math.Cos(theta) - 1.0));
                WriteVert(xyz, uv, i, x, -halfH, z, t, 0f);
                WriteVert(xyz, uv, n + i, x, halfH, z, t, 1f);
            }
            int ti = 0;
            for (int i = 0; i < columns; i++)
            {
                int b0 = i;
                int b1 = i + 1;
                int t0 = n + i;
                int t1 = n + i + 1;
                tris[ti++] = b0;
                tris[ti++] = t0;
                tris[ti++] = b1;
                tris[ti++] = b1;
                tris[ti++] = t0;
                tris[ti++] = t1;
            }
            return true;
        }

        public static Mesh BuildMesh(float width, float height, float radius, int columns)
        {
            columns = ClampColumns(columns);
            int verts = VertexCount(columns);
            var xyz = new float[verts * 3];
            var uv = new float[verts * 2];
            var tris = new int[TriangleIndexCount(columns)];
            if (!TryWrite(width, height, radius, columns, xyz, uv, tris)) return null;
            var mesh = new Mesh();
            mesh.name = "SimScreenCurve";
            var vertices = new Vector3[verts];
            var uvs = new Vector2[verts];
            for (int i = 0; i < verts; i++)
            {
                vertices[i] = new Vector3(xyz[i * 3], xyz[i * 3 + 1], xyz[i * 3 + 2]);
                uvs[i] = new Vector2(uv[i * 2], uv[i * 2 + 1]);
            }
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }

        public static int ClampColumns(int columns)
        {
            if (columns < 2) return 2;
            if (columns > 64) return 64;
            return columns;
        }

        private static void WriteVert(float[] xyz, float[] uv, int index, float x, float y, float z, float u, float v)
        {
            int p = index * 3;
            xyz[p] = x;
            xyz[p + 1] = y;
            xyz[p + 2] = z;
            uv[index * 2] = u;
            uv[index * 2 + 1] = v;
        }
    }
}
