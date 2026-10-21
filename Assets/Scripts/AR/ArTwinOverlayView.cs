using System.Collections.Generic;
using OmniBot.VR.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace OmniBot.VR.AR
{
    /// <summary>
    /// 1:1 kinematic twin. The root is the robot base in headset space.
    /// Display only: no colliders and no command publishing.
    /// </summary>
    public sealed class ArTwinOverlayView : MonoBehaviour
    {
        private readonly Dictionary<string, Transform> _joints = new Dictionary<string, Transform>();
        private ArTwinKinematics.ArmGeometry _geometry;
        private bool _built;

        public void Apply(Pose worldPose, bool visible, IReadOnlyList<ArJointReadout> joints, ArTwinKinematics.ArmGeometry geometry)
        {
            transform.SetPositionAndRotation(worldPose.position, worldPose.rotation);
            transform.localScale = Vector3.one;
            if (!_built || !SameGeometry(_geometry, geometry))
            {
                Build(geometry);
            }

            ApplyChain(joints, geometry);
            if (gameObject.activeSelf != visible)
            {
                gameObject.SetActive(visible);
            }
        }

        private void ApplyChain(IReadOnlyList<ArJointReadout> joints, ArTwinKinematics.ArmGeometry geometry)
        {
            var chain = ArTwinKinematics.Chain(joints, geometry);
            for (var i = 1; i < chain.Length; i++)
            {
                Transform joint;
                if (!_joints.TryGetValue(chain[i].Name, out joint) || joint == null)
                {
                    continue;
                }

                joint.localPosition = chain[i].LocalPosition;
                joint.localRotation = chain[i].LocalRotation;
            }
        }

        private void Build(ArTwinKinematics.ArmGeometry geometry)
        {
            ClearBuilt();
            _geometry = geometry;
            var chain = ArTwinKinematics.Chain(null, geometry);
            var parent = transform;
            for (var i = 1; i < chain.Length; i++)
            {
                var joint = new GameObject(chain[i].Name);
                joint.transform.SetParent(parent, false);
                joint.transform.localPosition = chain[i].LocalPosition;
                joint.transform.localRotation = chain[i].LocalRotation;
                _joints[chain[i].Name] = joint.transform;
                parent = joint.transform;
            }

            AddChassis();
            AddSegment(RobotConfig.ARM_JOINT_NAMES[1], geometry.UpperArm, "upper_arm");
            AddSegment(RobotConfig.ARM_JOINT_NAMES[2], geometry.Forearm, "forearm");
            AddSegment(RobotConfig.ARM_JOINT_NAMES[3], geometry.Wrist, "wrist");
            _built = true;
        }

        private void AddChassis()
        {
            var size = ArTwinKinematics.ChassisSizeMeters;
            var chassis = CreateMarker("chassis", new Color(0.15f, 0.72f, 0.95f, 1f));
            chassis.transform.SetParent(transform, false);
            chassis.transform.localPosition = new Vector3(0f, size.y * 0.5f, 0f);
            chassis.transform.localRotation = Quaternion.identity;
            chassis.transform.localScale = size;
        }

        private void AddSegment(string jointName, float length, string visualName)
        {
            Transform joint;
            if (!_joints.TryGetValue(jointName, out joint) || joint == null)
            {
                return;
            }

            var span = Mathf.Max(0.01f, length);
            var visual = CreateMarker(visualName, new Color(0.35f, 0.9f, 1f, 1f));
            visual.transform.SetParent(joint, false);
            visual.transform.localPosition = new Vector3(0f, 0f, span * 0.5f);
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = new Vector3(0.025f, 0.025f, span);
        }

        private void ClearBuilt()
        {
            for (var i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (child.name == "ArRobotHud")
                {
                    continue;
                }

                DestroyImmediate(child.gameObject);
            }

            _joints.Clear();
            _built = false;
        }

        private static Mesh _cube;

        private static GameObject CreateMarker(string name, Color color)
        {
            var marker = new GameObject(name);
            var filter = marker.AddComponent<MeshFilter>();
            filter.sharedMesh = SharedCube();
            var renderer = marker.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            var shader = Shader.Find("Unlit/Color");
            if (shader != null)
            {
                var material = new Material(shader);
                material.color = color;
                renderer.sharedMaterial = material;
            }

            return marker;
        }

        private static Mesh SharedCube()
        {
            if (_cube != null)
            {
                return _cube;
            }

            var mesh = new Mesh();
            mesh.vertices = new[]
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
            mesh.triangles = new[]
            {
                0, 2, 1, 0, 3, 2,
                4, 5, 6, 4, 6, 7,
                0, 1, 5, 0, 5, 4,
                3, 6, 2, 3, 7, 6,
                1, 2, 6, 1, 6, 5,
                0, 4, 7, 0, 7, 3,
            };
            mesh.RecalculateNormals();
            _cube = mesh;
            return mesh;
        }

        private static bool SameGeometry(ArTwinKinematics.ArmGeometry a, ArTwinKinematics.ArmGeometry b)
        {
            return a.BaseHeight == b.BaseHeight
                && a.UpperArm == b.UpperArm
                && a.Forearm == b.Forearm
                && a.Wrist == b.Wrist;
        }
    }
}
