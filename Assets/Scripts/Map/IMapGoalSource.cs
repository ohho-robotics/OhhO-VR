using System;

namespace OmniBot.VR.Mapping
{
    /// <summary>
    /// Map-frame pose raised when the operator taps the map. OHH-227 consumes
    /// this. Nothing in this assembly sends a Nav2 goal.
    /// </summary>
    public readonly struct MapFramePose
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Yaw;
        public readonly string FrameId;

        public MapFramePose(float x, float y, float yaw, string frameId)
        {
            X = x;
            Y = y;
            Yaw = yaw;
            FrameId = string.IsNullOrEmpty(frameId) ? MapTopics.DefaultMapFrame : frameId;
        }
    }

    public interface IMapGoalSource
    {
        event Action<MapFramePose> GoalRequested;
    }
}
