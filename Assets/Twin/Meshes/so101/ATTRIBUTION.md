# SO-101 mesh attribution

These OBJ files are vertex-clustered decimations of the STL meshes in
`omnibot_description/meshes/so101/` from ohho-robotics/OmniBot.

Those STLs are the SO-ARM101 parts published by TheRobotStudio in
[SO-ARM100](https://github.com/TheRobotStudio/SO-ARM100), licensed under the
Apache License, Version 2.0. OmniBot vendors them under the same Apache-2.0
licence as the rest of that repository.

http://www.apache.org/licenses/LICENSE-2.0

The meshes were modified for Quest: triangle counts were reduced by vertex
clustering so the set stays under 50 000 triangles. See `budget.json`. Units
are unchanged from the STLs (metres). The live twin does not instance these
OBJs. It draws the URDF boxes and cylinders, which is what Gazebo draws. The
loader will use an OBJ when a URDF visual references a mesh filename
(`package://` paths resolve to this folder by file name).

Keep this file with any copy of the OBJs.
