# Beta session feedback

Copy this file, fill it in after one session, and send it back. One file per session.
`tools/beta_triage.py` reads this shape. Do not rename the keys. Leave `notes:` as the last key.
Write `none` when a list or a crash field is empty. Do not delete a key.

tester_id:
date:
build:
sim: local
completed_first_drive: yes
time_to_first_drive_s:
time_to_first_drive_source: headset-log
crashes: none
crash_count: 0
comfort: fine
comfort_notes:
confusion:
- 
notes:

<!--
tester_id          id from the invite. Not a legal name.
date               YYYY-MM-DD
build              apk file name you installed
sim                local, cloud, or unknown
                   local = Gazebo on a machine you or Varun started
                   cloud = a rosbridge host Varun sent (there is no public cloud URL in this repo)
completed_first_drive
                   yes or no. yes only if the headset logged [FirstDrive] or you are sure a stick command was sent
time_to_first_drive_s
                   the since_connect_s number from the [FirstDrive] log line. Blank if you never got one.
time_to_first_drive_source
                   headset-log, self-timed, or not-reached
crashes            none, or what the headset did
crash_count        integer, 0 if none
comfort            fine, mild, or quit
                   quit = you stopped because you felt unwell
comfort_notes      short
confusion          one "- " line per thing that was unclear. Delete the sample dash if there were none and write: confusion: none
notes              anything else. Lines under notes are not extra fields.
-->
