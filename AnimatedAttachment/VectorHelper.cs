using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace VectorHelper
{
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
}
