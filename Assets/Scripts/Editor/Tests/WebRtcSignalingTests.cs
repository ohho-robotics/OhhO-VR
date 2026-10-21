using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using OmniBot.VR.Video;

namespace OmniBot.VR.Tests
{
    /// <summary>
    /// EditMode coverage for the ohho_webrtc viewer handshake. A fake peer
    /// stands in for the robot so the shapes, ICE session record, multi-track
    /// offer, and one-frame jitter buffer can be checked without a headset.
    /// </summary>
    public class WebRtcSignalingTests
    {
        [Test]
        public void JoinOfferAndIce_MatchServerShapes()
        {
            var session = new OhhoViewerSession(new[] { "chase", "front" });
            ClientSignal join = OhhoSignaling.ParseClient(session.CreateJoin());
            Assert.AreEqual("join", join.Type);
            Assert.AreEqual("viewer", join.Role);
            CollectionAssert.AreEqual(new[] { "chase", "front" }, join.Tracks);

            ClientSignal offer = OhhoSignaling.ParseClient(session.CreateOffer("v=0\r\noffer\r\n"));
            Assert.AreEqual("offer", offer.Type);
            Assert.AreEqual("chase", offer.Track);
            CollectionAssert.AreEqual(new[] { "chase", "front" }, offer.Tracks);
            Assert.IsTrue(offer.Sdp.StartsWith("v="));

            ClientSignal ice = OhhoSignaling.ParseClient(
                session.CreateIce("front", "candidate:1 1 UDP 1 127.0.0.1 9 typ host", "0", 1));
            Assert.AreEqual("ice", ice.Type);
            Assert.AreEqual("front", ice.Track);
            Assert.AreEqual("0", ice.SdpMid);
            Assert.AreEqual(1, ice.SdpMLineIndex);
            Assert.IsTrue(ice.Candidate.StartsWith("candidate:"));
        }

        [Test]
        public void ParseClient_RejectsBadShapes()
        {
            AssertCode("invalid_json", () => OhhoSignaling.ParseClient("{"));
            AssertCode("unknown_type", () => OhhoSignaling.ParseClient("{\"type\":\"trickle\"}"));
            AssertCode("invalid_sdp", () => OhhoSignaling.ParseClient(
                "{\"type\":\"offer\",\"sdp\":\"not-sdp\",\"track\":\"chase\"}"));
            AssertCode("invalid_role", () => OhhoSignaling.ParseClient(
                "{\"type\":\"join\",\"role\":\"robot\",\"tracks\":[\"chase\"]}"));
            AssertCode("invalid_candidate", () => OhhoSignaling.ParseClient(
                "{\"type\":\"ice\",\"track\":\"chase\",\"candidate\":5}"));
            Assert.Throws<SignalingFormatException>(() =>
                new OhhoViewerSession(new[] { "chase" }).CreateOffer("not-sdp"));
        }

        [Test]
        public void FakePeer_WelcomeCarriesStunAndTurnFromTheSessionRecord()
        {
            FakeOhhoSignalingPeer peer = FakeOhhoSignalingPeer.WithLanTurn();
            var session = new OhhoViewerSession(new[] { "chase", "front" });

            SignalingInbound welcome = session.Accept(peer.Receive(session.CreateJoin()));
            Assert.IsFalse(session.Failed);
            Assert.AreEqual("welcome", welcome.Type);
            Assert.AreEqual("viewer", session.Record.Role);
            Assert.AreEqual("v1", session.Record.ViewerId);
            Assert.AreEqual("/signaling", session.Record.SignalingPath);
            Assert.AreEqual("/webrtc/signaling", session.Record.GatewayPath);
            CollectionAssert.AreEquivalent(
                new[] { "chase", "front", "wrist" },
                session.Record.Tracks.Select(track => track.Name).ToArray());

            WebRtcIceServer stun = session.Record.IceServers[0];
            WebRtcIceServer turn = session.Record.IceServers[1];
            Assert.AreEqual("stun:stun.l.google.com:19302", stun.Urls[0]);
            Assert.IsNull(stun.Username);
            Assert.AreEqual("turn:turn.example.com:3478?transport=udp", turn.Urls[0]);
            Assert.AreEqual("ohho", turn.Username);
            Assert.AreEqual("secret", turn.Credential);
            CollectionAssert.AreEqual(
                new[]
                {
                    "stun:stun.l.google.com:19302",
                    "turn:turn.example.com:3478?transport=udp"
                },
                OhhoSignaling.IceUrls(session.Record.IceServers));
        }

        [Test]
        public void IceUrls_EmptyRecord_DoesNotInventStun()
        {
            Assert.AreEqual(0, OhhoSignaling.IceUrls(new WebRtcIceServer[0]).Count);
            Assert.AreEqual(0, OhhoSignaling.IceUrls(null).Count);
        }

        [Test]
        public void Welcome_StunUrlAsString_AndTurnUrlAsArray()
        {
            string json = OhhoSignaling.Welcome(
                "viewer",
                "v3",
                new[] { new WebRtcTrackListing("chase", "/camera/chase/image_raw") },
                new[]
                {
                    new WebRtcIceServer(new[] { "stun:stun.l.google.com:19302" }, null, null),
                    new WebRtcIceServer(new[] { "turns:turn.example.com:5349" }, "user", "pass")
                });
            JObject obj = JObject.Parse(json);
            Assert.AreEqual(JTokenType.String, obj["iceServers"][0]["urls"].Type);
            Assert.AreEqual(JTokenType.Array, obj["iceServers"][1]["urls"].Type);

            var session = new OhhoViewerSession(new[] { "chase" });
            session.Accept(json);
            Assert.AreEqual("stun:stun.l.google.com:19302", session.Record.IceServers[0].Urls[0]);
            Assert.AreEqual("user", session.Record.IceServers[1].Username);
            Assert.AreEqual("pass", session.Record.IceServers[1].Credential);
        }

        [Test]
        public void FakePeer_AnswersOneOfferForSeveralTracks_AndExchangesIce()
        {
            FakeOhhoSignalingPeer peer = FakeOhhoSignalingPeer.WithLanTurn();
            var session = new OhhoViewerSession(new[] { "chase", "wrist" });
            session.Accept(peer.Receive(session.CreateJoin()));

            string offer = session.CreateOffer("v=0\r\nm=video 9 UDP/TLS/RTP/SAVPF 102\r\n");
            SignalingInbound answer = session.Accept(peer.Receive(offer));
            Assert.AreEqual("answer", answer.Type);
            Assert.IsTrue(answer.Sdp.StartsWith("v="));
            Assert.AreEqual("v1", answer.Viewer);
            CollectionAssert.AreEqual(new[] { "chase", "wrist" }, peer.LastOfferTracks);

            string ice = session.CreateIce("chase", "candidate:1 1 UDP 1 10.0.0.2 9 typ host", "0", 0);
            Assert.IsNull(peer.Receive(ice));
            Assert.AreEqual("candidate:1 1 UDP 1 10.0.0.2 9 typ host", peer.ReceivedIce[0]);

            SignalingInbound remote = session.Accept(
                peer.EmitIce("wrist", "candidate:2 1 UDP 1 10.0.0.8 9 typ host", "1", 1));
            Assert.AreEqual("ice", remote.Type);
            Assert.AreEqual("wrist", remote.Track);
            Assert.AreEqual(1, remote.SdpMLineIndex);
            Assert.IsFalse(session.Failed);
        }

        [Test]
        public void FakePeer_RequiresJoin_AndNamesTheUnknownTrack()
        {
            FakeOhhoSignalingPeer peer = FakeOhhoSignalingPeer.WithLanTurn();
            var early = new OhhoViewerSession(new[] { "chase" });
            SignalingInbound tooSoon = early.Accept(peer.Receive(early.CreateOffer("v=0\r\n")));
            Assert.AreEqual("join_required", tooSoon.Error);
            Assert.IsTrue(early.Failed);

            var session = new OhhoViewerSession(new[] { "chase", "bev" });
            SignalingInbound rejected = session.Accept(peer.Receive(session.CreateJoin()));
            Assert.AreEqual("unknown_track", rejected.Error);
            Assert.AreEqual("bev", rejected.Track);
            Assert.IsTrue(session.Failed);
        }

        [Test]
        public void TrackNames_MapImageTopics()
        {
            Assert.AreEqual("chase", WebRtcTracks.NameForTopic("/camera/chase/image_raw"));
            Assert.AreEqual("front", WebRtcTracks.NameForTopic("/camera/front/image_raw"));
            Assert.AreEqual("wrist", WebRtcTracks.NameForTopic("/camera/wrist/image_raw"));
            Assert.AreEqual("bev", WebRtcTracks.NameForTopic("/camera/base/bev/image_raw"));
            Assert.IsNull(WebRtcTracks.NameForTopic("/not/a/camera"));
            Assert.IsNull(WebRtcTracks.NameForTopic("/camera/Front/image_raw"));
            Assert.IsNull(WebRtcTracks.NameForTopic(""));

            CollectionAssert.AreEqual(
                new[] { "front", "wrist", "bev" },
                WebRtcTracks.NamesForTopics(new[]
                {
                    "/camera/front/image_raw",
                    "/camera/wrist/image_raw",
                    "/camera/base/bev/image_raw",
                    "/camera/front/image_raw"
                }));
        }

        [Test]
        public void Endpoints_TokenUsesGateway_OtherwiseDirectLan()
        {
            Assert.AreEqual(
                "wss://10.0.0.5:8443/webrtc/signaling?token=a%2Fb",
                WebRtcEndpoints.Resolve("10.0.0.5", null, "a/b"));
            Assert.AreEqual(
                "ws://10.0.0.5:8766/signaling",
                WebRtcEndpoints.Resolve("10.0.0.5", "  ", null));
            Assert.AreEqual(
                "ws://192.168.1.9:9000/custom",
                WebRtcEndpoints.Resolve("10.0.0.5", " ws://192.168.1.9:9000/custom ", "ignored"));
            Assert.AreEqual(
                "wss://10.0.0.5:8443/webrtc/signaling",
                WebRtcEndpoints.Describe("wss://10.0.0.5:8443/webrtc/signaling?token=secret"));
        }

        [Test]
        public void TransportChoice_PrefersWebRtcOnlyWhenAvailable()
        {
            Assert.AreEqual("WebRTC", VideoTransportChoice.Select(true, true));
            Assert.AreEqual("MJPEG", VideoTransportChoice.Select(true, false));
            Assert.AreEqual("MJPEG", VideoTransportChoice.Select(false, true));
        }

        [Test]
        public void HudLabel_ShowsTheActiveTransport()
        {
            Assert.AreEqual(
                "/camera/chase/image_raw  ·  WebRTC",
                VideoHud.Label("/camera/chase/image_raw", "WebRTC"));
            Assert.AreEqual(
                "/camera/front/image_raw  ·  MJPEG",
                VideoHud.Label("/camera/front/image_raw", "MJPEG"));
            StringAssert.Contains("WebRTC", VideoHud.Label("/camera/chase/image_raw", "WebRTC"));
            StringAssert.Contains("MJPEG", VideoHud.Label("/camera/front/image_raw", "MJPEG"));
        }

        [Test]
        public void JitterBuffer_KeepsOnlyTheLatestFrame()
        {
            var buffer = new MinimalJitterBuffer<int>();
            Assert.AreEqual(1, MinimalJitterBuffer<int>.Capacity);
            buffer.Push("chase", 1);
            buffer.Push("chase", 2);
            buffer.Push("chase", 3);
            buffer.Push("front", 9);

            Assert.AreEqual(1, buffer.Depth("chase"));
            Assert.AreEqual(2, buffer.Dropped("chase"));
            Assert.IsTrue(buffer.TryLatest("chase", out int chase));
            Assert.AreEqual(3, chase);
            Assert.IsTrue(buffer.TryLatest("front", out int front));
            Assert.AreEqual(9, front);
            Assert.AreEqual(0, buffer.Depth("wrist"));
        }

        private static void AssertCode(string code, TestDelegate call)
        {
            var ex = Assert.Throws<SignalingFormatException>(call);
            Assert.AreEqual(code, ex.Code);
        }
    }
}
