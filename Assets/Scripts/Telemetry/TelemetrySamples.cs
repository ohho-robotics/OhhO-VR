namespace OmniBot.VR.Telemetry
{
    /// <summary>Schema-valid ohho.telemetry.v1 documents for the HUD tests and for manual checks.</summary>
    public static class TelemetrySamples
    {
        public const string Full =
            "{\"v\":\"1.0.0\",\"t\":1735689600.5,\"robot_id\":\"omnibot-sim\",\"kind\":\"sim\"," +
            "\"mode\":{\"base\":\"teleop\",\"arm\":\"teleop\"}," +
            "\"estop\":{\"active\":false,\"latched\":false,\"source\":\"none\"},\"deadman\":true," +
            "\"link\":{\"rtt_ms\":12.5,\"hz\":20}," +
            "\"base\":{\"cmd\":{\"vx\":0.1,\"vy\":0,\"wz\":0.2},\"meas\":{\"vx\":0.08,\"vy\":-0.01,\"wz\":0.18}," +
            "\"pose\":{\"x\":1.25,\"y\":-0.4,\"yaw\":0.5},\"frame\":\"odom\"}," +
            "\"joints\":[" +
            "{\"name\":\"arm_shoulder_pan\",\"pos\":0.2,\"vel\":0.1,\"effort\":0.02,\"cmd\":0.25,\"min\":-3.14,\"max\":3.14}," +
            "{\"name\":\"arm_elbow_flex\",\"pos\":0,\"vel\":0,\"effort\":0,\"cmd\":0,\"min\":-1.57,\"max\":1.57}" +
            "]," +
            "\"gripper\":{\"pos\":0.02,\"cmd\":0.03}," +
            "\"imu\":{\"roll\":0.1,\"pitch\":-0.2,\"yaw\":0.3,\"ax\":0,\"ay\":0,\"az\":9.8}," +
            "\"battery\":{\"v\":12.6,\"pct\":80,\"simulated\":true}," +
            "\"nav\":{\"state\":\"navigating\",\"goal\":{\"x\":2,\"y\":3,\"yaw\":0.1},\"eta\":4.5}," +
            "\"policy\":{\"name\":\"smolvla\",\"state\":\"running\",\"latency_ms\":18}," +
            "\"rec\":{\"active\":true,\"episode\":\"ep1\",\"frames\":120}," +
            "\"sys\":{\"cpu\":22.5,\"temp\":48.2}," +
            "\"age_ms\":{\"base\":10,\"base.cmd\":10,\"base.meas\":12,\"base.pose\":12,\"joints\":20," +
            "\"joints.arm_shoulder_pan\":20,\"joints.arm_elbow_flex\":20,\"gripper\":15,\"imu\":30," +
            "\"battery\":100,\"nav\":40,\"policy\":50,\"rec\":60,\"sys\":70,\"mode\":80,\"mode.base\":80," +
            "\"mode.arm\":90,\"estop\":100,\"deadman\":100,\"link\":0}}";

        public const string RequiredOnly =
            "{\"v\":\"1.0.0\",\"t\":10,\"robot_id\":\"omnibot-sim\",\"kind\":\"sim\"," +
            "\"mode\":{\"base\":\"idle\",\"arm\":\"not_present\"}," +
            "\"estop\":{\"active\":false,\"latched\":false,\"source\":\"none\"},\"deadman\":false," +
            "\"link\":{\"rtt_ms\":0,\"hz\":0}}";

        public const string NearLimit =
            "{\"v\":\"1.0.0\",\"t\":11,\"robot_id\":\"omnibot-sim\",\"kind\":\"sim\"," +
            "\"mode\":{\"base\":\"teleop\",\"arm\":\"teleop\"}," +
            "\"estop\":{\"active\":false,\"latched\":false,\"source\":\"none\"},\"deadman\":true," +
            "\"link\":{\"rtt_ms\":1,\"hz\":20}," +
            "\"joints\":[{\"name\":\"arm_shoulder_pan\",\"pos\":3.0,\"vel\":0,\"effort\":0,\"cmd\":2.9,\"min\":-3.14,\"max\":3.14}]," +
            "\"age_ms\":{\"joints\":10,\"joints.arm_shoulder_pan\":10,\"estop\":10,\"deadman\":10,\"link\":0,\"mode\":10}}";

        public const string AmberJoint =
            "{\"v\":\"1.0.0\",\"t\":12,\"robot_id\":\"omnibot-sim\",\"kind\":\"sim\"," +
            "\"mode\":{\"base\":\"teleop\",\"arm\":\"teleop\"}," +
            "\"estop\":{\"active\":false,\"latched\":false,\"source\":\"none\"},\"deadman\":true," +
            "\"link\":{\"rtt_ms\":1,\"hz\":20}," +
            "\"joints\":[{\"name\":\"arm_wrist_flex\",\"pos\":1.15,\"vel\":0.2,\"effort\":0.3,\"cmd\":0.4,\"min\":-1.57,\"max\":1.57}]," +
            "\"age_ms\":{\"joints\":10,\"joints.arm_wrist_flex\":10,\"estop\":10,\"deadman\":10,\"link\":0,\"mode\":10}}";

        public const string SchemaSafety =
            "{\"v\":\"1.0.0\",\"t\":20,\"robot_id\":\"omnibot\",\"kind\":\"real\"," +
            "\"mode\":{\"base\":\"fault\",\"arm\":\"idle\"}," +
            "\"estop\":{\"active\":true,\"latched\":true,\"source\":\"hardware\"},\"deadman\":false," +
            "\"link\":{\"rtt_ms\":4,\"hz\":10}}";

        public const string GateTrip = "{\"armed\":false,\"latched\":true,\"reason\":\"heartbeat_watchdog\"}";

        public const string GatePermit = "{\"armed\":true,\"latched\":false,\"reason\":\"\"}";

        public const string GateNumericBool = "{\"armed\":1,\"latched\":false}";
    }
}
