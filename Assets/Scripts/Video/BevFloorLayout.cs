using System.Collections.Generic;
using UnityEngine;
using OmniBot.VR.Control.Session;

namespace OmniBot.VR.Video
{
    /// <summary>
    /// Ground size and desk pose for the BEV floor panel. Sim and the real
    /// robot share <see cref="SimScreenCameras.BevTopic"/>. In sim that topic
    /// is the OHH-200 synthetic nadir camera. On the robot it is the stitcher.
    /// </summary>
    public struct BevFloorSpec
    {
        public bool Show;
        public bool Sim;
        public string Source;
        public float WidthMeters;
        public float DepthMeters;
        public bool ParentedToTwin;
        public float AnchorLocalY;
        public Vector3 AnchorScale;
        public Vector3 WorldPosition;
        public Quaternion WorldRotation;
        public string StreamUrl;
    }

    public static class BevFloorLayout
    {
        /// <summary>Matches the tabletop twin's 0.25 desk scale.</summary>
        public const float TabletopScale = 0.25f;

        /// <summary>World gap between the twin origin and the quad, metres.</summary>
        public const float ClearanceMeters = 0.004f;

        public const float DeskHeightMeters = 0.72f;
        public const float DeskDistanceMeters = 0.85f;

        /// <summary>OHH-200 <c>bev_camera_joint</c>: 2.2 m above <c>base_link</c>.</summary>
        public const float SimCameraHeightM = 2.2f;

        /// <summary>OHH-200 nadir camera horizontal field of view, radians.</summary>
        public const float SimHorizontalFovRad = 2.094f;

        public const int SimImageWidth = 960;
        public const int SimImageHeight = 540;

        /// <summary>Stitcher <c>bev_range_m</c>. The canvas covers ±this around the base.</summary>
        public const float RealHalfRangeM = 2f;

        public const int StreamQuality = 70;
        public const int StreamWidth = 640;

        public const string TwinObjectName = "OmniBotTwin";
        public const string TwinTypeName = "TwinView";

        public const string SimSource = "synthetic top-down camera (OHH-200)";
        public const string RealSource = "bev stitcher";

        public static bool ShouldShow(SimScreenPlan plan)
        {
            if (plan == null) return false;
            if (plan.IsSim) return true;
            return plan.IndexOf(SimScreenCameras.BevTopic) >= 0;
        }

        public static bool TrySyntheticExtent(float heightM, float hfovRad, int imageWidth, int imageHeight,
            out float widthMeters, out float depthMeters)
        {
            widthMeters = 0f;
            depthMeters = 0f;
            if (heightM <= 0f || imageWidth <= 0 || imageHeight <= 0) return false;
            if (hfovRad <= 0.05f || hfovRad >= Mathf.PI - 0.05f) return false;
            if (float.IsNaN(heightM) || float.IsNaN(hfovRad)) return false;
            float half = Mathf.Tan(hfovRad * 0.5f);
            widthMeters = 2f * heightM * half;
            depthMeters = widthMeters * imageHeight / imageWidth;
            return widthMeters > 0f && depthMeters > 0f;
        }

        /// <summary>
        /// Image width spans robot left-right. Image height spans forward-back.
        /// Image up is robot +X, which is Unity +Z.
        /// </summary>
        public static bool TryExtent(bool sim, out float widthMeters, out float depthMeters)
        {
            if (sim)
            {
                return TrySyntheticExtent(SimCameraHeightM, SimHorizontalFovRad, SimImageWidth, SimImageHeight,
                    out widthMeters, out depthMeters);
            }
            widthMeters = RealHalfRangeM * 2f;
            depthMeters = RealHalfRangeM * 2f;
            return RealHalfRangeM > 0f;
        }

        public static string StreamUrl(SimScreenPlan plan)
        {
            if (plan == null || !ShouldShow(plan)) return null;
            int index = plan.IndexOf(SimScreenCameras.BevTopic);
            if (index >= 0)
                return plan.UrlFor(index, StreamQuality, StreamWidth);
            if (!plan.IsSim || string.IsNullOrEmpty(plan.Host)) return null;
            return MjpegUrl.Build(plan.Host, MjpegUrl.Port, SimScreenCameras.BevTopic, StreamQuality, StreamWidth);
        }

        public static void DeskPose(Vector3 cameraPosition, Vector3 cameraForward, out Vector3 position, out Quaternion rotation)
        {
            Vector3 flat = cameraForward;
            flat.y = 0f;
            if (flat.sqrMagnitude < 1e-6f) flat = Vector3.forward;
            else flat.Normalize();
            position = new Vector3(cameraPosition.x, DeskHeightMeters, cameraPosition.z) + flat * DeskDistanceMeters;
            rotation = Quaternion.LookRotation(flat, Vector3.up);
        }

        public static BevFloorSpec Build(SimScreenPlan plan, Transform twin, Vector3 cameraPosition, Vector3 cameraForward)
        {
            var spec = new BevFloorSpec();
            spec.Show = ShouldShow(plan);
            spec.Sim = plan != null && plan.IsSim;
            spec.Source = spec.Sim ? SimSource : RealSource;
            spec.StreamUrl = StreamUrl(plan);
            if (!spec.Show || !TryExtent(spec.Sim, out float width, out float depth))
            {
                spec.Show = false;
                spec.WorldRotation = Quaternion.identity;
                return spec;
            }
            spec.WidthMeters = width;
            spec.DepthMeters = depth;
            spec.ParentedToTwin = twin != null;
            if (twin != null)
            {
                float scale = twin.lossyScale.x;
                if (scale < 1e-4f) scale = TabletopScale;
                spec.AnchorLocalY = -ClearanceMeters / scale;
                spec.AnchorScale = new Vector3(width, 1f, depth);
                spec.WorldRotation = Quaternion.identity;
                return spec;
            }
            spec.AnchorLocalY = 0f;
            spec.AnchorScale = new Vector3(width * TabletopScale, 1f, depth * TabletopScale);
            DeskPose(cameraPosition, cameraForward, out spec.WorldPosition, out spec.WorldRotation);
            return spec;
        }

        public static Transform FindTwin()
        {
            Transform named = FindNamed(TwinObjectName);
            if (named != null) return named;
            var behaviours = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            return FindTwinAmong(behaviours, null);
        }

        public static Transform FindTwinAmong(IList<MonoBehaviour> behaviours, IList<Transform> named)
        {
            if (named != null)
            {
                for (int i = 0; i < named.Count; i++)
                {
                    if (named[i] != null && IsTwin(named[i].name, null)) return named[i];
                }
            }
            if (behaviours == null) return null;
            for (int i = 0; i < behaviours.Count; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour != null && IsTwin(behaviour.name, behaviour.GetType().Name))
                    return behaviour.transform;
            }
            return null;
        }

        public static bool IsTwin(string objectName, string componentTypeName)
        {
            if (objectName == TwinObjectName) return true;
            return componentTypeName == TwinTypeName;
        }

        /// <summary>
        /// Unit quad on the XZ plane. UV (0,0) is image bottom-left (robot back-left).
        /// UV (0,1) is image top-left (robot forward-left). Both windings are written
        /// so the mat is visible from above and below.
        /// </summary>
        public static bool TryWriteQuad(float[] xyz, float[] uv, int[] tris)
        {
            if (xyz == null || xyz.Length < 12) return false;
            if (uv == null || uv.Length < 8) return false;
            if (tris == null || tris.Length < 12) return false;
            WriteVert(xyz, uv, 0, -0.5f, 0f, -0.5f, 0f, 0f);
            WriteVert(xyz, uv, 1, 0.5f, 0f, -0.5f, 1f, 0f);
            WriteVert(xyz, uv, 2, -0.5f, 0f, 0.5f, 0f, 1f);
            WriteVert(xyz, uv, 3, 0.5f, 0f, 0.5f, 1f, 1f);
            // Top, normal +Y.
            tris[0] = 0; tris[1] = 2; tris[2] = 1;
            tris[3] = 2; tris[4] = 3; tris[5] = 1;
            // Bottom.
            tris[6] = 0; tris[7] = 1; tris[8] = 2;
            tris[9] = 1; tris[10] = 3; tris[11] = 2;
            return true;
        }

        public static Mesh BuildMesh()
        {
            var xyz = new float[12];
            var uv = new float[8];
            var tris = new int[12];
            if (!TryWriteQuad(xyz, uv, tris)) return null;
            var mesh = new Mesh { name = "BevFloor" };
            var vertices = new Vector3[4];
            var uvs = new Vector2[4];
            for (int i = 0; i < 4; i++)
            {
                vertices[i] = new Vector3(xyz[i * 3], xyz[i * 3 + 1], xyz[i * 3 + 2]);
                uvs[i] = new Vector2(uv[i * 2], uv[i * 2 + 1]);
            }
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Transform FindNamed(string name)
        {
            var go = GameObject.Find(name);
            return go != null ? go.transform : null;
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
