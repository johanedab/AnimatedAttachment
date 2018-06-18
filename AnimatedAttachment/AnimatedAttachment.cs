using System;
using System.Collections.Generic;
using System.Xml;
using UnityEngine;
using VectorHelper;

/******************************************************************************
 *
 * This KSP part module plugin allows generic animations to move attached parts.
 * 
 * The concept is built on retreiving the position and rotation of the attach
 * node transform in the model, and initially saving this value as a reference 
 * value, together with the position and rotation of the attach part. The position 
 * and rotation is then continously read, and the attach node is updated with 
 * this new information. Meanwhile, the delta between the original and current 
 * transform is calculated. This delta is then applied to the attached part. 
 * Instead
 *****************************************************************************/

public class AnimatedAttachment : PartModule
{
    [KSPField(isPersistant = false, guiName = "Animated attachments", guiActiveEditor = true, guiActive = true, advancedTweakable = true)]
    [UI_Toggle(disabledText = "Disabled", enabledText = "Enabled")]
    public bool activated = true;
    [KSPField(isPersistant = true, guiName = "Debug", guiActiveEditor = true, guiActive = true, advancedTweakable = true)]
    [UI_Toggle(disabledText = "Disabled", enabledText = "Enabled")]
    public bool debugVectors = false;
    [KSPField(isPersistant = true, guiName = "Maximum force", guiActiveEditor = true, advancedTweakable = true)]
    [UI_FloatRange(minValue = 1f, maxValue = 10000f, stepIncrement = 1f)]
    public float maximumForce = 100f;
    [KSPField(isPersistant = true, guiName = "Damper", guiActiveEditor = true, advancedTweakable = true)]
    [UI_FloatRange(minValue = 1f, maxValue = 1000f, stepIncrement = 1f )]
    public float positionDamper = 10f;
    [UI_FloatRange(minValue = 1f, maxValue = 1000f, stepIncrement = 1f)]
    [KSPField(isPersistant = true, guiName = "Spring", guiActiveEditor = true, advancedTweakable = true)]
    public float positionSpring = 100f;

    private void Update()
    {
        // Debug.Log("Update()");
    }

    private void FixedUpdate()
    {
        UpdateAttachments();
        UpdateDebugAxes();
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
        PosRot result = new PosRot
        {
            rotation = Quaternion.identity
        };

        do
        {
            // Walk up the tree to the part transform, adding up all the local positions and rotations
            // to make them relative to the part transform
            // TODO: add in scaling
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
        public bool partAttached;

        enum State
        {
            UNKNOWN,
            PART_POSITION_SAVED,
            WAITING_FOR_ANIMATIONS,
            VALID,
            PART_ATTACHED,
        };

        public OrientationInfo orientationAttachNode;
        public OrientationInfo orientationJoint;
        public AxisInfo axisJoint;

        public PosRot originalPosRot;
        public Vector3 originalOrientation;
        public Quaternion partRotation;
        public Vector3 partPosition;
    };

    // During the first pass after a scene is loaded, connected part positions will
    // have their correct local positions, but animations will not have been deployed yet
    // so the attach nodes part transforms won't match the position of the attached part.
    // On subsequent passes, the attached parts will have their local positions updated
    // to world positions, which isn't useful for calculating the position of the joint
    // anchor point. Therefor we must use the first pass part transforms, and second pass
    // attach node transforms. 
    int passCounter;

    // For debugging purposes, we want to limit the console output a bit 
    int debugCounter;

    // Opotionally show unit vectors of the axes for debugging purposes
    public AxisInfo axisWorld;
    public AxisInfo axisAttachNode;

    // Contains info for all the attach nodes of the part
    AttachNodeInfo[] attachNodeInfos;

    private void UpdateAttachments(int index)
    {
        AttachNode attachNode = part.attachNodes[index];

        // We don't want to mess with the joint attaching this part to its parent.
        // Also, take of the special case where they are both null, otherwise we
        if ((attachNode.attachedPart == part.parent) && 
            (part.parent != null))
            return;

        // If this attach node is not based on a transform from the model, then 
        // there is nothing more we can do about it.
        if (attachNode.nodeTransform == null)
            return;

        AttachNodeInfo attachNodeInfo = attachNodeInfos[index];

        // Get the position and rotation of the node transform relative to the part.
        // The nodeTransform itself will only contain its positions and rotation 
        // relative to the immediate parent in the model
        PosRot posRot = GetPosRot(attachNode.nodeTransform);

        // Include the rescale factor
        posRot.position *= part.rescaleFactor;

        // Update the attachNode
        attachNode.position = posRot.position;
        attachNode.orientation = posRot.rotation * Vector3.forward;

        // If the is no actual part attached to the attach node, then we can bail out.
        Part attachedPart = attachNode.attachedPart;

        if (attachedPart == null)
        {
            // Make sure to mark the attachNode as invalid if there is nothing attached to it
            attachNodeInfo.valid = false;
            attachNodeInfo.partAttached = false;
            return;
        }

        if (attachNodeInfo.valid && activated)
        {
            // Rotation and position delta from saved value
            //Quaternion rotation = Quaternion.FromToRotation(attachNodeInfo.originalOrientation, attachNode.orientation);
            PosRot delta = new PosRot
            {
                rotation = posRot.rotation * attachNodeInfo.originalPosRot.rotation.Inverse(),
                position = posRot.position - attachNodeInfo.originalPosRot.position
            };

            Transform parent = attachedPart.partTransform.parent;
            ConfigurableJoint joint = attachedPart.attachJoint ? attachedPart.attachJoint.Joint : null;

            /* A sub part can either be connected directly by their transform having a parent transform,
                * or be connected through a joint. In the first case, the sub part will directly move with
                * their parent as their position is in in the reference frame of the parent local space.
                * In the latter case, the sub part lacks a parent transform, and the position is in the vessel
                * space instead, and parts are held together by forces working through the joints. 
                * The first case occurs in two situations. In the VAB editor, all parts are connected by
                * parent transforms. And, during flight, a physicsless part will also be connected to the parent
                * this way - for example some science parts.
                * Joints are used for normal physics based parts during flight.
                */
            if (attachedPart.transform.parent != null)
            {
                // If a parent was found, we will just update the position of the part directly since no physics is involved
                attachedPart.transform.localRotation = delta.rotation * attachNodeInfo.partRotation;
                Vector3 partOffset = attachNodeInfo.partPosition - attachNodeInfo.originalPosRot.position;
                attachedPart.transform.localPosition = posRot.position + delta.rotation * partOffset;
            }
            else
            {
                // Things get tricker if the parts are connected by joints. We need to setup the joint
                // to apply forces to the sub part.

                joint.xMotion = ConfigurableJointMotion.Free;
                joint.yMotion = ConfigurableJointMotion.Free;
                joint.zMotion = ConfigurableJointMotion.Free;
                joint.angularXMotion = ConfigurableJointMotion.Free;
                joint.angularYMotion = ConfigurableJointMotion.Free;
                joint.angularZMotion = ConfigurableJointMotion.Free;

                // It is not possible to change values of a JointDrive after creation, so we must create a 
                // new one and apply it to the joint. 
                // TODO: This should be cached, and only recreated when the settings have changed
                JointDrive jointDrive = new JointDrive();

                // Create a new joint with settings from the cfg file or user selection
                jointDrive.maximumForce = maximumForce;
                jointDrive.positionDamper = positionDamper;
                jointDrive.positionSpring = positionSpring;

                // Same drive in all directions.. is there benefits of separating them?
                joint.angularXDrive = jointDrive;
                joint.angularYZDrive = jointDrive;
                joint.xDrive = jointDrive;
                joint.yDrive = jointDrive;
                joint.zDrive = jointDrive;

                // Update the joint.targetRotation using this convenience function, since the joint
                // reference frame has weird axes. Arguments are current and original rotation.
                joint.SetTargetRotationLocal(delta.rotation * attachNodeInfo.partRotation, attachNodeInfo.partRotation);

                // Move the attached part by updating the connectedAnchor instead of the joint.targetPosition.
                // This is easier since the anchor is in the reference frame of this part, and we already have the
                // position in the reference frame. It also makes sense from the view that since it really is the 
                // attachment point of the attached part that is moving. There might be benefits of using the targetPosition
                // though, and should be possible to calculate it fairly easily if needed.
                joint.connectedAnchor = posRot.position;

                // Debug info
                if (debugVectors)
                {
                    // Show debug vectors for the child part
                    /*
                    if (attachNodeInfo.axisJoint == null)
                        attachNodeInfo.axisJoint = new AxisInfo(joint.transform);
                    */
                }
                else
                {
                    if (attachNodeInfo.axisJoint != null)
                        attachNodeInfo.axisJoint = null;
                }
            }
            if (debugVectors && ((debugCounter % 100) == 0))
            {
                Debug.Log(string.Format("{0} {1} {2} {3} {4} {5} {6}",
                    attachNodeInfo.originalPosRot.position,
                    attachNodeInfo.originalPosRot.rotation.eulerAngles,
                    attachNodeInfo.originalOrientation,
                    attachNodeInfo.partRotation.eulerAngles,
                    posRot.position,
                    posRot.rotation.eulerAngles,
                    delta.rotation.eulerAngles));
            }

        }
        else
        {
            // On the first iteration after attaching an object or loading a vessel, get
            // some reference values to use during animations
            Debug.Log(string.Format("original ({0}: {1} {2} {3} {4} {5}",
                index,
                passCounter,
                posRot.position,
                posRot.rotation,
                attachedPart.transform.localPosition,
                attachedPart.transform.localRotation));

            if (!activated)
                passCounter = 0;

            // In the first pass, only get the position and rotation of the attached part
            if (passCounter == 0)
            {
                attachNodeInfo.partRotation = attachNode.attachedPart.transform.localRotation;
                attachNodeInfo.partPosition = attachNode.attachedPart.transform.localPosition;
                attachNodeInfo.partAttached = true;
                attachNodeInfo.valid = false;
            }
            else if (passCounter >= 2)
            {
                // On the third pass, get values of the attach node transform
                attachNodeInfo.originalOrientation = attachNode.orientation;
                attachNodeInfo.originalPosRot = posRot;

                // Newly attached parts after intial creating need to be handled here
                if (!attachNodeInfo.partAttached)
                {
                    attachNodeInfo.partRotation = attachNode.attachedPart.transform.localRotation;
                    attachNodeInfo.partPosition = attachNode.attachedPart.transform.localPosition;
                    attachNodeInfo.partAttached = true;
                }

                // Now we are okey to start animating the attach node and any attached part
                attachNodeInfo.valid = true;
            }
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

    private void UpdateAttachments()
    {
        // Save original orientation for each attachNode
        for (int i = 0; i < part.attachNodes.Count; i++)
            UpdateAttachments(i);

        if (passCounter < 1000)
            passCounter++;
    }

    private void UpdateDebugAxes()
    {
        // Debug info
        if (debugVectors)
        {
            // Show debug vectors for this part itselft
            /*
            if (axisAttachNode == null)
                axisAttachNode = new AxisInfo(part.transform);
            if (axisWorld == null)
                axisWorld = new AxisInfo(null);
            */
        }
        else
        {
            if (axisAttachNode != null)
                axisAttachNode = null;
            if (axisWorld != null)
                axisWorld = null;
        }
        debugCounter++;
    }

    // Save the part rotations early, before they have been recalculated into world positions
    public void SavePartRotation()
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

            if (attachNode.attachedPart == null)
                continue;

            //
        }
    }

    public override void OnStart(StartState state)
    {
        base.OnStart(state);

        // Set up our array containing info about each attach node and their connected parts
        if (attachNodeInfos == null)
        {
            attachNodeInfos = new AttachNodeInfo[part.attachNodes.Count];

            for (int i = 0; i < part.attachNodes.Count; i++)
            {
                AttachNode attachNode = part.attachNodes[i];
                attachNodeInfos[i] = new AttachNodeInfo();
            }
        }

        // Save original attached part position and rotation early
        SavePartRotation();
        passCounter = 0;
    }
}
