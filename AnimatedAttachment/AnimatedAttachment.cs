using System;
using System.Collections.Generic;
using System.Xml;
using UnityEngine;
using VectorHelper;

public class AnimatedAttachment : PartModule
{
    [KSPField(isPersistant = true, guiName = "Debug", guiActiveEditor = true, guiActive = true, advancedTweakable = true)]
    [UI_Toggle(disabledText = "Disabled", enabledText = "Enabled")]
    public bool debugVectors = false;
    [KSPField(isPersistant = true, guiName = "Maximum force", guiActiveEditor = true)]
    [UI_FloatRange(minValue = 1f, maxValue = 1000f, stepIncrement = 1f)]
    public float maximumForce = 1f;
    [KSPField(isPersistant = true, guiName = "Damper", guiActiveEditor = true)]
    [UI_FloatRange(minValue = 1f, maxValue = 1000f, stepIncrement = 1f )]
    public float positionDamper = 1f;
    [UI_FloatRange(minValue = 1f, maxValue = 1000f, stepIncrement = 1f)]
    [KSPField(isPersistant = true, guiName = "Spring", guiActiveEditor = true)]
    public float positionSpring = 100f;

    /*
        internal void OnLoad(ConfigNode node)
        {
        Debug.Log("AnimatedAttachment.OnLoad");
        Debug.Log(node);

        if (node.HasValue("debugVectors"))
            debugVectors = bool.Parse(node.GetValue("debugVectors"));

        if (node.HasValue("maximumForce"))
            maximumForce = float.Parse(node.GetValue("maximumForce"));

        if (node.HasValue("positionDamper"))
            positionDamper = float.Parse(node.GetValue("positionDamper"));

        if (node.HasValue("positionSpring"))
            positionSpring = float.Parse(node.GetValue("positionSpring"));
    }

    internal void OnSave(ConfigNode node)
        {
            Debug.Log("AnimatedAttachment.OnSave");

            node.SetValue("debugVectors", debugVectors);

            if (!float.IsNaN(maximumForce))
                node.SetValue("maximumForce", maximumForce);

            if (!float.IsNaN(positionDamper))
                node.SetValue("positionDamper", positionDamper);

            if (!float.IsNaN(positionSpring))
                node.SetValue("positionSpring", positionSpring);

            Debug.Log(node);
        }
    }
    */

    //Settings settings = new Settings();

    private void Update()
    {
        UpdateAttachments(true);
    }

    private void FixedUpdate()
    {
        UpdateAttachments(true);
    }

    private void LateUpdate()
    {
        UpdateAttachments(true);
    }

    // Contains info on each attached node
    public class AttachNodeInfo
    {
        public bool valid;

        public OrientationInfo orientationAttachNode;
        public OrientationInfo orientationJoint;
        public AxisInfo axisAttachNode;
        public AxisInfo axisJoint;

        public Vector3 originalOrientation;
        public Vector3 originalPosition;
        public Quaternion partRotation;
        public Vector3 partPosition;
    };

    //JointDrive[] jointDriveSettings;
    AttachNodeInfo[] attachNodeInfos;

    int test;
    private void UpdateAttachments(bool moving)
    {
        // Save original orientation for each attachNode
        for (int i = 0; i < part.attachNodes.Count; i++)
        {
            AttachNode attachNode = part.attachNodes[i];
            AttachNodeInfo attachNodeInfo = attachNodeInfos[i];

            if (attachNode.nodeTransform != null)
            {
                bool debug = false;
                if ((test++ % 100) == 0)
                    debug = true;

                Vector3 localPosition = Vector3.zero;
                Transform transform = attachNode.nodeTransform;
                Quaternion localRotation = Quaternion.identity;
                do
                {
                    localPosition = transform.localRotation * localPosition + transform.localPosition;
                    localRotation = transform.localRotation * localRotation;

                    transform = transform.parent;
                }
                while (transform != null && transform != part.transform);

                /*
                localPosition = part.partTransform.rotation.Inverse() * 
                    (attachNode.nodeTransform.position - part.partTransform.position);
                */

                // In flight mode, transforms don't have parent, apperently...
                // Recalculate local positions explicitly based on the parts transforms
                if (part.transform.parent == null)
                {
                    // localPosition = part.transform.TransformPoint(localPosition);
                    //childRotation = Quaternion.LookRotation(localRotation * Vector3.back, Vector3.up);
                }

                // Include the rescale factor
                localPosition *= part.rescaleFactor;

                attachNode.position = localPosition;
                attachNode.orientation = localRotation * Vector3.forward;

                // Rotation and position delta from saved value
                Quaternion rotation = Quaternion.FromToRotation(attachNodeInfo.originalOrientation, attachNode.orientation);
                Vector3 position = localPosition - attachNodeInfo.originalPosition;
                Vector3 partOffset = attachNodeInfo.partPosition - attachNodeInfo.originalPosition;

                Part attachedPart = attachNode.attachedPart;
                if (attachedPart != null)
                {
                    Transform parent = attachedPart.partTransform.parent;
                    ConfigurableJoint joint = attachedPart.attachJoint ? attachedPart.attachJoint.Joint : null;

                    if (moving && attachNodeInfo.valid)
                    {
                        if (attachedPart.transform.parent != null)
                        {
                            attachedPart.transform.localRotation = rotation * attachNodeInfo.partRotation;
                            attachedPart.transform.localPosition = localPosition - attachedPart.transform.localRotation * attachNode.FindOpposingNode().position;
                        }
                        else
                        { 
                            // attachedPart.transform.localRotation = (part.transform.rotation * rotation) * attachNodeInfo.partRotation;

                            if (joint != null)
                            {
                                joint.xMotion = ConfigurableJointMotion.Free;
                                joint.yMotion = ConfigurableJointMotion.Free;
                                joint.zMotion = ConfigurableJointMotion.Free;
                                joint.angularXMotion = ConfigurableJointMotion.Free;
                                joint.angularYMotion = ConfigurableJointMotion.Free;
                                joint.angularZMotion = ConfigurableJointMotion.Free;


                                /*int size = 
                                    attachNode.size < jointDriveSettings.Length ?
                                    attachNode.size : jointDriveSettings.Length - 1;
                                JointDrive jointDrive = jointDriveSettings[size];
                                */
                                JointDrive jointDrive = new JointDrive();

                                // Overrid default settings from cfg file
                                if (!float.IsNaN(maximumForce))
                                    jointDrive.maximumForce = maximumForce;
                                if (!float.IsNaN(positionDamper))
                                    jointDrive.positionDamper = positionDamper;
                                if (!float.IsNaN(positionSpring))
                                    jointDrive.positionSpring = positionSpring;

                                joint.angularXDrive = jointDrive;
                                joint.angularYZDrive = jointDrive;
                                joint.xDrive = jointDrive;
                                joint.yDrive = jointDrive;
                                joint.zDrive = jointDrive;

                                joint.SetTargetRotationLocal(rotation * attachNodeInfo.partRotation, attachNodeInfo.partRotation);
                                joint.connectedAnchor = localPosition;

                                // Debug info
                                if (debugVectors)
                                {
                                    // Show debug vectors for the attachNodes
                                    if (attachNodeInfo.orientationAttachNode == null)
                                        attachNodeInfo.orientationAttachNode = new OrientationInfo(part.transform, localPosition, localPosition + attachNodeInfo.originalOrientation);
                                    attachNodeInfo.orientationAttachNode.Update(localPosition, localPosition + attachNode.orientation);

                                    // Show debug vectors for this part itselft
                                    if (attachNodeInfo.axisAttachNode == null)
                                        attachNodeInfo.axisAttachNode = new AxisInfo(part.transform);

                                    // Show debug vectors for the child part
                                    if (attachNodeInfo.axisJoint == null)
                                        attachNodeInfo.axisJoint = new AxisInfo(joint.transform);
                                }
                                else
                                {
                                    if (attachNodeInfo.orientationAttachNode != null)
                                        attachNodeInfo.orientationAttachNode = null;
                                    if (attachNodeInfo.axisAttachNode != null)
                                        attachNodeInfo.axisAttachNode = null;
                                    if (attachNodeInfo.axisJoint != null)
                                        attachNodeInfo.axisJoint = null;
                                }
                            }

                            if (debug)
                            {
                                Debug.Log(string.Format("{0} {1} {2} {3} {4} {5} {6}",
                                    position,
                                    rotation,
                                    attachNodeInfo.partRotation,
                                    joint.targetRotation,
                                    attachedPart.transform.rotation,
                                    joint.angularXDrive.maximumForce,
                                    joint.currentTorque
                                    ));
                            }
                        }
                    }
                    else
                    {
                          // On the first iteration after attaching an object or loading a vessel, get
                        // some reference values to use during animations
                        attachNodeInfo.originalOrientation = attachNode.orientation;
                        attachNodeInfo.originalPosition = attachNode.position;
                        attachNodeInfo.partRotation = attachedPart.transform.localRotation;
                        attachNodeInfo.partPosition = attachedPart.transform.localPosition;
                        attachNodeInfo.valid = true;
                    }
                }
                else
                {
                    // Make sure to mark the attachNode as invalid if there is nothing attached to it
                    attachNodeInfo.valid = false;
                }
            }
        }
    }

    /*
    public override void OnSave(ConfigNode node)
    {
        base.OnSave(node);
        foreach(ConstrainedObjectEx objectEx in ObjectsList)
            objectEx.Save(node.)
    }
    */

    private void OnEditorAttach()
    {
        AttachNode attachNode = part.FindAttachNodeByPart(part.parent);
        AttachNode parentAttachNode = part.parent.FindAttachNodeByPart(part);
        Debug.Log(string.Format("AnimatedAttachment.OnEditorAttach:"));
        Debug.Log(string.Format("part: {0}", part.name));
        Debug.Log(string.Format("parent: {0}", part.parent.name));
        Debug.Log(string.Format("part.attachNode: {0}", attachNode.id));
        Debug.Log(string.Format("parent.attachNode: {0}", parentAttachNode != null ? parentAttachNode.id : "null"));

        Debug.Log(string.Format("attachNode.position: {0}", attachNode.position));
        Debug.Log(string.Format("attachNode.originalPosition: {0}", attachNode.originalPosition));
        if (attachNode.nodeTransform == null)
            Debug.Log(string.Format("attachNode.nodeTransform: null"));
        else
        {
            Debug.Log(string.Format("attachNode.nodeTransform.name: {0}", attachNode.nodeTransform.name));
            Debug.Log(string.Format("attachNode.nodeTransform.position: {0}", attachNode.nodeTransform.position));
            Debug.Log(string.Format("attachNode.nodeTransform.localPosition: {0}", attachNode.nodeTransform.localPosition));
        }
    }

    public override void OnStart(StartState state)
    {
        base.OnStart(state);
        part.OnEditorAttach += OnEditorAttach;

        Debug.Log("AnimatedAttachment.OnStart");

        // Set up our array containing info about each attach node and their connected parts
        if (attachNodeInfos == null)
            attachNodeInfos = new AttachNodeInfo[part.attachNodes.Count];

        for (int i = 0; i < part.attachNodes.Count; i++)
        {
            AttachNode attachNode = part.attachNodes[i];

            if (attachNodeInfos[i] == null)
                attachNodeInfos[i] = new AttachNodeInfo();
        }

        // Default settings per size
        /*
        jointDriveSettings = new JointDrive[5]
        {
            new JointDrive(),
            new JointDrive(),
            new JointDrive(),
            new JointDrive(),
            new JointDrive()
        };
        jointDriveSettings[0].maximumForce = 1f;
        jointDriveSettings[0].positionDamper = 1f;
        jointDriveSettings[0].positionSpring = 10f;
        jointDriveSettings[1].maximumForce = 5f;
        jointDriveSettings[1].positionDamper = 5f;
        jointDriveSettings[1].positionSpring = 50f;
        jointDriveSettings[2].maximumForce = 10f;
        jointDriveSettings[2].positionDamper = 10f;
        jointDriveSettings[2].positionSpring = 100f;
        jointDriveSettings[3].maximumForce = 50f;
        jointDriveSettings[3].positionDamper = 50f;
        jointDriveSettings[3].positionSpring = 500f;
        jointDriveSettings[4].maximumForce = 100f;
        jointDriveSettings[4].positionDamper = 100f;
        jointDriveSettings[4].positionSpring = 1000f;
        */
    }

    public override void OnLoad(ConfigNode node)
    {
        //OnLoad(node);
    }

    public override void OnSave(ConfigNode node)
    {
        //OnSave(node);
    }

    // The fields of this class will persist from the cfg database to the instanced part.
    // However, it will instanced by reference - do not save any part-specific info in this
    // object!
    /*
    public class ConstrainedObjectEx : ScriptableObject
    {
        // A small class that keeps track of a transform and the weight assigned to that transform
        public class ConstrainedObjectMover
        {
            public double weight;
            public string transformName;
        }

        public ConstrainedObjectEx()
        {
            movers = new List<ConstrainedObjectMover>();
        }

        public void Load(ConfigNode node)
        {
            targetName = node.GetValue("targetName");
            moversName = node.GetValue("moversName");
        }

        public void Save(ConfigNode node)
        {
            node.AddValue("targetName", this.targetName);
            node.AddValue("moversName", this.moversName);
        }

        public string targetName;
        public string moversName;

        public List<ConstrainedObjectMover> movers;
    }

    public new List<ConstrainedObjectEx> ObjectsList;
    */
}
