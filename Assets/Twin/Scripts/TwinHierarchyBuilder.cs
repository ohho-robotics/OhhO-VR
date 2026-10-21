using System.Collections.Generic;
using UnityEngine;

namespace OmniBot.VR.Twin
{
    /// <summary>One URDF link in the built hierarchy. The GameObject name is the link name.</summary>
    public sealed class TwinLinkNode : MonoBehaviour
    {
        public string LinkName;
        public string JointName;
        public bool Ghost;
    }

    public sealed class TwinBuiltHierarchy
    {
        public Transform Root;
        public readonly List<TwinLinkNode> Nodes = new List<TwinLinkNode>();
    }

    public static class TwinPoseApplicator
    {
        public static void Apply(Transform transform, TwinPose pose)
        {
            if (transform == null)
                return;
            transform.localPosition = new Vector3(pose.X, pose.Y, pose.Z);
            transform.localRotation = new Quaternion(pose.Qx, pose.Qy, pose.Qz, pose.Qw);
        }
    }

    /// <summary>
    /// Builds a link tree whose GameObject names match the URDF. Primitives are
    /// shared meshes. The ghost copy uses one transparent material.
    /// </summary>
    public static class TwinHierarchyBuilder
    {
        static Material _ghostMaterial;
        static readonly Dictionary<int, Material> ColorMaterials = new Dictionary<int, Material>();

        public static TwinBuiltHierarchy Build(UrdfDocument document, Transform parent, bool ghost, string urdfDirectory)
        {
            var rootObject = new GameObject(ghost ? "Ghost" : "Measured");
            if (parent != null)
                rootObject.transform.SetParent(parent, false);
            var built = new TwinBuiltHierarchy { Root = rootObject.transform };
            if (document == null)
                return built;

            var transforms = new Dictionary<string, Transform>();
            for (int i = 0; i < document.Links.Count; i++)
            {
                UrdfLink link = document.Links[i];
                var linkObject = new GameObject(link.Name);
                linkObject.transform.SetParent(rootObject.transform, false);
                TwinLinkNode node = linkObject.AddComponent<TwinLinkNode>();
                node.LinkName = link.Name;
                node.Ghost = ghost;
                built.Nodes.Add(node);
                transforms[link.Name] = linkObject.transform;
                AddVisuals(linkObject.transform, link, ghost, urdfDirectory);
            }

            for (int i = 0; i < document.Joints.Count; i++)
            {
                UrdfJoint joint = document.Joints[i];
                Transform child;
                Transform parentLink;
                if (!transforms.TryGetValue(joint.Child, out child) || !transforms.TryGetValue(joint.Parent, out parentLink))
                    continue;
                child.SetParent(parentLink, false);
                TwinLinkNode node = child.GetComponent<TwinLinkNode>();
                if (node != null)
                    node.JointName = joint.Name;
            }
            return built;
        }

        static void AddVisuals(Transform link, UrdfLink urdfLink, bool ghost, string urdfDirectory)
        {
            for (int i = 0; i < urdfLink.Visuals.Count; i++)
            {
                UrdfVisual visual = urdfLink.Visuals[i];
                var origin = new GameObject("visual_" + i);
                origin.transform.SetParent(link, false);
                TwinPoseApplicator.Apply(origin.transform, TwinMath.ToUnity(TwinMath.FromRpy(
                    visual.OriginX, visual.OriginY, visual.OriginZ, visual.Roll, visual.Pitch, visual.Yaw)));

                Mesh mesh = null;
                Vector3 scale = Vector3.one;
                switch (visual.Geometry.Kind)
                {
                    case UrdfGeometryKind.Box:
                        mesh = TwinPrimitives.Box();
                        scale = new Vector3(visual.Geometry.SizeY, visual.Geometry.SizeZ, visual.Geometry.SizeX);
                        break;
                    case UrdfGeometryKind.Cylinder:
                        mesh = TwinPrimitives.Cylinder();
                        scale = new Vector3(visual.Geometry.Radius * 2f, visual.Geometry.Length, visual.Geometry.Radius * 2f);
                        break;
                    case UrdfGeometryKind.Sphere:
                        mesh = TwinPrimitives.Sphere();
                        float diameter = visual.Geometry.Radius * 2f;
                        scale = new Vector3(diameter, diameter, diameter);
                        break;
                    case UrdfGeometryKind.Mesh:
                        mesh = TwinPrimitives.LoadObjFile(
                            visual.Geometry.MeshFilename,
                            urdfDirectory,
                            visual.Geometry.MeshScaleX,
                            visual.Geometry.MeshScaleY,
                            visual.Geometry.MeshScaleZ);
                        break;
                }
                if (mesh == null)
                    continue;
                var shape = new GameObject("shape");
                shape.transform.SetParent(origin.transform, false);
                shape.transform.localScale = scale;
                MeshFilter filter = shape.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                MeshRenderer renderer = shape.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = ghost ? GhostMaterial() : ColorMaterial(visual.Color);
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        static Material ColorMaterial(UrdfColor color)
        {
            if (!color.Specified)
                color = new UrdfColor { R = 0.35f, G = 0.35f, B = 0.35f, A = 1f, Specified = true };
            int key = ColorKey(color);
            Material material;
            if (ColorMaterials.TryGetValue(key, out material) && material != null)
                return material;
            material = NewMaterial(new Color(color.R, color.G, color.B, color.A <= 0f ? 1f : color.A));
            ColorMaterials[key] = material;
            return material;
        }

        static Material GhostMaterial()
        {
            if (_ghostMaterial != null)
                return _ghostMaterial;
            _ghostMaterial = NewMaterial(new Color(0.3f, 0.8f, 1f, 0.35f));
            if (_ghostMaterial == null)
                return null;
            _ghostMaterial.SetFloat("_Mode", 3f);
            _ghostMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _ghostMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _ghostMaterial.SetInt("_ZWrite", 0);
            _ghostMaterial.DisableKeyword("_ALPHATEST_ON");
            _ghostMaterial.EnableKeyword("_ALPHABLEND_ON");
            _ghostMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            _ghostMaterial.renderQueue = 3000;
            return _ghostMaterial;
        }

        static Material NewMaterial(Color color)
        {
            Shader shader = Shader.Find("Standard");
            if (shader == null)
                shader = Shader.Find("Diffuse");
            if (shader == null)
                shader = Shader.Find("Hidden/InternalErrorShader");
            if (shader == null)
                return null;
            var material = new Material(shader);
            material.hideFlags = HideFlags.HideAndDontSave;
            material.color = color;
            return material;
        }

        static int ColorKey(UrdfColor color)
        {
            int r = Mathf.RoundToInt(color.R * 255f);
            int g = Mathf.RoundToInt(color.G * 255f);
            int b = Mathf.RoundToInt(color.B * 255f);
            int a = Mathf.RoundToInt((color.A <= 0f ? 1f : color.A) * 255f);
            return (r << 24) | (g << 16) | (b << 8) | a;
        }
    }
}
