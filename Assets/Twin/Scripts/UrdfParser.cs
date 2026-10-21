using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;

namespace OmniBot.VR.Twin
{
    /// <summary>
    /// Reads link, joint, and material elements from a plain URDF. Gazebo plugins,
    /// transmissions, and collision tags are ignored — the twin is a display.
    /// </summary>
    public static class UrdfParser
    {
        public static UrdfDocument Parse(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml))
                throw new FormatException("URDF text is empty.");

            XDocument document = XDocument.Parse(xml);
            XElement robot = document.Root;
            if (robot == null || robot.Name.LocalName != "robot")
                throw new FormatException("URDF root element must be <robot>.");

            var colors = new Dictionary<string, UrdfColor>();
            foreach (XElement element in robot.Descendants())
            {
                if (element.Name.LocalName != "material")
                    continue;
                XElement color = element.Element("color");
                XAttribute name = element.Attribute("name");
                if (color == null || name == null)
                    continue;
                colors[name.Value] = ReadColor(color.Attribute("rgba") != null ? color.Attribute("rgba").Value : null);
            }

            var result = new UrdfDocument();
            XAttribute robotName = robot.Attribute("name");
            result.Name = robotName != null ? robotName.Value : "";

            foreach (XElement child in robot.Elements())
            {
                if (child.Name.LocalName == "link")
                {
                    UrdfLink link = ReadLink(child, colors);
                    if (link != null)
                        result.Links.Add(link);
                }
                else if (child.Name.LocalName == "joint")
                {
                    UrdfJoint joint = ReadJoint(child);
                    if (joint != null)
                        result.Joints.Add(joint);
                }
            }
            return result;
        }

        static UrdfLink ReadLink(XElement element, Dictionary<string, UrdfColor> colors)
        {
            string name = Attr(element, "name");
            if (string.IsNullOrEmpty(name))
                return null;
            var link = new UrdfLink { Name = name };
            foreach (XElement visual in element.Elements("visual"))
                link.Visuals.Add(ReadVisual(visual, colors));
            return link;
        }

        static UrdfVisual ReadVisual(XElement visual, Dictionary<string, UrdfColor> colors)
        {
            var item = new UrdfVisual();
            ReadOrigin(visual.Element("origin"), out item.OriginX, out item.OriginY, out item.OriginZ, out item.Roll, out item.Pitch, out item.Yaw);
            XElement geometry = visual.Element("geometry");
            if (geometry != null)
                item.Geometry = ReadGeometry(geometry);
            item.Color = ReadMaterialRef(visual.Element("material"), colors);
            return item;
        }

        static UrdfGeometry ReadGeometry(XElement geometry)
        {
            var shape = new UrdfGeometry
            {
                MeshScaleX = 1f,
                MeshScaleY = 1f,
                MeshScaleZ = 1f,
            };
            foreach (XElement child in geometry.Elements())
            {
                string kind = child.Name.LocalName;
                if (kind == "box")
                {
                    float[] size = ReadFloats(Attr(child, "size"), 3, "box size");
                    shape.Kind = UrdfGeometryKind.Box;
                    shape.SizeX = size[0];
                    shape.SizeY = size[1];
                    shape.SizeZ = size[2];
                    return shape;
                }
                if (kind == "cylinder")
                {
                    shape.Kind = UrdfGeometryKind.Cylinder;
                    shape.Radius = ReadFloat(Attr(child, "radius"), "cylinder radius");
                    shape.Length = ReadFloat(Attr(child, "length"), "cylinder length");
                    return shape;
                }
                if (kind == "sphere")
                {
                    shape.Kind = UrdfGeometryKind.Sphere;
                    shape.Radius = ReadFloat(Attr(child, "radius"), "sphere radius");
                    return shape;
                }
                if (kind == "mesh")
                {
                    shape.Kind = UrdfGeometryKind.Mesh;
                    shape.MeshFilename = Attr(child, "filename");
                    string scale = Attr(child, "scale");
                    if (!string.IsNullOrEmpty(scale))
                    {
                        float[] values = ReadFloats(scale, 3, "mesh scale");
                        shape.MeshScaleX = values[0];
                        shape.MeshScaleY = values[1];
                        shape.MeshScaleZ = values[2];
                    }
                    return shape;
                }
            }
            return shape;
        }

        static UrdfJoint ReadJoint(XElement element)
        {
            string name = Attr(element, "name");
            if (string.IsNullOrEmpty(name))
                return null;
            var joint = new UrdfJoint
            {
                Name = name,
                Type = ParseType(Attr(element, "type")),
            };
            XElement parent = element.Element("parent");
            XElement child = element.Element("child");
            joint.Parent = parent != null ? Attr(parent, "link") : "";
            joint.Child = child != null ? Attr(child, "link") : "";
            ReadOrigin(element.Element("origin"), out joint.OriginX, out joint.OriginY, out joint.OriginZ, out joint.Roll, out joint.Pitch, out joint.Yaw);
            XElement axis = element.Element("axis");
            if (axis != null && !string.IsNullOrEmpty(Attr(axis, "xyz")))
            {
                float[] xyz = ReadFloats(Attr(axis, "xyz"), 3, "joint axis");
                Normalize(xyz[0], xyz[1], xyz[2], out joint.AxisX, out joint.AxisY, out joint.AxisZ);
            }
            XElement limit = element.Element("limit");
            if (limit != null && (limit.Attribute("lower") != null || limit.Attribute("upper") != null))
            {
                joint.HasLimits = true;
                joint.Lower = limit.Attribute("lower") != null ? ReadFloat(limit.Attribute("lower").Value, "lower") : 0f;
                joint.Upper = limit.Attribute("upper") != null ? ReadFloat(limit.Attribute("upper").Value, "upper") : 0f;
            }
            return joint;
        }

        static UrdfColor ReadMaterialRef(XElement material, Dictionary<string, UrdfColor> colors)
        {
            if (material == null)
                return new UrdfColor();
            XElement inline = material.Element("color");
            if (inline != null)
                return ReadColor(inline.Attribute("rgba") != null ? inline.Attribute("rgba").Value : null);
            string name = Attr(material, "name");
            UrdfColor color;
            if (!string.IsNullOrEmpty(name) && colors.TryGetValue(name, out color))
                return color;
            return new UrdfColor();
        }

        static UrdfColor ReadColor(string rgba)
        {
            if (string.IsNullOrWhiteSpace(rgba))
                return new UrdfColor();
            string[] parts = Split(rgba);
            if (parts.Length < 3)
                return new UrdfColor();
            return new UrdfColor
            {
                R = ReadFloat(parts[0], "color"),
                G = ReadFloat(parts[1], "color"),
                B = ReadFloat(parts[2], "color"),
                A = parts.Length > 3 ? ReadFloat(parts[3], "color") : 1f,
                Specified = true,
            };
        }

        static void ReadOrigin(XElement origin, out float x, out float y, out float z, out float roll, out float pitch, out float yaw)
        {
            x = y = z = roll = pitch = yaw = 0f;
            if (origin == null)
                return;
            string xyz = Attr(origin, "xyz");
            if (!string.IsNullOrEmpty(xyz))
            {
                float[] values = ReadFloats(xyz, 3, "origin xyz");
                x = values[0];
                y = values[1];
                z = values[2];
            }
            string rpy = Attr(origin, "rpy");
            if (!string.IsNullOrEmpty(rpy))
            {
                float[] values = ReadFloats(rpy, 3, "origin rpy");
                roll = values[0];
                pitch = values[1];
                yaw = values[2];
            }
        }

        static UrdfJointType ParseType(string type)
        {
            switch (type)
            {
                case "revolute": return UrdfJointType.Revolute;
                case "continuous": return UrdfJointType.Continuous;
                case "prismatic": return UrdfJointType.Prismatic;
                case "fixed": return UrdfJointType.Fixed;
                case "floating": return UrdfJointType.Floating;
                case "planar": return UrdfJointType.Planar;
                default: return UrdfJointType.Unknown;
            }
        }

        public static int CountVisualTriangles(UrdfDocument document)
        {
            if (document == null)
                return 0;
            int count = 0;
            for (int i = 0; i < document.Links.Count; i++)
            {
                List<UrdfVisual> visuals = document.Links[i].Visuals;
                for (int v = 0; v < visuals.Count; v++)
                    count += TwinPrimitives.TrianglesFor(visuals[v].Geometry.Kind);
            }
            return count;
        }

        static void Normalize(float x, float y, float z, out float ox, out float oy, out float oz)
        {
            double length = Math.Sqrt(x * x + y * y + z * z);
            if (length < 1e-12)
            {
                ox = 1f;
                oy = 0f;
                oz = 0f;
                return;
            }
            float inv = (float)(1.0 / length);
            ox = x * inv;
            oy = y * inv;
            oz = z * inv;
        }

        static string Attr(XElement element, string name)
        {
            if (element == null)
                return "";
            XAttribute attribute = element.Attribute(name);
            return attribute != null ? attribute.Value.Trim() : "";
        }

        static float ReadFloat(string text, string label)
        {
            float value;
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                throw new FormatException("URDF " + label + " is not a number: '" + text + "'");
            return value;
        }

        static float[] ReadFloats(string text, int count, string label)
        {
            string[] parts = Split(text);
            if (parts.Length < count)
                throw new FormatException("URDF " + label + " expected " + count + " numbers: '" + text + "'");
            var values = new float[count];
            for (int i = 0; i < count; i++)
                values[i] = ReadFloat(parts[i], label);
            return values;
        }

        static string[] Split(string text)
        {
            return text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
