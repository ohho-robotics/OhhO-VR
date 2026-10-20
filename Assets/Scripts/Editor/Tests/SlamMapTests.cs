using System;
using Newtonsoft.Json;
using NUnit.Framework;
using OmniBot.VR.Control;
using OmniBot.VR.Core;
using OmniBot.VR.Core.Platform;
using OmniBot.VR.Mapping;
using UnityEngine;

namespace OmniBot.VR.Tests
{
    /// <summary>
    /// Edit-mode coverage for the SLAM map panel: both occupancy encodings,
    /// world/pixel transforms, the pose arrow, and the map/scan throttles.
    /// </summary>
    public class SlamMapTests
    {
        // Compact publish JSON, png-compressed by PIL the way rosbridge does
        // (square RGB image, '\n' padding, standard base64).
        const string PublishJson =
            "{\"op\":\"publish\",\"topic\":\"/map\",\"msg\":{\"header\":{\"frame_id\":\"map\"},\"info\":{\"width\":2,\"height\":2,\"resolution\":0.05,\"origin\":{\"position\":{\"x\":-1.0,\"y\":-2.0,\"z\":0.0},\"orientation\":{\"x\":0.0,\"y\":0.0,\"z\":0.0,\"w\":1.0}}},\"data\":[-1,0,100,50]}}";

        const string PilPngBase64 =
            "iVBORw0KGgoAAAANSUhEUgAAAAgAAAAKCAIAAAAGpYjXAAABBUlEQVR4nAH6AAX/AXsib/UAy7JOO0D89BH8ubm6UkNO9fSy0QKnDf7xTugKsvgR+7nHEwA8Q+319Qm/GEECADcFAP1DM0f3r9MAM+ZOuhjLvgT8RDWnBBgVsBbuQv3/vhj4CrUH9UfqPAu7zL4KAAFyZXP9BwIF/fr6ucvCDPYF/vI6Rkf49wUBIjp7ADb0UfkF9gb6ucsNAD6nGLUP9AP7BAA/pxi0ELsD+vRK9hj2DPYFCuhCQUf2/ARS6FL1Qvq5yw0A/qcAAAAAr/ZX9si3DPYCuMEGucvCDPaxAP8AAAEAAFFbBAroNDNEBDUAwPrzAdEDAA+5DvIFADEAAI3e6ADelnQ2chybQGt4AAAAAElFTkSuQmCC";

        const string RawMsgJson =
            "{\"header\":{\"frame_id\":\"map\"},\"info\":{\"width\":2,\"height\":2,\"resolution\":0.05,\"origin\":{\"position\":{\"x\":-1.0,\"y\":-2.0,\"z\":0.0},\"orientation\":{\"x\":0.0,\"y\":0.0,\"z\":0.0,\"w\":1.0}}},\"data\":[-1,0,100,50]}";

        [Test]
        public void Decode_RawInt8_ReadsCellsOriginAndColours()
        {
            var grid = new OccupancyGrid();
            Assert.IsTrue(OccupancyGridCodec.TryDecode(RawMsgJson, grid));
            AssertGrid(grid);
            Assert.AreEqual(128, OccupancyColors.ForOccupancy(grid.CellAt(0, 0)).r);
            Assert.AreEqual(255, OccupancyColors.ForOccupancy(grid.CellAt(1, 0)).r);
            Assert.AreEqual(0, OccupancyColors.ForOccupancy(grid.CellAt(0, 1)).r);
            Color32 mid = OccupancyColors.ForOccupancy(grid.CellAt(1, 1));
            Assert.AreEqual(128, mid.r);
            Assert.AreEqual(128, mid.g);
            Assert.AreEqual(OccupancyColors.Unknown, OccupancyColors.ForOccupancy(-1));
            Assert.AreEqual(OccupancyColors.Unknown, OccupancyColors.ForOccupancy(255));
            Assert.AreEqual(OccupancyColors.Free, OccupancyColors.ForOccupancy(0));
            Assert.AreEqual(OccupancyColors.Occupied, OccupancyColors.ForOccupancy(100));
        }

        [Test]
        public void Decode_RawInt8Bytes_SignedValues()
        {
            string b64 = Convert.ToBase64String(new byte[] { 255, 0, 100, 50 });
            string json =
                "{\"header\":{\"frame_id\":\"map\"},\"info\":{\"width\":2,\"height\":2,\"resolution\":0.05," +
                "\"origin\":{\"position\":{\"x\":0,\"y\":0,\"z\":0},\"orientation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1}}}," +
                "\"data\":\"" + b64 + "\"}";
            var grid = new OccupancyGrid();
            Assert.IsTrue(OccupancyGridCodec.TryDecode(json, grid));
            Assert.AreEqual(-1, grid.CellAt(0, 0));
            Assert.AreEqual(0, grid.CellAt(1, 0));
            Assert.AreEqual(100, grid.CellAt(0, 1));
            Assert.AreEqual(50, grid.CellAt(1, 1));
        }

        [Test]
        public void Decode_RosbridgePng_MatchesRawGrid()
        {
            Assert.IsTrue(RosbridgePng.TryDecodeToJson(PilPngBase64, out string json));
            Assert.AreEqual(PublishJson, json);

            string envelope = "{\"op\":\"png\",\"data\":\"" + PilPngBase64 + "\"}";
            Assert.IsTrue(RosbridgeIncoming.TryExtractPublish(envelope, out string topic, out string msg));
            Assert.AreEqual("/map", topic);

            var fromPng = new OccupancyGrid();
            var fromRaw = new OccupancyGrid();
            Assert.IsTrue(OccupancyGridCodec.TryDecode(envelope, fromPng));
            Assert.IsTrue(OccupancyGridCodec.TryDecode(msg, fromRaw));
            AssertGrid(fromPng);
            AssertGrid(fromRaw);
        }

        [Test]
        public void Decode_PngRoundTrip_PreservesJson()
        {
            string encoded = RosbridgePng.EncodeToBase64(PublishJson);
            Assert.IsTrue(RosbridgePng.TryDecodeToJson(encoded, out string decoded));
            Assert.AreEqual(PublishJson, decoded);
        }

        [Test]
        public void WorldPixel_RoundTrip_AndOriginYaw()
        {
            var grid = new OccupancyGrid
            {
                Width = 4,
                Height = 4,
                Resolution = 0.05f,
                OriginX = -1f,
                OriginY = -2f,
                OriginYaw = 0f
            };
            grid.WorldToPixel(0f, 0f, out float px, out float py);
            Assert.AreEqual(20f, px, 1e-4f);
            Assert.AreEqual(40f, py, 1e-4f);
            grid.PixelToWorld(px, py, out float wx, out float wy);
            Assert.AreEqual(0f, wx, 1e-4f);
            Assert.AreEqual(0f, wy, 1e-4f);

            grid.OriginX = 0f;
            grid.OriginY = 0f;
            grid.OriginYaw = Mathf.PI / 2f;
            grid.Resolution = 1f;
            grid.PixelToWorld(2f, 0f, out wx, out wy);
            Assert.AreEqual(0f, wx, 1e-4f);
            Assert.AreEqual(2f, wy, 1e-4f);
            grid.WorldToPixel(wx, wy, out px, out py);
            Assert.AreEqual(2f, px, 1e-4f);
            Assert.AreEqual(0f, py, 1e-4f);
        }

        [Test]
        public void PoseArrow_FromTfChain_SitsOnPixelAndNose()
        {
            var tf = new TfBuffer();
            Assert.IsTrue(PoseMessages.TryApplyTf(
                "{\"transforms\":[" +
                "{\"header\":{\"frame_id\":\"map\"},\"child_frame_id\":\"odom\",\"transform\":{\"translation\":{\"x\":1,\"y\":0,\"z\":0},\"rotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1}}}," +
                "{\"header\":{\"frame_id\":\"odom\"},\"child_frame_id\":\"base_footprint\",\"transform\":{\"translation\":{\"x\":0.5,\"y\":0,\"z\":0},\"rotation\":{\"x\":0,\"y\":0,\"z\":0.7071067811865476,\"w\":0.7071067811865476}}}" +
                "]}", tf));
            Assert.IsTrue(tf.TryRobotInMap(out Se2 pose));
            Assert.AreEqual(1.5f, pose.X, 1e-4f);
            Assert.AreEqual(0f, pose.Y, 1e-4f);
            Assert.AreEqual(Mathf.PI / 2f, pose.Yaw, 1e-3f);

            var grid = new OccupancyGrid { Width = 20, Height = 20, Resolution = 0.5f };
            ArrowPlacement arrow = OccupancyGridCodec.PlaceArrow(grid, pose.X, pose.Y, pose.Yaw, 2f);
            Assert.AreEqual(3f, arrow.PixelX, 1e-4f);
            Assert.AreEqual(0f, arrow.PixelY, 1e-4f);
            Assert.AreEqual(3f, arrow.NoseX, 1e-3f);
            Assert.AreEqual(2f, arrow.NoseY, 1e-3f);

            var odomOnly = new TfBuffer();
            Assert.IsTrue(PoseMessages.TryApplyOdom(
                "{\"header\":{\"frame_id\":\"odom\"},\"child_frame_id\":\"base_footprint\"," +
                "\"pose\":{\"pose\":{\"position\":{\"x\":1.25,\"y\":-0.5,\"z\":0},\"orientation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1}}}}",
                odomOnly, out _));
            Assert.IsTrue(odomOnly.TryRobotInMap(out Se2 odomPose));
            Assert.AreEqual(1.25f, odomPose.X, 1e-4f);
            Assert.AreEqual(-0.5f, odomPose.Y, 1e-4f);
        }

        [Test]
        public void ProjectScan_ForwardRange_LandsOnMapPixel()
        {
            var grid = new OccupancyGrid { Width = 100, Height = 100, Resolution = 0.1f };
            var scan = new LaserScanView
            {
                Ranges = new[] { 2f, float.PositiveInfinity },
                Count = 2,
                AngleMin = 0f,
                AngleIncrement = 0f,
                RangeMin = 0.05f,
                RangeMax = 10f
            };
            var xs = new int[2];
            var ys = new int[2];
            int n = OccupancyGridCodec.ProjectScan(scan, new Se2(1f, 1f, 0f), grid, xs, ys);
            Assert.AreEqual(1, n);
            Assert.AreEqual(30, xs[0]);
            Assert.AreEqual(10, ys[0]);
        }

        [Test]
        public void Tap_InsideMap_ReturnsMapFramePose()
        {
            var grid = new OccupancyGrid
            {
                Width = 4,
                Height = 2,
                Resolution = 1f,
                FrameId = "map"
            };
            Assert.IsTrue(OccupancyGridCodec.TryLocalPointToPose(
                new Rect(0f, 0f, 100f, 50f), new Vector2(25f, 25f), grid, out MapFramePose pose));
            Assert.AreEqual(1f, pose.X, 1e-4f);
            Assert.AreEqual(1f, pose.Y, 1e-4f);
            Assert.AreEqual(0f, pose.Yaw, 1e-4f);
            Assert.AreEqual("map", pose.FrameId);
            Assert.IsFalse(OccupancyGridCodec.TryLocalPointToPose(
                new Rect(0f, 0f, 100f, 50f), new Vector2(-1f, 0f), grid, out _));
        }

        [Test]
        public void Throttle_MapAt1Hz_ScanAt10Hz()
        {
            var map = new RateGate(1.0);
            Assert.IsTrue(map.TryEnter(0.0));
            Assert.IsFalse(map.TryEnter(0.0));
            Assert.IsFalse(map.TryEnter(0.999));
            Assert.IsTrue(map.TryEnter(1.0));
            Assert.IsFalse(map.TryEnter(1.5));
            Assert.IsTrue(map.TryEnter(2.0));

            var scan = new RateGate(10.0);
            Assert.IsTrue(scan.TryEnter(0.0));
            Assert.IsFalse(scan.TryEnter(0.05));
            Assert.IsTrue(scan.TryEnter(0.1));
            Assert.IsFalse(scan.TryEnter(0.19));
            Assert.IsTrue(scan.TryEnter(0.2));
        }

        [Test]
        public void Topics_MissingProfile_UsesOmniBotDefaults()
        {
            MapTopicSet topics = MapTopics.Resolve(null);
            Assert.AreEqual("/map", topics.Map);
            Assert.AreEqual("/scan", topics.Scan);
            Assert.AreEqual("/odom", topics.Odom);
            Assert.AreEqual("/global_costmap/costmap", topics.GlobalCostmap);
            Assert.AreEqual("/local_costmap/costmap", topics.LocalCostmap);

            var custom = new RobotProfile
            {
                Topics = new RobotTopics
                {
                    Map = "/robot/map",
                    Scan = "/robot/scan",
                    Odom = "/robot/odom",
                    GlobalCostmap = "/robot/global",
                    LocalCostmap = "/robot/local"
                }
            };
            MapTopicSet over = MapTopics.Resolve(custom);
            Assert.AreEqual("/robot/map", over.Map);
            Assert.AreEqual("/robot/scan", over.Scan);
            Assert.AreEqual("/robot/odom", over.Odom);
            Assert.AreEqual("/robot/global", over.GlobalCostmap);
            Assert.AreEqual("/robot/local", over.LocalCostmap);
        }

        [Test]
        public void Topics_Factory_SetsOmniBotMapScanCostmap()
        {
            var robot = new GarageRobot
            {
                UserRobot = new UserRobot { Name = "Omni" },
                Category = new VrCategory { Id = "mobile-manipulator" },
                RobotType = new VrRobotType { Id = "mm" },
                HardwareModel = new VrHardwareModel
                {
                    Id = "omnibot",
                    Name = "OmniBot",
                    Locomotion = "mecanum",
                    HasArm = true
                }
            };
            RobotProfile profile = RobotProfileFactory.FromGarageRobot(robot);
            Assert.IsNotNull(profile);
            Assert.AreEqual(MapTopics.DefaultMap, profile.Topics.Map);
            Assert.AreEqual(MapTopics.DefaultScan, profile.Topics.Scan);
            Assert.AreEqual(MapTopics.DefaultGlobalCostmap, profile.Topics.GlobalCostmap);
            Assert.AreEqual(MapTopics.DefaultLocalCostmap, profile.Topics.LocalCostmap);
        }

        [Test]
        public void Subscribe_PngThrottle_IsOnTheWire()
        {
            string json = JsonConvert.SerializeObject(
                new ROSSubscribeMessage("/map", "nav_msgs/OccupancyGrid", 1000, "png"));
            StringAssert.Contains("\"compression\":\"png\"", json);
            StringAssert.Contains("\"throttle_rate\":1000", json);
            StringAssert.Contains("\"topic\":\"/map\"", json);

            string plain = JsonConvert.SerializeObject(new ROSSubscribeMessage("/odom", "nav_msgs/Odometry"));
            Assert.IsFalse(plain.Contains("compression"));
            Assert.IsFalse(plain.Contains("throttle_rate"));
        }

        static void AssertGrid(OccupancyGrid grid)
        {
            Assert.AreEqual(2, grid.Width);
            Assert.AreEqual(2, grid.Height);
            Assert.AreEqual(0.05f, grid.Resolution, 1e-6f);
            Assert.AreEqual(-1f, grid.OriginX, 1e-4f);
            Assert.AreEqual(-2f, grid.OriginY, 1e-4f);
            Assert.AreEqual(0f, grid.OriginYaw, 1e-4f);
            Assert.AreEqual("map", grid.FrameId);
            Assert.AreEqual(-1, grid.CellAt(0, 0));
            Assert.AreEqual(0, grid.CellAt(1, 0));
            Assert.AreEqual(100, grid.CellAt(0, 1));
            Assert.AreEqual(50, grid.CellAt(1, 1));
        }
    }
}
