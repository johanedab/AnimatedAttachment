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
    [KSPField(isPersistant = true, guiName = "Maximum force", guiActiveEditor = true, advancedTweakable = true)]
    [UI_FloatRange(minValue = 1f, maxValue = 10000f, stepIncrement = 1f)]
    public float maximumForce = 100f;
    [KSPField(isPersistant = true, guiName = "Damper", guiActiveEditor = true, advancedTweakable = true)]
    [UI_FloatRange(minValue = 1f, maxValue = 1000f, stepIncrement = 1f )]
    public float positionDamper = 1f;
    [UI_FloatRange(minValue = 1f, maxValue = 1000f, stepIncrement = 1f)]
    [KSPField(isPersistant = true, guiName = "Spring", guiActiveEditor = true, advancedTweakable = true)]
    public float positionSpring = 100f;

    private void Update()
    {
        // Debug.Log("Update()");
    }

    private void FixedUpdate()
    {
        UpdateAttachments(true);
    }

    private void LateUpdate()
    {
    }

    public class PosRot
    {
        public Quaternion rotation;
        public Vector3 position;
    }

    // Get a rotation from a node in a part relative to the part instead of the immediate parent
    private PosRot GetPosRot(Transform transform)
    {
        PosRot result = new PosRot();

        result.rotation = Quaternion.identity;

        do
        {
            result.position = transform.localRotation * result.position + transform.localPosition;
            result.rotation = transform.localRotation * result.rotation;
            transform = transform.parent;
        }
        while (transform != null && transform != part.transform);
        return result;
    }

    // Contains info on each attached node
    public class AttachNodeInfo
    {
        public bool valid;

        public OrientationInfo orientationAttachNode;
        public OrientationInfo orientationJoint;
        public AxisInfo axisJoint;

        public PosRot originalPosRot;
        public Vector3 originalOrientation;
        public Quaternion partRotation;
        public Vector3 partPosition;
    };

    public AxisInfo axisWorld;
    public AxisInfo axisAttachNode;

    int test;
    AttachNodeInfo[] attachNodeInfos;

    private void UpdateAttachments(bool moving)
    {
        // Save original orientation for each attachNode
        for (int i = 0; i < part.attachNodes.Count; i++)
        {
            AttachNode attachNode = part.attachNodes[i];
            AttachNodeInfo attachNodeInfo = attachNodeInfos[i];

            if (attachNode.attachedPart == part.parent)
                continue;

            if (attachNode.nodeTransform == null)
                continue;

            Part attachedPart = attachNode.attachedPart;
            if (attachedPart == null)
            {
                // Make sure to mark the attachNode as invalid if there is nothing attached to it
                attachNodeInfo.valid = false;
                continue;
            }

            Transform transform = attachNode.nodeTransform;
            PosRot posRot = GetPosRot(attachNode.nodeTransform);

            // Include the rescale factor
            posRot.position *= part.rescaleFactor;

            if (moving && attachNodeInfo.valid)
            {
                attachNode.position = posRot.position;
                attachNode.orientation = posRot.rotation * Vector3.forward;

                // Rotation and position delta from saved value
                //Quaternion rotation = Quaternion.FromToRotation(attachNodeInfo.originalOrientation, attachNode.orientation);
                Quaternion rotation = posRot.rotation * attachNodeInfo.originalPosRot.rotation.Inverse();
                Vector3 position = posRot.position - attachNodeInfo.originalPosRot.position;
                Vector3 partOffset = attachNodeInfo.partPosition - attachNodeInfo.originalPosRot.position;

                Transform parent = attachedPart.partTransform.parent;
                ConfigurableJoint joint = attachedPart.attachJoint ? attachedPart.attachJoint.Joint : null;

                if (attachedPart.transform.parent != null)
                {
                    attachedPart.transform.localRotation = rotation * attachNodeInfo.partRotation;
                    attachedPart.transform.localPosition = posRot.position - attachedPart.transform.localRotation * attachNode.FindOpposingNode().position;
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


                        JointDrive jointDrive = new JointDrive();

                        // Overrid default settings from cfg file
                        jointDrive.maximumForce = maximumForce;
                        jointDrive.positionDamper = positionDamper;
                        jointDrive.positionSpring = positionSpring;

                        joint.angularXDrive = jointDrive;
                        joint.angularYZDrive = jointDrive;
                        joint.xDrive = jointDrive;
                        joint.yDrive = jointDrive;
                        joint.zDrive = jointDrive;

                        joint.SetTargetRotationLocal(rotation * attachNodeInfo.partRotation, attachNodeInfo.partRotation);
                        joint.connectedAnchor = posRot.position;

                        // Debug info
                        if (debugVectors)
                        {
                            // Show debug vectors for the child part
                            if (attachNodeInfo.axisJoint == null)
                                attachNodeInfo.axisJoint = new AxisInfo(joint.transform);
                        }
                        else
                        {
                            if (attachNodeInfo.axisJoint != null)
                                attachNodeInfo.axisJoint = null;
                        }
                        if((test++ % 100) == 0)
                        {
                            Debug.Log(string.Format("{0} {1} {2} {3} {4} {4}",
                                attachNodeInfo.originalPosRot.position,
                                attachNodeInfo.originalPosRot.rotation.eulerAngles,
                                posRot.position,
                                posRot.rotation.eulerAngles,
                                rotation.eulerAngles,
                                joint.targetRotation.eulerAngles));
                        }
                    }
                }
            }
            else
            {
                // On the first iteration after attaching an object or loading a vessel, get
                // some reference values to use during animations
                attachNodeInfo.originalPosRot = posRot;
                Debug.Log(string.Format("onStart: {0} {1}",
                    posRot.position,
                    posRot.rotation));

                attachNodeInfo.originalOrientation = attachNode.orientation;
                attachNodeInfo.partRotation = attachedPart.transform.localRotation;
                attachNodeInfo.partPosition = attachedPart.transform.localPosition;
                attachNodeInfo.valid = true;
            }

            // Debug info
            if (debugVectors)
            {
                // Show debug vectors for the attachNodes
                if (attachNodeInfo.orientationAttachNode == null)
                    attachNodeInfo.orientationAttachNode = new OrientationInfo(part.transform, posRot.position, posRot.position + attachNodeInfo.originalOrientation);
                attachNodeInfo.orientationAttachNode.Update(posRot.position, posRot.position + attachNode.orientation);
            }
            else
            {
                if (attachNodeInfo.orientationAttachNode != null)
                    attachNodeInfo.orientationAttachNode = null;
            }
        }

        // Debug info
        if (debugVectors)
        {
            // Show debug vectors for this part itselft
            if (axisAttachNode == null)
                axisAttachNode = new AxisInfo(part.transform);
            if (axisWorld == null)
                axisWorld = new AxisInfo(null);
        }
        else
        {
            if (axisAttachNode != null)
                axisAttachNode = null;
            if (axisWorld != null)
                axisWorld = null;
        }
    }

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

        UpdateAttachments(false);
    }
}
