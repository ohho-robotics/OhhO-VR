using System;
using System.Collections.Generic;

namespace OmniBot.VR.Twin
{
    /// <summary>1:1 AR scale, or the 0.25 tabletop "robot on the desk" scale.</summary>
    public enum TwinScaleMode
    {
        OneToOne = 0,
        Tabletop = 1,
    }

    public static class TwinScale
    {
        public const float TabletopFactor = 0.25f;

        public static float Factor(TwinScaleMode mode)
        {
            return mode == TwinScaleMode.Tabletop ? TabletopFactor : 1f;
        }
    }

    /// <summary>
    /// One ROS name rewritten onto a URDF joint name. First match wins.
    /// A name that is not listed falls through unchanged, so a partial map
    /// only renames the joints that differ.
    /// </summary>
    [Serializable]
    public class TwinJointAlias
    {
        public string RosName;
        public string UrdfName;

        public TwinJointAlias() { }

        public TwinJointAlias(string rosName, string urdfName)
        {
            RosName = rosName;
            UrdfName = urdfName;
        }
    }

    /// <summary>
    /// Topics the twin subscribes to. Strings are filled by <see cref="TwinProfiles"/>
    /// (or later by the RobotDescriptor). The animator never reads a topic itself.
    /// </summary>
    [Serializable]
    public class TwinTopics
    {
        public string Odom;
        public string Tf;
        public string[] JointStates;
        public string[] JointCommands;
    }

    /// <summary>
    /// Inputs OHH-225 will move onto the shared RobotDescriptor: a URDF path
    /// (project path or http(s) URL) and a joint-name map. Until that lands,
    /// <see cref="TwinProfiles.ForRobot"/> builds this from a <c>RobotProfile</c>.
    /// </summary>
    [Serializable]
    public class TwinProfile
    {
        public string UrdfPath;
        public string BaseFrame = "base_footprint";
        public string OdomFrame = "odom";
        public TwinScaleMode Scale = TwinScaleMode.Tabletop;
        public float StaleTimeoutSeconds = 1f;
        public TwinTopics Topics = new TwinTopics();
        public List<TwinJointAlias> JointMap = new List<TwinJointAlias>();

        public string ResolveJointName(string rosName)
        {
            if (string.IsNullOrEmpty(rosName))
                return rosName;
            if (JointMap != null)
            {
                for (int i = 0; i < JointMap.Count; i++)
                {
                    TwinJointAlias alias = JointMap[i];
                    if (alias != null && alias.RosName == rosName)
                        return string.IsNullOrEmpty(alias.UrdfName) ? rosName : alias.UrdfName;
                }
            }
            return rosName;
        }
    }
}
