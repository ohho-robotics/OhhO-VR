using System;

namespace OmniBot.VR.Mapping
{
    /// <summary>
    /// A pose in a 2D plane: translation plus yaw about the vertical axis.
    /// Composition is parent-then-child (the child's pose expressed in the parent).
    /// </summary>
    public struct Se2
    {
        public float X;
        public float Y;
        public float Yaw;

        public Se2(float x, float y, float yaw)
        {
            X = x;
            Y = y;
            Yaw = yaw;
        }

        public static Se2 Identity => new Se2(0f, 0f, 0f);

        public static Se2 Compose(Se2 parent, Se2 child)
        {
            double c = Math.Cos(parent.Yaw);
            double s = Math.Sin(parent.Yaw);
            return new Se2(
                (float)(parent.X + c * child.X - s * child.Y),
                (float)(parent.Y + s * child.X + c * child.Y),
                parent.Yaw + child.Yaw);
        }

        public static Se2 Inverse(Se2 t)
        {
            double c = Math.Cos(t.Yaw);
            double s = Math.Sin(t.Yaw);
            return new Se2(
                (float)(-(c * t.X + s * t.Y)),
                (float)(-(-s * t.X + c * t.Y)),
                -t.Yaw);
        }

        public static void Transform(Se2 frame, float x, float y, out float ox, out float oy)
        {
            double c = Math.Cos(frame.Yaw);
            double s = Math.Sin(frame.Yaw);
            ox = (float)(frame.X + c * x - s * y);
            oy = (float)(frame.Y + s * x + c * y);
        }
    }

    /// <summary>
    /// Latest TF edges. Each child has one parent. Pose of the child is
    /// expressed in the parent frame, which is how <c>tf2_msgs/TFMessage</c>
    /// is defined. Used to put the robot and the scan into the map frame.
    /// </summary>
    public sealed class TfBuffer
    {
        struct Edge
        {
            public string Parent;
            public string Child;
            public Se2 Pose;
        }

        readonly System.Collections.Generic.List<Edge> _edges = new System.Collections.Generic.List<Edge>(16);
        readonly System.Collections.Generic.List<Se2> _chain = new System.Collections.Generic.List<Se2>(8);

        public void Set(string parent, string child, Se2 pose)
        {
            parent = Frame(parent);
            child = Frame(child);
            if (parent.Length == 0 || child.Length == 0) return;
            for (int i = 0; i < _edges.Count; i++)
            {
                if (_edges[i].Child == child)
                {
                    var edge = _edges[i];
                    edge.Parent = parent;
                    edge.Pose = pose;
                    _edges[i] = edge;
                    return;
                }
            }
            _edges.Add(new Edge { Parent = parent, Child = child, Pose = pose });
        }

        /// <summary>Pose of <paramref name="child"/> expressed in <paramref name="frame"/>.</summary>
        public bool TryPoseOf(string child, string frame, out Se2 pose)
        {
            pose = Se2.Identity;
            child = Frame(child);
            frame = Frame(frame);
            if (child.Length == 0 || frame.Length == 0) return false;
            if (child == frame) return true;

            _chain.Clear();
            string cursor = child;
            for (int depth = 0; depth < 8; depth++)
            {
                if (!TryParent(cursor, out string parent, out Se2 childInParent))
                    return false;
                _chain.Add(childInParent);
                if (parent == frame)
                {
                    for (int i = _chain.Count - 1; i >= 0; i--)
                        pose = Se2.Compose(pose, _chain[i]);
                    return true;
                }
                cursor = parent;
            }
            return false;
        }

        public bool TryRobotInMap(out Se2 pose)
        {
            if (TryPoseOf(MapTopics.DefaultBaseFrame, MapTopics.DefaultMapFrame, out pose)) return true;
            if (TryPoseOf("base_link", MapTopics.DefaultMapFrame, out pose)) return true;
            // Before the first map→odom correction the two frames coincide.
            if (TryPoseOf(MapTopics.DefaultBaseFrame, "odom", out pose)) return true;
            if (TryPoseOf("base_link", "odom", out pose)) return true;
            pose = Se2.Identity;
            return false;
        }

        bool TryParent(string child, out string parent, out Se2 pose)
        {
            for (int i = 0; i < _edges.Count; i++)
            {
                if (_edges[i].Child == child)
                {
                    parent = _edges[i].Parent;
                    pose = _edges[i].Pose;
                    return true;
                }
            }
            parent = null;
            pose = Se2.Identity;
            return false;
        }

        public static string Frame(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            return id[0] == '/' ? id.Substring(1) : id;
        }
    }

    /// <summary>Where the pose arrow sits on the grid, and where its nose points.</summary>
    public struct ArrowPlacement
    {
        public float PixelX;
        public float PixelY;
        public float NoseX;
        public float NoseY;
        /// <summary>Yaw in pixel space (robot yaw minus the grid origin yaw).</summary>
        public float PixelYaw;
    }

    public struct MapViewState
    {
        public int Width;
        public int Height;
        public float Resolution;
        public float OriginX;
        public float OriginY;
        public float OriginYaw;
        public float RobotX;
        public float RobotY;
        public float RobotYaw;
        public bool HasRobot;
        public int MapRevision;
        public int PoseRevision;
    }
}
