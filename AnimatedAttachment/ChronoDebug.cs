namespace Chrononaut
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using UnityEngine;
    using UnityEngine.UI;

    public class ChronoDebug
    {
        static public string DumpPartHierarchy(GameObject obj)
        {
            return DumpGameObjectChilds(obj, "");
        }

        // A bit messy. The code could be simplified by beeing smarter with when I add
        // characters to pre but it works like that and it does not need to be efficient
        static public string DumpGameObjectChilds(GameObject go, string pre)
        {
            StringBuilder sb = new StringBuilder();
            bool first = pre == "";
            List<GameObject> neededChilds = new List<GameObject>();
            int count = go.transform.childCount;
            for (int i = 0; i < count; i++)
            {
                GameObject child = go.transform.GetChild(i).gameObject;
                if (!child.GetComponent<Part>() && child.name != "main camera pivot")
                    neededChilds.Add(child);
            }

            count = neededChilds.Count;

            sb.Append(pre);
            if (!first)
            {
                sb.Append(count > 0 ? "--+" : "---");
            }
            else
            {
                sb.Append("+");
            }
            sb.AppendFormat("{0} T:{1} L:{2} ({3})\n", go.name, go.tag, go.layer, LayerMask.LayerToName(go.layer));

            string front = first ? "" : "  ";
            string preComp = pre + front + (count > 0 ? "| " : "  ");

            Component[] comp = go.GetComponents<Component>();

            for (int i = 0; i < comp.Length; i++)
            {
                if (comp[i] is Transform)
                {
                    Transform transform = (Transform)comp[i];
                    sb.AppendFormat("{0}  [{1}] - {2} -> {3}, {4} -> {5}, {6}\n", preComp, comp[i].GetType().Name, transform.localPosition, transform.position, transform.localRotation, transform.rotation, transform.localScale);
                }
                else if (comp[i] is Text)
                {
                    Text t = (Text)comp[i];
                    sb.AppendFormat("{0}  [{1}] - {2} - {3} - {4} - {5} - {6}\n", preComp, comp[i].GetType().Name, t.text, t.alignByGeometry, t.pixelsPerUnit, t.font.dynamic, t.fontSize);
                }
                else
                {
                    sb.AppendFormat("{0}  [{1}] - {2}, {3}\n", preComp, comp[i].GetType().Name, comp[i].name, comp[i].ToString());
                }
            }

            sb.AppendLine(preComp);

            for (int i = 0; i < count; i++)
            {
                sb.Append(DumpGameObjectChilds(neededChilds[i], i == count - 1 ? pre + front + " " : pre + front + "|"));
            }
            return sb.ToString();
        }
    }
}