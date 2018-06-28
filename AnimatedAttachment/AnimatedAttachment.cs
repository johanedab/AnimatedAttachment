using ModuleWheels;
using System;
using System.Collections.Generic;
using System.Xml;
using UnityEngine;
using VectorHelpers;

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
    [KSPField(isPersistant = false, guiName = "Animated attachments", guiActiveEditor = true, advancedTweakable = true)]
    [UI_Toggle(disabledText = "Disabled", enabledText = "Enabled")]
    public bool activated = true;
    [KSPField(isPersistant = false, guiName = "Debug", guiActiveEditor = true, guiActive = true, advancedTweakable = true)]
    [UI_Toggle(disabledText = "Disabled", enabledText = "Enabled")]
    public bool debugVectors = false;
    [KSPField(isPersistant = true, guiName = "Maximum force", guiActiveEditor = true, advancedTweakable = true)]
    [UI_FloatRange(minValue = 1f, maxValue = 100000f, stepIncrement = 1f)]
    public float maximumForce = 10000f;
    [KSPField(isPersistant = true, guiName = "Damper", guiActiveEditor = true, advancedTweakable = true)]
    [UI_FloatRange(minValue = 1f, maxValue = 10000f, stepIncrement = 1f )]
    public float positionDamper = 1000f;
    [UI_FloatRange(minValue = 1f, maxValue = 100000f, stepIncrement = 1f)]
    [KSPField(isPersistant = true, guiName = "Spring", guiActiveEditor = true, advancedTweakable = true)]
    public float positionSpring = 10000f;

    private void Update()
    {
        // Debug.Log("Update()");
    }

    private void FixedUpdate()
    {
        UpdateAttachments();
        UpdateState();
        UpdateDebugAxes();
    }

    private void UpdateState()
    {
        if (!activated)
        {
            /*
            foreach (AttachNodeInfo attachNodeInfo in attachNodeInfos)
            {
                attachNodeInfo.attachedPartOffset = null;
            }
            */
        }
        else
        {
            if (flightState == State.INIT)
                flightState = State.STARTING;
        }
    }

    private void LateUpdate()
    {
    }

    public class PosRot
    {
        public Quaternion rotation;
        public Vector3 position;
        public Vector3 orientation;

        public override string ToString()
        {
            if(this == null)
                return "null";

            if (orientation != Vector3.zero)
                return string.Format("{0}, {1}, {2}",
                    position,
                    rotation.eulerAngles,
                    orientation);
            return string.Format("{0}, {1}",
                position,
                rotation.eulerAngles);
        }

        public void Save(string name, ConfigNode node)
        {
            if (name == null)
                return;

            node.AddValue("name", name);
            node.AddValue("position", position);
            node.AddValue("rotation", rotation);
            node.AddValue("orientation", orientation);
        }

        public void Load(ConfigNode node)
        {
            if (node == null)
                return;

            position = VectorHelper.StringToVector3(node.GetValue("position"));
            rotation = VectorHelper.StringToQuaternion(node.GetValue("rotation"));
            orientation = VectorHelper.StringToVector3(node.GetValue("orientation"));
        }
    }

    // During the first two passes after a scene is loaded, connected part positions will
    // have their correct local positions, but animations will not have been deployed yet
    // so the attach nodes part transforms won't match the position of the attached part.
    // On subsequent passes, the attached parts will have their local positions updated
    // to world positions, which isn't useful for calculating the position of the joint
    // anchor point. Therefor we must use the first pass part transforms, and third pass
    // attach node transforms. 
    // In flight mode, the first state is happening after OnStart but before OnStartFininshed.
    // In the editor, OnStartFinished is called directly after OnStart without FixedUpdate
    // being called at all.
    public enum State
    {
        INIT,
        STARTING,
        STARTED,
    };
    public State flightState = State.INIT;

    // Contains info on each attached node
    public class AttachNodeInfo
    {
        public AttachNode attachNode;
        // Designed position of the attached part from the editor, expressed
        // as an offset from the attach node
        public PosRot attachedPartOffset;
        // Original rotation of an attached part at the start of the scene
        public PosRot attachedPartOriginal;

        public LineInfo lineAnchor;
        public LineInfo lineNodeToPart;
        public OrientationInfo orientationAttachNode;
        public OrientationInfo orientationJoint;
        public AxisInfo axisJoint;
        public int counter;

        private AnimatedAttachment animatedAttachment;
        private JointDrive jointDrive;

        public AttachNodeInfo(AnimatedAttachment animatedAttachment, AttachNode attachNode)
        {
            this.animatedAttachment = animatedAttachment;
            this.attachNode = attachNode;
        }

        internal void Save(ConfigNode root)
        {
            ConfigNode attachNodeInfo = root.AddNode("ATTACH_NODE");
            if (attachedPartOffset != null)
                attachedPartOffset.Save(
                    attachNode.nodeTransform.name, 
                    attachNodeInfo.AddNode("OFFSET"));
        }

        internal void Load(int index, ConfigNode root)
        {
            ConfigNode attachNodeInfo = root.GetNode("ATTACH_NODE", index);
            if (attachNodeInfo == null)
                return;

            if (!attachNodeInfo.HasNode("OFFSET"))
                return;

            if (attachedPartOffset == null)
                attachedPartOffset = new PosRot();

            attachedPartOffset.Load(attachNodeInfo.GetNode("OFFSET"));
        }

        // Get a rotation from a node in a part relative to the part instead of the immediate parent
        public PosRot GetPosRot(Transform transform, Part part)
        {
            PosRot result = new PosRot
            {
                rotation = Quaternion.identity
            };

            do
            {
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

        public void UpdateAttachments(State flightState, bool debugVectors)
        {
            // We don't want to mess with the joint attaching this part to its parent.
            // Also, take of the special case where they are both null, otherwise we
            if ((attachNode.attachedPart == animatedAttachment.part.parent) &&
                (animatedAttachment.part.parent != null))
                return;

            // If this attach node is not based on a transform from the model, then 
            // there is nothing more we can do about it.
            if (attachNode.nodeTransform == null)
                    return;

            // For debugging purposes
            counter++;

            // Get the position and rotation of the node transform relative to the part.
            // The nodeTransform itself will only contain its positions and rotation 
            // relative to the immediate parent in the model
            PosRot attachNodePosRot = GetPosRot(attachNode.nodeTransform, animatedAttachment.part);

            // Update the attachNode
            attachNode.position = attachNodePosRot.position;
            attachNode.orientation = attachNodePosRot.orientation;

            // If the is no actual part attached to the attach node, then we can bail out.
            Part attachedPart = attachNode.attachedPart;

            // Take note of newly attached parts, including at initial ship load
            if (attachedPart == null || !animatedAttachment.activated)
            {
                attachedPartOffset = null;
                return;
            }
            else
            {
                if (attachedPartOffset == null)
                {
                    attachedPartOffset = new PosRot();

                    Debug.Log("Recording attachedPartOffset");

                    // Get attached parts
                    attachedPartOffset.rotation =
                        attachNodePosRot.rotation.Inverse() *
                        attachNode.attachedPart.transform.localRotation;

                    attachedPartOffset.position =
                        attachNodePosRot.rotation.Inverse() *
                        (attachNode.attachedPart.transform.localPosition -
                        attachNodePosRot.position);
                }
            }

            switch (flightState)
            {
                // In the first pass, set the local position of the part
                case State.INIT:
                    {
                        if (attachedPart == null)
                            break;

                        if (attachedPartOriginal == null)
                        {
                            attachedPartOriginal = new PosRot();
                            attachedPartOriginal.rotation = attachNode.attachedPart.transform.localRotation;
                        }
                    }
                    break;

                case State.STARTING:
                    break;

                // On the third pass, get values of the attach node transform
                case State.STARTED:
                    {
                        // Calculate the attached parts position in the frame of reference of this part
                        PosRot attachedPartPosRot = new PosRot
                        {
                            rotation = attachNodePosRot.rotation * attachedPartOffset.rotation,
                            position = attachNodePosRot.position + attachNodePosRot.rotation * attachedPartOffset.position
                        };

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
                            attachedPart.transform.localRotation = attachedPartPosRot.rotation;
                            attachedPart.transform.localPosition = attachedPartPosRot.position;

                            // There is nothing more to do, so bail out
                            break;
                        }

                        // In the editor, while changing action groups, the parent will be null for some reason.
                        // We can catch that here by making sure there is axists a joint 
                        if (attachedPart.attachJoint == null)
                            break;

                        // Things get tricker if the parts are connected by joints. We need to setup the joint
                        // to apply forces to the sub part.
                        ConfigurableJoint joint = attachedPart.attachJoint.Joint;

                        // It is not possible to change values of a JointDrive after creation, so we must create a 
                        // new one and apply it to the joint. Seems we can't only create it at startup either. 
                        /*
                        if (!jointDriveInitialized)
                        {
                            jointDriveInitialized = true;
                            Debug.Log("Creating a new drive mode");
                            Debug.Log(string.Format("maximumForce: {0}", animatedAttachment.maximumForce));
                            Debug.Log(string.Format("positionDamper: {0}", animatedAttachment.positionDamper));
                            Debug.Log(string.Format("positionSpring: {0}", animatedAttachment.positionSpring));
                            */
                            // The joint will not respond to changes to targetRotation/Position in locked mode,
                            // so change it to free in all directions
                            joint.xMotion = ConfigurableJointMotion.Free;
                            joint.yMotion = ConfigurableJointMotion.Free;
                            joint.zMotion = ConfigurableJointMotion.Free;
                            joint.angularXMotion = ConfigurableJointMotion.Free;
                            joint.angularYMotion = ConfigurableJointMotion.Free;
                            joint.angularZMotion = ConfigurableJointMotion.Free;

                            // Create a new joint with settings from the cfg file or user selection
                            jointDrive.maximumForce = animatedAttachment.maximumForce;
                            jointDrive.positionDamper = animatedAttachment.positionDamper;
                            jointDrive.positionSpring = animatedAttachment.positionSpring;

                            // Same drive in all directions.. is there benefits of separating them?
                            joint.angularXDrive = jointDrive;
                            joint.angularYZDrive = jointDrive;
                            joint.xDrive = jointDrive;
                            joint.yDrive = jointDrive;
                            joint.zDrive = jointDrive;
                        //}

                        // Update the joint.targetRotation using this convenience function, since the joint
                        // reference frame has weird axes. Arguments are current and original rotation.
                        joint.SetTargetRotationLocal(
                            attachedPartPosRot.rotation,
                            attachedPartOriginal.rotation);

                        /* Move the attached part by updating the connectedAnchor instead of the joint.targetPosition.
                         * This is easier since the anchor is in the reference frame of this part, and we already have the
                         * position in that reference frame. It also makes sense from the view that since it really is the 
                         * attachment point of the attached part that is moving. There might be benefits of using the targetPosition
                         * though, and should be possible to calculate it fairly easily if needed.
                         */
                        joint.connectedAnchor = attachNodePosRot.position;

                        // Make sure the target position is zero
                        joint.targetPosition = Vector3.zero;

                        // This scaling and rotation is to convert to joint space... maybe? 
                        // Determined by random tinkering and magical as far as I am concerned
                        joint.anchor = attachedPartOffset.rotation.Inverse() * 
                            Vector3.Scale(
                                new Vector3(-1, -1, -1),
                                attachedPartOffset.position);

                        // Debug info
                        if (debugVectors)
                        {
                            if ((counter % 100) == 0)
                                Debug.Log(string.Format("{0}; {1}; {2} -> {3}; {4} -> {5}; {6}",
                                    attachNodePosRot,
                                    attachedPartPosRot,
                                    attachedPartOffset,
                                    attachedPartOriginal.rotation.eulerAngles,
                                    joint.targetRotation.eulerAngles,
                                    joint.anchor,
                                    joint.connectedAnchor
                                    ));

                            // Show debug vectors for the child part
                            if (axisJoint == null)
                                axisJoint = new AxisInfo(joint.transform);

                            if (lineAnchor == null)
                                lineAnchor = new LineInfo(animatedAttachment.part.transform, Color.cyan);
                            lineAnchor.Update(Vector3.zero, joint.connectedAnchor);

                            if (lineNodeToPart == null)
                                lineNodeToPart = new LineInfo(animatedAttachment.part.transform, Color.magenta);
                            lineNodeToPart.Update(
                                attachNodePosRot.position,
                                attachedPartPosRot.position);                                                    
                        }
                        else
                        {
                            if (axisJoint != null)
                                axisJoint = null;
                        }
                    }
                    break;
            }

            // Debug info
            if (debugVectors)
            {
                // Show debug vectors for the attachNodes
                if (orientationAttachNode == null)
                    orientationAttachNode = new OrientationInfo(animatedAttachment.part.transform, attachNodePosRot.position, attachNodePosRot.position + attachedPartOffset.orientation);
                orientationAttachNode.Update(attachNodePosRot.position, attachNodePosRot.position + attachNode.orientation);
            }
            else
            {
                if (orientationAttachNode != null)
                    orientationAttachNode = null;
            }
        }
    };

    // For debugging purposes, we want to limit the console output a bit 
    int debugCounter;
    
    // Opotionally show unit vectors of the axes for debugging purposes
    public AxisInfo axisWorld;
    public AxisInfo axisAttachNode;

    // Contains info for all the attach nodes of the part
    AttachNodeInfo[] attachNodeInfos;

    private void UpdateAttachments()
    {
        // Bail out if init failed
        if (attachNodeInfos == null)
            return;

        for (int i = 0; i < part.attachNodes.Count; i++)
            attachNodeInfos[i].UpdateAttachments(flightState, debugVectors);
    }

    private void UpdateDebugAxes()
    {
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
        debugCounter++;
    }

    public void InitAttachNodeList()
    {
        // Set up our array containing info about each attach node and their connected parts
        if (attachNodeInfos == null)
        {
            attachNodeInfos = new AttachNodeInfo[part.attachNodes.Count];

            for (int i = 0; i < part.attachNodes.Count; i++)
            {
                AttachNode attachNode = part.attachNodes[i];
                attachNodeInfos[i] = new AttachNodeInfo(this, attachNode);
            }
        }
    }

    public override void OnStart(StartState state)
    {
        base.OnStart(state);

        InitAttachNodeList();

        flightState = State.INIT;
        UpdateAttachments();
        flightState = State.STARTING;
    }

    public override void OnStartFinished(StartState state)
    {
        base.OnStartFinished(state);

        flightState = State.STARTED;
    }

    public override void OnSave(ConfigNode node)
    {
        base.OnSave(node);

        if (attachNodeInfos != null)
            foreach (AttachNodeInfo attachNodInfo in attachNodeInfos)
                attachNodInfo.Save(node);

        if (debugVectors)
        {
            Debug.Log("AnimatedAttachment: OnSave");
            Debug.Log(node);
        }

        // Save original positions when saving the ship.
        // Don't do it at the save occuring at initial scene start.
        if (flightState == State.STARTED)
        {
            //SetOriginalPositions();
            AnimatedAttachmentUpdater.UpdateOriginalPositions();
        }
    }

    public override void OnLoad(ConfigNode node)
    {
        base.OnLoad(node);

        if (debugVectors)
        {
            Debug.Log("AnimatedAttachment: OnLoad");
            Debug.Log(node);
        }

        InitAttachNodeList();

        foreach (AttachNodeInfo attachNodInfo in attachNodeInfos)
            attachNodInfo.Load(
                attachNodeInfos.IndexOf(attachNodInfo), 
                node);
    }
}

/* 
 * We need to save original positions when going to TimeWarp and leaving the flight scene.
 * This is easier handled by a mono behaviour instead of letting part modules react to the
 * same event event multiple times.
 */
[KSPAddon(KSPAddon.Startup.FlightAndEditor, false)]
public class AnimatedAttachmentUpdater : MonoBehaviour
{
    static private AnimatedAttachmentUpdater _this;
    float timeWarpCurrent;
    private bool wasMoving;

    // Collect info about all the parts in the vessel and their earlier auto strut mode
    class PartInfo
    {
        public Part part;
        public Part.AutoStrutMode autoStrutMode;
    }

    PartInfo[] partInfos;

    static public AnimatedAttachmentUpdater GetSingleton()
    {
        return _this;
    }

    // Retrieve the active vessel and its parts
    public static List<Part> GetParts()
    {
        List<Part> parts = null;

        if (FlightGlobals.ActiveVessel)
            parts = FlightGlobals.ActiveVessel.parts;
        else
            parts = EditorLogic.fetch.ship.parts;

        return parts;
    }

    // Make sure to update original positions when starting warping time,
    // since KSP stock will reset the positions to the original positions
    // at this time.
    void FixedUpdate()
    {
        if (timeWarpCurrent != TimeWarp.CurrentRate)
        {
            if (TimeWarp.CurrentRate != 1 && timeWarpCurrent == 1)
            {
                Debug.Log("AnimatedAttachment: TimeWarp started");
                UpdateOriginalPositions();
            }
            timeWarpCurrent = TimeWarp.CurrentRate;
        }

        UpdateStruts();
        // UpdateOriginalPositions();
    }

    private void UpdateStruts()
    {
        bool isMoving = AnyAnimationMoving();

        if (isMoving == wasMoving)
            return;
        wasMoving = isMoving;

        Debug.Log(isMoving ? "Started moving" : "Stopped moving");

        List<Part> parts = AnimatedAttachmentUpdater.GetParts();

        if (isMoving)
        {
            partInfos = new PartInfo[parts.Count];

            // If any part is moving, we need to de-strut any wheels
            foreach (Part part in parts)
            {
                // Ignore parts that don't have struting
                if (part.autoStrutMode == Part.AutoStrutMode.Off)
                    continue;

                // Create a record to keep track of the part and the current mode
                PartInfo partInfo = new PartInfo();
                partInfos[parts.IndexOf(part)] = partInfo;

                partInfo.part = part;
                partInfo.autoStrutMode = part.autoStrutMode;

                Debug.Log(string.Format("Changing auto strut of {0} from {1} to {2}",
                    part.name,
                    part.autoStrutMode,
                    Part.AutoStrutMode.Off));

                // Remove the struting
                part.autoStrutMode = Part.AutoStrutMode.Off;
                part.ReleaseAutoStruts();
            }
        }
        else
        {
            // Go through our list of de-strutted parts and put their original strutting back again
            foreach(PartInfo partInfo in partInfos)
            {
                if (partInfo == null)
                    continue;

                Debug.Log(string.Format("Changing auto strut of {0} from {1} to {2}",
                    partInfo.part.name,
                    partInfo.part.autoStrutMode,
                    partInfo.autoStrutMode));

                // Bring struty back
                partInfo.part.autoStrutMode = partInfo.autoStrutMode;
            }
        }
    }

    // Save all current positions as original positions, so that parts start in the
    // the correct positions after reloading the vessel.
    public static void UpdateOriginalPositions()
    {
        List<Part> parts = GetParts();
        foreach (Part part in parts)
            part.UpdateOrgPosAndRot(part.localRoot);    
    }    

    void Awake()
    {
        _this = this;
    }

    // Check if any animation is moving
    public static bool AnyAnimationMoving()
    {
        List<Part> parts = GetParts();
        foreach (Part part in parts)
            foreach (PartModule partModule in part.Modules)
                if (partModule.moduleName == "ModuleAnimateGeneric")
                    if (((ModuleAnimateGeneric)partModule).aniState == ModuleAnimateGeneric.animationStates.MOVING)
                        return true;
        return false;
    }
}
