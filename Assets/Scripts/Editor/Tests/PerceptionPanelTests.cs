using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using OmniBot.VR.Control.Session;
using OmniBot.VR.Video;

namespace OmniBot.VR.Tests
{
    /// <summary>
    /// BEV floor panel under the tabletop twin, the BEV camera tab, and the
    /// front-camera detection overlay. The overlay stays off: OmniBot does not
    /// publish vision_msgs/Detection2DArray.
    /// </summary>
    public class PerceptionPanelTests
    {
        [Test]
        public void SimBev_IsTheSyntheticNadirCamera()
        {
            Assert.IsTrue(BevFloorLayout.TryExtent(true, out float width, out float depth));
            float expectedWidth = 2f * BevFloorLayout.SimCameraHeightM * Mathf.Tan(BevFloorLayout.SimHorizontalFovRad * 0.5f);
            Assert.AreEqual(2.2f, BevFloorLayout.SimCameraHeightM, 1e-4f);
            Assert.AreEqual(2.094f, BevFloorLayout.SimHorizontalFovRad, 1e-4f);
            Assert.AreEqual(960, BevFloorLayout.SimImageWidth);
            Assert.AreEqual(540, BevFloorLayout.SimImageHeight);
            Assert.AreEqual(expectedWidth, width, 1e-3f);
            Assert.AreEqual(expectedWidth * 540f / 960f, depth, 1e-3f);
            Assert.Greater(width, depth);
            Assert.AreEqual(SimScreenCameras.BevTopic, "/camera/base/bev/image_raw");
        }

        [Test]
        public void RealBev_IsTheStitcherSquare()
        {
            Assert.IsTrue(BevFloorLayout.TryExtent(false, out float width, out float depth));
            Assert.AreEqual(4f, width, 1e-4f);
            Assert.AreEqual(4f, depth, 1e-4f);
            Assert.AreEqual(BevFloorLayout.RealSource, "bev stitcher");
        }

        [Test]
        public void SyntheticExtent_RejectsBadCamera()
        {
            Assert.IsFalse(BevFloorLayout.TrySyntheticExtent(0f, 1f, 960, 540, out _, out _));
            Assert.IsFalse(BevFloorLayout.TrySyntheticExtent(2.2f, 0f, 960, 540, out _, out _));
            Assert.IsFalse(BevFloorLayout.TrySyntheticExtent(2.2f, Mathf.PI, 960, 540, out _, out _));
            Assert.IsFalse(BevFloorLayout.TrySyntheticExtent(2.2f, 1f, 0, 540, out _, out _));
            Assert.IsFalse(BevFloorLayout.TrySyntheticExtent(float.NaN, 1f, 960, 540, out _, out _));
        }

        [Test]
        public void Bev_IsACameraTab_ForSimAndReal()
        {
            var sim = SimScreenCameras.ViewsFor(SessionKind.Sim, new[]
            {
                SimScreenCameras.FrontTopic,
                SimScreenCameras.WristTopic,
                SimScreenCameras.BevTopic,
            });
            Assert.AreEqual("BEV", sim[sim.Count - 1].Label);
            Assert.AreEqual("bev", sim[sim.Count - 1].Id);
            Assert.AreEqual(SimScreenCameras.BevTopic, sim[sim.Count - 1].Topic);

            var real = SimScreenCameras.ViewsFor(SessionKind.Real, new[]
            {
                SimScreenCameras.FrontTopic,
                SimScreenCameras.BevTopic,
            });
            Assert.AreEqual(SimScreenCameras.FrontTopic, real[0].Topic);
            Assert.AreEqual("BEV", real[1].Label);
            Assert.AreEqual(SimScreenCameras.BevTopic, real[1].Topic);

            var plan = SimScreenPlan.Build(SessionKind.Real, new[]
            {
                SimScreenCameras.FrontTopic,
                SimScreenCameras.BevTopic,
            }, "10.0.0.8", null);
            Assert.AreEqual(1, plan.IndexOf(SimScreenCameras.BevTopic));
            Assert.AreEqual(-1, plan.IndexOf(null));
            Assert.AreEqual(-1, plan.IndexOf("/camera/missing"));
        }

        [Test]
        public void FloorStream_UsesTheBevTopic()
        {
            var sim = SimScreenPlan.Build(SessionKind.Sim, new[]
            {
                SimScreenCameras.FrontTopic,
                SimScreenCameras.WristTopic,
                SimScreenCameras.BevTopic,
            }, "192.168.1.101", null);
            Assert.AreEqual(
                "http://192.168.1.101:8080/stream?topic=/camera/base/bev/image_raw&quality=70&width=640",
                BevFloorLayout.StreamUrl(sim));

            var omitted = SimScreenPlan.Build(SessionKind.Sim, new[] { SimScreenCameras.FrontTopic }, "192.168.1.101", null);
            Assert.AreEqual(-1, omitted.IndexOf(SimScreenCameras.BevTopic));
            Assert.IsTrue(BevFloorLayout.ShouldShow(omitted));
            Assert.AreEqual(
                "http://192.168.1.101:8080/stream?topic=/camera/base/bev/image_raw&quality=70&width=640",
                BevFloorLayout.StreamUrl(omitted));

            var urls = new[] { "http://10.0.0.8:8080/stream?topic=/camera/base/bev/image_raw" };
            var retarget = SimScreenPlan.Build(SessionKind.Sim, new[] { SimScreenCameras.BevTopic }, "", urls);
            string url = BevFloorLayout.StreamUrl(retarget);
            StringAssert.StartsWith("http://10.0.0.8:8080/stream?topic=/camera/base/bev/image_raw", url);
            StringAssert.Contains("quality=70", url);
            StringAssert.Contains("width=640", url);

            var noHost = SimScreenPlan.Build(SessionKind.Sim, new string[0], "", null);
            Assert.IsTrue(BevFloorLayout.ShouldShow(noHost));
            Assert.IsNull(BevFloorLayout.StreamUrl(noHost));
            Assert.IsNull(BevFloorLayout.StreamUrl(null));
        }

        [Test]
        public void RealFloor_FollowsTheProfile()
        {
            var withBev = SimScreenPlan.Build(SessionKind.Real, new[]
            {
                SimScreenCameras.FrontTopic,
                SimScreenCameras.BevTopic,
            }, "10.0.0.8", null);
            Assert.IsTrue(BevFloorLayout.ShouldShow(withBev));
            var spec = BevFloorLayout.Build(withBev, null, Vector3.zero, Vector3.forward);
            Assert.IsTrue(spec.Show);
            Assert.IsFalse(spec.Sim);
            Assert.AreEqual(BevFloorLayout.RealSource, spec.Source);
            Assert.AreEqual(4f, spec.WidthMeters, 1e-4f);
            Assert.AreEqual(4f * BevFloorLayout.TabletopScale, spec.AnchorScale.x, 1e-4f);
            Assert.AreEqual(
                "http://10.0.0.8:8080/stream?topic=/camera/base/bev/image_raw&quality=70&width=640",
                spec.StreamUrl);

            var frontOnly = SimScreenPlan.Build(SessionKind.Real, new[] { SimScreenCameras.FrontTopic }, "10.0.0.8", null);
            Assert.IsFalse(BevFloorLayout.ShouldShow(frontOnly));
            Assert.IsFalse(BevFloorLayout.Build(frontOnly, null, Vector3.zero, Vector3.forward).Show);
            Assert.IsNull(BevFloorLayout.StreamUrl(frontOnly));
        }

        [Test]
        public void DeskPose_SitsInFrontOfTheHeadset()
        {
            BevFloorLayout.DeskPose(Vector3.zero, Vector3.forward, out Vector3 ahead, out Quaternion rotation);
            Assert.AreEqual(0f, ahead.x, 1e-4f);
            Assert.AreEqual(BevFloorLayout.DeskHeightMeters, ahead.y, 1e-4f);
            Assert.AreEqual(BevFloorLayout.DeskDistanceMeters, ahead.z, 1e-4f);
            Assert.Less(Quaternion.Angle(rotation, Quaternion.identity), 0.1f);

            BevFloorLayout.DeskPose(new Vector3(1f, 1.6f, 2f), Vector3.back, out Vector3 behind, out Quaternion turned);
            Assert.AreEqual(1f, behind.x, 1e-4f);
            Assert.AreEqual(0.72f, behind.y, 1e-4f);
            Assert.AreEqual(1.15f, behind.z, 1e-4f);
            Assert.Less(Quaternion.Angle(turned, Quaternion.LookRotation(Vector3.back, Vector3.up)), 0.1f);

            BevFloorLayout.DeskPose(Vector3.zero, Vector3.up, out Vector3 fallback, out _);
            Assert.AreEqual(BevFloorLayout.DeskDistanceMeters, fallback.z, 1e-4f);
        }

        [Test]
        public void Quad_MapsImageUpToRobotForward()
        {
            var xyz = new float[12];
            var uv = new float[8];
            var tris = new int[12];
            Assert.IsFalse(BevFloorLayout.TryWriteQuad(new float[3], uv, tris));
            Assert.IsTrue(BevFloorLayout.TryWriteQuad(xyz, uv, tris));
            // Vertex 2 is forward-left: image top-left.
            Assert.AreEqual(-0.5f, xyz[6], 1e-4f);
            Assert.AreEqual(0f, xyz[7], 1e-4f);
            Assert.AreEqual(0.5f, xyz[8], 1e-4f);
            Assert.AreEqual(0f, uv[4], 1e-4f);
            Assert.AreEqual(1f, uv[5], 1e-4f);
            // Vertex 0 is back-left: image bottom-left.
            Assert.AreEqual(0f, uv[1], 1e-4f);
            Assert.AreEqual(0, tris[0]);
            Assert.AreEqual(2, tris[1]);
            Assert.AreEqual(1, tris[2]);

            Mesh mesh = null;
            try
            {
                mesh = BevFloorLayout.BuildMesh();
                Assert.AreEqual("BevFloor", mesh.name);
                Assert.AreEqual(4, mesh.vertexCount);
                Assert.AreEqual(4, mesh.triangles.Length / 3);
            }
            finally
            {
                if (mesh != null) Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void Floor_ParentsUnderTheTwin()
        {
            GameObject twin = null;
            GameObject host = null;
            try
            {
                twin = new GameObject("desk-twin");
                twin.transform.localScale = Vector3.one * BevFloorLayout.TabletopScale;
                host = new GameObject("bev-host");
                var panel = host.AddComponent<BevFloorPanel>();
                var plan = SimScreenPlan.Build(SessionKind.Sim, new[] { SimScreenCameras.BevTopic }, "192.168.1.101", null);
                panel.Apply(plan, true, new Vector3(0f, 1.6f, 0f), Vector3.forward, twin.transform);

                Assert.IsTrue(panel.Spec.Show);
                Assert.IsTrue(panel.Spec.Sim);
                Assert.AreEqual(BevFloorLayout.SimSource, panel.Spec.Source);
                Assert.IsTrue(panel.Spec.ParentedToTwin);
                Assert.AreSame(twin.transform, panel.Anchor.parent);
                Assert.AreEqual("BevFloor", panel.Anchor.name);
                Assert.AreEqual(panel.Spec.WidthMeters, panel.Anchor.localScale.x, 1e-3f);
                Assert.AreEqual(panel.Spec.DepthMeters, panel.Anchor.localScale.z, 1e-3f);
                Assert.AreEqual(-BevFloorLayout.ClearanceMeters / BevFloorLayout.TabletopScale, panel.Anchor.localPosition.y, 1e-4f);
                Assert.Greater(panel.Anchor.localScale.x, 4f);
                Assert.IsNull(panel.Anchor.GetComponent<Collider>());
            }
            finally
            {
                if (host != null) Object.DestroyImmediate(host);
                if (twin != null) Object.DestroyImmediate(twin);
                CleanupFloor();
            }
        }

        [Test]
        public void Floor_UsesDeskScaleWhenTheTwinIsAbsent()
        {
            GameObject host = null;
            try
            {
                host = new GameObject("bev-host");
                var panel = host.AddComponent<BevFloorPanel>();
                var plan = SimScreenPlan.Build(SessionKind.Real, new[]
                {
                    SimScreenCameras.FrontTopic,
                    SimScreenCameras.BevTopic,
                }, "10.0.0.8", null);
                panel.Apply(plan, false, Vector3.zero, Vector3.forward, null);

                Assert.IsFalse(panel.Spec.ParentedToTwin);
                Assert.IsNull(panel.Anchor.parent);
                Assert.AreEqual(4f * BevFloorLayout.TabletopScale, panel.Anchor.localScale.x, 1e-3f);
                Assert.AreEqual(4f * BevFloorLayout.TabletopScale, panel.Anchor.localScale.z, 1e-3f);
                Assert.AreEqual(BevFloorLayout.DeskHeightMeters, panel.Anchor.position.y, 1e-3f);
                Assert.AreEqual(BevFloorLayout.DeskDistanceMeters, panel.Anchor.position.z, 1e-3f);
            }
            finally
            {
                if (host != null) Object.DestroyImmediate(host);
                CleanupFloor();
            }
        }

        [Test]
        public void Floor_HidesWhenTheRealProfileHasNoBev()
        {
            GameObject host = null;
            try
            {
                host = new GameObject("bev-host");
                var panel = host.AddComponent<BevFloorPanel>();
                var plan = SimScreenPlan.Build(SessionKind.Real, new[] { SimScreenCameras.FrontTopic }, "10.0.0.8", null);
                panel.Apply(plan, false, Vector3.zero, Vector3.forward, null);
                Assert.IsFalse(panel.Spec.Show);
                Assert.IsNull(panel.Anchor);
            }
            finally
            {
                if (host != null) Object.DestroyImmediate(host);
                CleanupFloor();
            }
        }

        [Test]
        public void FindTwin_MatchesNameOrTwinView()
        {
            GameObject named = null;
            try
            {
                named = new GameObject(BevFloorLayout.TwinObjectName);
                Assert.IsTrue(BevFloorLayout.IsTwin(BevFloorLayout.TwinObjectName, "SomethingElse"));
                Assert.IsTrue(BevFloorLayout.IsTwin("renamed-twin", BevFloorLayout.TwinTypeName));
                Assert.IsFalse(BevFloorLayout.IsTwin("renamed-twin", "Camera"));
                Assert.AreSame(named.transform, BevFloorLayout.FindTwinAmong(null, new[] { named.transform }));
                Assert.IsNull(BevFloorLayout.FindTwinAmong(null, null));

                Transform found = BevFloorLayout.FindTwin();
                Assert.IsTrue(found == named.transform);
            }
            finally
            {
                if (named != null) Object.DestroyImmediate(named);
            }
        }

        [Test]
        public void Detections_AreNotAvailable()
        {
            Assert.IsFalse(DetectionTopics.IsAvailable);
            Assert.IsFalse(DetectionTopics.TryResolve(out string topic));
            Assert.IsNull(topic);
            Assert.AreEqual("vision_msgs/Detection2DArray", DetectionTopics.MessageType);
            StringAssert.Contains("Detection2DArray", DetectionTopics.UnavailableReason);
            StringAssert.Contains("/perception/objects", DetectionTopics.UnavailableReason);
        }

        [Test]
        public void Parser_ReadsPixelBoxesAndNormalizedBoxes()
        {
            var boxes = new List<DetectionBox>();
            const string pixels =
                "{\"detections\":[{\"bbox\":{\"center\":{\"x\":10,\"y\":20,\"theta\":0.1},\"size_x\":6,\"size_y\":8},\"id\":\"cup\"}]}";
            Assert.IsTrue(Detection2DParser.TryParse(pixels, boxes, out string error), error);
            Assert.AreEqual(1, boxes.Count);
            Assert.IsFalse(boxes[0].Normalized);
            Assert.AreEqual("cup", boxes[0].Label);
            Assert.IsTrue(boxes[0].TryPixelBounds(100, 80, out int x0, out int y0, out int x1, out int y1));
            Assert.AreEqual(7, x0);
            Assert.AreEqual(16, y0);
            Assert.AreEqual(12, x1);
            Assert.AreEqual(23, y1);

            const string normalized =
                "{\"detections\":[{\"bbox\":{\"center\":{\"x\":0.5,\"y\":0.25},\"size_x\":0.5,\"size_y\":0.5}," +
                "\"results\":[{\"hypothesis\":{\"class_id\":\"cube\",\"score\":0.8}}]}]}";
            Assert.IsTrue(Detection2DParser.TryParse(normalized, boxes, out error), error);
            Assert.AreEqual(1, boxes.Count);
            Assert.IsTrue(boxes[0].Normalized);
            Assert.AreEqual("cube", boxes[0].Label);
            Assert.IsTrue(boxes[0].TryPixelBounds(8, 8, out x0, out y0, out x1, out y1));
            Assert.AreEqual(2, x0);
            Assert.AreEqual(0, y0);
            Assert.AreEqual(5, x1);
            Assert.AreEqual(3, y1);
        }

        [Test]
        public void Parser_UnwrapsRosbridge_AndRejectsGarbage()
        {
            var boxes = new List<DetectionBox>
            {
                new DetectionBox { CenterX = 3f, CenterY = 3f, SizeX = 2f, SizeY = 2f },
            };
            Assert.IsTrue(Detection2DParser.TryParse("{\"op\":\"publish\",\"msg\":{\"detections\":[]}}", boxes, out string error), error);
            Assert.AreEqual(0, boxes.Count);

            Assert.IsFalse(Detection2DParser.TryParse("nope", boxes, out error));
            Assert.AreEqual(0, boxes.Count);
            Assert.IsFalse(string.IsNullOrEmpty(error));

            boxes.Add(new DetectionBox { CenterX = 1f, CenterY = 1f, SizeX = 2f, SizeY = 2f });
            Assert.IsFalse(Detection2DParser.TryParse("{\"detections\":1}", boxes, out _));
            Assert.AreEqual(1, boxes.Count);

            const string mixed =
                "{\"detections\":[{\"nope\":1},{\"bbox\":{\"center\":{\"x\":4,\"y\":4},\"size_x\":2,\"size_y\":2}}]}";
            Assert.IsTrue(Detection2DParser.TryParse(mixed, boxes, out error), error);
            Assert.AreEqual(1, boxes.Count);
            Assert.IsFalse(boxes[0].Normalized);
            Assert.IsFalse(Detection2DParser.TryParse("", boxes, out _));
            Assert.IsFalse(Detection2DParser.TryParse("{}", null, out error));
            Assert.AreEqual("no list", error);
        }

        [Test]
        public void Painter_DrawsABorderOnTheFrontTexture()
        {
            Texture2D source = null;
            Texture2D scratch = null;
            try
            {
                source = new Texture2D(16, 16, TextureFormat.RGB24, false, true);
                var pixels = new byte[16 * 16 * 3];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = 20;
                var uploaded = new byte[pixels.Length];
                System.Buffer.BlockCopy(pixels, 0, uploaded, 0, pixels.Length);
                source.LoadRawTextureData(uploaded);
                source.Apply(false, false);

                var boxes = new List<DetectionBox>
                {
                    new DetectionBox { CenterX = 8f, CenterY = 8f, SizeX = 10f, SizeY = 10f },
                };
                var paintBuf = new byte[pixels.Length];
                System.Buffer.BlockCopy(pixels, 0, paintBuf, 0, pixels.Length);
                Assert.AreEqual(1, DetectionPainter.Paint(paintBuf, 16, 16, boxes));
                AssertByte(paintBuf, 16, 3, 8, DetectionPainter.CyanR, DetectionPainter.CyanG, DetectionPainter.CyanB);
                AssertByte(paintBuf, 16, 8, 8, 20, 20, 20);
                AssertByte(paintBuf, 16, 0, 0, 20, 20, 20);

                byte[] before = source.GetRawTextureData();
                AssertByte(before, 16, 3, 8, 20, 20, 20);
                Assert.IsTrue(DetectionPainter.TryPaint(source, boxes, ref scratch));
                byte[] after = source.GetRawTextureData();
                CollectionAssert.AreEqual(before, after);
                byte[] painted = scratch.GetRawTextureData();
                AssertByte(painted, 16, 3, 8, DetectionPainter.CyanR, DetectionPainter.CyanG, DetectionPainter.CyanB);
                AssertByte(painted, 16, 8, 8, 20, 20, 20);

                var compositor = new FrontDetectionCompositor();
                Texture2D shown = compositor.Present(source, SimScreenCameras.FrontTopic, boxes);
                Assert.AreSame(source, shown);
                Assert.IsNull(compositor.Present(null, SimScreenCameras.FrontTopic, boxes));
                compositor.Release();

                Assert.AreEqual(0, DetectionPainter.Paint(pixels, 16, 16, new List<DetectionBox>()));
                Assert.IsFalse(DetectionPainter.TryPaint(source, new List<DetectionBox>(), ref scratch));
            }
            finally
            {
                if (source != null) Object.DestroyImmediate(source);
                if (scratch != null) Object.DestroyImmediate(scratch);
            }
        }

        private static void AssertByte(byte[] raw, int width, int x, int topDownY, byte r, byte g, byte b)
        {
            int height = raw.Length / (width * 3);
            int rawY = height - 1 - topDownY;
            int i = (rawY * width + x) * 3;
            Assert.AreEqual(r, raw[i], x + "," + topDownY + " R");
            Assert.AreEqual(g, raw[i + 1], x + "," + topDownY + " G");
            Assert.AreEqual(b, raw[i + 2], x + "," + topDownY + " B");
        }

        private static void CleanupFloor()
        {
            var leftovers = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < leftovers.Length; i++)
            {
                if (leftovers[i] != null && leftovers[i].name == "BevFloor")
                    Object.DestroyImmediate(leftovers[i].gameObject);
            }
        }
    }
}
