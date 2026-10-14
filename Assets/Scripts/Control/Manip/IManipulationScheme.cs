namespace OmniBot.VR.Control.Manip
{
    /// <summary>
    /// Manipulation strategy — maps tracked hand motion to the selected robot's
    /// arm joint commands and publishes them to
    /// <c>RobotProfile.Topics.ArmCommands</c>. One implementation per arm
    /// topology, selected by <see cref="ControlSchemeFactory"/> from the profile's
    /// <c>ArmDof</c> + capabilities.
    ///
    /// Today: <see cref="HandIK6DofScheme"/> (one hand → one 6-DOF arm, the
    /// OmniBot/SO-101 + UR5e case). Phase 3 adds DualArm, GripperOnly and None.
    /// The scheme owns its publish-rate timing and the arm-enable toggle so
    /// <see cref="TeleopController"/> stays a thin input pump.
    /// </summary>
    public interface IManipulationScheme
    {
        /// <summary>Configure the scheme from the selected robot's profile.</summary>
        void Configure(RobotProfile profile, UnityEngine.Transform workspaceOrigin);

        /// <summary>
        /// Advance the scheme one frame: retarget the hand pose → IK → joint
        /// angles → publish. Called every Unity frame by
        /// <see cref="TeleopController"/> with the latest left/right hand poses.
        /// Returns true if a command was published this tick.
        /// </summary>
        bool Tick(ref HandPose rightHand, ref HandPose leftHand, IRobotLink link);

        /// <summary>Stop publishing arm commands (disarm). Called on e-stop or robot switch.</summary>
        void Stop(IRobotLink link);

        /// <summary>Whether the arm is torqued on. Motion still requires the clutch.</summary>
        bool ArmEnabled { get; }

        /// <summary>Enable gate phase. Real arms sit in <see cref="ArmEnablePhase.AwaitingRealConfirmation"/> until confirmed.</summary>
        ArmEnablePhase EnablePhase { get; }

        /// <summary>True while torque is on and the clutch trigger is held.</summary>
        bool ClutchEngaged { get; }

        /// <summary>
        /// Operator intent for this frame: enable request, REAL confirm, clutch,
        /// jog, and e-stop. Levels persist; one-shot edges are consumed by
        /// <see cref="Tick"/>.
        /// </summary>
        void FeedArmInput(ArmOperatorInput input);

        /// <summary>
        /// Request (true) or disarm (false). True does not confirm a pending
        /// REAL enable — call <see cref="FeedArmInput"/> with
        /// <see cref="ArmOperatorInput.ConfirmRequested"/> for that.
        /// </summary>
        void SetArmEnabled(bool enabled, IRobotLink link);

        /// <summary>One-line HUD hint, e.g. "Right trigger: clutch · Left X: enable".</summary>
        string HudHint { get; }

        /// <summary>Live gate status for the teleop HUD.</summary>
        string StatusText { get; }

        /// <summary>True once <see cref="Configure"/> has run with a valid profile + arm.</summary>
        bool IsConfigured { get; }
    }
}
