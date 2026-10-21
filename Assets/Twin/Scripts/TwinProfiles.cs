using System.Collections.Generic;
using OmniBot.VR.Control;

namespace OmniBot.VR.Twin
{
    /// <summary>
    /// The one place twin topic names are written down. OHH-225 replaces
    /// <see cref="OmniBot"/> / <see cref="ForRobot"/> with the RobotDescriptor;
    /// callers already pass a <see cref="TwinProfile"/> (URDF path + joint map).
    ///
    /// OmniBot arm joints are the OHH-199 sim names.
    /// </summary>
    public static class TwinProfiles
    {
        public const string OmniBotUrdfPath = "Assets/Twin/URDF/omnibot.urdf.xml";

        public static TwinProfile OmniBot()
        {
            return new TwinProfile
            {
                UrdfPath = OmniBotUrdfPath,
                BaseFrame = "base_footprint",
                OdomFrame = "odom",
                Scale = TwinScaleMode.Tabletop,
                StaleTimeoutSeconds = 1f,
                Topics = new TwinTopics
                {
                    Odom = "/odom",
                    Tf = "/tf",
                    // Arm telemetry, then the wheel JointState Gazebo publishes separately.
                    JointStates = new[] { "/arm/joint_states", "/joint_states" },
                    JointCommands = new[] { "/arm/joint_commands" },
                },
                JointMap = Identity(
                    "arm_shoulder_pan",
                    "arm_shoulder_lift",
                    "arm_elbow_flex",
                    "arm_wrist_flex",
                    "arm_wrist_roll",
                    "arm_gripper",
                    "front_left_wheel_joint",
                    "front_right_wheel_joint",
                    "rear_left_wheel_joint",
                    "rear_right_wheel_joint"),
            };
        }

        /// <summary>
        /// Generic robot: URDF path plus the profile's own topics and joint names.
        /// OmniBot / OmniBot Pro keep the mecanum wheel topic from <see cref="OmniBot"/>.
        /// </summary>
        public static TwinProfile ForRobot(RobotProfile robot, string urdfPath)
        {
            if (robot != null && (robot.RobotId == "omnibot" || robot.RobotId == "omnibot-pro"))
            {
                TwinProfile omni = OmniBot();
                if (!string.IsNullOrEmpty(urdfPath))
                    omni.UrdfPath = urdfPath;
                return omni;
            }

            var profile = new TwinProfile
            {
                UrdfPath = string.IsNullOrEmpty(urdfPath) ? OmniBotUrdfPath : urdfPath,
                BaseFrame = "base_footprint",
                OdomFrame = "odom",
                Scale = TwinScaleMode.Tabletop,
                StaleTimeoutSeconds = 1f,
                Topics = new TwinTopics
                {
                    Odom = robot != null && robot.Topics != null ? robot.Topics.Odom : "",
                    Tf = "/tf",
                    JointStates = One(robot != null && robot.Topics != null ? robot.Topics.JointStates : null),
                    JointCommands = One(robot != null && robot.Topics != null ? robot.Topics.ArmCommands : null),
                },
            };
            if (robot != null && robot.Joints != null)
            {
                for (int i = 0; i < robot.Joints.Length; i++)
                {
                    string name = robot.Joints[i].Name;
                    if (!string.IsNullOrEmpty(name))
                        profile.JointMap.Add(new TwinJointAlias(name, name));
                }
            }
            return profile;
        }

        static List<TwinJointAlias> Identity(params string[] names)
        {
            var map = new List<TwinJointAlias>(names.Length);
            for (int i = 0; i < names.Length; i++)
                map.Add(new TwinJointAlias(names[i], names[i]));
            return map;
        }

        static string[] One(string topic)
        {
            return string.IsNullOrEmpty(topic) ? new string[0] : new[] { topic };
        }
    }
}
