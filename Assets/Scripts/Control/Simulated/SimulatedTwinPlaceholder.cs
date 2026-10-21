using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

[assembly: InternalsVisibleTo("Assembly-CSharp-Editor")]

namespace OmniBot.VR.Control
{
    /// <summary>
    /// Minimal stand-in for the Unity twin (OHH-211). If any type in
    /// <c>OmniBot.VR.Twin</c> is loaded, this stub does not build geometry.
    /// Otherwise it builds a 4 m room and a block robot that tracks the sim pose.
    /// ROS x/y/yaw map to Unity as (x: -y, y: up, z: x) and yaw = -theta.
    /// </summary>
    public sealed class SimulatedTwinPlaceholder : MonoBehaviour
    {
        public const string TwinNamespace = "OmniBot.VR.Twin";
        public const string RootName = "InHeadsetSimRoom";
        public const float RoomSizeMeters = 4f;
        public const float WallHeightMeters = 2f;
        public const float RobotRestHeight = 0.08f;

        /// <summary>
        /// When set, replaces the <see cref="TwinNamespace"/> scan.
        /// Tests assign <c>false</c> so the placeholder room still builds while
        /// <c>OmniBot.VR.Twin</c> is loaded, then clear the override.
        /// </summary>
        internal static bool? TwinPresentOverride { get; set; }

        readonly List<Material> _ownedMaterials = new List<Material>();
        Transform _robot;

        public static bool IsUnityTwinPresent()
        {
            if (TwinPresentOverride.HasValue)
                return TwinPresentOverride.Value;

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int a = 0; a < assemblies.Length; a++)
            {
                Type[] types;
                try { types = assemblies[a].GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types; }
                catch { continue; }

                if (types == null) continue;
                for (int i = 0; i < types.Length; i++)
                {
                    var type = types[i];
                    if (type?.Namespace == null) continue;
                    if (type.Namespace == TwinNamespace || type.Namespace.StartsWith(TwinNamespace + ".", StringComparison.Ordinal))
                        return true;
                }
            }
            return false;
        }

        /// <summary>Builds the room when the twin is absent. Returns null when OHH-211 is present.</summary>
        public static SimulatedTwinPlaceholder BuildIfNeeded(Transform parent)
        {
            if (IsUnityTwinPresent()) return null;

            var root = new GameObject(RootName);
            if (parent != null) root.transform.SetParent(parent, false);
            var view = root.AddComponent<SimulatedTwinPlaceholder>();
            view.BuildGeometry();
            return view;
        }

        public void ApplyPose(SimPose pose)
        {
            if (_robot == null) return;
            _robot.localPosition = new Vector3((float)-pose.Y, RobotRestHeight, (float)pose.X);
            _robot.localRotation = Quaternion.Euler(0f, (float)(-pose.Theta * Mathf.Rad2Deg), 0f);
        }

        void BuildGeometry()
        {
            float half = RoomSizeMeters * 0.5f;
            Cube("SimFloor", new Vector3(0f, -0.025f, 0f), new Vector3(RoomSizeMeters, 0.05f, RoomSizeMeters), new Color(0.75f, 0.75f, 0.78f));

            Cube("SimWallNorth", new Vector3(0f, WallHeightMeters * 0.5f, half), new Vector3(RoomSizeMeters, WallHeightMeters, 0.08f), new Color(0.92f, 0.90f, 0.86f));
            Cube("SimWallSouth", new Vector3(0f, WallHeightMeters * 0.5f, -half), new Vector3(RoomSizeMeters, WallHeightMeters, 0.08f), new Color(0.92f, 0.90f, 0.86f));
            Cube("SimWallEast", new Vector3(half, WallHeightMeters * 0.5f, 0f), new Vector3(0.08f, WallHeightMeters, RoomSizeMeters), new Color(0.92f, 0.90f, 0.86f));
            Cube("SimWallWest", new Vector3(-half, WallHeightMeters * 0.5f, 0f), new Vector3(0.08f, WallHeightMeters, RoomSizeMeters), new Color(0.92f, 0.90f, 0.86f));

            var robot = Cube("SimRobot", new Vector3(0f, RobotRestHeight, 0f), new Vector3(0.42f, 0.16f, 0.32f), new Color(0.15f, 0.45f, 0.85f));
            _robot = robot.transform;
            var arm = Cube("SimArmStub", new Vector3(0f, 0.22f, 0.06f), new Vector3(0.06f, 0.28f, 0.06f), new Color(0.95f, 0.55f, 0.15f));
            arm.transform.SetParent(_robot, false);
        }

        GameObject Cube(string objectName, Vector3 localPosition, Vector3 scale, Color color)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = objectName;
            cube.transform.SetParent(transform, false);
            cube.transform.localPosition = localPosition;
            cube.transform.localScale = scale;
            Tint(cube, color);
            return cube;
        }

        void Tint(GameObject target, Color color)
        {
            var renderer = target.GetComponent<Renderer>();
            if (renderer == null) return;
            var shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Diffuse");
            if (shader == null) return;
            var material = new Material(shader) { color = color };
            _ownedMaterials.Add(material);
            renderer.sharedMaterial = material;
        }

        void OnDestroy()
        {
            for (int i = 0; i < _ownedMaterials.Count; i++)
            {
                if (_ownedMaterials[i] == null) continue;
                if (Application.isPlaying) Destroy(_ownedMaterials[i]);
                else DestroyImmediate(_ownedMaterials[i]);
            }
            _ownedMaterials.Clear();
        }
    }
}
