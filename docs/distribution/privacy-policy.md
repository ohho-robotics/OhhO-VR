# Privacy policy (draft)

**Draft for Varun to host. This is not legal advice, and it is not published.**

Host the reviewed text at a public HTTPS URL on a site OhhO controls, with a certificate from a publicly trusted certificate authority. Put that URL in the Meta Horizon submission and in the SideQuest listing. Replace every TODO before hosting. The in-repo copy can stay as the source draft.

Suggested public title: Privacy policy for OhhO Pilot.

Effective date: TODO(Varun).

## Who we are

OhhO Pilot is the Meta Quest headset app from OhhO Robotics. It is the headset client for an OhhO robot and uses the same account as https://ohho-robotics.com.

- Controller: TODO(Varun): legal name, postal address, and country.
- Privacy contact: TODO(Varun): a monitored email address or web form.
- Website: https://ohho-robotics.com

In the Unity project the application id and display name are not final (`productName` is `vr_app`). The policy should use the public name Varun chooses. The draft uses OhhO Pilot.

## What this app does

You sign in, see the robots on your OhhO account, and connect the headset to a robot on your network. Controllers can drive the robot. Hand tracking can move the arm. The headset can show the robot's camera and can record a demonstration on the device. You can upload that recording to the robot you connected.

## Data the app processes

### Account

- Your email address, which the app sends to the OhhO account service (Supabase, the same auth service as the website) to email you a one-time code.
- The code you type back, which the app sends to that service to open a session.
- Signing in can create an OhhO account for that email if one does not exist yet.
- A short-lived access token, held in memory while the app is running.
- A refresh token, stored on the headset in the app's private preferences (`ohho.supabase.refresh_token`) so you can stay signed in.
- Your account id and email, returned by that service and held in memory for the session.
- The list of robots on your account: name, identifiers, status, configuration, and timestamps. The app reads this from your account so the garage matches the website.

The app uses this data to sign you in, show your robots, and keep the session until you sign out.

### On the headset

These values stay in the app's private storage on the headset:

- Robot address and port you entered, and whether to reconnect automatically.
- The last robot you selected (id, name, address, port).
- Calibration numbers for a robot (position and scale adjustments).
- A flag that you have seen the onboarding hint.

The app uses them to restore the last session.

### Demonstrations you record

If you start a recording, the app stores a file on the headset. Each line contains time, arm joint positions, base velocity, odometry, and the arm and base commands the app sent. The app writes that file so you can keep the demonstration and, if you choose export, upload it to the robot address you entered.

The recording file does not contain camera images. The robot's camera is shown live from the robot and is not saved by the recorder in this client.

### The robot you connect to

When you connect, the headset sends teleop commands to that robot and receives robot telemetry (joint state, odometry, and related status) from it. The camera view is fetched from the robot. If you export a recording, the file is uploaded to that robot.

You choose the robot address. That computer is yours (or one your account is allowed to use). The headset does not send that recording to OhhO's website as part of this client.

### Hands, controllers, and passthrough

Horizon OS tracks your head, controllers, and, if you allow it, your hands, so the app can place the screen and drive the robot. Hand tracking runs so the arm can follow your hand. The app's recording stores the resulting joint and base commands, not a video of your hands or your room.

The app shows passthrough (your real room) through Horizon OS. Passthrough camera access is off in this client's Meta project settings, so this app does not request camera frames of the room.

### Meta Quest software

This app is built with the Meta XR SDK. That SDK can send diagnostic telemetry to Meta, including a project identifier that the SDK places in the Android manifest (`com.oculus.telemetry.project_guid`). Meta's own privacy policy describes what Meta does with Quest and SDK data: https://www.meta.com/legal/privacy-policy/

TODO(Varun): confirm whether Meta telemetry stays enabled for the build you ship, and name any other Meta data uses (Platform entitlement, cloud backup, or analytics) if you turn them on later. They are not part of this draft's OhhO account flow.

### What this client does not collect

This client does not request the microphone, precise location, contacts, photos, or advertising ID. It does not include an advertising SDK. It does not sell personal information.

## Why we process it

- To provide the app: sign-in, your robot list, the connection, teleop, the camera view, and recordings you start.
- To keep the session and the last robot on the headset so you do not have to enter them every time.
- To send a recording to your robot when you tap export.

## Who receives it

- OhhO's account host (Supabase for the OhhO website project) receives your email, one-time code, session, and garage data.
- The robot computer at the address you enter receives teleop commands, camera requests, and any recording you export.
- Meta receives the SDK and device data described in Meta's privacy policy.
- TODO(Varun): list any other processor (support inbox, error reporting, or a hosted database) if one is added.

## How long it is kept

- The refresh token and the on-device preferences stay on the headset until you sign out, clear app data, or uninstall.
- Recordings stay on the headset until you delete them in the app or uninstall.
- A recording you exported stays on the robot computer until someone deletes it there.
- The account and garage stay in the OhhO account service until you ask for deletion or delete them from the website tools that exist for your account.
- TODO(Varun): state the retention period for account data on the server, matching the website policy.

## How to ask for deletion

You can remove the headset copy yourself:

1. Sign out in the app. That deletes the refresh token stored on the headset.
2. Delete recordings from the teleop page, or uninstall OhhO Pilot. Uninstalling removes the app's private storage on that headset, including preferences, calibration, and local recordings.

To delete the OhhO account and the robot list stored with it, contact TODO(Varun): privacy contact email. Include the email address on the account. We will delete the account data we hold for that address, except where we have to keep a record for a legal reason. TODO(Varun): say how long that request takes.

Recordings already uploaded to a robot are on that robot's computer. Delete those files on the robot. Meta-held telemetry has to be requested from Meta under Meta's policy.

## Children

OhhO Pilot is not directed at children under 13. It controls a physical robot. Do not use it if you are under 13. If a parent believes a child has created an account, use the contact above and we will delete that account.

If this app is distributed on the Meta Horizon platform, Meta's rules also prohibit use by children under 10. TODO(Varun): match this section to the age group you self-certify (the engineering recommendation is Teens and Adults, 13+).

## Security

Account traffic to OhhO uses HTTPS. The refresh token is stored in the app's private on-device preferences. That storage is private to the app. It is not a separate hardware keystore.

The connection from the headset to a robot is aimed at the local network address you type. Treat that network as you would any other device that can drive the robot.

## Changes

TODO(Varun): if the policy changes, update the effective date on the hosted page. If a change materially expands what the app collects, say how users will hear about it.

## Contact

TODO(Varun): privacy contact email

OhhO Robotics

TODO(Varun): postal address
