# Cloud sim

Not ready. There is no cloud simulator to connect to.

[OHH-216](https://linear.app/ohho-robotics/issue/OHH-216) has no branch in the local OhhO-VR or OmniBot checkouts that were read for this page (14 Oct 2026). OhhO-VR commit [`4ebc555`](https://github.com/ohho-robotics/OhhO-VR/commit/4ebc555a7495ee7a2cd0f62cbe85fbc201ad1d4b) has no cloud client. The session picker on `ohh-212-session-picker` ([PR #9](https://github.com/ohho-robotics/OhhO-VR/pull/9), commit `cb6a6b3`) has a Cloud page, and [`SessionCatalog.TryStartCloud`](https://github.com/ohho-robotics/OhhO-VR/blob/ohh-212-session-picker/Assets/Scripts/Control/Session/SessionCatalog.cs) returns this string when `CloudStarter` is null, which is how the shared catalog is constructed:

> Cloud orchestrator is not configured. Attach with a rosbridge URL, or use Local desktop.

Nothing in that commit assigns `CloudStarter`. "Start cloud session" does not launch a VM, a container, or a session row. There is no gateway, no Supabase check on the WebSocket, and no TURN server in either repo.

The Attach field on that same page parses a `ws://` or `wss://` URL ([`EndpointAddresses.TryRosbridge`](https://github.com/ohho-robotics/OhhO-VR/blob/ohh-212-session-picker/Assets/Scripts/Control/Session/RobotEndpoint.cs)). That is a manual socket address, the same class of connection as typing an IP. It is not an OhhO cloud product. Do not point the headset at a rosbridge on the public internet. `sim.launch.py` documents that `rosbridge_address:=0.0.0.0` has no authentication ([launch file](https://github.com/ohho-robotics/OmniBot/blob/ohh-205-wsl2-network/omnibot-ros2/omnibot_bringup/launch/sim.launch.py)).

Use [local sim](setup-local-sim.md) on your own PC. Real robots use [setup-real-omnibot.md](setup-real-omnibot.md) and [safety.md](safety.md).

When a cloud orchestrator exists, this page should be replaced with the launch command, the exact URL the headset receives, how the token is checked, and what happens to the robot if the session process dies. Until those are in a repo, this page stays a refusal.
