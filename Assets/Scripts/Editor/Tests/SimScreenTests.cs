using System;
using System.Globalization;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using OmniBot.VR.Control.Session;
using OmniBot.VR.Video;

namespace OmniBot.VR.Tests
{
    /// <summary>
    /// Sim screen: chase camera first, wrist picture-in-picture, MJPEG quality
    /// and width, off-thread JPEG decode, 30 fps cap, and remembered pose.
    /// </summary>
    public class SimScreenTests
    {
        // 16x16 solid red, 32x32 quadrants, 16x32 top-white gradient, 15x13 red.
        // Produced by System.Drawing at JPEG quality 90–95 and checked against GDI+.
        private const string RedJpeg =
            "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAIBAQIBAQICAgICAgICAwUDAwMDAwYEBAMFBwYHBwcGBwcICQsJCAgKCAcHCg0KCgsMDAwMBwkODw0MDgsMDAz/2wBDAQICAgMDAwYDAwYMCAcIDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAz/wAARCAAQABADASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwD4jooor+cz/aQ//9k=";

        private const string QuadJpeg =
            "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAIBAQIBAQICAgICAgICAwUDAwMDAwYEBAMFBwYHBwcGBwcICQsJCAgKCAcHCg0KCgsMDAwMBwkODw0MDgsMDAz/2wBDAQICAgMDAwYDAwYMCAcIDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAz/wAARCAAgACADASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwD4jooor+cz/aQ+yKKKK/Iz/kbPzLooor/pcP7oP64KKKK/yLP1g//Z";

        private const string GradJpeg =
            "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAIBAQIBAQICAgICAgICAwUDAwMDAwYEBAMFBwYHBwcGBwcICQsJCAgKCAcHCg0KCgsMDAwMBwkODw0MDgsMDAz/2wBDAQICAgMDAwYDAwYMCAcIDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAz/wAARCAAgABADASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwD9if8Ahpf/AKeB/wB9Uf8ADS//AE8D/vqvz4/4aV/6b/8Aj1H/AA0r/wBN/wDx6gD89/8AhpX/AKb/APj1H/DSv/Tf/wAer8+P+Gl/+ng/99Uf8NL/APTwf++qAP/Z";

        private const string OddJpeg =
            "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAQCAwMDAgQDAwMEBAQEBQkGBQUFBQsICAYJDQsNDQ0LDAwOEBQRDg8TDwwMEhgSExUWFxcXDhEZGxkWGhQWFxb/2wBDAQQEBAUFBQoGBgoWDwwPFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhb/wAARCAANAA8DASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwDxGiiivzk/tI//2Q==";

        [Test]
        public void Sim_ChaseIsFirst_ThenProfileOrder()
        {
            var views = SimScreenCameras.ViewsFor(SessionKind.Sim, ProfileTopics());
            AssertTopics(views,
                SimScreenCameras.ChaseTopic,
                SimScreenCameras.FrontTopic,
                SimScreenCameras.WristTopic,
                SimScreenCameras.BevTopic);
            Assert.AreEqual("Chase", views[0].Label);
            Assert.AreEqual("chase", views[0].Id);
            Assert.AreEqual("Front", views[1].Label);
            Assert.AreEqual("Wrist", views[2].Label);
            Assert.AreEqual("BEV", views[3].Label);
            Assert.AreEqual("bev", views[3].Id);

            var plan = SimScreenPlan.Build(SessionKind.Sim, ProfileTopics(), "192.168.1.101", null);
            Assert.IsTrue(plan.IsSim);
            Assert.AreEqual(0, plan.DefaultIndex);
            Assert.AreEqual(SimScreenCameras.ChaseTopic, plan.Views[plan.DefaultIndex].Topic);
        }

        [Test]
        public void Sim_EmptyProfile_UsesCanonicalFour()
        {
            var views = SimScreenCameras.ViewsFor(SessionKind.Sim, new string[0]);
            AssertTopics(views,
                SimScreenCameras.ChaseTopic,
                SimScreenCameras.FrontTopic,
                SimScreenCameras.WristTopic,
                SimScreenCameras.BevTopic);

            var blanks = SimScreenCameras.ViewsFor(SessionKind.Sim, new[] { "", null });
            AssertTopics(blanks,
                SimScreenCameras.ChaseTopic,
                SimScreenCameras.FrontTopic,
                SimScreenCameras.WristTopic,
                SimScreenCameras.BevTopic);
        }

        [Test]
        public void Sim_ProfileChase_IsNotDuplicated()
        {
            var views = SimScreenCameras.ViewsFor(SessionKind.Sim, new[]
            {
                SimScreenCameras.ChaseTopic,
                SimScreenCameras.FrontTopic,
            });
            AssertTopics(views, SimScreenCameras.ChaseTopic, SimScreenCameras.FrontTopic);
        }

        [Test]
        public void Sim_ExtraTopic_AppendsAfterProfile()
        {
            var views = SimScreenCameras.ViewsFor(SessionKind.Sim, new[]
            {
                SimScreenCameras.FrontTopic,
                "/camera/fpv/image_raw",
                SimScreenCameras.FrontTopic,
            });
            AssertTopics(views,
                SimScreenCameras.ChaseTopic,
                SimScreenCameras.FrontTopic,
                "/camera/fpv/image_raw");
            Assert.AreEqual("Fpv", views[2].Label);
            Assert.AreEqual("fpv", views[2].Id);
        }

        [Test]
        public void Real_KeepsProfileOrder_WithoutChase()
        {
            var views = SimScreenCameras.ViewsFor(SessionKind.Real, new[]
            {
                SimScreenCameras.WristTopic,
                SimScreenCameras.FrontTopic,
            });
            AssertTopics(views, SimScreenCameras.WristTopic, SimScreenCameras.FrontTopic);

            var plan = SimScreenPlan.Build(SessionKind.Real, ProfileTopics(), "10.0.0.8", null);
            Assert.IsFalse(plan.IsSim);
            Assert.AreEqual(0, plan.DefaultIndex);
            Assert.AreEqual(SimScreenCameras.FrontTopic, plan.Views[0].Topic);
            Assert.AreEqual(-1, plan.PipIndexFor(0));
        }

        [Test]
        public void Real_EmptyProfile_HasNoCameras()
        {
            var views = SimScreenCameras.ViewsFor(SessionKind.Real, null);
            Assert.AreEqual(0, views.Count);
            var unknown = SimScreenCameras.ViewsFor(null, ProfileTopics());
            Assert.AreEqual(SimScreenCameras.FrontTopic, unknown[0].Topic);
            Assert.AreEqual(-1, SimScreenPlan.Build(null, ProfileTopics(), "10.0.0.8", null).PipIndexFor(0));
        }

        [Test]
        public void Pip_ShowsWristUntilWristIsMain()
        {
            var plan = SimScreenPlan.Build(SessionKind.Sim, ProfileTopics(), "192.168.1.101", null);
            int wrist = IndexOf(plan, SimScreenCameras.WristTopic);
            Assert.AreEqual(wrist, plan.PipIndexFor(0));
            Assert.AreEqual(wrist, plan.PipIndexFor(1));
            Assert.AreEqual(-1, plan.PipIndexFor(wrist));
        }

        [Test]
        public void Pip_HiddenWithoutWrist_AndOnReal()
        {
            var noWrist = SimScreenPlan.Build(SessionKind.Sim, new[]
            {
                SimScreenCameras.FrontTopic,
                SimScreenCameras.BevTopic,
            }, "192.168.1.101", null);
            Assert.AreEqual(-1, noWrist.PipIndexFor(0));

            var real = SimScreenPlan.Build(SessionKind.Real, ProfileTopics(), "10.0.0.8", null);
            Assert.AreEqual(-1, real.PipIndexFor(0));
            Assert.AreEqual(-1, real.PipIndexFor(1));
        }

        [Test]
        public void ChaseUrl_UsesQualityAndWidth_AndKeepsSlashes()
        {
            var urls = EndpointAddresses.VideoUrlsFor("192.168.1.101", ProfileTopics());
            var plan = SimScreenPlan.Build(SessionKind.Sim, ProfileTopics(), "192.168.1.101", urls);
            string chase = plan.UrlFor(0, SimScreenCameras.MainQuality, SimScreenCameras.MainWidth);
            Assert.AreEqual(
                "http://192.168.1.101:8080/stream?topic=/sim/chase_cam/image_raw&quality=80&width=960",
                chase);
            Assert.AreEqual(80, SimScreenCameras.MainQuality);
            Assert.AreEqual(960, SimScreenCameras.MainWidth);
            Assert.AreEqual(60, SimScreenCameras.PipQuality);
            Assert.AreEqual(320, SimScreenCameras.PipWidth);

            int wrist = IndexOf(plan, SimScreenCameras.WristTopic);
            Assert.AreEqual(
                "http://192.168.1.101:8080/stream?topic=/camera/wrist/image_raw&quality=60&width=320",
                plan.UrlFor(wrist, SimScreenCameras.PipQuality, SimScreenCameras.PipWidth));
        }

        [Test]
        public void ExistingUrl_IsPreferred_AndParamsReplace()
        {
            var urls = EndpointAddresses.VideoUrlsFor("192.168.1.101", ProfileTopics());
            Assert.AreEqual(
                "http://192.168.1.101:8080/stream?topic=/camera/front/image_raw",
                urls[0]);
            var plan = SimScreenPlan.Build(SessionKind.Sim, ProfileTopics(), "192.168.1.101", urls);
            int front = IndexOf(plan, SimScreenCameras.FrontTopic);
            Assert.AreEqual(
                "http://192.168.1.101:8080/stream?topic=/camera/front/image_raw&quality=80&width=960",
                plan.UrlFor(front, 80, 960));

            string replaced = MjpegUrl.WithParams(
                "http://192.168.1.101:8080/stream?topic=/camera/front/image_raw&quality=95&width=640&foo=1",
                80, 960);
            Assert.AreEqual(
                "http://192.168.1.101:8080/stream?topic=/camera/front/image_raw&quality=80&width=960&foo=1",
                replaced);
            Assert.IsNull(plan.UrlFor(-1, 80, 960));
            Assert.IsNull(MjpegUrl.WithParams(null, 80, 960));
        }

        [Test]
        public void RetargetHost_RewritesBakedUrls()
        {
            var urls = EndpointAddresses.VideoUrlsFor("192.168.1.101", ProfileTopics());
            var plan = SimScreenPlan.Build(SessionKind.Sim, ProfileTopics(), "192.168.1.101", urls);
            plan.RetargetHost("10.0.0.8");
            Assert.AreEqual("10.0.0.8", plan.Host);
            Assert.AreEqual(
                "http://10.0.0.8:8080/stream?topic=/sim/chase_cam/image_raw&quality=80&width=960",
                plan.UrlFor(0, 80, 960));
            int front = IndexOf(plan, SimScreenCameras.FrontTopic);
            Assert.AreEqual(
                "http://10.0.0.8:8080/stream?topic=/camera/front/image_raw&quality=80&width=960",
                plan.UrlFor(front, 80, 960));
            plan.RetargetHost("");
            Assert.AreEqual("10.0.0.8", plan.Host);
        }

        [Test]
        public void MjpegUrl_ClampsQualityAndWidth()
        {
            Assert.AreEqual(1, MjpegUrl.ClampQuality(0));
            Assert.AreEqual(100, MjpegUrl.ClampQuality(500));
            Assert.AreEqual(16, MjpegUrl.ClampWidth(1));
            Assert.AreEqual(1920, MjpegUrl.ClampWidth(8000));
            Assert.AreEqual(
                "http://10.0.0.8:8080/stream?topic=/camera/front/image_raw&quality=1&width=1920",
                MjpegUrl.Build("10.0.0.8", MjpegUrl.Port, "/camera/front/image_raw", 0, 8000));
            Assert.AreEqual("/camera/front/image_raw", MjpegUrl.TopicOf(
                "http://10.0.0.8:8080/stream?topic=/camera/front/image_raw&quality=80"));
            Assert.IsNull(MjpegUrl.TopicOf("http://10.0.0.8:8080/stream"));
            Assert.IsNull(MjpegUrl.FindByTopic(null, "/camera/front/image_raw"));
        }

        [Test]
        public void MjpegUrl_WithHost_KeepsPortAndTopic()
        {
            const string url = "http://192.168.1.101:8080/stream?topic=/camera/front/image_raw&quality=80";
            Assert.AreEqual(
                "http://10.0.0.8:8080/stream?topic=/camera/front/image_raw&quality=80",
                MjpegUrl.WithHost(url, "10.0.0.8"));
            Assert.AreEqual(url, MjpegUrl.WithHost(url, ""));
            Assert.AreEqual("not a url", MjpegUrl.WithHost("not a url", "10.0.0.8"));
        }

        [Test]
        public void MjpegUrl_PortMatchesVideoPort()
        {
            Assert.AreEqual(EndpointAddresses.VideoPort, MjpegUrl.Port);
            Assert.AreEqual(8080, MjpegUrl.Port);
        }

        [Test]
        public void Parser_ContentLength_BeatsEmbeddedMarker()
        {
            byte[] body = { 0xFF, 0xD8, 0x01, 0x02, 0xFF, 0xD9, 0x11, 0x22 };
            byte[] buf = Join(Encoding.ASCII.GetBytes("content-length: 8\r\n\r\n"), body);
            Assert.IsTrue(MjpegFrameParser.TryExtractJpeg(buf, buf.Length, 0, out int start, out int len));
            Assert.AreEqual(buf.Length - 8, start);
            Assert.AreEqual(8, len);
            Assert.AreEqual(0x11, buf[start + 6]);
            Assert.AreEqual(0x22, buf[start + 7]);
        }

        [Test]
        public void Parser_FallsBackToSoiEoi()
        {
            byte[] jpeg = { 0xFF, 0xD8, 0x10, 0x20, 0xFF, 0xD9 };
            byte[] buf = Join(Encoding.ASCII.GetBytes("nope"), jpeg, Encoding.ASCII.GetBytes("tail"));
            Assert.IsTrue(MjpegFrameParser.TryExtractJpeg(buf, buf.Length, 0, out int start, out int len));
            Assert.AreEqual(4, start);
            Assert.AreEqual(6, len);
            Assert.IsFalse(MjpegFrameParser.TryExtractJpeg(buf, 2, 0, out _, out _));
            Assert.IsFalse(MjpegFrameParser.TryExtractJpeg(null, 0, 0, out _, out _));
        }

        [Test]
        public void Parser_IncompleteContentLength_UsesMarker()
        {
            byte[] jpeg = { 0xFF, 0xD8, 0x01, 0xFF, 0xD9 };
            byte[] buf = Join(Encoding.ASCII.GetBytes("Content-Length: 100\r\n\r\n"), jpeg);
            Assert.IsTrue(MjpegFrameParser.TryExtractJpeg(buf, buf.Length, 0, out int start, out int len));
            Assert.AreEqual(5, len);
            Assert.AreEqual(0xFF, buf[start]);
            Assert.AreEqual(0xD8, buf[start + 1]);
        }

        [Test]
        public void ChunkBuffer_AssemblesSplitFrame()
        {
            byte[] jpeg = { 0xFF, 0xD8, 0xAA, 0xBB, 0xFF, 0xD9 };
            byte[] packet = Multipart(jpeg);
            int cut = packet.Length / 2;
            var buffer = new MjpegChunkBuffer();
            byte[] got = null;
            int first = buffer.Append(packet, cut, (frame, offset, length) => got = Copy(frame, offset, length));
            Assert.AreEqual(0, first);
            Assert.IsNull(got);
            int second = buffer.Append(Slice(packet, cut), packet.Length - cut, (frame, offset, length) =>
            {
                got = Copy(frame, offset, length);
            });
            Assert.AreEqual(1, second);
            AssertBytes(jpeg, got);
            Assert.AreEqual(0, buffer.Fill);
        }

        [Test]
        public void ChunkBuffer_ExtractsTwoFrames()
        {
            byte[] a = { 0xFF, 0xD8, 0x01, 0xFF, 0xD9 };
            byte[] b = { 0xFF, 0xD8, 0x02, 0x03, 0xFF, 0xD9 };
            byte[] packet = Join(Multipart(a), Multipart(b));
            var buffer = new MjpegChunkBuffer();
            var frames = new System.Collections.Generic.List<byte[]>();
            int found = buffer.Append(packet, packet.Length, (frame, offset, length) =>
            {
                frames.Add(Copy(frame, offset, length));
            });
            Assert.AreEqual(2, found);
            AssertBytes(a, frames[0]);
            AssertBytes(b, frames[1]);
            Assert.AreEqual(0, buffer.Append(null, 4, null));
        }

        [Test]
        public void FramePacer_CapsAtThirty()
        {
            Assert.AreEqual(30, FramePacer.MaxFps);
            Assert.IsTrue(FramePacer.ShouldTake(-1, 0));
            Assert.IsTrue(FramePacer.ShouldTake(5, 1));
            Assert.IsFalse(FramePacer.ShouldTake(0, 0));
            Assert.IsFalse(FramePacer.ShouldTake(0, 0.02));
            Assert.IsTrue(FramePacer.ShouldTake(0, 1.0 / FramePacer.MaxFps));
        }

        [Test]
        public void Overlay_FormatsFpsAge_OrNoSignal()
        {
            Assert.AreEqual("No signal", StreamOverlay.Format(30f, 10f, false));
            Assert.AreEqual("30 fps  ·  40 ms", StreamOverlay.Format(30f, 40f, true));
            Assert.AreEqual("0 fps  ·  0 ms", StreamOverlay.Format(-2f, -4f, true));
            Assert.AreEqual(0f, StreamOverlay.AgeMilliseconds(0, 5));
            Assert.AreEqual(0f, StreamOverlay.AgeMilliseconds(10, 4), 0.001f);
            Assert.AreEqual(40f, StreamOverlay.AgeMilliseconds(1.0, 1.04), 0.05f);
        }

        [Test]
        public void FpsCounter_KeepsOneSecondWindow()
        {
            var counter = new StreamFpsCounter();
            counter.Mark(0.0);
            counter.Mark(0.5);
            counter.Mark(1.0);
            Assert.AreEqual(3f, counter.Fps(1.0));
            Assert.AreEqual(2f, counter.Fps(1.01));
            Assert.AreEqual(0f, counter.Fps(2.2));
            counter.Clear();
            Assert.AreEqual(0f, counter.Fps(2.2));
        }

        [Test]
        public void Pose_RoundTrips_AndClamps()
        {
            var state = SimScreenPose.Default();
            Assert.IsFalse(state.HasSaved);
            Assert.AreEqual(1600f, state.Width);
            Assert.AreEqual(900f, state.Height);
            Assert.AreEqual(2.2f, state.Radius);
            state.LocalX = -0.25f;
            state.LocalY = 1.5f;
            state.RotY = 0.3f;
            state.RotW = 0.9f;
            state.Width = 1800f;
            state.Height = 1000f;
            state.Radius = 2.5f;
            Assert.IsTrue(SimScreenPose.TryParse(SimScreenPose.Serialize(state), out var back));
            Assert.IsTrue(back.HasSaved);
            Assert.AreEqual(-0.25f, back.LocalX, 1e-5f);
            Assert.AreEqual(1.5f, back.LocalY, 1e-5f);
            Assert.AreEqual(1800f, back.Width, 0.01f);
            Assert.AreEqual(1000f, back.Height, 0.01f);
            Assert.AreEqual(2.5f, back.Radius, 1e-4f);
            float mag = (float)Math.Sqrt(0.3f * 0.3f + 0.9f * 0.9f);
            Assert.AreEqual(0.3f / mag, back.RotY, 1e-5f);
            Assert.AreEqual(0.9f / mag, back.RotW, 1e-5f);

            var clamped = SimScreenPose.Clamp(new SimScreenPoseState
            {
                LocalX = 50f,
                LocalY = float.NaN,
                Width = 12f,
                Height = 9000f,
                Radius = 0.1f,
            });
            Assert.AreEqual(20f, clamped.LocalX);
            Assert.AreEqual(0f, clamped.LocalY);
            Assert.AreEqual(SimScreenPose.MinWidth, clamped.Width);
            Assert.AreEqual(SimScreenPose.MaxHeight, clamped.Height);
            Assert.AreEqual(SimScreenPose.MinRadius, clamped.Radius);
            Assert.AreEqual(1f, clamped.RotW);

            Assert.IsFalse(SimScreenPose.TryParse("1 2 3", out var bad));
            Assert.IsFalse(bad.HasSaved);
            Assert.AreEqual(1600f, bad.Width);
            Assert.IsFalse(SimScreenPose.TryParse(
                "NaN 0 0 0 0 0 1 1600 900 2.2", out _));
        }

        [Test]
        public void PoseStore_RoundTrip_RestoresPreviousKey()
        {
            string previous = PlayerPrefs.GetString(SimScreenPoseStore.Key, "");
            bool had = PlayerPrefs.HasKey(SimScreenPoseStore.Key);
            try
            {
                var state = SimScreenPose.Default();
                state.LocalZ = 0.4f;
                state.Width = 1700f;
                state.Height = 960f;
                SimScreenPoseStore.Save(state);
                Assert.IsTrue(SimScreenPoseStore.TryLoad(out var loaded));
                Assert.IsTrue(loaded.HasSaved);
                Assert.AreEqual(0.4f, loaded.LocalZ, 1e-4f);
                Assert.AreEqual(1700f, loaded.Width, 0.01f);
                Assert.AreEqual(960f, loaded.Height, 0.01f);
                SimScreenPoseStore.Clear();
                Assert.IsFalse(SimScreenPoseStore.TryLoad(out var cleared));
                Assert.IsFalse(cleared.HasSaved);
            }
            finally
            {
                if (!had || string.IsNullOrEmpty(previous)) PlayerPrefs.DeleteKey(SimScreenPoseStore.Key);
                else PlayerPrefs.SetString(SimScreenPoseStore.Key, previous);
                PlayerPrefs.Save();
            }
        }

        [Test]
        public void Curve_BendsEdgesTowardViewer()
        {
            const int columns = 32;
            int verts = CurvedScreenGeometry.VertexCount(columns);
            var xyz = new float[verts * 3];
            var uv = new float[verts * 2];
            var tris = new int[CurvedScreenGeometry.TriangleIndexCount(columns)];
            Assert.IsTrue(CurvedScreenGeometry.TryWrite(1600f, 900f, 2200f, columns, xyz, uv, tris));
            int center = columns / 2;
            Assert.Less(xyz[2], xyz[center * 3 + 2]);
            Assert.Less(xyz[columns * 3 + 2], xyz[center * 3 + 2]);
            Assert.AreEqual(0f, uv[1], 1e-4f);
            int top = columns + 1;
            Assert.AreEqual(1f, uv[top * 2 + 1], 1e-4f);

            Mesh mesh = null;
            try
            {
                mesh = CurvedScreenGeometry.BuildMesh(1600f, 900f, 2200f, columns);
                Assert.IsNotNull(mesh);
                Assert.AreEqual("SimScreenCurve", mesh.name);
                Assert.AreEqual(verts, mesh.vertexCount);
                Assert.Less(mesh.vertices[0].z, mesh.vertices[center].z);
            }
            finally
            {
                if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void Curve_RejectsBadSize_AndClampsColumns()
        {
            var xyz = new float[6];
            var uv = new float[4];
            var tris = new int[6];
            Assert.IsFalse(CurvedScreenGeometry.TryWrite(0f, 10f, 1f, 8, xyz, uv, tris));
            Assert.IsFalse(CurvedScreenGeometry.TryWrite(10f, -1f, 1f, 8, xyz, uv, tris));
            Assert.IsFalse(CurvedScreenGeometry.TryWrite(float.NaN, 10f, 1f, 8, xyz, uv, tris));
            Assert.AreEqual(2, CurvedScreenGeometry.ClampColumns(1));
            Assert.AreEqual(64, CurvedScreenGeometry.ClampColumns(100));
            Assert.AreEqual(6, CurvedScreenGeometry.VertexCount(1));
            Assert.IsNull(CurvedScreenGeometry.BuildMesh(0f, 10f, 1f, 8));
        }

        [Test]
        public void FlipVertical_SwapsRows()
        {
            byte[] src =
            {
                1, 2, 3, 4, 5, 6,
                7, 8, 9, 10, 11, 12,
            };
            var dst = new byte[src.Length];
            BaselineJpegDecoder.FlipVertical(src, dst, 2, 2);
            AssertBytes(new byte[] { 7, 8, 9, 10, 11, 12, 1, 2, 3, 4, 5, 6 }, dst);
        }

        [Test]
        public void Jpeg_SolidRed_MatchesBaseline()
        {
            byte[] rgb = Decode(RedJpeg, out int w, out int h);
            Assert.AreEqual(16, w);
            Assert.AreEqual(16, h);
            for (int i = 0; i < w * h; i++)
            {
                Assert.Greater(rgb[i * 3], 180);
                Assert.Less(rgb[i * 3 + 1], 40);
                Assert.Less(rgb[i * 3 + 2], 40);
            }
        }

        [Test]
        public void Jpeg_Quadrants_StayInPlace()
        {
            byte[] rgb = Decode(QuadJpeg, out int w, out int h);
            Assert.AreEqual(32, w);
            Assert.AreEqual(32, h);
            Near(rgb, w, 8, 8, 220, 10, 10, 12);
            Near(rgb, w, 24, 8, 10, 200, 20, 12);
            Near(rgb, w, 8, 24, 15, 30, 210, 12);
            Near(rgb, w, 24, 24, 230, 230, 230, 12);
        }

        [Test]
        public void Jpeg_Gradient_IsTopDown()
        {
            byte[] rgb = Decode(GradJpeg, out int w, out int h);
            Assert.AreEqual(16, w);
            Assert.AreEqual(32, h);
            int top = rgb[0];
            int bottom = rgb[((h - 1) * w) * 3];
            Assert.Greater(top, bottom + 40);
        }

        [Test]
        public void Jpeg_OddSize_Decodes()
        {
            byte[] rgb = Decode(OddJpeg, out int w, out int h);
            Assert.AreEqual(15, w);
            Assert.AreEqual(13, h);
            Near(rgb, w, 7, 6, 220, 10, 10, 20);
        }

        [Test]
        public void Jpeg_Truncated_Fails()
        {
            var decoder = new BaselineJpegDecoder();
            byte[] rgb = null;
            Assert.IsFalse(decoder.TryDecode(new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 }, 4, ref rgb, out int w, out int h, out string error));
            Assert.AreEqual(0, w);
            Assert.AreEqual(0, h);
            Assert.IsFalse(string.IsNullOrEmpty(error));
            Assert.IsFalse(decoder.TryDecode(null, 0, ref rgb, out _, out _, out error));
            Assert.IsFalse(string.IsNullOrEmpty(error));
        }

        [Test]
        public void Jpeg_ReusesDecoder()
        {
            var decoder = new BaselineJpegDecoder();
            byte[] red = Convert.FromBase64String(RedJpeg);
            byte[] quad = Convert.FromBase64String(QuadJpeg);
            byte[] rgb = null;
            Assert.IsTrue(decoder.TryDecode(red, red.Length, ref rgb, out int rw, out int rh, out string err), err);
            Assert.AreEqual(16, rw);
            Assert.AreEqual(16, rh);
            Assert.IsTrue(decoder.TryDecode(quad, quad.Length, ref rgb, out int qw, out int qh, out err), err);
            Assert.AreEqual(32, qw);
            Assert.AreEqual(32, qh);
            Near(rgb, qw, 8, 8, 220, 10, 10, 12);
            Near(rgb, qw, 24, 24, 230, 230, 230, 12);
        }

        private static string[] ProfileTopics()
        {
            return new[]
            {
                SimScreenCameras.FrontTopic,
                SimScreenCameras.WristTopic,
                SimScreenCameras.BevTopic,
            };
        }

        private static void AssertTopics(System.Collections.Generic.IList<SimCameraView> views, params string[] topics)
        {
            Assert.AreEqual(topics.Length, views.Count);
            for (int i = 0; i < topics.Length; i++)
                Assert.AreEqual(topics[i], views[i].Topic);
        }

        private static int IndexOf(SimScreenPlan plan, string topic)
        {
            for (int i = 0; i < plan.Views.Count; i++)
            {
                if (plan.Views[i].Topic == topic) return i;
            }
            Assert.Fail("missing " + topic);
            return -1;
        }

        private static byte[] Decode(string b64, out int width, out int height)
        {
            byte[] jpeg = Convert.FromBase64String(b64);
            var decoder = new BaselineJpegDecoder();
            byte[] rgb = null;
            Assert.IsTrue(decoder.TryDecode(jpeg, jpeg.Length, ref rgb, out width, out height, out string error), error);
            return rgb;
        }

        private static void Near(byte[] rgb, int width, int x, int y, int r, int g, int b, int tol)
        {
            int i = (y * width + x) * 3;
            Assert.LessOrEqual(Math.Abs(rgb[i] - r), tol, x + "," + y + " R");
            Assert.LessOrEqual(Math.Abs(rgb[i + 1] - g), tol, x + "," + y + " G");
            Assert.LessOrEqual(Math.Abs(rgb[i + 2] - b), tol, x + "," + y + " B");
        }

        private static byte[] Multipart(byte[] jpeg)
        {
            string head = "--frame\r\nContent-Type: image/jpeg\r\nContent-Length: "
                + jpeg.Length.ToString(CultureInfo.InvariantCulture) + "\r\n\r\n";
            return Join(Encoding.ASCII.GetBytes(head), jpeg);
        }

        private static byte[] Join(params byte[][] parts)
        {
            int n = 0;
            for (int i = 0; i < parts.Length; i++) n += parts[i].Length;
            var all = new byte[n];
            int o = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                Buffer.BlockCopy(parts[i], 0, all, o, parts[i].Length);
                o += parts[i].Length;
            }
            return all;
        }

        private static byte[] Slice(byte[] src, int offset)
        {
            var dst = new byte[src.Length - offset];
            Buffer.BlockCopy(src, offset, dst, 0, dst.Length);
            return dst;
        }

        private static void AssertBytes(byte[] expected, byte[] actual)
        {
            Assert.IsNotNull(actual);
            Assert.AreEqual(expected.Length, actual.Length);
            for (int i = 0; i < expected.Length; i++)
                Assert.AreEqual(expected[i], actual[i], "byte " + i);
        }

        private static byte[] Copy(byte[] src, int offset, int length)
        {
            var dst = new byte[length];
            Buffer.BlockCopy(src, offset, dst, 0, length);
            return dst;
        }
    }
}
