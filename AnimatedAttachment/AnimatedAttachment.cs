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

public class AnimatedAttachment : PartModule, IJointLockState
{
    [KSPField(isPersistant = true, guiName = "Animated attachments", guiActiveEditor = true, guiActive = true, advancedTweakable = true)]
    [UI_Toggle(disabledText = "Disabled", enabledText = "Enabled")]
    public bool activated = true;
    [KSPField(isPersistant = false, guiName = "Debug", guiActiveEditor = true, guiActive = true, advancedTweakable = true)]
    [UI_Toggle(disabledText = "Disabled", enabledText = "Enabled")]
    public bool debug = false;
    [KSPField(isPersistant = true, guiName = "Maximum force", guiActiveEditor = true, advancedTweakable = true)]
    [UI_FloatRange(minValue = 1f, maxValue = 1000000f, stepIncrement = 1f)]
    public float maximumForce = 100000f;
    [KSPField(isPersistant = true, guiName = "Damper", guiActiveEditor = true, advancedTweakable = true)]
    [UI_FloatRange(minValue = 1f, maxValue = 10000f, stepIncrement = 1f)]
    public float positionDamper = 1000f;
    [UI_FloatRange(minValue = 1f, maxValue = 1000000f, stepIncrement = 1f)]
    [KSPField(isPersistant = true, guiName = "Spring", guiActiveEditor = true, advancedTweakable = true)]
    public float positionSpring = 100000f;

    // For debugging purposes, we want to limit the console output a bit 
    private int debugCounter;

    // Opotionally show unit vectors of the axes for debugging purposes
    private AxisInfo axisWorld;
    private AxisInfo axisAttachNode;

    // Contains info for all the attached sub parts
    List<AttachedPartInfo> attachedPartInfos;

    private static void printf(string format, params object[] a)
    {
        int i = 0;
        string s = (format is string) ? System.Text.RegularExpressions.Regex.Replace((string)format, "%[sdi%]",
          match => match.Value == "%%" ? "%" : i < a.Length ? (a[i++] != null ? a[i - 1].ToString() : "null") : match.Value) : format.ToString();
        //for (; i < a.Length; i++)
        //  s += " " + a[i] != null ? a[i] : "null";
        Debug.Log("AnimatedAttachment: " + s);
    }

    private void Update()
    {
    }

    private void FixedUpdate()
    {
        bool debugLog = debug && ((debugCounter++ % 100) == 0);

        UpdateAttachments(attachedPartInfos, debugLog);
        UpdateState();
        UpdateDebugAxes();
    }

    private void UpdateState()
    {
        if (activated && flightState == State.INIT)
            flightState = State.STARTING;
    }

    private void LateUpdate()
    {
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
    public class AttachedPartInfo
    {
        private AttachNode.NodeType nodeType;
        public AttachNode stackAttachNode;
        internal Part attachedPart;

        // Designed position of the attached part from the editor, expressed
        // as an offset from the attach node
        public PosRot attachedPartOffset;
        // Original rotation of an attached part at the start of the scene
        public PosRot attachedPartOriginal;
        public Collider collider;

        public LineInfo lineAnchor;
        public LineInfo lineNodeToPart;
        public OrientationInfo orientationAttachNode;
        public OrientationInfo orientationJoint;
        public AxisInfo axisJoint;

        private AnimatedAttachment animatedAttachment;
        private JointDrive jointDrive;
        internal bool loaded;

        public AttachedPartInfo(AnimatedAttachment animatedAttachment, Part attachedPart)
        {
            this.animatedAttachment = animatedAttachment;
            this.attachedPart = attachedPart;

            AttachNode.NodeType nodeType = AttachNode.NodeType.Stack;
            if (attachedPart && attachedPart.srfAttachNode.attachedPart)
                nodeType = AttachNode.NodeType.Surface;
            this.nodeType = nodeType;
        }

        private Transform GetReferenceTransform()
        {
            switch (nodeType)
            {
                case AttachNode.NodeType.Stack:
                    return stackAttachNode.nodeTransform;

                case AttachNode.NodeType.Surface:
                    return collider.transform;

                default:
                    throw new NotImplementedException();
            }
        }

        internal void Save(ConfigNode root)
        {
            ConfigNode attachNodeInfo = root.AddNode("ATTACH_NODE_INFO");

            //attachNodeInfo.AddValue("name", GetReferenceTransform().name);
            //attachNodeInfo.AddValue("attachedPart", attachedPart.name);
            attachNodeInfo.AddValue("colliderName", collider ? collider.name : "");
            attachNodeInfo.AddValue("nodeType", nodeType);

            if (attachedPartOffset != null)
                attachedPartOffset.Save(attachNodeInfo, "offset");
        }

        internal void Load(int index, ConfigNode root)
        {
            ConfigNode attachNodeInfo = root.GetNode("ATTACH_NODE_INFO", index);
            if (attachNodeInfo == null)
            {
                printf("Failed to find ATTACH_NODE_INFO!");
                return;
            }

            loaded = true;

            if (!attachNodeInfo.HasNode("POS_ROT"))
            {
                printf("Failed to find POS_ROT!");
                return;
            }

            //string attachedPartName = attachNodeInfo.GetValue("attachedPart");
            string colliderName = attachNodeInfo.GetValue("colliderName");
            Collider[] colliders = animatedAttachment.part.GetPartColliders();

            foreach (Collider collider in colliders)
                if (collider.name == colliderName)
                    this.collider = collider;

            nodeType = (AttachNode.NodeType)Enum.Parse(typeof(AttachNode.NodeType), attachNodeInfo.GetValue("nodeType"));

            if (attachedPartOffset == null)
                attachedPartOffset = new PosRot();

            attachedPartOffset.Load(attachNodeInfo, "offset");
        }

        public void UpdateAttachments(State flightState, bool debug, bool debugPeriodic)
        {
            // If the is no actual part attached to the attach node, then we can bail out.
            // Part attachedPart = GetAttachedPart();

            if (debugPeriodic)
                printf("UA: %s %s %s %s",
                    attachedPart != null ? attachedPart.name : null,
                    nodeType,
                    stackAttachNode != null ? stackAttachNode.id : "null",
                    stackAttachNode != null ? stackAttachNode.attachedPartId.ToString() : "null");

            // We don't want to mess with the joint attaching this part to its parent.
            // Also, take of the special case where they are both null, otherwise we
            // will incorrectly get a match between them, resulting in loss of function
            // if the animated part is the root part.
            if ((attachedPart == animatedAttachment.part.parent) &&
                (attachedPart != null))
            {
                if (debugPeriodic)
                    printf("Skipping parent");

                return;
            }

            switch (nodeType)
            {
                case AttachNode.NodeType.Surface:
                    if (collider == null)
                    {
                        SetCollider();

                        if (debug)
                            printf("Setting collider to %s",
                                collider.name);
                    }
                    break;
                case AttachNode.NodeType.Stack:
                    if (stackAttachNode == null)
                    {
                        stackAttachNode = animatedAttachment.part.FindAttachNodeByPart(attachedPart);

                        // Sometimes life throws lemons at you, like when the user actvated attachments in flight
                        if (stackAttachNode == null)
                        {
                            // Try again as surface attached
                            nodeType = AttachNode.NodeType.Surface;
                            return;
                        }

                        if (debug)
                            printf("Setting attach node to %s",
                                stackAttachNode.id);
                    }
                    break;
            }

            Transform referenceTransform = GetReferenceTransform();

            // If this attach node is defined in the cfg, then bail out now, it will not be movable
            if (referenceTransform == null)
            {
                if (debugPeriodic)
                    printf("Skipping cfg based node: %s", stackAttachNode.id);
                return;
            }

            // Get the position and rotation of the node transform relative to the part.
            // The nodeTransform itself will only contain its positions and rotation 
            // relative to the immediate parent in the model
            PosRot referencePosRot = PosRot.GetPosRot(referenceTransform, animatedAttachment.part);

            // We can't animate decoupling shrouds
            if (referencePosRot == null)
            {
                if (debugPeriodic)
                    printf("Skipping decoupler shroud");
                return;
            }

            bool active = animatedAttachment.activated;
            if (EditorLogic.fetch && (EditorLogic.fetch.EditorConstructionMode != ConstructionMode.Place))
                active = false;

            // Take note of newly attached parts, including at initial ship load
            if (attachedPart == null || !active)
            {
                if (debugPeriodic)
                    if (attachedPart == null)
                        printf("No part attached");

                attachedPartOffset = null;
                return;
            }
            else
            {
                if (debug)
                    printf("attachedPartOffset: %s", attachedPartOffset);

                if (attachedPartOffset == null)
                {
                    attachedPartOffset = new PosRot();

                    // Get attached part position relative to this part
                    PosRot localPosRot = new PosRot();

                    printf("Recording attachedPartOffset, world position");

                    Transform parent = attachedPart.transform.parent;

                    // Let the engine calculate the local position instead of doing the calculation ourselves..
                    attachedPart.transform.parent = animatedAttachment.part.transform;
                    localPosRot.position = attachedPart.transform.localPosition;
                    localPosRot.rotation = attachedPart.transform.localRotation;
                    attachedPart.transform.parent = parent;

                    // We could do parenting trick for this too, but seems we loose the scaling
                    attachedPartOffset.rotation =
                        referencePosRot.rotation.Inverse() *
                        localPosRot.rotation;

                    attachedPartOffset.position =
                        referencePosRot.rotation.Inverse() *
                        (localPosRot.position -
                        referencePosRot.position);

                    attachedPartOriginal = new PosRot();
                    attachedPartOriginal.rotation = localPosRot.rotation;
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
                            attachedPartOriginal.rotation = attachedPart.transform.localRotation;
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
                            rotation = referencePosRot.rotation * attachedPartOffset.rotation,
                            position = referencePosRot.position + referencePosRot.rotation * attachedPartOffset.position
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

                            if (debugPeriodic)
                                printf("Updated pos without physics");

                            // There is nothing more to do, so bail out
                            break;
                        }

                        // In the editor, while changing action groups, the parent will be null for some reason.
                        // We can catch that here by making sure there is axists a joint 
                        if (attachedPart.attachJoint == null)
                        {
                            if (debugPeriodic)
                                printf("No attach joint found");
                            break;
                        }

                        // Things get tricker if the parts are connected by joints. We need to setup the joint
                        // to apply forces to the sub part.
                        ConfigurableJoint joint = attachedPart.attachJoint.Joint;

                        // It is not possible to change values of a JointDrive after creation, so we must create a 
                        // new one and apply it to the joint. Seems we can't only create it at startup either. 
                        /*
                        if (!jointDriveInitialized)
                        {
                            jointDriveInitialized = true;
                            printff("Creating a new drive mode");
                            printf(string.Format("maximumForce: {0}", animatedAttachment.maximumForce));
                            printf(string.Format("positionDamper: {0}", animatedAttachment.positionDamper));
                            printf(string.Format("positionSpring: {0}", animatedAttachment.positionSpring));
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
                        if (debug)
                            printf("%s", joint);
                        if (debug)
                            printf("%s", attachedPartPosRot);
                        if (debug)
                            printf("%s", attachedPartOriginal);

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
                        joint.connectedAnchor = referencePosRot.position;

                        // Make sure the target position is zero
                        joint.targetPosition = Vector3.zero;

                        // This scaling and rotation is to convert to joint space... maybe? 
                        // Determined by random tinkering and is magical as far as I am concerned
                        joint.anchor = attachedPartOffset.rotation.Inverse() *
                            Vector3.Scale(
                                new Vector3(-1, -1, -1),
                                attachedPartOffset.position);

                        if (debugPeriodic)
                            printf("%s; %s; %s -> %s; %s -> %s; %s",
                                referencePosRot,
                                attachedPartPosRot,
                                attachedPartOffset,
                                attachedPartOriginal.rotation.eulerAngles,
                                joint.targetRotation.eulerAngles,
                                joint.anchor,
                                joint.connectedAnchor
                                );

                        // Debug info
                        if (debug)
                        {
                            // Show debug vectors for the child part
                            if (axisJoint == null)
                                axisJoint = new AxisInfo(joint.transform);

                            if (lineAnchor == null)
                                lineAnchor = new LineInfo(animatedAttachment.part.transform, Color.cyan);
                            lineAnchor.Update(Vector3.zero, joint.connectedAnchor);

                            if (lineNodeToPart == null)
                                lineNodeToPart = new LineInfo(animatedAttachment.part.transform, Color.magenta);
                            lineNodeToPart.Update(
                                referencePosRot.position,
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
            if (debug)
            {
                // Show debug vectors for the attachNodes
                if (orientationAttachNode == null)
                    orientationAttachNode = new OrientationInfo(animatedAttachment.part.transform, referencePosRot.position, referencePosRot.position + attachedPartOffset.orientation);

                if (stackAttachNode != null)
                    orientationAttachNode.Update(referencePosRot.position, referencePosRot.position + stackAttachNode.orientation);
            }
            else
            {
                if (orientationAttachNode != null)
                    orientationAttachNode = null;
            }
        }

        private void SetCollider()
        {
            Collider[] colliders = animatedAttachment.part.GetPartColliders();

            float bestDistance = 99999.0f;
            Collider bestCollider = null;

            foreach (Collider collider in colliders)
            {
                Vector3 attachPosition = attachedPart.transform.TransformPoint(attachedPart.srfAttachNode.position);
                Vector3 closestPoint = collider.ClosestPoint(attachPosition);

                /*
                 According to the unity doc, if the attachPosition is inside the collider,
                 then the input point should be returned by ClosestPoint. Which makes sense. 
                 But, it seems that actually the position of the collider is returned instead. 
                 In this case, move the closest point to the attach position manually instead.
                 */
                if (closestPoint == collider.transform.position)
                    closestPoint = attachPosition;

                float distance = Vector3.Distance(closestPoint, attachPosition);

                if (animatedAttachment.debug)
                    printf("Collider %s: %s + %s = %s, %s, %sm",
                        collider.transform.position,
                        attachedPart.transform.localPosition,
                        attachedPart.srfAttachNode.position,
                        attachPosition,
                        closestPoint,
                        distance);

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestCollider = collider;

                    if (distance == 0)
                        break;
                }
            }
            collider = bestCollider;
        }
    }

    private void UpdateAttachments(List<AttachedPartInfo> attachedPartInfos, bool debugPeriodic)
    {
        // Bail out if init failed
        if (attachedPartInfos == null)
        {
            if (debugPeriodic)
                print("Empty attach node info list!");
            return;
        }

        if (debugPeriodic)
            printf("Updating %s/%s parts",
                attachedPartInfos.Count,
                part.children.Count);

        foreach (AttachNode attachNode in part.attachNodes)
        {
            // We can't move attach nodes that are positioned in the cfg file
            if (attachNode.nodeTransform == null)
            {
                if (debugPeriodic)
                    printf("Skipping %s since it lacks a node transform",
                        attachNode.id);
                continue;
            }

            // Get the position and rotation of the node transform relative to the part.
            // The nodeTransform itself will only contain its positions and rotation 
            // relative to the immediate parent in the model
            PosRot attachNodePosRot = PosRot.GetPosRot(attachNode.nodeTransform, part);

            // We can't animate decoupling shrouds
            if (attachNodePosRot == null)
            {
                if (debugPeriodic)
                    printf("Skipping %s since it is a decoupler shroud",
                        attachNode.id);
                continue;
            }

            // Update the attachNode
            attachNode.position = attachNodePosRot.position;
            attachNode.orientation = attachNodePosRot.orientation;
        }

        for (int i = 0; i < part.children.Count; i++)
        {
            Part child = part.children[i];
            // Create new entries in the list if needed
            if (i >= attachedPartInfos.Count)
            {
                if (debug)
                    printf("Adding child %s [%s]",
                        child.name,
                        i);

                attachedPartInfos.Add(new AttachedPartInfo(this, child));
            }

            AttachedPartInfo attachedPartInfo = attachedPartInfos[i];
            // Assign parts to info structs newly loaded from a save file
            if (attachedPartInfo.loaded)
            {
                attachedPartInfo.attachedPart = child;
                attachedPartInfo.loaded = false;

                if(debug)
                    printf("Assigning child %s [%s]",
                        attachedPartInfo.attachedPart,
                        i);
            }
            else
            // Remove stale entries
            if (attachedPartInfo.attachedPart != child)
            {
                if (debug)
                    printf("Deleting child %s/%s",
                        attachedPartInfo.attachedPart,
                        child.name);
                attachedPartInfos.Remove(attachedPartInfo);
                i--;
                continue;
            }

            if (debugPeriodic)
                printf("Updating child %s [%s]",
                    attachedPartInfo,
                    i);
            attachedPartInfo.UpdateAttachments(flightState, debug, debugPeriodic);
        }

        // Continue deleting surplus entries
        while (attachedPartInfos.Count > part.children.Count)
        {
            AttachedPartInfo attachedPartInfo = attachedPartInfos[part.children.Count];
            if (debug)
                printf("Deleting child %s [%s]",
                    attachedPartInfo.attachedPart,
                    part.children.Count);
            attachedPartInfos.Remove(attachedPartInfo);
        }
    }

    private void UpdateDebugAxes()
    {
        // Debug info
        if (debug)
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

    private void InitAttachNodeLists()
    {
        if (attachedPartInfos == null)
        {
            // Set up our array containing info about each attach node and their connected parts
            attachedPartInfos = new List<AttachedPartInfo>();
        }
    }

    public override void OnStart(StartState state)
    {
        base.OnStart(state);

        RemoveNoAttach();

        InitAttachNodeLists();

        flightState = State.INIT;
        UpdateAttachments(attachedPartInfos, false);
        flightState = State.STARTING;
    }

    private void RemoveNoAttach()
    {
        Collider[] colliders = part.GetPartColliders();

        foreach (Collider collider in colliders)
        {
            if (collider.tag == "NoAttach")
            {
                /*
                printf("AnimatedAttachment: Removing tag %s from collider %s in part %s",
                    collider.tag,
                    collider.name,
                    part.name);
                */

                collider.tag = "Untagged";
            }
        }
    }

    public override void OnStartFinished(StartState state)
    {
        base.OnStartFinished(state);

        flightState = State.STARTED;
    }

    private void Save(ConfigNode node, List<AttachedPartInfo> attachNodeInfos)
    {
        if (attachNodeInfos == null)
            return;

        foreach (AttachedPartInfo attachNodInfo in attachNodeInfos)
            attachNodInfo.Save(node);

        if (debug)
            printf("Save: %s", node);
    }

    public override void OnSave(ConfigNode node)
    {
        base.OnSave(node);

        Save(node.AddNode("ATTACHED_PART_INFOS"), attachedPartInfos);

        if (debug)
            printf("Save: %s", node);

        // Save original positions when saving the ship.
        // Don't do it at the save occuring at initial scene start.
        if (flightState == State.STARTED)
        {
            //SetOriginalPositions();
            AnimatedAttachmentUpdater.UpdateOriginalPositions();
        }
    }

    private void LoadAttachedParts(ConfigNode node)
    {
        if (node == null)
            return;

        for (int i = 0; i < node.CountNodes; i++)
        {
            AttachedPartInfo attachedPartInfo = new AttachedPartInfo(this, null);
            attachedPartInfos.Add(attachedPartInfo);
            attachedPartInfo.Load(i, node);
        }
    }

    public override void OnLoad(ConfigNode node)
    {
        base.OnLoad(node);

        InitAttachNodeLists();

        LoadAttachedParts(node.GetNode("ATTACHED_PART_INFOS"));
    }

    public bool IsJointUnlocked()
    {
        return true;
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
    float timeWarpCurrent;

    private static void printf(string format, params object[] a)
    {
        int i = 0;
        string s = (format is string) ? System.Text.RegularExpressions.Regex.Replace((string)format, "%[sdi%]",
          match => match.Value == "%%" ? "%" : i < a.Length ? (a[i++] != null ? a[i - 1].ToString() : "null") : match.Value) : format.ToString();
        Debug.Log("AnimatedAttachmentUpdater: " + s);
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
                printf("AnimatedAttachment: TimeWarp started");
                UpdateOriginalPositions();
            }
            timeWarpCurrent = TimeWarp.CurrentRate;
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
}

/* 
 * Stock auto-strut from wheels cause issues by not implementing the IJointLockState properly.
 * Work-around this by temporarily disabling all auto-struts when something is moving (here 
 * defined as any animation is running).
 */
[KSPAddon(KSPAddon.Startup.Flight, false)]
public class AutoStrutUpdater: MonoBehaviour
{
    bool wasMoving;

    // Collect info about all the parts in the vessel and their earlier auto strut mode
    class PartInfo
    {
        public Part part;
        public Part.AutoStrutMode autoStrutMode;
    }

    PartInfo[] partInfos;

    private static void printf(string format, params object[] a)
    {
        int i = 0;
        string s = (format is string) ? System.Text.RegularExpressions.Regex.Replace((string)format, "%[sdi%]",
          match => match.Value == "%%" ? "%" : i < a.Length ? (a[i++] != null ? a[i - 1].ToString() : "null") : match.Value) : format.ToString();
        Debug.Log(s);
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

    void FixedUpdate()
    {
        bool isMoving = AnyAnimationMoving();

        if (isMoving == wasMoving)
            return;
        wasMoving = isMoving;

        printf(isMoving ? "Started moving" : "Stopped moving");

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

                printf("Changing auto strut of %s from %s to %s",
                    part.name,
                    part.autoStrutMode,
                    Part.AutoStrutMode.Off);

                // Remove the struting
                part.autoStrutMode = Part.AutoStrutMode.Off;
                part.ReleaseAutoStruts();
            }
        }
        else
        {
            // Go through our list of de-strutted parts and put their original strutting back again
            foreach (PartInfo partInfo in partInfos)
            {
                if (partInfo == null)
                    continue;

                printf("Changing auto strut of %s from %s to %s",
                    partInfo.part.name,
                    partInfo.part.autoStrutMode,
                    partInfo.autoStrutMode);

                // Bring struty back
                partInfo.part.autoStrutMode = partInfo.autoStrutMode;
            }
        }
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
