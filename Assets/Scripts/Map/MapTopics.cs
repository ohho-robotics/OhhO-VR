using OmniBot.VR.Control;

namespace OmniBot.VR.Mapping
{
    /// <summary>
    /// Topic names the SLAM panel subscribes to. Empty profile fields fall back
    /// to the OmniBot contract from OHH-198 (<c>docs/vr.md</c>,
    /// <c>sim_mapping.launch.py</c>, Nav2 <c>nav2_params.yaml</c>):
    /// <c>/map</c>, <c>/scan</c>, <c>/odom</c>, <c>/tf</c> (map → odom →
    /// base_footprint), and the OccupancyGrid costmaps
    /// <c>/global_costmap/costmap</c> and <c>/local_costmap/costmap</c>.
    /// </summary>
    public static class MapTopics
    {
        public const string DefaultMap = "/map";
        public const string DefaultScan = "/scan";
        public const string DefaultOdom = "/odom";
        public const string DefaultTf = "/tf";
        public const string DefaultGlobalCostmap = "/global_costmap/costmap";
        public const string DefaultLocalCostmap = "/local_costmap/costmap";
        public const string DefaultMapFrame = "map";
        public const string DefaultBaseFrame = "base_footprint";

        public static MapTopicSet Resolve(RobotProfile profile)
        {
            RobotTopics topics = profile != null ? profile.Topics : null;
            return new MapTopicSet(
                Or(topics != null ? topics.Map : null, DefaultMap),
                Or(topics != null ? topics.Scan : null, DefaultScan),
                Or(topics != null ? topics.Odom : null, DefaultOdom),
                Or(topics != null ? topics.GlobalCostmap : null, DefaultGlobalCostmap),
                Or(topics != null ? topics.LocalCostmap : null, DefaultLocalCostmap));
        }

        static string Or(string value, string fallback)
        {
            return string.IsNullOrEmpty(value) ? fallback : value;
        }
    }

    public struct MapTopicSet
    {
        public readonly string Map;
        public readonly string Scan;
        public readonly string Odom;
        public readonly string GlobalCostmap;
        public readonly string LocalCostmap;

        public MapTopicSet(string map, string scan, string odom, string globalCostmap, string localCostmap)
        {
            Map = map;
            Scan = scan;
            Odom = odom;
            GlobalCostmap = globalCostmap;
            LocalCostmap = localCostmap;
        }

        public bool Equals(MapTopicSet other)
        {
            return Map == other.Map && Scan == other.Scan && Odom == other.Odom &&
                   GlobalCostmap == other.GlobalCostmap && LocalCostmap == other.LocalCostmap;
        }
    }
}
