using Renderite.Shared;
using Renderite.Unity;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using Transform = UnityEngine.Transform;

public class OculusDriver : InputDriver, IDriverHeadDevice, IOutputDriver
{
    const string LEFT_HAND_ROOT = "hands:b_l_hand";
    const string RIGHT_HAND_ROOT = "hands:b_r_hand";

    const float VIBRATE_INTERVAL = 100f * 0.001f;

    public int UpdateOrder => 0;

    public HeadOutputDevice Device => HeadOutputDevice.Oculus;

    InputManager input;

    HeadsetState head;

    TouchControllerState leftTouch;
    TouchControllerState rightTouch;

    HapticSimulationData _leftHapticData;
    HapticSimulationData _rightHapticData;

    float painPhi = 0f;

    Transform localAvatar;

    Transform leftHandReference;
    Transform rightHandReference;

    Transform[] leftHandFingerRoots;
    Transform[] rightHandFingerRoots;

    HandState leftHand;
    HandState rightHand;

    double leftVibrateRemaining = 0f;
    double rightVibrateRemaining = 0f;

    float leftIntensity = 0f;
    float rightIntensity = 0f;

    Vector3[] preFingerOffsets = new Vector3[]
    {
new Vector3(-10.00f, 0.00f, 0.00f),
new Vector3(0.00f, 0.00f, 20.00f),
new Vector3(0.00f, 0.00f, 0.00f),
new Vector3(0,0,0),
new Vector3(-5.00f, 10.00f, 10.00f),
new Vector3(5.00f, -3.00f, 10.00f),
new Vector3(-5.00f, -5.00f, 10.00f),
new Vector3(5.00f, -5.00f, 10.00f),
new Vector3(0,0,0),
new Vector3(0.00f, 2.00f, 10.00f),
new Vector3(-10.00f, -10.00f, 10.00f),
new Vector3(-40.00f, -15.00f, 20.00f),
new Vector3(-40.00f, -15.00f, 20.00f),
new Vector3(0,0,0),
new Vector3(2.00f, -5.00f, 10.00f),
new Vector3(-15.00f, -10.00f, -15.00f),
new Vector3(-45.00f, -20.00f, 15.00f),
new Vector3(-50.00f, -15.00f, 15.00f),
new Vector3(0,0,0),
new Vector3(10.00f, -8.00f, -20.00f),
new Vector3(-40.00f, -10.00f, -20.00f),
new Vector3(-50.00f, -20.00f, 0.00f),
new Vector3(-50.00f, -10.00f, 0.00f),
new Vector3(0,0,0),
    };

    Vector3[] postFingerOffsets = new Vector3[]
    {
new Vector3(0.00f, 0.00f, 0.00f),
new Vector3(0.00f, 0.00f, 0.00f),
new Vector3(0.00f, 0.00f, 0.00f),
new Vector3(0,0,0),
new Vector3(0.00f, 0.00f, 0.00f),
new Vector3(0.00f, 0.00f, 0.00f),
new Vector3(0.00f, 0.00f, 0.00f),
new Vector3(0.00f, 0.00f, 0.00f),
new Vector3(0,0,0),
new Vector3(0.00f, 0.00f, 0.00f),
new Vector3(0.00f, 0.00f, 0.00f),
new Vector3(0.00f, 0.00f, 0.00f),
new Vector3(0.00f, 0.00f, -15.00f),
new Vector3(0,0,0),
new Vector3(0.00f, 0.00f, 0.00f),
new Vector3(0.00f, 0.00f, 0.00f),
new Vector3(0.00f, 0.00f, 0.00f),
new Vector3(0.00f, 0.00f, -15.00f),
new Vector3(0,0,0),
new Vector3(0.00f, 0.00f, 0.00f),
new Vector3(0.00f, 0.00f, 0.00f),
new Vector3(0.00f, 0.00f, 0.00f),
new Vector3(0.00f, 0.00f, -15.00f),
new Vector3(0,0,0),
    };

    Quaternion[] preLeftFingerOffsets;
    Quaternion[] postLeftFingerOffsets;
    Quaternion[] preRightFingerOffsets;
    Quaternion[] postRightFingerOffsets;

    [Serializable]
    public struct FingerCompensation
    {
        public BodyNode node;
        public Vector3 compensation;
    }

    (TouchControllerState, HandState) CreateTouchController(Chirality side, TouchControllerModel model)
    {
        var touch = new TouchControllerState();

        touch.side = side;
        touch.bodyNode = BodyNode.LeftController.GetSide(side);

        touch.deviceID = $"Touch {side}";
        touch.deviceModel = "Touch Controller";
        touch.model = model;

        var hand = new HandState();
        hand.uniqueId = touch.deviceID;
        hand.chirality = side;
        hand.tracksMetacarpals = false;
        hand.isTracking = false;

        input.State.vr.controllers.Add(touch);
        input.State.vr.hands.Add(hand);

        return (touch, hand);
    }

    public override void Initialize(InputManager manager)
    {
        this.input = manager;

        head = new HeadsetState();
        head.headsetModel = "Oculus Rift HMD";
        head.headsetManufacturer = "Oculus";

        input.State.vr.headsetState = head;

        if (input.State.vr.controllers == null)
            input.State.vr.controllers = new List<VR_ControllerState>();

        if (input.State.vr.hands == null)
            input.State.vr.hands = new List<HandState>();

        UnityEngine.XR.InputTracking.trackingAcquired += InputTracking_trackingAcquired;
        UnityEngine.XR.InputTracking.trackingLost += InputTracking_trackingLost;

        //if (inputInterface.Engine.Platform == Platform.Android)
        //  OVRManager.fixedFoveatedRenderingLevel = OVRManager.FixedFoveatedRenderingLevel.Medium;

        var headset = OVRPlugin.GetSystemHeadsetType();
        var touchModel = headset != OVRPlugin.SystemHeadset.Rift_CV1 ? TouchControllerModel.QuestAndRiftS : TouchControllerModel.CV1;

        (leftTouch, leftHand) = CreateTouchController(Chirality.Left, touchModel);
        (rightTouch, rightHand) = CreateTouchController(Chirality.Right, touchModel);

        var leftHandPosition = new Vector3(-0.04f, -0.025f, -0.1f);
        var leftHandRotation = Quaternion.Euler(185f, -95f, -90f) * Quaternion.Inverse(Quaternion.Euler(90, 90, 90));
        var rightHandPosition = new Vector3(0.04f, -0.025f, -0.1f);
        var rightHandRotation = Quaternion.Euler(5f, -95f, -90f) * Quaternion.Inverse(Quaternion.Euler(90, 90, 90));

        preLeftFingerOffsets = new Quaternion[preFingerOffsets.Length];
        postLeftFingerOffsets = new Quaternion[preFingerOffsets.Length];
        preRightFingerOffsets = new Quaternion[preFingerOffsets.Length];
        postRightFingerOffsets = new Quaternion[preFingerOffsets.Length];

        for (int i = 0; i < preLeftFingerOffsets.Length; i++)
        {
            preLeftFingerOffsets[i] = Quaternion.Euler(preFingerOffsets[i]);
            postLeftFingerOffsets[i] = Quaternion.Euler(postFingerOffsets[i]);

            preRightFingerOffsets[i] = Quaternion.Euler(ElementWiseMultiply(preFingerOffsets[i], new Vector3(1, -1, -1)));
            postRightFingerOffsets[i] = Quaternion.Euler(ElementWiseMultiply(postFingerOffsets[i], new Vector3(1, -1, -1)));
        }
    }

    static Vector3 ElementWiseMultiply(Vector3 a, Vector3 b) => new Vector3(a.x * b.x, a.y * b.y, a.z * b.z);

    void EnsureInitializedHand(string rootName, ref Transform handReference, ref Transform[] fingerRoots)
    {
        if (handReference != null)
            return;

        handReference = FindChildRecursively(transform, rootName);

        if (handReference != null)
        {
            fingerRoots = new Transform[5];

            fingerRoots[0] = handReference.FindContaining("thumb");
            fingerRoots[1] = handReference.FindContaining("index");
            fingerRoots[2] = handReference.FindContaining("middle");
            fingerRoots[3] = handReference.FindContaining("ring");
            fingerRoots[4] = handReference.FindContaining("pinky");
        }
    }

    static Transform FindChildRecursively(Transform root, string name)
    {
        for (int i = 0; i < root.childCount; i++)
        {
            var child = root.GetChild(i);
            if (child.name == name)
                return child;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            var child = root.GetChild(i);

            var foundChild = FindChildRecursively(child, name);

            if (foundChild != null)
                return foundChild;
        }

        return null;
    }

    private void InputTracking_trackingLost(UnityEngine.XR.XRNodeState obj)
    {
        if (obj.nodeType == UnityEngine.XR.XRNode.CenterEye)
            head.isTracking = false;
    }

    private void InputTracking_trackingAcquired(UnityEngine.XR.XRNodeState obj)
    {
        if (obj.nodeType == UnityEngine.XR.XRNode.CenterEye)
            head.isTracking = true;
    }

    void VibrateController(double time, OVRInput.Controller controller)
    {
        time = Math.Max(time, 0);

        if (time == 0)
            return;

        if (controller == OVRInput.Controller.LTouch)
        {
            leftVibrateRemaining = time;
            leftIntensity = Mathf.Clamp01((float)(time / VIBRATE_INTERVAL));
        }
        else
        {
            rightVibrateRemaining = time;
            rightIntensity = Mathf.Clamp01((float)(time / VIBRATE_INTERVAL));
        }
    }

    //public void CollectDeviceInfos(DataTreeList list)
    //{
    //    var dict = new DataTreeDictionary();

    //    dict.Add("Name", "Oculus Rift");
    //    dict.Add("Type", "HMD");
    //    dict.Add("Model", OVRPlugin.productName);

    //    list.Add(dict);

    //    if (UseTouches)
    //    {
    //        dict = new DataTreeDictionary();

    //        dict.Add("Name", "Oculus Touch Hand Model");
    //        dict.Add("Type", "Finger Tracking");
    //        dict.Add("Model", OVRPlugin.productName);

    //        list.Add(dict);
    //    }
    //}

    Quaternion _axisCompensation = Quaternion.AngleAxis(180, Vector3.up);

    Quaternion _fingerCompensationRight = Quaternion.LookRotation(new Vector3(1, 0, 0), new Vector3(0, 1, 0));
    Quaternion _wristCompensationRight = Quaternion.LookRotation(new Vector3(-1, 0, 0), new Vector3(0, 0, 1));

    Quaternion _fingerCompensationLeft = Quaternion.LookRotation(new Vector3(-1, 0, 0), new Vector3(0, -1, 0));
    Quaternion _wristCompensationLeft = Quaternion.LookRotation(new Vector3(1, 0, 0), new Vector3(0, 0, -1));

    OVRPlugin.HandState _handState;

    bool UpdateHandFromSkeleton(HandState hand)
    {
        OVRPlugin.Hand ovrHand;

        if (hand.chirality == Chirality.Left)
            ovrHand = OVRPlugin.Hand.HandLeft;
        else
            ovrHand = OVRPlugin.Hand.HandRight;

        if (OVRPlugin.GetHandState(OVRPlugin.Step.Render, ovrHand, ref _handState))
        {
            bool isTracked = (_handState.Status & OVRPlugin.HandStatus.HandTracked) != 0;

            //Debug.Log("GetHandState true, status: " + isTracked);

            if (!isTracked)
                return false;

            hand.isTracking = true;

            hand.wristPosition = _handState.RootPose.Position.FromFlippedZVector3f().ToRender();
            hand.wristRotation = _handState.RootPose.Orientation.FromFlippedZQuatf().ToRender();

            for (int i = 0; i < _handState.BoneRotations.Length; i++)
            {
                var ovrBone = (OVRPlugin.BoneId)i;
                var engineBone = ToEngine(ovrBone);

                if (!engineBone.IsFinger())
                    continue;

                engineBone = engineBone.GetSide(hand.chirality);

                var index = engineBone.FlatSegmentIndex();

                hand.segmentRotations[index] = _handState.BoneRotations[i].FromFlippedZQuatf().ToRender();
            }
        }
        //else
        //    Debug.Log("NoHandState");

        return false;
    }

    static BodyNode ToEngine(OVRPlugin.BoneId boneId)
    {
        switch (boneId)
        {
            case OVRPlugin.BoneId.Hand_ForearmStub:
                return BodyNode.LeftLowerArm;

            case OVRPlugin.BoneId.Hand_Thumb1:
                return BodyNode.LeftThumb_Metacarpal;

            case OVRPlugin.BoneId.Hand_Thumb2:
                return BodyNode.LeftThumb_Proximal;

            case OVRPlugin.BoneId.Hand_Thumb3:
                return BodyNode.LeftThumb_Distal;

            case OVRPlugin.BoneId.Hand_Index1:
                return BodyNode.LeftIndexFinger_Proximal;

            case OVRPlugin.BoneId.Hand_Index2:
                return BodyNode.LeftIndexFinger_Intermediate;

            case OVRPlugin.BoneId.Hand_Index3:
                return BodyNode.LeftIndexFinger_Distal;

            case OVRPlugin.BoneId.Hand_IndexTip:
                return BodyNode.LeftIndexFinger_Tip;

            case OVRPlugin.BoneId.Hand_Middle1:
                return BodyNode.LeftMiddleFinger_Proximal;

            case OVRPlugin.BoneId.Hand_Middle2:
                return BodyNode.LeftMiddleFinger_Intermediate;

            case OVRPlugin.BoneId.Hand_Middle3:
                return BodyNode.LeftMiddleFinger_Distal;

            case OVRPlugin.BoneId.Hand_MiddleTip:
                return BodyNode.LeftMiddleFinger_Tip;

            case OVRPlugin.BoneId.Hand_Ring1:
                return BodyNode.LeftRingFinger_Proximal;

            case OVRPlugin.BoneId.Hand_Ring2:
                return BodyNode.LeftRingFinger_Intermediate;

            case OVRPlugin.BoneId.Hand_Ring3:
                return BodyNode.LeftRingFinger_Distal;

            case OVRPlugin.BoneId.Hand_RingTip:
                return BodyNode.LeftRingFinger_Tip;

            case OVRPlugin.BoneId.Hand_Pinky0:
                return BodyNode.LeftPinky_Metacarpal;

            case OVRPlugin.BoneId.Hand_Pinky1:
                return BodyNode.LeftPinky_Proximal;

            case OVRPlugin.BoneId.Hand_Pinky2:
                return BodyNode.LeftPinky_Intermediate;

            case OVRPlugin.BoneId.Hand_Pinky3:
                return BodyNode.LeftPinky_Distal;

            case OVRPlugin.BoneId.Hand_PinkyTip:
                return BodyNode.LeftPinky_Tip;

            default:
                return BodyNode.NONE;
        }
    }

    void UpdateHandFromAvatar(HandState hand, Transform handRoot, Transform[] fingerRoots)
    {
        var wristPos = localAvatar.InverseTransformPoint(handRoot.position);
        var wristRot = localAvatar.InverseTransformRotation(handRoot.rotation);

        wristRot = wristRot * (hand.chirality == Chirality.Left ? _wristCompensationLeft : _wristCompensationRight);

        hand.wristPosition = wristPos.ToRender();
        hand.wristRotation = wristRot.ToRender();

        for (FingerType finger = FingerType.Thumb; finger <= FingerType.Pinky; finger++)
            UpdateFingerFromAvatar(hand, finger, handRoot, fingerRoots[(int)finger]);
    }

    void UpdateFingerFromAvatar(HandState hand, FingerType finger, Transform handRoot, Transform root)
    {
        if (hand.segmentPositions == null)
            hand.segmentPositions = new List<RenderVector3>();

        if (hand.segmentRotations == null)
            hand.segmentRotations = new List<RenderQuaternion>();

        while (hand.segmentPositions.Count < FingerHelper.FINGER_SEGMENT_COUNT)
            hand.segmentPositions.Add(default);

        while (hand.segmentRotations.Count < FingerHelper.FINGER_SEGMENT_COUNT)
            hand.segmentRotations.Add(default);

        var _origRoot = root;

        bool isLeft = hand.chirality == Chirality.Left;

        FingerSegmentType segmentType = FingerSegmentType.Proximal;
        int segments = 3;

        if (finger == FingerType.Thumb)
            segmentType = FingerSegmentType.Metacarpal;

        // because Oculus is special
        if (finger == FingerType.Pinky)
        {
            segmentType = FingerSegmentType.Metacarpal;
            segments = 4;
        }

        var _fingerCompensation = isLeft ? _fingerCompensationLeft : _fingerCompensationRight;
        var _wristCompensation = isLeft ? _wristCompensationLeft : _wristCompensationRight;

        for (int i = 0; i < segments; i++)
        {
            var bodyNode = finger.ComposeFinger(segmentType, hand.chirality);
            var index = bodyNode.FlatSegmentIndex();

            var pos = handRoot.InverseTransformPoint(root.position);
            var rot = handRoot.InverseTransformRotation(root.rotation);

            if (i == 0 && finger == FingerType.Pinky)
            {
                // because Oculus is EXTRA special
                rot = rot * (isLeft ? _fingerCompensationRight : _fingerCompensationLeft);
            }
            else
                rot = rot * _fingerCompensation;

            hand.segmentPositions[index] = (Quaternion.Inverse(_wristCompensation) * pos).ToRender();
            hand.segmentRotations[index] = (Quaternion.Inverse(_wristCompensation) * rot).ToRender();

            ApplyOffset(hand, bodyNode, index, isLeft);

            // go to the child
            root = root.GetChild(0);

            segmentType++;

            if (finger == FingerType.Thumb && segmentType == FingerSegmentType.Intermediate)
                segmentType++;
        }

        if (finger != FingerType.Thumb && finger != FingerType.Pinky)
        {
            // need to insert a metacarpal, since it's not part of the Oculus skeletal model
            var bodyNode = finger.ComposeFinger(FingerSegmentType.Metacarpal, hand.chirality);
            var index = bodyNode.FlatSegmentIndex();

            var proximalIndex = finger.ComposeFinger(FingerSegmentType.Proximal, hand.chirality).FlatSegmentIndex();

            var dir = _origRoot.GetChild(0).localPosition.normalized;

            if (!isLeft)
                dir = -dir;

            var rot = Quaternion.LookRotation(dir, (isLeft ? Vector3.down : Vector3.up));
            rot *= _fingerCompensation;

            // just copy the rotation
            hand.segmentPositions[index] = (hand.segmentPositions[proximalIndex].ToUnity() * 0.4f).ToRender();
            hand.segmentRotations[index] = rot.ToRender();

            ApplyOffset(hand, bodyNode, index, isLeft);
        }
    }

    void ApplyOffset(HandState hand, BodyNode node, int segmentIndex, bool isLeft)
    {
        var offsetIndex = node - (isLeft ? BodyNode.LeftThumb_Metacarpal : BodyNode.RightThumb_Metacarpal);

        Quaternion preOffset;
        Quaternion postOffset;

        if (isLeft)
        {
            preOffset = preLeftFingerOffsets[offsetIndex];
            postOffset = postLeftFingerOffsets[offsetIndex];
        }
        else
        {
            preOffset = preRightFingerOffsets[offsetIndex];
            postOffset = postRightFingerOffsets[offsetIndex];
        }

        hand.segmentRotations[segmentIndex] = (postOffset * (hand.segmentRotations[segmentIndex].ToUnity() * preOffset)).ToRender();
    }

    List<XRNodeState> nodeStates = new List<XRNodeState>();

    void UpdateVibration(float deltaTime, HapticPointState point, OVRInput.Controller controller, ref HapticSimulationData hapticData,
        ref double remainingVibration, float externalIntensity)
    {
        if (remainingVibration >= 0f)
            remainingVibration -= deltaTime;

        float intensity = 0f;
        float frequency = 0f;
        float weightSum = 0f;

        intensity += point.force * point.force;
        frequency += Mathf.Lerp(20f, 160f, point.force) * point.force;
        weightSum += point.force;

        var vibrationIntensity = Mathf.Clamp01(point.vibration * 20f);

        intensity += vibrationIntensity * point.vibration;
        frequency += Mathf.Lerp(5f, 320f, point.vibration) * point.vibration;
        weightSum += point.vibration;

        var painAmplitude = Mathf.Pow(Mathf.Abs(Mathf.Sin(painPhi)), 0.25f) * Mathf.Max(0, Mathf.Sign(Mathf.Sin(painPhi * 0.5f)));
        var pulseAmplitude = painAmplitude;
        painAmplitude *= Mathf.Pow(point.pain, 0.25f);
        painAmplitude += UnityEngine.Random.value * Mathf.Pow(point.pain, 0.25f) * 0.2f;

        intensity += painAmplitude * point.pain;
        frequency += Mathf.Lerp(60f + pulseAmplitude * 80f, 80f + pulseAmplitude * 120f, point.pain) * point.pain;
        weightSum += point.pain;

        var normalizedTemp = Mathf.Abs(point.temperature / 100f);

        hapticData.tempPhi += normalizedTemp * 4;
        hapticData.tempPhi %= 20000;

        var tempAmplitude = normalizedTemp * Mathf.PerlinNoise(hapticData.tempPhi, 0f);

        intensity += tempAmplitude * normalizedTemp;
        frequency += Mathf.Lerp(5f, 200f, normalizedTemp) * normalizedTemp;
        weightSum += normalizedTemp;

        if (remainingVibration > 0f)
        {
            intensity += externalIntensity;
            frequency += 10f;
            weightSum += externalIntensity;
        }

        if (Mathf.Approximately(intensity, 0))
            OVRInput.SetControllerVibration(0, 0, controller);
        else
        {
            intensity /= weightSum;
            frequency /= weightSum;

            OVRInput.SetControllerVibration(Mathf.Clamp01(frequency / 320f), Mathf.Clamp01(intensity), controller);
        }
    }

    public override void UpdateState(InputState state)
    {
        var vr = state.vr;

        vr.dashboardOpen = !OVRPlugin.hasInputFocus;

        InputTracking.GetNodeStates(nodeStates);

        foreach (var node in nodeStates)
        {
            if (node.nodeType == XRNode.CenterEye)
            {
                head.isTracking = node.tracked;

                if (node.TryGetPosition(out Vector3 position))
                    head.position = position.ToRender();

                if (node.TryGetRotation(out Quaternion rotation))
                    head.rotation = rotation.ToRender();
            }
        }

        nodeStates.Clear();

        vr.userPresentInHeadset = OVRPlugin.userPresent;

        if (localAvatar == null)
            localAvatar = FindChildRecursively(transform, "LocalAvatar");

        EnsureInitializedHand(LEFT_HAND_ROOT, ref leftHandReference, ref leftHandFingerRoots);
        EnsureInitializedHand(RIGHT_HAND_ROOT, ref rightHandReference, ref rightHandFingerRoots);

        leftTouch.isDeviceActive = true;
        rightTouch.isDeviceActive = true;

        leftTouch.isTracking = OVRInput.GetControllerPositionTracked(OVRInput.Controller.LTouch);
        rightTouch.isTracking = OVRInput.GetControllerPositionTracked(OVRInput.Controller.RTouch);

        leftHand.isTracking = leftTouch.isTracking && leftHandReference != null;
        rightHand.isTracking = rightTouch.isTracking && rightHandReference != null;

        leftHand.isDeviceActive = leftHand.isTracking;
        rightHand.isDeviceActive = rightHand.isTracking;

        if (leftTouch.isTracking)
        {
            leftTouch.position = OVRInput.GetLocalControllerPosition(OVRInput.Controller.LTouch).ToRender();
            leftTouch.rotation = OVRInput.GetLocalControllerRotation(OVRInput.Controller.LTouch).ToRender();
        }

        if (rightTouch.isTracking)
        {
            rightTouch.position = OVRInput.GetLocalControllerPosition(OVRInput.Controller.RTouch).ToRender();
            rightTouch.rotation = OVRInput.GetLocalControllerRotation(OVRInput.Controller.RTouch).ToRender();
        }

        //Debug.Log($"LeftHand.IsTracking: {LeftHand.IsTracking}, Reference: {leftHandReference}");

        if (!UpdateHandFromSkeleton(leftHand) && leftHand.isTracking)
            UpdateHandFromAvatar(leftHand, leftHandReference, leftHandFingerRoots);

        if (!UpdateHandFromSkeleton(rightHand) && rightHand.isTracking)
            UpdateHandFromAvatar(rightHand, rightHandReference, rightHandFingerRoots);

        //LeftHand.RawPosition = leftTouch.RawPosition;
        //LeftHand.RawRotation = leftTouch.RawRotation;

        //RightHand.RawPosition = rightTouch.RawPosition;
        //RightHand.RawRotation = rightTouch.RawRotation;

        // Trigger

        leftTouch.trigger = OVRInput.Get(OVRInput.RawAxis1D.LIndexTrigger);
        rightTouch.trigger = OVRInput.Get(OVRInput.RawAxis1D.RIndexTrigger);

        leftTouch.triggerClick = OVRInput.Get(OVRInput.RawButton.LIndexTrigger);
        rightTouch.triggerClick = OVRInput.Get(OVRInput.RawButton.RIndexTrigger);

        leftTouch.triggerTouch = OVRInput.Get(OVRInput.RawTouch.LIndexTrigger);
        rightTouch.triggerTouch = OVRInput.Get(OVRInput.RawTouch.RIndexTrigger);

        // Joystick

        leftTouch.joystickRaw = OVRInput.Get(OVRInput.RawAxis2D.LThumbstick).ToRender();
        rightTouch.joystickRaw = OVRInput.Get(OVRInput.RawAxis2D.RThumbstick).ToRender();

        leftTouch.joystickClick = OVRInput.Get(OVRInput.RawButton.LThumbstick);
        rightTouch.joystickClick = OVRInput.Get(OVRInput.RawButton.RThumbstick);

        leftTouch.joystickTouch = OVRInput.Get(OVRInput.RawTouch.LThumbstick);
        rightTouch.joystickTouch = OVRInput.Get(OVRInput.RawTouch.RThumbstick);

        // Grip

        leftTouch.grip = OVRInput.Get(OVRInput.RawAxis1D.LHandTrigger);
        rightTouch.grip = OVRInput.Get(OVRInput.RawAxis1D.RHandTrigger);


        leftTouch.gripClick = OVRInput.Get(OVRInput.RawButton.LHandTrigger);
        rightTouch.gripClick = OVRInput.Get(OVRInput.RawButton.RHandTrigger);

        // Buttons

        leftTouch.buttonXA = OVRInput.Get(OVRInput.RawButton.X);
        rightTouch.buttonXA = OVRInput.Get(OVRInput.RawButton.A);

        leftTouch.buttonYB = OVRInput.Get(OVRInput.RawButton.Y);
        rightTouch.buttonYB = OVRInput.Get(OVRInput.RawButton.B);

        leftTouch.buttonXA_touch = OVRInput.Get(OVRInput.RawTouch.X);
        rightTouch.buttonXA_touch = OVRInput.Get(OVRInput.RawTouch.A);

        leftTouch.buttonYB_touch = OVRInput.Get(OVRInput.RawTouch.Y);
        rightTouch.buttonYB_touch = OVRInput.Get(OVRInput.RawTouch.B);

        // Thumbrest
        leftTouch.thumbrestTouch = OVRInput.Get(OVRInput.RawTouch.LThumbRest);
        rightTouch.thumbrestTouch = OVRInput.Get(OVRInput.RawTouch.RThumbRest);

        // Start button
        leftTouch.start = OVRInput.Get(OVRInput.RawButton.Start);

        leftTouch.batteryLevel = OVRInput.GetControllerBatteryPercentRemaining(OVRInput.Controller.LTouch) * 0.01f;
        rightTouch.batteryLevel = OVRInput.GetControllerBatteryPercentRemaining(OVRInput.Controller.RTouch) * 0.01f;
    }

    public void HandleOutputState(OutputState state)
    {
        var leftData = state.vr?.leftController?.hapticState ?? default;
        var rightData = state.vr?.rightController?.hapticState ?? default;

        var leftVibrateTime = state.vr?.leftController?.vibrateTime ?? default;
        var rightVibrateTime = state.vr?.rightController?.vibrateTime ?? default;

        var maxPain = Mathf.Max(leftData.pain, rightData.pain);

        var dt = Time.deltaTime;

        painPhi += Mathf.PI * 2 * dt * Mathf.Lerp(80f / 60f, 140f / 60f, maxPain);
        painPhi %= Mathf.PI * 2 * 2;

        UpdateVibration(dt, leftData, OVRInput.Controller.LTouch, ref _leftHapticData, ref leftVibrateRemaining, leftIntensity);
        UpdateVibration(dt, rightData, OVRInput.Controller.RTouch, ref _rightHapticData, ref rightVibrateRemaining, rightIntensity);
    }
}
