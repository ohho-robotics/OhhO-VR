using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace OmniBot.VR.Latency
{
    /// <summary>Writes a session report as CSV and JSON. Does not talk to a robot.</summary>
    public static class LatencyLog
    {
        public static void Write(string directory, LatencyReport report)
        {
            if (string.IsNullOrEmpty(directory))
                throw new System.ArgumentException("log directory is required", nameof(directory));
            if (report == null)
                throw new System.ArgumentNullException(nameof(report));
            Directory.CreateDirectory(directory);
            string stem = Path.Combine(directory, "latency-" + (report.Profile ?? "local"));
            File.WriteAllText(stem + ".csv", ToCsv(report), new UTF8Encoding(false));
            File.WriteAllText(stem + ".json", ToJson(report), new UTF8Encoding(false));
        }

        public static string ToCsv(LatencyReport report)
        {
            var sb = new StringBuilder();
            sb.Append("t_unix_ms,kind,segment,latency_ms,topic,ack,transport,profile\n");
            if (report.Samples == null) return sb.ToString();
            for (int i = 0; i < report.Samples.Count; i++)
            {
                LatencySample s = report.Samples[i];
                sb.Append(s.TUnixMs.ToString(CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(Csv(s.Kind));
                sb.Append(',');
                sb.Append(Csv(s.Segment));
                sb.Append(',');
                sb.Append(s.LatencyMs.HasValue
                    ? s.LatencyMs.Value.ToString(CultureInfo.InvariantCulture)
                    : "");
                sb.Append(',');
                sb.Append(Csv(s.Topic));
                sb.Append(',');
                sb.Append(Csv(s.Ack));
                sb.Append(',');
                sb.Append(Csv(s.Transport));
                sb.Append(',');
                sb.Append(Csv(s.Profile));
                sb.Append('\n');
            }
            return sb.ToString();
        }

        public static string ToJson(LatencyReport report)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"profile\": ").Append(JsonString(report.Profile)).Append(",\n");
            sb.Append("  \"clock\": ").Append(JsonString(report.Clock)).Append(",\n");
            sb.Append("  \"hardware_run\": ").Append(report.HardwareRun ? "true" : "false").Append(",\n");
            sb.Append("  \"truncated\": ").Append(report.Truncated ? "true" : "false").Append(",\n");
            sb.Append("  \"budgets_p95_ms\": ");
            AppendBudgets(sb, report.Profile);
            sb.Append(",\n");
            sb.Append("  \"samples\": [\n");
            var samples = report.Samples ?? new List<LatencySample>();
            for (int i = 0; i < samples.Count; i++)
            {
                AppendSample(sb, samples[i]);
                sb.Append(i == samples.Count - 1 ? "\n" : ",\n");
            }
            sb.Append("  ],\n");
            sb.Append("  \"summary\": [\n");
            var summary = report.Summary ?? new List<LatencySegmentSummary>();
            for (int i = 0; i < summary.Count; i++)
            {
                AppendSummary(sb, summary[i]);
                sb.Append(i == summary.Count - 1 ? "\n" : ",\n");
            }
            sb.Append("  ]\n");
            sb.Append("}\n");
            return sb.ToString();
        }

        private static void AppendBudgets(StringBuilder sb, string profileId)
        {
            LatencyProfile profile = LatencyProfile.Local;
            LatencyBudget.TryParseProfile(profileId, out profile);
            sb.Append("{\n");
            for (int s = 0; s < LatencyBudget.SegmentCount; s++)
            {
                var segment = (LatencySegment)s;
                sb.Append("    \"").Append(LatencyBudget.SegmentId(segment)).Append("\": ");
                sb.Append(LatencyBudget.P95Milliseconds(profile, segment));
                sb.Append(s == LatencyBudget.SegmentCount - 1 ? "\n" : ",\n");
            }
            sb.Append("  }");
        }

        private static void AppendSample(StringBuilder sb, LatencySample s)
        {
            sb.Append("    {");
            sb.Append("\"t_unix_ms\": ").Append(s.TUnixMs);
            sb.Append(", \"kind\": ").Append(JsonString(s.Kind));
            sb.Append(", \"segment\": ").Append(JsonString(s.Segment));
            sb.Append(", \"latency_ms\": ");
            if (s.LatencyMs.HasValue) sb.Append(s.LatencyMs.Value);
            else sb.Append("null");
            sb.Append(", \"topic\": ").Append(JsonString(s.Topic));
            sb.Append(", \"ack\": ").Append(JsonString(s.Ack));
            sb.Append(", \"transport\": ").Append(JsonString(s.Transport));
            sb.Append(", \"profile\": ").Append(JsonString(s.Profile));
            sb.Append('}');
        }

        private static void AppendSummary(StringBuilder sb, LatencySegmentSummary row)
        {
            sb.Append("    {");
            sb.Append("\"segment\": ").Append(JsonString(row.Segment));
            sb.Append(", \"n\": ").Append(row.N);
            sb.Append(", \"incomplete\": ").Append(row.Incomplete);
            sb.Append(", \"p50_ms\": ");
            AppendNullable(sb, row.P50Ms);
            sb.Append(", \"p95_ms\": ");
            AppendNullable(sb, row.P95Ms);
            sb.Append(", \"budget_p95_ms\": ").Append(row.BudgetP95Ms);
            sb.Append(", \"verdict\": ").Append(JsonString(row.Verdict));
            sb.Append('}');
        }

        private static void AppendNullable(StringBuilder sb, long? value)
        {
            if (value.HasValue) sb.Append(value.Value);
            else sb.Append("null");
        }

        private static string JsonString(string value)
        {
            if (value == null) return "null";
            var sb = new StringBuilder(value.Length + 2);
            sb.Append('"');
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        private static string Csv(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            bool quote = false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == ',' || c == '"' || c == '\n' || c == '\r') { quote = true; break; }
            }
            if (!quote) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
