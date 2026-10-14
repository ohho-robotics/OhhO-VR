using System.Collections.Generic;

namespace OmniBot.VR.Core
{
    /// <summary>
    /// Reassembles rosbridge <c>fragment</c> frames into one JSON message.
    /// Large <c>/map</c> and <c>/plan</c> payloads arrive this way.
    /// </summary>
    public sealed class RosbridgeFragmentBuffer
    {
        public const int MaxFragments = 512;
        private readonly Dictionary<string, string[]> _parts = new Dictionary<string, string[]>();

        /// <summary>Returns the concatenated message when every fragment has arrived.</summary>
        public string Push(string id, int num, int total, string data)
        {
            if (string.IsNullOrEmpty(id) || data == null) return null;
            if (total <= 0 || total > MaxFragments || num < 0 || num >= total) return null;

            if (!_parts.TryGetValue(id, out var slots) || slots.Length != total)
            {
                slots = new string[total];
                _parts[id] = slots;
            }
            slots[num] = data;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] == null) return null;
            }
            _parts.Remove(id);
            return string.Concat(slots);
        }
    }
}
