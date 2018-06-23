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
        UpdateState();
        UpdateAttachments();
        UpdateDebugAxes();
        passCounter++;
    }

    private void UpdateState()
    {
        if (!activated)
        {
            foreach (AttachNodeInfo attachNodeInfo in attachNodeInfos)
            {
                attachNodeInfo.attachedPartOriginal = null;
                attachNodeInfo.attachNodeOriginal = null;
            }
        }
        else
        {
            //if (flightState == State.INIT)
            //    flightState = State.STARTED;
        }
    }

    private void LateUpdate()
    {
    }

    public static Vector3 StringToVector3(string sVector)
    {
        if (sVector == null)
            return Vector3.zero;

        // Remove the parentheses
        if (sVector.StartsWith("(") && sVector.EndsWith(")"))
            sVector = sVector.Substring(1, sVector.Length - 2);

        // Split the items
        string[] sArray = sVector.Split(',');

        // Store as a Vector3
        return new Vector3(
            float.Parse(sArray[0]),
            float.Parse(sArray[1]),
            float.Parse(sArray[2]));
    }

    public static Quaternion StringToQuaternion(string sVector)
    {
        // Remove the parentheses
        if (sVector.StartsWith("(") && sVector.EndsWith(")"))
            sVector = sVector.Substring(1, sVector.Length - 2);

        if (sVector == null)
            return Quaternion.identity;

        // Split the items
        string[] sArray = sVector.Split(',');

        // Store as a Quaternion
        return new Quaternion(
            float.Parse(sArray[0]),
            float.Parse(sArray[1]),
            float.Parse(sArray[2]),
            float.Parse(sArray[3]));
    }


    public class PosRot
    {
        public Quaternion rotation;
        public Vector3 position;
        public Vector3 orientation;

        public override string ToString()
        {
            if(orientation != Vector3.zero)
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

            position = StringToVector3(node.GetValue("position"));
            rotation = StringToQuaternion(node.GetValue("rotation"));
            orientation = StringToVector3(node.GetValue("orientation"));
        }
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

        // Update the orientation vector
        result.orientation = result.rotation* Vector3.forward;

        // Include the rescale factor
        result.position *= part.rescaleFactor;

        return result;
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
        ENDING,
        REINIT,
    };
    public State flightState = State.INIT;

    // Contains info on each attached node
    public class AttachNodeInfo
    {
        public AttachNode attachNode;

        public PosRot attachedPartOriginal;
        public PosRot attachNodeOriginal;

        public LineInfo lineAnchor;
        public LineInfo lineNodeToPart;
        public OrientationInfo orientationAttachNode;
        public OrientationInfo orientationJoint;
        public AxisInfo axisJoint;
        public int counter;

        public AttachNodeInfo(AttachNode attachNode)
        {
            this.attachNode = attachNode;
        }

        internal void Save(ConfigNode root)
        {
            Debug.Log("AttachNodeInfo.Save");

            ConfigNode attachNodeInfo = root.AddNode("ATTACH_NODE");
            if (attachNodeOriginal != null)
                attachNodeOriginal.Save(
                    attachNode.nodeTransform.name, 
                    attachNodeInfo.AddNode("NODE"));

            if (attachedPartOriginal != null && attachNode.attachedPart != null)
                attachedPartOriginal.Save(
                    attachNode.attachedPart.name,
                    attachNodeInfo.AddNode("PART"));
        }

        internal void Load(int index, ConfigNode root)
        {
            Debug.Log("AttachNodeInfo.Load");

            ConfigNode attachNodeInfo = root.GetNode("ATTACH_NODE", index);
            if (attachNodeInfo == null)
                return;

            if (attachNodeOriginal == null)
                attachNodeOriginal = new PosRot();
            attachNodeOriginal.Load(attachNodeInfo.GetNode("NODE"));

            if (attachedPartOriginal == null)
                attachedPartOriginal = new PosRot();
            attachedPartOriginal.Load(attachNodeInfo.GetNode("PART"));
        }
    };

    // For debugging purposes, we want to limit the console output a bit 
    int debugCounter;
    int passCounter;

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

        attachNodeInfo.counter++;

        // Get the position and rotation of the node transform relative to the part.
        // The nodeTransform itself will only contain its positions and rotation 
        // relative to the immediate parent in the model
        PosRot posRot = GetPosRot(attachNode.nodeTransform);

        // Update the attachNode
        attachNode.position = posRot.position;
        attachNode.orientation = posRot.orientation;

        // If the is no actual part attached to the attach node, then we can bail out.
        Part attachedPart = attachNode.attachedPart;

        // Take note of newly attached parts, including at initial ship load
        if (attachedPart == null)
            attachNodeInfo.attachedPartOriginal = null;
        else
        {
            if (attachNodeInfo.attachedPartOriginal == null)
            {
                Debug.Log("Recording attachedPartOriginal");

                attachNodeInfo.attachedPartOriginal = new PosRot();
                attachNodeInfo.attachedPartOriginal.rotation = attachNode.attachedPart.transform.localRotation;
                attachNodeInfo.attachedPartOriginal.position = attachNode.attachedPart.transform.localPosition;

                // Make sure to grab a new reference value of the attach node pos/rot
                attachNodeInfo.attachNodeOriginal = null;
            }
        }

        if (attachNodeInfo.counter < 10)
            Debug.Log(flightState);

        switch (flightState)
        {
            // In the first pass, set the local position of the part
            case State.INIT:
            case State.STARTING:
                {
                    if (passCounter >= 1)
                        break;

                    Debug.Log(string.Format("State.ENDING: {0} {1}",
                        attachNodeInfo.attachedPartOriginal,
                        attachedPart));

                    if (attachNodeInfo.attachedPartOriginal == null)
                        break;

                    // Rotation and position delta from saved value
                    PosRot delta = new PosRot
                    {
                        rotation = posRot.rotation * attachNodeInfo.attachNodeOriginal.rotation.Inverse(),
                        position = posRot.position - attachNodeInfo.attachNodeOriginal.position
                    };

                    // Calculate the attached parts position in the frame of reference of this part
                    PosRot local = new PosRot
                    {
                        rotation = delta.rotation * attachNodeInfo.attachedPartOriginal.rotation,
                        position = posRot.position + delta.rotation * (attachNodeInfo.attachedPartOriginal.position - attachNodeInfo.attachNodeOriginal.position)
                    };

                    Debug.Log(string.Format("State.ENDING: {0} {1}",
                        local.position,
                        local.rotation));

                    //attachedPart.transform.localRotation = local.rotation;
                    //attachedPart.transform.localPosition = local.position;
                }
                break;

                /*
            case State.STARTING:
                Debug.Log(string.Format("State.STARTING: {0} {1}",
                    attachedPart.transform.localRotation,
                    attachedPart.transform.localPosition));

                break;
                */

            // On the third pass, get values of the attach node transform
            case State.STARTED:
                {
                    if (attachNodeInfo.attachNodeOriginal == null)
                    {
                        Debug.Log("Recording attachNodeOriginal");

                        // Now we are okey to start animating the attach node and any attached part
                        attachNodeInfo.attachNodeOriginal = posRot;
                    }

                    if (attachNodeInfo.attachedPartOriginal == null)
                        break;

                    // Rotation and position delta from saved value
                    PosRot delta = new PosRot
                    {
                        rotation = posRot.rotation * attachNodeInfo.attachNodeOriginal.rotation.Inverse(),
                        position = posRot.position - attachNodeInfo.attachNodeOriginal.position
                    };

                    // Calculate the attached parts position in the frame of reference of this part
                    PosRot local = new PosRot
                    {
                        rotation = delta.rotation * attachNodeInfo.attachedPartOriginal.rotation,
                        position = posRot.position + delta.rotation * (attachNodeInfo.attachedPartOriginal.position - attachNodeInfo.attachNodeOriginal.position)
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
                        attachedPart.transform.localRotation = local.rotation;
                        attachedPart.transform.localPosition = local.position;

                        // There is nothing more to do, so bail out
                        break;
                    }

                    // Things get tricker if the parts are connected by joints. We need to setup the joint
                    // to apply forces to the sub part.
                    ConfigurableJoint joint = attachedPart.attachJoint.Joint;

                    // The joint will not respond to changes to targetRotation/Position in locked mode,
                    // so change it to free in all directions
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
                    joint.SetTargetRotationLocal(
                        delta.rotation * attachNodeInfo.attachedPartOriginal.rotation, 
                        attachNodeInfo.attachedPartOriginal.rotation);

                    // Move the attached part by updating the connectedAnchor instead of the joint.targetPosition.
                    // This is easier since the anchor is in the reference frame of this part, and we already have the
                    // position in that reference frame. It also makes sense from the view that since it really is the 
                    // attachment point of the attached part that is moving. There might be benefits of using the targetPosition
                    // though, and should be possible to calculate it fairly easily if needed.
                    //joint.connectedAnchor = local.position;
                    joint.connectedAnchor = posRot.position;

                    PosRot org = new PosRot();
                    org.position = attachedPart.orgPos;
                    org.rotation = attachedPart.orgRot;
                    if ((attachNodeInfo.counter % 100) == 0)
                        Debug.Log(string.Format("{0} -> {1}, {2}", posRot, delta, org));

                    // Debug info
                    if (debugVectors)
                    {
                        // Show debug vectors for the child part
                        /*
                        if (attachNodeInfo.axisJoint == null)
                            attachNodeInfo.axisJoint = new AxisInfo(joint.transform);
                        */
                        if (attachNodeInfo.lineAnchor == null)
                            attachNodeInfo.lineAnchor = new LineInfo(part.transform, Color.cyan);
                        attachNodeInfo.lineAnchor.Update(Vector3.zero, joint.connectedAnchor);

                        if (attachNodeInfo.lineNodeToPart == null)
                            attachNodeInfo.lineNodeToPart = new LineInfo(part.transform, Color.magenta);
                        attachNodeInfo.lineNodeToPart.Update(attachNodeInfo.attachNodeOriginal.position, attachNodeInfo.attachedPartOriginal.position);

                        
                    }
                    else
                    {
                        if (attachNodeInfo.axisJoint != null)
                            attachNodeInfo.axisJoint = null;
                    }
                }
                break;
        }

        // Debug info
        if (debugVectors)
        {
            // Show debug vectors for the attachNodes
            /*
            if (attachNodeInfo.orientationAttachNode == null)
                attachNodeInfo.orientationAttachNode = new OrientationInfo(part.transform, posRot.position, posRot.position + attachNodeInfo.attachNodeOriginal.orientation);
            attachNodeInfo.orientationAttachNode.Update(posRot.position, posRot.position + attachNode.orientation);
            */
        }
        else
        {
            if (attachNodeInfo.orientationAttachNode != null)
                attachNodeInfo.orientationAttachNode = null;
        }
    }

    private void UpdateAttachments()
    {
        // Bail out if init failed
        if (attachNodeInfos == null)
            return;

        // Save original orientation for each attachNode
        for (int i = 0; i < part.attachNodes.Count; i++)
            UpdateAttachments(i);
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

    public void InitAttachNodeList()
    {
        // Set up our array containing info about each attach node and their connected parts
        if (attachNodeInfos == null)
        {
            attachNodeInfos = new AttachNodeInfo[part.attachNodes.Count];

            for (int i = 0; i < part.attachNodes.Count; i++)
            {
                AttachNode attachNode = part.attachNodes[i];
                attachNodeInfos[i] = new AttachNodeInfo(attachNode);
            }
        }
    }

    public override void OnStart(StartState state)
    {
        base.OnStart(state);

        Debug.Log("OnStart");
        Debug.Log(state);

        flightState = State.INIT;
        passCounter = 0;
        InitAttachNodeList();
        UpdateAttachments();

        flightState = State.STARTING;
    }

    public override void OnStartFinished(StartState state)
    {
        base.OnStartFinished(state);

        flightState = State.STARTED;

        Debug.Log("OnStartFinished");
        Debug.Log(state);
    }

    public override void OnSave(ConfigNode node)
    {
        base.OnSave(node);

        if (attachNodeInfos != null)
            foreach (AttachNodeInfo attachNodInfo in attachNodeInfos)
                attachNodInfo.Save(node);

        Debug.Log("OnSave");
        Debug.Log(node);

        /*
        if (flightState == State.STARTED)
        {
            Debug.Log("SetOriginalPositions");
            SetOriginalPositions();
            UpdateOriginalPositions();
        }
        */
    }

    public override void OnLoad(ConfigNode node)
    {
        base.OnLoad(node);

        Debug.Log("OnLoad");
        Debug.Log(node);

        InitAttachNodeList();

        foreach (AttachNodeInfo attachNodInfo in attachNodeInfos)
            attachNodInfo.Load(
                attachNodeInfos.IndexOf(attachNodInfo), 
                node);
    }

    // Retrieve the active vessel and its parts
    List<Part> GetParts()
    {
        List<Part> parts = null;

        if (FlightGlobals.ActiveVessel)
            parts = FlightGlobals.ActiveVessel.parts;
        else
            if(EditorLogic.fetch)
                parts = EditorLogic.fetch.ship.parts;

        return parts;
    }

    void UpdateOriginalPositions()
    {
        List<Part> parts = GetParts();
        if (parts == null)
            return;

        foreach (Part part in parts)
            part.UpdateOrgPosAndRot(part.localRoot);
    }

    void SetOriginalPositions()
    {
        flightState = State.ENDING;
        UpdateAttachments();
    }
}

/* 
 * We need to save original positions when going to TimeWarp and leaving the flight scene.
 * This is easier handled by a mono behaviour instead of letting part modules react to the
 * same event event multiple times.
 */
/*
[KSPAddon(KSPAddon.Startup.FlightAndEditor, false)]
public class AnimatedAttachmentUpdater : MonoBehaviour
{
 float timeWarpCurrent;

 // Retrieve the active vessel and its parts
 List<Part> GetParts()
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
     if (timeWarpCurrent != TimeWarp.CurrentRate)
     {
         if (TimeWarp.CurrentRate != 1 && timeWarpCurrent == 1)
         {
             Debug.Log("TimeWarp started");
             UpdateOriginalPositions();
         }
         timeWarpCurrent = TimeWarp.CurrentRate;
     }
     UpdateOriginalPositions();
 }

 void UpdateOriginalPositions()
 {
     List<Part> parts = GetParts();
     foreach (Part part in parts)
         part.UpdateOrgPosAndRot(part.localRoot);
 }    
}
*/
