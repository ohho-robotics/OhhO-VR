using System.Collections.Generic;

namespace OmniBot.VR.Latency
{
    public sealed class LatencySample
    {
        public long TUnixMs;
        public string Kind;
        public string Segment;
        public long? LatencyMs;
        public string Topic;
        public string Ack;
        public string Transport;
        public string Profile;
    }

    public sealed class LatencySegmentSummary
    {
        public string Segment;
        public int N;
        public int Incomplete;
        public long? P50Ms;
        public long? P95Ms;
        public int BudgetP95Ms;
        public string Verdict;
    }

    public sealed class LatencyReport
    {
        public string Profile;
        public string Clock;
        public bool HardwareRun;
        public bool Truncated;
        public List<LatencySample> Samples;
        public List<LatencySegmentSummary> Summary;

        public LatencySegmentSummary Find(string segmentId)
        {
            if (Summary == null) return null;
            for (int i = 0; i < Summary.Count; i++)
            {
                if (Summary[i].Segment == segmentId) return Summary[i];
            }
            return null;
        }
    }

    /// <summary>
    /// Records input, command, telemetry, heartbeat, and video timestamps and
    /// scores them against <see cref="LatencyBudget"/>. Observational: it does
    /// not publish, drop, or rewrite robot messages.
    /// </summary>
    public sealed class LatencySession
    {
        public const int MaxSamples = 50000;
        public const long InputPairWindowMs = 500;

        private readonly List<LatencySample> _samples = new List<LatencySample>();
        private readonly ILatencyClock _clock;
        private long? _lastInputMs;
        private long? _lastMotionInputMs;
        private PendingCommand _pending;
        private double[] _jointBaseline;
        private bool _truncated;

        public LatencyProfile Profile { get; }
        public bool HardwareRun { get; set; }
        public string LogDirectory { get; set; }
        public ILatencyClock Clock => _clock;

        public LatencySession(LatencyProfile profile, ILatencyClock clock)
        {
            Profile = profile;
            _clock = clock ?? new SystemLatencyClock();
        }

        public void MarkInput()
        {
            _lastInputMs = _clock.NowMs;
        }

        public void ClearInput()
        {
            _lastInputMs = null;
        }

        public void ObserveOutgoing(string topic, string json)
        {
            if (string.IsNullOrEmpty(topic)) return;
            long now = _clock.NowMs;
            string body = LatencyJson.MessageBody(json);

            if (LatencyJson.IsEmergencyStopTopic(topic))
            {
                Add(now, LatencyKind.Command, "command_sent", null, topic, "", "");
                return;
            }

            if (!LatencyJson.IsMotionCommandTopic(topic)) return;

            Add(now, LatencyKind.Command, "command_sent", null, topic, "", "");
            if (!LatencyJson.CommandHasMotion(body)) return;

            long? inputLatency = null;
            _lastMotionInputMs = null;
            if (_lastInputMs.HasValue)
            {
                long age = now - _lastInputMs.Value;
                if (age >= 0 && age <= InputPairWindowMs)
                {
                    inputLatency = age;
                    _lastMotionInputMs = _lastInputMs;
                }
            }

            Add(now, LatencyKind.Command, LatencyBudget.SegmentId(LatencySegment.InputToCommand),
                inputLatency, topic, LatencyAck.LocalSend, "");

            if (_pending != null)
            {
                Add(now, LatencyKind.Command, LatencyBudget.SegmentId(LatencySegment.CommandToMotion),
                    null, _pending.Topic, LatencyAck.Superseded, "");
            }
            _pending = new PendingCommand(topic, now);
        }

        public void ObserveIncoming(string topic, string json)
        {
            if (string.IsNullOrEmpty(topic)) return;
            long now = _clock.NowMs;
            string body = LatencyJson.MessageBody(json);

            if (LatencyJson.IsCommandAckTopic(topic))
            {
                long stamp;
                if (LatencyJson.TryReadStampUnixMs(body, out stamp))
                    NoteCommandAccepted(stamp, topic);
                else
                    Add(now, LatencyKind.Command, LatencyBudget.SegmentId(LatencySegment.InputToCommand),
                        null, topic, LatencyAck.RemoteUnstamped, "");
                return;
            }

            if (LatencyJson.IsHeartbeatTopic(topic))
            {
                long? age = null;
                long stamp;
                if (LatencyJson.TryReadStampUnixMs(body, out stamp))
                    age = now - stamp;
                Add(now, LatencyKind.Heartbeat, LatencyBudget.SegmentId(LatencySegment.HeartbeatAge),
                    age, topic, "", "");
                return;
            }

            if (!LatencyJson.IsTelemetryTopic(topic)) return;

            long? telAge = null;
            long telStamp;
            if (LatencyJson.TryReadStampUnixMs(body, out telStamp))
                telAge = now - telStamp;
            Add(now, LatencyKind.Telemetry, LatencyBudget.SegmentId(LatencySegment.TelemetryAge),
                telAge, topic, "", "");

            if (_pending != null && LatencyJson.TelemetryShowsMotion(body, _jointBaseline))
            {
                long motion = now - _pending.SentMs;
                string commandTopic = _pending.Topic;
                _pending = null;
                Add(now, LatencyKind.Command, LatencyBudget.SegmentId(LatencySegment.CommandToMotion),
                    motion, commandTopic, "", "");
            }

            RememberJoints(body);
        }

        public void NoteVideo(long? captureUnixMs, long displayedUnixMs, string transport)
        {
            long? latency = null;
            if (captureUnixMs.HasValue)
                latency = displayedUnixMs - captureUnixMs.Value;
            Add(displayedUnixMs, LatencyKind.Video, LatencyBudget.SegmentId(LatencySegment.GlassToGlass),
                latency, "", "", transport ?? "");
        }

        public void NoteCommandAccepted(long ackUnixMs)
        {
            NoteCommandAccepted(ackUnixMs, LatencyBudget.CommandAckTopic);
        }

        public LatencyReport BuildReport()
        {
            var summary = new List<LatencySegmentSummary>(LatencyBudget.SegmentCount);
            for (int s = 0; s < LatencyBudget.SegmentCount; s++)
            {
                var segment = (LatencySegment)s;
                summary.Add(Summarize(segment));
            }
            var samples = new List<LatencySample>(_samples.Count);
            for (int i = 0; i < _samples.Count; i++) samples.Add(_samples[i]);
            return new LatencyReport
            {
                Profile = LatencyBudget.ProfileId(Profile),
                Clock = _clock.Kind,
                HardwareRun = HardwareRun,
                Truncated = _truncated,
                Samples = samples,
                Summary = summary
            };
        }

        private void NoteCommandAccepted(long ackUnixMs, string topic)
        {
            long? latency = null;
            if (_lastMotionInputMs.HasValue)
                latency = ackUnixMs - _lastMotionInputMs.Value;
            Add(ackUnixMs, LatencyKind.Command, LatencyBudget.SegmentId(LatencySegment.InputToCommand),
                latency, topic, LatencyAck.Remote, "");
        }

        private void RememberJoints(string body)
        {
            double[] pos = LatencyJson.ReadNumberArray(body, "position");
            if (pos != null && pos.Length > 0)
                _jointBaseline = pos;
        }

        private LatencySegmentSummary Summarize(LatencySegment segment)
        {
            string id = LatencyBudget.SegmentId(segment);
            var complete = new List<long>();
            int n = 0;
            int incomplete = 0;
            for (int i = 0; i < _samples.Count; i++)
            {
                LatencySample sample = _samples[i];
                if (sample.Segment != id) continue;
                if (!Include(sample, segment)) continue;
                n++;
                if (!sample.LatencyMs.HasValue) incomplete++;
                else complete.Add(sample.LatencyMs.Value);
            }
            if (segment == LatencySegment.CommandToMotion && _pending != null)
                incomplete++;

            var row = new LatencySegmentSummary
            {
                Segment = id,
                N = n,
                Incomplete = incomplete,
                BudgetP95Ms = LatencyBudget.P95Milliseconds(Profile, segment)
            };

            if (complete.Count == 0)
            {
                row.Verdict = (n == 0 && incomplete == 0)
                    ? LatencyVerdict.NotMeasured
                    : LatencyVerdict.Incomplete;
                return row;
            }

            complete.Sort();
            long[] sorted = complete.ToArray();
            row.P50Ms = LatencyStats.PercentileNearestRank(sorted, 0.50);
            row.P95Ms = LatencyStats.PercentileNearestRank(sorted, 0.95);
            bool negative = false;
            for (int i = 0; i < sorted.Length; i++)
            {
                if (sorted[i] < 0) { negative = true; break; }
            }
            if (negative || row.P95Ms.Value > row.BudgetP95Ms)
                row.Verdict = LatencyVerdict.Fail;
            else if (incomplete > 0)
                row.Verdict = LatencyVerdict.Incomplete;
            else
                row.Verdict = LatencyVerdict.Pass;
            return row;
        }

        private bool Include(LatencySample sample, LatencySegment segment)
        {
            if (segment != LatencySegment.InputToCommand) return true;
            if (LatencyBudget.RequiresRemoteCommandAck(Profile, segment))
                return sample.Ack == LatencyAck.Remote;
            return sample.Ack == LatencyAck.LocalSend;
        }

        private void Add(long tUnixMs, string kind, string segment, long? latencyMs, string topic, string ack, string transport)
        {
            if (_samples.Count >= MaxSamples)
            {
                _truncated = true;
                return;
            }
            _samples.Add(new LatencySample
            {
                TUnixMs = tUnixMs,
                Kind = kind,
                Segment = segment,
                LatencyMs = latencyMs,
                Topic = topic ?? "",
                Ack = ack ?? "",
                Transport = transport ?? "",
                Profile = LatencyBudget.ProfileId(Profile)
            });
        }

        private sealed class PendingCommand
        {
            public readonly string Topic;
            public readonly long SentMs;

            public PendingCommand(string topic, long sentMs)
            {
                Topic = topic;
                SentMs = sentMs;
            }
        }
    }
}
