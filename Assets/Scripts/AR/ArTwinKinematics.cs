using System.Collections.Generic;
using OmniBot.VR.Control;
using OmniBot.VR.Core;
using UnityEngine;

namespace OmniBot.VR.AR
{
    /// <summary>
    /// 1:1 arm chain in the Unity base frame. Positive shoulder lift raises the
    /// arm. Link lengths default to the SO-101 lengths in <see cref="RobotConfig"/>.
    /// </summary>
    public static class ArTwinKinematics
    {
        public static readonly Vector3 ChassisSizeMeters = new Vector3(0.30f, 0.10f, 0.40f);
        public const float GripperOffsetMeters = 0.02f;

        public readonly struct LinkPose
        {
            public readonly string Name;
            public readonly Vector3 LocalPosition;
            public readonly Quaternion LocalRotation;

            public LinkPose(string name, Vector3 localPosition, Quaternion localRotation)
            {
                Name = name;
                LocalPosition = localPosition;
                LocalRotation = localRotation;
            }
        }

        public readonly struct ArmGeometry
        {
            public readonly float BaseHeight;
            public readonly float UpperArm;
            public readonly float Forearm;
            public readonly float Wrist;

            public ArmGeometry(float baseHeight, float upperArm, float forearm, float wrist)
            {
                BaseHeight = baseHeight;
                UpperArm = upperArm;
                Forearm = forearm;
                Wrist = wrist;
            }

            public static ArmGeometry OmniBotNominal => new ArmGeometry(
                RobotConfig.ARM_BASE_HEIGHT,
                RobotConfig.ARM_LINK1_LENGTH,
                RobotConfig.ARM_LINK2_LENGTH,
                RobotConfig.ARM_LINK3_LENGTH);

            public static ArmGeometry FromProfile(RobotProfile profile)
            {
                var nominal = OmniBotNominal;
                if (profile == null)
                {
                    return nominal;
                }

                var height = profile.ArmBaseHeight > 0f ? profile.ArmBaseHeight : nominal.BaseHeight;
                var upper = nominal.UpperArm;
                var forearm = nominal.Forearm;
                var wrist = nominal.Wrist;
                var links = profile.ArmLinkLengths;
                if (links != null)
                {
                    if (links.Length > 0 && links[0] > 0f) upper = links[0];
                    if (links.Length > 1 && links[1] > 0f) forearm = links[1];
                    if (links.Length > 2 && links[2] > 0f) wrist = links[2];
                }

                return new ArmGeometry(height, upper, forearm, wrist);
            }
        }

        public static float Radians(IReadOnlyList<ArJointReadout> joints, string name)
        {
            if (joints == null || string.IsNullOrEmpty(name))
            {
                return 0f;
            }

            for (var i = 0; i < joints.Count; i++)
            {
                if (joints[i].Name == name)
                {
                    return joints[i].Radians;
                }
            }

            return 0f;
        }

        public static LinkPose[] Chain(IReadOnlyList<ArJointReadout> joints, ArmGeometry geometry)
        {
            var names = RobotConfig.ARM_JOINT_NAMES;
            var pan = Radians(joints, names[0]);
            var lift = Radians(joints, names[1]);
            var elbow = Radians(joints, names[2]);
            var flex = Radians(joints, names[3]);
            var roll = Radians(joints, names[4]);
            return new[]
            {
                new LinkPose("base", Vector3.zero, Quaternion.identity),
                new LinkPose(names[0], new Vector3(0f, geometry.BaseHeight, 0f), Quaternion.AngleAxis(pan * Mathf.Rad2Deg, Vector3.up)),
                new LinkPose(names[1], Vector3.zero, Quaternion.AngleAxis(-lift * Mathf.Rad2Deg, Vector3.right)),
                new LinkPose(names[2], new Vector3(0f, 0f, geometry.UpperArm), Quaternion.AngleAxis(-elbow * Mathf.Rad2Deg, Vector3.right)),
                new LinkPose(names[3], new Vector3(0f, 0f, geometry.Forearm), Quaternion.AngleAxis(-flex * Mathf.Rad2Deg, Vector3.right)),
                new LinkPose(names[4], new Vector3(0f, 0f, geometry.Wrist), Quaternion.AngleAxis(roll * Mathf.Rad2Deg, Vector3.forward)),
                new LinkPose(names[5], new Vector3(0f, 0f, GripperOffsetMeters), Quaternion.identity),
            };
        }

        public static Vector3 PointInBase(LinkPose[] chain, int index)
        {
            var position = Vector3.zero;
            var rotation = Quaternion.identity;
            if (chain == null)
            {
                return position;
            }

            var last = index;
            if (last >= chain.Length)
            {
                last = chain.Length - 1;
            }

            for (var i = 0; i <= last; i++)
            {
                position += rotation * chain[i].LocalPosition;
                rotation *= chain[i].LocalRotation;
            }

            return position;
        }
    }
}
