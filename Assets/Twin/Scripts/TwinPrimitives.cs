using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace OmniBot.VR.Twin
{
    /// <summary>
    /// Unit primitives used by every URDF visual. Box is 1×1×1, cylinder is
    /// radius 0.5 and height 1 along Y (URDF cylinder Z after the frame
    /// conversion), sphere is radius 0.5. Triangle counts are the Quest budget.
    /// </summary>
    public static class TwinPrimitives
    {
        public const int CylinderSegments = 12;
        public const int SphereStacks = 6;
        public const int SphereSlices = 8;
        public const int BoxTriangles = 12;

        static Mesh _box;
        static Mesh _cylinder;
        static Mesh _sphere;
        static readonly Dictionary<string, Mesh> ObjCache = new Dictionary<string, Mesh>();

        public static int CylinderTriangles => CylinderSegments * 4;
        public static int SphereTriangles => SphereStacks * SphereSlices * 2;

        public static int TrianglesFor(UrdfGeometryKind kind)
        {
            switch (kind)
            {
                case UrdfGeometryKind.Box: return BoxTriangles;
                case UrdfGeometryKind.Cylinder: return CylinderTriangles;
                case UrdfGeometryKind.Sphere: return SphereTriangles;
                default: return 0;
            }
        }

        public static Mesh Box()
        {
            if (_box != null)
                return _box;
            var verts = new Vector3[]
            {
                new Vector3(-0.5f, -0.5f, -0.5f),
                new Vector3(0.5f, -0.5f, -0.5f),
                new Vector3(0.5f, 0.5f, -0.5f),
                new Vector3(-0.5f, 0.5f, -0.5f),
                new Vector3(-0.5f, -0.5f, 0.5f),
                new Vector3(0.5f, -0.5f, 0.5f),
                new Vector3(0.5f, 0.5f, 0.5f),
                new Vector3(-0.5f, 0.5f, 0.5f),
            };
            int[] tris =
            {
                0, 2, 1, 0, 3, 2,
                4, 5, 6, 4, 6, 7,
                0, 1, 5, 0, 5, 4,
                1, 2, 6, 1, 6, 5,
                2, 3, 7, 2, 7, 6,
                3, 0, 4, 3, 4, 7,
            };
            _box = Finish("twin-box", verts, tris);
            return _box;
        }

        public static Mesh Cylinder()
        {
            if (_cylinder != null)
                return _cylinder;
            int segments = CylinderSegments;
            var verts = new List<Vector3>((segments + 1) * 2 + 2);
            var tris = new List<int>(segments * 12);
            for (int i = 0; i <= segments; i++)
            {
                float angle = (float)(Math.PI * 2.0 * i / segments);
                float x = 0.5f * Mathf.Cos(angle);
                float z = 0.5f * Mathf.Sin(angle);
                verts.Add(new Vector3(x, -0.5f, z));
                verts.Add(new Vector3(x, 0.5f, z));
            }
            for (int i = 0; i < segments; i++)
            {
                int bottom = i * 2;
                int top = bottom + 1;
                int nextBottom = bottom + 2;
                int nextTop = bottom + 3;
                tris.Add(bottom);
                tris.Add(nextBottom);
                tris.Add(top);
                tris.Add(top);
                tris.Add(nextBottom);
                tris.Add(nextTop);
            }
            int topCenter = verts.Count;
            verts.Add(new Vector3(0f, 0.5f, 0f));
            int bottomCenter = verts.Count;
            verts.Add(new Vector3(0f, -0.5f, 0f));
            for (int i = 0; i < segments; i++)
            {
                tris.Add(topCenter);
                tris.Add(i * 2 + 1);
                tris.Add((i + 1) * 2 + 1);
                tris.Add(bottomCenter);
                tris.Add((i + 1) * 2);
                tris.Add(i * 2);
            }
            _cylinder = Finish("twin-cylinder", verts.ToArray(), tris.ToArray());
            return _cylinder;
        }

        public static Mesh Sphere()
        {
            if (_sphere != null)
                return _sphere;
            int stacks = SphereStacks;
            int slices = SphereSlices;
            int stride = slices + 1;
            var verts = new List<Vector3>((stacks + 1) * stride);
            var tris = new List<int>(SphereTriangles * 3);
            for (int stack = 0; stack <= stacks; stack++)
            {
                float v = stack / (float)stacks;
                float phi = Mathf.PI * (v - 0.5f);
                float y = 0.5f * Mathf.Sin(phi);
                float radius = 0.5f * Mathf.Cos(phi);
                for (int slice = 0; slice <= slices; slice++)
                {
                    float theta = (float)(Math.PI * 2.0 * slice / slices);
                    verts.Add(new Vector3(radius * Mathf.Cos(theta), y, radius * Mathf.Sin(theta)));
                }
            }
            for (int stack = 0; stack < stacks; stack++)
            {
                for (int slice = 0; slice < slices; slice++)
                {
                    int a = stack * stride + slice;
                    int b = a + 1;
                    int c = a + stride;
                    int d = c + 1;
                    tris.Add(a);
                    tris.Add(c);
                    tris.Add(b);
                    tris.Add(b);
                    tris.Add(c);
                    tris.Add(d);
                }
            }
            _sphere = Finish("twin-sphere", verts.ToArray(), tris.ToArray());
            return _sphere;
        }

        /// <summary>
        /// OBJ vertices are in the ROS link frame. They are converted with the
        /// same reflection as <see cref="TwinMath.ToUnity"/>, then the winding
        /// is flipped so the outside stays the outside.
        /// </summary>
        public static Mesh MeshFromObj(string obj, float scaleX, float scaleY, float scaleZ)
        {
            if (string.IsNullOrEmpty(obj))
                return null;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            string[] lines = obj.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line[0] == '#')
                    continue;
                if (line.StartsWith("v ", StringComparison.Ordinal))
                {
                    string[] parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 4)
                        continue;
                    float x = Parse(parts[1]) * scaleX;
                    float y = Parse(parts[2]) * scaleY;
                    float z = Parse(parts[3]) * scaleZ;
                    verts.Add(new Vector3(-y, z, x));
                }
                else if (line.StartsWith("f ", StringComparison.Ordinal))
                {
                    string[] parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    var ids = new List<int>(parts.Length - 1);
                    for (int p = 1; p < parts.Length; p++)
                    {
                        string token = parts[p];
                        int slash = token.IndexOf('/');
                        if (slash >= 0)
                            token = token.Substring(0, slash);
                        int index;
                        if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
                            continue;
                        if (index < 0)
                            index = verts.Count + index;
                        else
                            index = index - 1;
                        if (index >= 0 && index < verts.Count)
                            ids.Add(index);
                    }
                    for (int t = 1; t + 1 < ids.Count; t++)
                    {
                        // Flip winding: ROS to Unity is a reflection.
                        tris.Add(ids[0]);
                        tris.Add(ids[t + 1]);
                        tris.Add(ids[t]);
                    }
                }
            }
            if (verts.Count == 0 || tris.Count == 0)
                return null;
            return Finish("twin-obj", verts.ToArray(), tris.ToArray(), forceOutward: false);
        }

        public static Mesh LoadObjFile(string filename, string urdfDirectory, float scaleX, float scaleY, float scaleZ)
        {
            string path = ResolveMeshPath(filename, urdfDirectory);
            if (string.IsNullOrEmpty(path))
                return null;
            string key = path + "|" + scaleX.ToString(CultureInfo.InvariantCulture)
                + "|" + scaleY.ToString(CultureInfo.InvariantCulture)
                + "|" + scaleZ.ToString(CultureInfo.InvariantCulture);
            Mesh cached;
            if (ObjCache.TryGetValue(key, out cached) && cached != null)
                return cached;
            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (Exception)
            {
                return null;
            }
            Mesh mesh = MeshFromObj(text, scaleX, scaleY, scaleZ);
            if (mesh != null)
                ObjCache[key] = mesh;
            return mesh;
        }

        public static string ResolveMeshPath(string filename, string urdfDirectory)
        {
            if (string.IsNullOrEmpty(filename))
                return null;
            string name = filename.Replace('\\', '/');
            if (Path.IsPathRooted(name) && File.Exists(name))
                return name;

            string objName = Path.GetFileNameWithoutExtension(name) + ".obj";
            string so101 = Path.Combine(Application.dataPath, "Twin", "Meshes", "so101", objName);
            if (name.StartsWith("package://", StringComparison.Ordinal) && File.Exists(so101))
                return so101;

            if (!string.IsNullOrEmpty(urdfDirectory))
            {
                string relative = Path.GetFullPath(Path.Combine(urdfDirectory, name));
                if (File.Exists(relative))
                    return relative;
                string asObj = Path.ChangeExtension(relative, ".obj");
                if (File.Exists(asObj))
                    return asObj;
            }
            return File.Exists(so101) ? so101 : null;
        }

        static float Parse(string text)
        {
            float value;
            float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            return value;
        }

        static Mesh Finish(string meshName, Vector3[] vertices, int[] triangles, bool forceOutward = true)
        {
            var mesh = new Mesh();
            mesh.name = meshName;
            mesh.hideFlags = HideFlags.HideAndDontSave;
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            if (forceOutward)
                EnsureOutward(mesh);
            else
            {
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
            }
            return mesh;
        }

        static void EnsureOutward(Mesh mesh)
        {
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            bool outward = true;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 a = vertices[triangles[i]];
                Vector3 b = vertices[triangles[i + 1]];
                Vector3 c = vertices[triangles[i + 2]];
                Vector3 normal = Vector3.Cross(b - a, c - a);
                Vector3 center = (a + b + c) / 3f;
                if (normal.sqrMagnitude < 1e-10f || center.sqrMagnitude < 1e-8f)
                    continue;
                outward = Vector3.Dot(normal, center) >= 0f;
                break;
            }
            if (!outward)
            {
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    int swap = triangles[i + 1];
                    triangles[i + 1] = triangles[i + 2];
                    triangles[i + 2] = swap;
                }
                mesh.triangles = triangles;
            }
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }
    }
}
