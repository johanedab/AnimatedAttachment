using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace VectorHelpers
{
    public class PosRot
    {
        public Quaternion rotation;
        public Vector3 position;
        public Vector3 orientation;

        public override string ToString()
        {
            if (this == null)
                return "null";

            if (orientation != Vector3.zero)
                return string.Format("{0}, {1}, {2}, {3}",
                    position,
                    rotation,
                    rotation.eulerAngles,
                    orientation);
            return string.Format("{0}, {1}, {2}",
                position,
                rotation,
                rotation.eulerAngles);
        }

        public void Save(ConfigNode root, string name)
        {
            ConfigNode node = root.AddNode("POS_ROT");

            node.AddValue("name", name);
            node.AddValue("position", position);
            node.AddValue("rotation", rotation);
            node.AddValue("orientation", orientation);
        }

        public void Load(ConfigNode root, string name)
        {
            if (root == null)
                return;

            ConfigNode node = root.GetNode("POS_ROT");

            if (node == null)
                return;

            if (node.GetValue("name") != "offset")
                return;

            position = VectorHelper.StringToVector3(node.GetValue("position"));
            rotation = VectorHelper.StringToQuaternion(node.GetValue("rotation"));
            orientation = VectorHelper.StringToVector3(node.GetValue("orientation"));
        }

        // Get a rotation from a node in a part relative to the part instead of the immediate parent
        public static PosRot GetPosRot(Transform transform, Part part)
        {
            PosRot result = new PosRot
            {
                rotation = Quaternion.identity
            };

            do
            {
                // Use the scaling from the parent, if there is one.
                // The only known situation where we will not have a parent
                // is if the transform is a jettisonable transform that has
                // its parent set to the decoupler part.
                if (transform.parent == null)
                    return null;

                // Walk up the tree to the part transform, adding up all the local positions and rotations
                // to make them relative to the part transform
                result.position = transform.localRotation * result.position + Vector3.Scale(transform.parent.localScale, transform.localPosition);
                result.rotation = transform.localRotation * result.rotation;

                transform = transform.parent;
            }
            while (transform != null && transform != part.transform);

            // Update the orientation vector
            result.orientation = result.rotation * Vector3.forward;

            // Include the rescale factor
            result.position *= part.rescaleFactor;

            return result;
        }
    }

    // Create a line in a specific reference frame and update it
    public class LineInfo
    {
        public GameObject gameObject;
        public LineRenderer lineRenderer;

        public LineInfo(Transform parent, Color color)
        {
            // First of all, create a GameObject to which LineRenderer will be attached
            gameObject = new GameObject("Line");

            // Then create renderer itself
            lineRenderer = gameObject.AddComponent<LineRenderer>();
            lineRenderer.transform.parent = parent;

            // The line moves along with the space (rather than staying in fixed world coordinates)
            lineRenderer.useWorldSpace = false;

            // Set the space for the line (?)
            lineRenderer.transform.localPosition = Vector3.zero;
            lineRenderer.transform.localEulerAngles = Vector3.zero;

            // Set the style of the line
            lineRenderer.material = new Material(Shader.Find("Particles/Additive"));
            lineRenderer.startColor = color;
            lineRenderer.endColor = color;
            lineRenderer.startWidth = 0.1f;
            lineRenderer.endWidth = 0.0f;
            lineRenderer.positionCount = 2;
        }

        // Set the coordinates of the line
        public void Update(Vector3 from, Vector3 to)
        {
            lineRenderer.SetPosition(0, from);
            lineRenderer.SetPosition(1, to);
        }

        internal void Destroy()
        {
            lineRenderer = null;
        }
    }

    // Show two vectors, one set at creation and one update in real-time
    public class OrientationInfo
    {
        public LineInfo original;
        public LineInfo current;

        public OrientationInfo(Transform transform, Vector3 from, Vector3 to)
        {
            original = new LineInfo(transform, Color.red);
            current = new LineInfo(transform, Color.blue);
            original.Update(from, to);
        }

        public void Update(Vector3 from, Vector3 to)
        {
            current.Update(from, to);
        }

        public void Destroy()
        {
            original.Destroy();
            original = null;
            current.Destroy();
            current = null;
        }
    }

    // Show the 3 unit vectors in a specific space
    public class AxisInfo
    {
        public LineInfo x;
        public LineInfo y;
        public LineInfo z;

        public AxisInfo(Transform transform)
        {
            x = new LineInfo(transform, Color.red);
            y = new LineInfo(transform, Color.green);
            z = new LineInfo(transform, Color.blue);
            x.Update(Vector3.zero, new Vector3(1f, 0, 0));
            y.Update(Vector3.zero, new Vector3(0, 1f, 0));
            z.Update(Vector3.zero, new Vector3(0, 0, 1f));
        }
    }
    public class VectorHelper
    {
        public static Vector3 StringToVector3(string str)
        {
            if (str == null)
                return Vector3.zero;

            // Remove the parentheses
            if (str.StartsWith("(") && str.EndsWith(")"))
                str = str.Substring(1, str.Length - 2);

            // Split the items
            string[] sArray = str.Split(',');

            // Store as a Vector3
            return new Vector3(
                float.Parse(sArray[0]),
                float.Parse(sArray[1]),
                float.Parse(sArray[2]));
        }

        public static Quaternion StringToQuaternion(string str)
        {
            // Remove the parentheses
            if (str.StartsWith("(") && str.EndsWith(")"))
                str = str.Substring(1, str.Length - 2);

            if (str == null)
                return Quaternion.identity;

            // Split the items
            string[] sArray = str.Split(',');

            // Store as a Quaternion
            return new Quaternion(
                float.Parse(sArray[0]),
                float.Parse(sArray[1]),
                float.Parse(sArray[2]),
                float.Parse(sArray[3]));
        }

    }
}