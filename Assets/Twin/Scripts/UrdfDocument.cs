using System.Collections.Generic;

namespace OmniBot.VR.Twin
{
    public enum UrdfJointType
    {
        Fixed,
        Revolute,
        Continuous,
        Prismatic,
        Floating,
        Planar,
        Unknown,
    }

    public enum UrdfGeometryKind
    {
        None,
        Box,
        Cylinder,
        Sphere,
        Mesh,
    }

    public struct UrdfColor
    {
        public float R;
        public float G;
        public float B;
        public float A;
        public bool Specified;
    }

    public struct UrdfGeometry
    {
        public UrdfGeometryKind Kind;
        public float SizeX;
        public float SizeY;
        public float SizeZ;
        public float Radius;
        public float Length;
        public string MeshFilename;
        public float MeshScaleX;
        public float MeshScaleY;
        public float MeshScaleZ;
    }

    public sealed class UrdfVisual
    {
        public float OriginX;
        public float OriginY;
        public float OriginZ;
        public float Roll;
        public float Pitch;
        public float Yaw;
        public UrdfGeometry Geometry;
        public UrdfColor Color;
    }

    public sealed class UrdfLink
    {
        public string Name;
        public readonly List<UrdfVisual> Visuals = new List<UrdfVisual>();
    }

    public sealed class UrdfJoint
    {
        public string Name;
        public UrdfJointType Type;
        public string Parent;
        public string Child;
        public float OriginX;
        public float OriginY;
        public float OriginZ;
        public float Roll;
        public float Pitch;
        public float Yaw;
        public float AxisX = 1f;
        public float AxisY;
        public float AxisZ;
        public bool HasLimits;
        public float Lower;
        public float Upper;
    }

    /// <summary>Kinematic URDF. Gazebo tags and transmissions are not stored.</summary>
    public sealed class UrdfDocument
    {
        public string Name;
        public readonly List<UrdfLink> Links = new List<UrdfLink>();
        public readonly List<UrdfJoint> Joints = new List<UrdfJoint>();

        public bool TryGetLink(string name, out UrdfLink link)
        {
            for (int i = 0; i < Links.Count; i++)
            {
                if (Links[i].Name == name)
                {
                    link = Links[i];
                    return true;
                }
            }
            link = null;
            return false;
        }

        public bool TryGetJoint(string name, out UrdfJoint joint)
        {
            for (int i = 0; i < Joints.Count; i++)
            {
                if (Joints[i].Name == name)
                {
                    joint = Joints[i];
                    return true;
                }
            }
            joint = null;
            return false;
        }
    }
}
