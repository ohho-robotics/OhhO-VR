using System;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OmniBot.VR.Descriptor
{
    /// <summary>
    /// Ceilings from ohho.descriptor.v1. These are the OmniBot limits.
    /// Do not raise them. A tighter value in a document stays in force.
    /// </summary>
    public static class DescriptorCeilings
    {
        public const double MaxVxMps = 0.2;
        public const double MaxVyMps = 0.2;
        public const double MaxWzRadS = 1.0;
        public const double MaxAccelLinMps2 = 0.5;
        public const double MaxAccelYawRadS2 = 2.0;
        public const double MaxJointStepRad = 0.15;
    }

    /// <summary>
    /// ohho.descriptor.v1 document, the latched JSON on <see cref="RobotDescriptor.Topic"/>.
    /// A valid document is not permission to skip the headset e-stop or to raise a cap.
    /// </summary>
    [JsonObject(MemberSerialization = MemberSerialization.OptIn)]
    public sealed class RobotDescriptor
    {
        public const string Topic = "/ohho/descriptor";
        public const string MessageType = "std_msgs/String";

        [JsonProperty("v", Required = Required.Always)] public string V { get; set; }
        [JsonProperty("id", Required = Required.Always)] public string Id { get; set; }
        [JsonProperty("model", Required = Required.Always)] public string Model { get; set; }
        [JsonProperty("drive", Required = Required.Always)] public DescriptorDrive Drive { get; set; }
        [JsonProperty("arms", Required = Required.Always)] public DescriptorArm[] Arms { get; set; }
        [JsonProperty("topics", Required = Required.Always)] public DescriptorTopics Topics { get; set; }
        [JsonProperty("cameras", Required = Required.Always)] public DescriptorCamera[] Cameras { get; set; }
        [JsonProperty("urdf_url", Required = Required.Always)] public string UrdfUrl { get; set; }
        [JsonProperty("apriltags", Required = Required.Always)] public DescriptorAprilTag[] AprilTags { get; set; }
        [JsonProperty("telemetry", Required = Required.Always)] public DescriptorTelemetry Telemetry { get; set; }
        [JsonProperty("safety", Required = Required.Always)] public DescriptorSafety Safety { get; set; }

        /// <summary>
        /// Parse a descriptor document or a rosbridge <c>std_msgs/String</c> whose
        /// <c>data</c> field holds that document. Returns false on any schema failure.
        /// A failure must be treated as "no descriptor", never as unlimited motion.
        /// </summary>
        public static bool TryParse(string json, out RobotDescriptor descriptor, out string error)
        {
            descriptor = null;
            error = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "empty descriptor";
                return false;
            }

            try
            {
                JToken token = JToken.Parse(json);
                token = UnwrapRosString(token);
                if (!(token is JObject root))
                {
                    error = "descriptor must be an object";
                    return false;
                }

                ExpectObject(root, "descriptor", RootKeys);
                ExpectObject(root["drive"], "drive", DriveKeys);
                ExpectBool(root["drive"]["holonomic"], "drive.holonomic");
                ExpectObject(root["drive"]["caps"], "drive.caps", CapKeys);
                ExpectObject(root["safety"], "safety", SafetyKeys);
                ExpectObject(root["safety"]["hard"], "safety.hard", HardKeys);
                ExpectObject(root["topics"], "topics", TopicKeys);
                ExpectObject(root["telemetry"], "telemetry", TelemetryKeys);
                ExpectArray(root["arms"], "arms");
                ExpectArray(root["cameras"], "cameras");
                ExpectArray(root["apriltags"], "apriltags");
                ExpectArray(root["topics"]["images"], "topics.images");
                ExpectArray(root["telemetry"]["fields"], "telemetry.fields");

                foreach (var arm in (JArray)root["arms"])
                {
                    ExpectObject(arm, "arms", ArmKeys);
                    ExpectArray(arm["joints"], "arms.joints");
                    foreach (var joint in (JArray)arm["joints"])
                        ExpectObject(joint, "joints", JointKeys);
                }

                foreach (var camera in (JArray)root["cameras"])
                    ExpectObject(camera, "cameras", CameraKeys);

                foreach (var tag in (JArray)root["apriltags"])
                {
                    ExpectObject(tag, "apriltags", TagKeys);
                    ExpectInteger(tag["id"], "apriltags.id");
                    ExpectArray(tag["T_tag_base"], "apriltags.T_tag_base");
                }

                var serializer = JsonSerializer.Create(new JsonSerializerSettings
                {
                    MissingMemberHandling = MissingMemberHandling.Error,
                });
                descriptor = root.ToObject<RobotDescriptor>(serializer);
                if (descriptor == null)
                {
                    error = "descriptor JSON was null";
                    return false;
                }

                descriptor.Validate();
                return true;
            }
            catch (Exception ex)
            {
                descriptor = null;
                error = ex.Message;
                return false;
            }
        }

        /// <summary>Throws when the document breaks ohho.descriptor.v1 or a ceiling.</summary>
        public void Validate()
        {
            Text("v", V, VersionPattern);
            Text("id", Id, RobotIdPattern);
            Text("model", Model, DisplayPattern);
            if (Drive == null) throw new InvalidOperationException("drive is required");
            Drive.Validate();
            if (Arms == null) throw new InvalidOperationException("arms is required");
            foreach (var arm in Arms)
            {
                if (arm == null) throw new InvalidOperationException("arms contains null");
                arm.Validate();
            }

            if (Topics == null) throw new InvalidOperationException("topics is required");
            Topics.Validate();
            if (Cameras == null) throw new InvalidOperationException("cameras is required");
            foreach (var camera in Cameras)
            {
                if (camera == null) throw new InvalidOperationException("cameras contains null");
                camera.Validate();
                if (Array.IndexOf(Topics.Images, camera.Topic) < 0)
                    throw new InvalidOperationException("camera topic " + camera.Topic + " is not in topics.images");
            }

            Text("urdf_url", UrdfUrl, UrlPattern);
            if (AprilTags == null) throw new InvalidOperationException("apriltags is required");
            foreach (var tag in AprilTags)
            {
                if (tag == null) throw new InvalidOperationException("apriltags contains null");
                tag.Validate();
            }

            if (Telemetry == null) throw new InvalidOperationException("telemetry is required");
            Telemetry.Validate();
            if (!string.Equals(Telemetry.RobotId, Id, StringComparison.Ordinal))
                throw new InvalidOperationException("telemetry.robot_id must match id");

            if (Safety?.Hard == null)
                throw new InvalidOperationException("safety.hard is required");
            Safety.Hard.Validate();

            var hard = Safety.Hard;
            var caps = Drive.Caps;
            if (caps.VxMps > hard.MaxVxMps)
                throw new InvalidOperationException("drive.caps.vx_mps exceeds safety.hard.max_vx_mps");
            if (caps.VyMps > hard.MaxVyMps)
                throw new InvalidOperationException("drive.caps.vy_mps exceeds safety.hard.max_vy_mps");
            if (caps.WzRadS > hard.MaxWzRadS)
                throw new InvalidOperationException("drive.caps.wz_rad_s exceeds safety.hard.max_wz_rad_s");
        }

        internal static void Text(string name, string value, Regex pattern)
        {
            if (string.IsNullOrEmpty(value) || !pattern.IsMatch(value))
                throw new InvalidOperationException(name + " is invalid");
        }

        internal static void Finite(string name, double value, double minimum, double maximum)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < minimum || value > maximum + 1e-9)
                throw new InvalidOperationException(name + " is outside [" + minimum + ", " + maximum + "]");
        }

        private static JToken UnwrapRosString(JToken token)
        {
            if (token is JObject obj && obj["v"] == null && obj["data"] != null && obj["data"].Type == JTokenType.String)
            {
                string data = obj["data"].Value<string>();
                if (string.IsNullOrWhiteSpace(data))
                    throw new InvalidOperationException("std_msgs/String data is empty");
                return JToken.Parse(data);
            }

            return token;
        }

        private static void ExpectObject(JToken token, string name, string[] keys)
        {
            if (token == null || token.Type == JTokenType.Null)
                throw new InvalidOperationException(name + " is required");
            if (token.Type != JTokenType.Object)
                throw new InvalidOperationException(name + " must be an object");

            var obj = (JObject)token;
            foreach (var prop in obj.Properties())
            {
                if (Array.IndexOf(keys, prop.Name) < 0)
                    throw new InvalidOperationException(name + " has unknown field " + prop.Name);
                if (prop.Value.Type == JTokenType.Null)
                    throw new InvalidOperationException(prop.Name + " must not be null");
            }

            foreach (var key in keys)
            {
                if (obj[key] == null)
                    throw new InvalidOperationException(key + " is required");
            }
        }

        private static void ExpectArray(JToken token, string name)
        {
            if (token == null || token.Type != JTokenType.Array)
                throw new InvalidOperationException(name + " must be an array");
        }

        private static void ExpectBool(JToken token, string name)
        {
            if (token == null || token.Type != JTokenType.Boolean)
                throw new InvalidOperationException(name + " must be a boolean");
        }

        private static void ExpectInteger(JToken token, string name)
        {
            if (token == null || token.Type != JTokenType.Integer)
                throw new InvalidOperationException(name + " must be an integer");
        }

        private static readonly string[] RootKeys =
        {
            "v", "id", "model", "drive", "arms", "topics", "cameras", "urdf_url", "apriltags", "telemetry", "safety",
        };

        private static readonly string[] DriveKeys = { "kind", "holonomic", "caps" };
        private static readonly string[] CapKeys = { "vx_mps", "vy_mps", "wz_rad_s" };
        private static readonly string[] SafetyKeys = { "hard" };
        private static readonly string[] HardKeys =
        {
            "max_vx_mps", "max_vy_mps", "max_wz_rad_s",
            "max_accel_x_mps2", "max_accel_y_mps2", "max_accel_wz_rad_s2",
            "max_joint_step_rad",
        };
        private static readonly string[] TopicKeys = { "cmd", "state", "estop", "heartbeat", "telemetry", "images" };
        private static readonly string[] TelemetryKeys = { "schema_id", "robot_id", "kind", "fields" };
        private static readonly string[] ArmKeys = { "name", "joints" };
        private static readonly string[] JointKeys = { "name", "label", "min", "max", "home" };
        private static readonly string[] CameraKeys = { "name", "kind", "frame", "topic" };
        private static readonly string[] TagKeys = { "id", "size", "T_tag_base" };

        private static readonly Regex VersionPattern = new Regex(@"^1\.0\.(0|[1-9]\d*)$", RegexOptions.Compiled);
        private static readonly Regex RobotIdPattern = new Regex(@"^[A-Za-z0-9][A-Za-z0-9_.:-]{0,127}$", RegexOptions.Compiled);
        private static readonly Regex DisplayPattern = new Regex(@"^\S(?:.*\S)?$", RegexOptions.Compiled);
        private static readonly Regex UrlPattern = new Regex(@"^(package://|https://|http://)\S+$", RegexOptions.Compiled);
    }

    [JsonObject(MemberSerialization = MemberSerialization.OptIn)]
    public sealed class DescriptorDrive
    {
        [JsonProperty("kind", Required = Required.Always)] public string Kind { get; set; }
        [JsonProperty("holonomic", Required = Required.Always)] public bool Holonomic { get; set; }
        [JsonProperty("caps", Required = Required.Always)] public DescriptorDriveCaps Caps { get; set; }

        public void Validate()
        {
            if (Array.IndexOf(Kinds, Kind) < 0)
                throw new InvalidOperationException("drive.kind is invalid");
            if (Caps == null) throw new InvalidOperationException("drive.caps is required");
            Caps.Validate();
        }

        internal static readonly string[] Kinds =
        {
            "mecanum", "differential", "ackermann", "skid-steer", "tracked", "quadruped", "bipedal", "fixed-base",
        };
    }

    [JsonObject(MemberSerialization = MemberSerialization.OptIn)]
    public sealed class DescriptorDriveCaps
    {
        [JsonProperty("vx_mps", Required = Required.Always)] public double VxMps { get; set; }
        [JsonProperty("vy_mps", Required = Required.Always)] public double VyMps { get; set; }
        [JsonProperty("wz_rad_s", Required = Required.Always)] public double WzRadS { get; set; }

        public void Validate()
        {
            RobotDescriptor.Finite("drive.caps.vx_mps", VxMps, 0, DescriptorCeilings.MaxVxMps);
            RobotDescriptor.Finite("drive.caps.vy_mps", VyMps, 0, DescriptorCeilings.MaxVyMps);
            RobotDescriptor.Finite("drive.caps.wz_rad_s", WzRadS, 0, DescriptorCeilings.MaxWzRadS);
        }
    }

    [JsonObject(MemberSerialization = MemberSerialization.OptIn)]
    public sealed class DescriptorArm
    {
        [JsonProperty("name", Required = Required.Always)] public string Name { get; set; }
        [JsonProperty("joints", Required = Required.Always)] public DescriptorJoint[] Joints { get; set; }

        public void Validate()
        {
            RobotDescriptor.Text("arms.name", Name, NamePattern);
            if (Joints == null || Joints.Length < 1)
                throw new InvalidOperationException("arms.joints must contain at least one joint");
            foreach (var joint in Joints)
            {
                if (joint == null) throw new InvalidOperationException("joints contains null");
                joint.Validate();
            }
        }

        internal static readonly Regex NamePattern = new Regex(@"^[A-Za-z][A-Za-z0-9_./:-]{0,63}$", RegexOptions.Compiled);
    }

    [JsonObject(MemberSerialization = MemberSerialization.OptIn)]
    public sealed class DescriptorJoint
    {
        [JsonProperty("name", Required = Required.Always)] public string Name { get; set; }
        [JsonProperty("label", Required = Required.Always)] public string Label { get; set; }
        [JsonProperty("min", Required = Required.Always)] public double Min { get; set; }
        [JsonProperty("max", Required = Required.Always)] public double Max { get; set; }
        [JsonProperty("home", Required = Required.Always)] public double Home { get; set; }

        public void Validate()
        {
            RobotDescriptor.Text("joints.name", Name, DescriptorArm.NamePattern);
            RobotDescriptor.Text("joints.label", Label, new Regex(@"^\S(?:.*\S)?$", RegexOptions.Compiled));
            RobotDescriptor.Finite("joints.min", Min, double.NegativeInfinity, double.PositiveInfinity);
            RobotDescriptor.Finite("joints.max", Max, double.NegativeInfinity, double.PositiveInfinity);
            RobotDescriptor.Finite("joints.home", Home, double.NegativeInfinity, double.PositiveInfinity);
            if (Min > Max) throw new InvalidOperationException("joint " + Name + " min is above max");
            if (Home < Min || Home > Max)
                throw new InvalidOperationException("joint " + Name + " home is outside min/max");
        }
    }

    [JsonObject(MemberSerialization = MemberSerialization.OptIn)]
    public sealed class DescriptorTopics
    {
        [JsonProperty("cmd", Required = Required.Always)] public string Cmd { get; set; }
        [JsonProperty("state", Required = Required.Always)] public string State { get; set; }
        [JsonProperty("estop", Required = Required.Always)] public string Estop { get; set; }
        [JsonProperty("heartbeat", Required = Required.Always)] public string Heartbeat { get; set; }
        [JsonProperty("telemetry", Required = Required.Always)] public string Telemetry { get; set; }
        [JsonProperty("images", Required = Required.Always)] public string[] Images { get; set; }

        public void Validate()
        {
            Topic("topics.cmd", Cmd);
            Topic("topics.state", State);
            Topic("topics.estop", Estop);
            Topic("topics.heartbeat", Heartbeat);
            Topic("topics.telemetry", Telemetry);
            if (Images == null) throw new InvalidOperationException("topics.images is required");
            foreach (var image in Images)
                Topic("topics.images", image);
        }

        private static void Topic(string name, string value)
        {
            RobotDescriptor.Text(name, value, TopicPattern);
        }

        private static readonly Regex TopicPattern = new Regex(@"^/[A-Za-z][A-Za-z0-9_./-]*$", RegexOptions.Compiled);
    }

    [JsonObject(MemberSerialization = MemberSerialization.OptIn)]
    public sealed class DescriptorCamera
    {
        [JsonProperty("name", Required = Required.Always)] public string Name { get; set; }
        [JsonProperty("kind", Required = Required.Always)] public string Kind { get; set; }
        [JsonProperty("frame", Required = Required.Always)] public string Frame { get; set; }
        [JsonProperty("topic", Required = Required.Always)] public string Topic { get; set; }

        public void Validate()
        {
            RobotDescriptor.Text("cameras.name", Name, DescriptorArm.NamePattern);
            if (Kind != "rgb" && Kind != "depth" && Kind != "bev")
                throw new InvalidOperationException("cameras.kind is invalid");
            RobotDescriptor.Text("cameras.frame", Frame, DescriptorArm.NamePattern);
            RobotDescriptor.Text("cameras.topic", Topic, new Regex(@"^/[A-Za-z][A-Za-z0-9_./-]*$", RegexOptions.Compiled));
        }
    }

    [JsonObject(MemberSerialization = MemberSerialization.OptIn)]
    public sealed class DescriptorAprilTag
    {
        [JsonProperty("id", Required = Required.Always)] public long Id { get; set; }
        [JsonProperty("size", Required = Required.Always)] public double Size { get; set; }
        [JsonProperty("T_tag_base", Required = Required.Always)] public double[] TTagBase { get; set; }

        public void Validate()
        {
            if (Id < 0) throw new InvalidOperationException("apriltags.id must be >= 0");
            if (Size <= 0 || Size > 1)
                throw new InvalidOperationException("apriltags.size must be in (0, 1]");
            if (TTagBase == null || TTagBase.Length != 16)
                throw new InvalidOperationException("apriltags.T_tag_base must have 16 numbers");
            foreach (var value in TTagBase)
                RobotDescriptor.Finite("apriltags.T_tag_base", value, double.NegativeInfinity, double.PositiveInfinity);
        }
    }

    [JsonObject(MemberSerialization = MemberSerialization.OptIn)]
    public sealed class DescriptorTelemetry
    {
        [JsonProperty("schema_id", Required = Required.Always)] public string SchemaId { get; set; }
        [JsonProperty("robot_id", Required = Required.Always)] public string RobotId { get; set; }
        [JsonProperty("kind", Required = Required.Always)] public string Kind { get; set; }
        [JsonProperty("fields", Required = Required.Always)] public string[] Fields { get; set; }

        public void Validate()
        {
            if (SchemaId != "ohho.telemetry.v1")
                throw new InvalidOperationException("telemetry.schema_id is invalid");
            RobotDescriptor.Text("telemetry.robot_id", RobotId, new Regex(@"^[A-Za-z0-9][A-Za-z0-9_.:-]{0,127}$", RegexOptions.Compiled));
            if (Kind != "sim" && Kind != "real")
                throw new InvalidOperationException("telemetry.kind is invalid");
            if (Fields == null) throw new InvalidOperationException("telemetry.fields is required");
            foreach (var field in Fields)
            {
                if (Array.IndexOf(Blocks, field) < 0)
                    throw new InvalidOperationException("telemetry.fields has invalid block " + field);
            }
        }

        private static readonly string[] Blocks =
        {
            "base", "joints", "gripper", "imu", "battery", "nav", "policy", "rec", "sys",
        };
    }

    [JsonObject(MemberSerialization = MemberSerialization.OptIn)]
    public sealed class DescriptorSafety
    {
        [JsonProperty("hard", Required = Required.Always)] public DescriptorHardCaps Hard { get; set; }
    }

    [JsonObject(MemberSerialization = MemberSerialization.OptIn)]
    public sealed class DescriptorHardCaps
    {
        [JsonProperty("max_vx_mps", Required = Required.Always)] public double MaxVxMps { get; set; }
        [JsonProperty("max_vy_mps", Required = Required.Always)] public double MaxVyMps { get; set; }
        [JsonProperty("max_wz_rad_s", Required = Required.Always)] public double MaxWzRadS { get; set; }
        [JsonProperty("max_accel_x_mps2", Required = Required.Always)] public double MaxAccelXMps2 { get; set; }
        [JsonProperty("max_accel_y_mps2", Required = Required.Always)] public double MaxAccelYMps2 { get; set; }
        [JsonProperty("max_accel_wz_rad_s2", Required = Required.Always)] public double MaxAccelWzRadS2 { get; set; }
        [JsonProperty("max_joint_step_rad", Required = Required.Always)] public double MaxJointStepRad { get; set; }

        public void Validate()
        {
            RobotDescriptor.Finite("safety.hard.max_vx_mps", MaxVxMps, 0, DescriptorCeilings.MaxVxMps);
            RobotDescriptor.Finite("safety.hard.max_vy_mps", MaxVyMps, 0, DescriptorCeilings.MaxVyMps);
            RobotDescriptor.Finite("safety.hard.max_wz_rad_s", MaxWzRadS, 0, DescriptorCeilings.MaxWzRadS);
            RobotDescriptor.Finite("safety.hard.max_accel_x_mps2", MaxAccelXMps2, 0, DescriptorCeilings.MaxAccelLinMps2);
            RobotDescriptor.Finite("safety.hard.max_accel_y_mps2", MaxAccelYMps2, 0, DescriptorCeilings.MaxAccelLinMps2);
            RobotDescriptor.Finite("safety.hard.max_accel_wz_rad_s2", MaxAccelWzRadS2, 0, DescriptorCeilings.MaxAccelYawRadS2);
            RobotDescriptor.Finite("safety.hard.max_joint_step_rad", MaxJointStepRad, 0, DescriptorCeilings.MaxJointStepRad);
        }
    }
}
