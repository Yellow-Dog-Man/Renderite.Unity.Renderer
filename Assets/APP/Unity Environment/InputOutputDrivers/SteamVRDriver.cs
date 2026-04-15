#if !UNITY_WINRT
using Oculus.Platform;
using Renderite.Shared;
using Renderite.Unity;
using SharpDX.Direct3D11;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Valve.VR;
using static SteamVR_Utils;

struct HapticSimulationData
{
    public float tempPhi;
}

class SteamControllerData
{
    public Chirality Side { get; private set; }
    public int Index { get; set; }
    public string UniqueId { get; set; }
    public string RenderModel { get; set; }

    public HandState Hand;
    public VR_ControllerState Controller;
    public HapticSimulationData HapticData;

    public SteamControllerData(Chirality side, int index, string renderModel, string uniqueId)
    {
        this.Side = side;
        this.Index = index;
        this.RenderModel = renderModel;
        this.UniqueId = uniqueId;
    }

    public void ClearActiveStatus()
    {
        if (Controller != null)
        {
            Controller.isTracking = false;
            Controller.isDeviceActive = false;
        }

        if (Hand != null)
            Hand.isTracking = false;
    }
}

class TrackingReferenceData
{
    public int Index { get; private set; }
    public TrackingReferenceState State { get; private set; }

    public TrackingReferenceData(int index, TrackingReferenceState state)
    {
        this.Index = index;
        this.State = state;
    }
}


public class SteamVRDriver : InputDriver, IDriverHeadDevice, IOutputDriver
{
    public HeadOutputDevice Device { get; private set; }

    bool DisableSkeletalModel;

    InputManager inputManager;

    SteamControllerData LeftData;
    SteamControllerData RightData;

    float painPhi = 0f;

    HeadsetState head;

    Dictionary<int, TrackingReferenceData> trackingReferences = new Dictionary<int, TrackingReferenceData>();

    int headIndex = -1;

    public bool HasFingerTracking => LeftData?.Hand != null || RightData?.Hand != null;

    Dictionary<int, SteamControllerData> mappedData = new Dictionary<int, SteamControllerData>();
    Dictionary<int, VR_ControllerState> mappedControllers = new Dictionary<int, VR_ControllerState>();

    #region EVENTS

    public delegate bool TrackerConnectedHandler(int index, string serial, string deviceType);

    public TrackerConnectedHandler TrackerHandler;

    #endregion

    EDeviceActivityLevel _lastActivityLevel = EDeviceActivityLevel.k_EDeviceActivityLevel_Unknown;

    Dictionary<string, Tracker> trackers = new Dictionary<string, Tracker>();

    Queue<Action> actionQueue = new Queue<Action>();

    readonly struct InvalidRoleDevice
    {
        public readonly int index;
        public readonly string serial;

        public InvalidRoleDevice(int index, string serial)
        {
            this.index = index;
            this.serial = serial;
        }
    }

    List<InvalidRoleDevice> invalidRoleControllers = new List<InvalidRoleDevice>();

    Quaternion _touchRotationOffset = Quaternion.Euler(45, 0, 0);
    Vector3 _leftTouchOffset = new Vector3(-0.01f, 0.04f, 0.03f);
    Vector3 _rightTouchOffset = new Vector3(0.01f, 0.04f, 0.03f);

    static ETrackedControllerRole SteamRole(Chirality side) =>
        side == Chirality.Left ? ETrackedControllerRole.LeftHand : ETrackedControllerRole.RightHand;

    void Awake()
    {
        Device = HeadOutputDevice.SteamVR;
    }

    class Tracker
    {
        public TrackerState tracker;
        public int deviceIndex;
    }

    void OnDashboardActivated(VREvent_t vrEvent)
    {
        inputManager.State.vr.dashboardOpen = true;
    }

    void OnDashboardDeactivated(VREvent_t vrEvent)
    {
        inputManager.State.vr.dashboardOpen = false;
    }

    void TrackerConnected(int index, string serial, string deviceType)
    {
        lock (actionQueue)
            actionQueue.Enqueue(() =>
            {
                if (trackers.TryGetValue(serial, out Tracker existingTracker))
                {
                    existingTracker.deviceIndex = index;
                    existingTracker.tracker.isTracking = true;
                    return;
                }

                if (TrackerHandler == null || !TrackerHandler(index, serial, deviceType))
                {
                    Debug.Log($"Tracker connected: {index}, Serial: {serial}, DeviceType: {deviceType}");

                    var tracker = new TrackerState();
                    tracker.uniqueId = serial;
                    tracker.isTracking = false;

                    if (inputManager.State.vr.trackers == null)
                        inputManager.State.vr.trackers = new List<TrackerState>();

                    inputManager.State.vr.trackers.Add(tracker);

                    trackers.Add(serial, new Tracker()
                    {
                        tracker = tracker,
                        deviceIndex = index
                    });
                }
            });
    }

    string GetDeviceType(int index) => GetDeviceProperty(index, ETrackedDeviceProperty.Prop_ControllerType_String);

    string GetDeviceProperty(int index, ETrackedDeviceProperty property)
    {
        var error = ETrackedPropertyError.TrackedProp_Success;
        var result = new System.Text.StringBuilder((int)64);

        var capacity = OpenVR.System.GetStringTrackedDeviceProperty((uint)index, property, null, 0, ref error);

        if (capacity > 1)
        {
            result = new System.Text.StringBuilder((int)capacity);
            OpenVR.System.GetStringTrackedDeviceProperty((uint)index, property, result, capacity, ref error);
        }

        return result.ToString();
    }


    private void OnDeviceConnected(int index, bool connected)
    {
        if (!connected)
            return;

        var system = OpenVR.System;

        if (system == null)
            return;

        Debug.Log($"OnDeviceConnected: {index}");

        ETrackedPropertyError pError = default(ETrackedPropertyError);

        var deviceClass = system.GetTrackedDeviceClass((uint)index);

        Debug.Log($"DeviceClass: {deviceClass}, error: {pError}");

        if (deviceClass == ETrackedDeviceClass.GenericTracker)
        {
            var serial = GetSerialNumber(index);
            var deviceType = GetDeviceType(index);

            if (!string.IsNullOrWhiteSpace(serial))
                TrackerConnected(index, serial, deviceType);
        }

        if (deviceClass == ETrackedDeviceClass.Controller)
        {
            var neverTracked = system.GetBoolTrackedDeviceProperty((uint)index, ETrackedDeviceProperty.Prop_NeverTracked_Bool, ref pError);

            if (neverTracked)
            {
                Debug.Log("Controller is never tracked. Skipping...");
                return;
            }

            Debug.Log("Getting Role");

            var role = system.GetControllerRoleForTrackedDeviceIndex((uint)index);

            Debug.Log("Role: " + role);

            // schedule for later
            if (role == ETrackedControllerRole.Invalid)
            {
                // make sure it's not one of the already registered controllers
                if (mappedControllers.ContainsKey(index))
                    return;

                invalidRoleControllers.Add(new InvalidRoleDevice(index, null));
            }
            else
            {
                var error = ETrackedPropertyError.TrackedProp_Success;
                var capacity = system.GetStringTrackedDeviceProperty((uint)index, ETrackedDeviceProperty.Prop_RenderModelName_String, null, 0, ref error);
                if (capacity <= 1)
                {
                    Debug.LogError("Failed to get render model name for tracked object " + index);
                    return;
                }

                var buffer = new System.Text.StringBuilder((int)capacity);
                system.GetStringTrackedDeviceProperty((uint)index, ETrackedDeviceProperty.Prop_RenderModelName_String, buffer, capacity, ref error);

                Debug.Log("Result: " + error);

                var renderModel = buffer.ToString();

                capacity = system.GetStringTrackedDeviceProperty((uint)index, ETrackedDeviceProperty.Prop_SerialNumber_String, null, 0, ref error);
                if (capacity <= 1)
                {
                    Debug.LogError("Failed to get serial number for tracked object " + index);
                    return;
                }

                buffer = new System.Text.StringBuilder((int)capacity);
                system.GetStringTrackedDeviceProperty((uint)index, ETrackedDeviceProperty.Prop_SerialNumber_String, buffer, capacity, ref error);

                Debug.Log("Result: " + error);

                var serialNumber = buffer.ToString();

                Debug.Log("Controller Connected, Device Index: " + index + ", Role: " + role + ", RenderModel: " + renderModel + ", Serial: " + serialNumber);

                RegisterController(index, role, renderModel, serialNumber);
                Debug.Log("Controller Processed");
            }
        }

        if (deviceClass == ETrackedDeviceClass.TrackingReference)
        {
            if(!trackingReferences.ContainsKey(index))
            {
                // Must add a new base!
                var reference = new TrackingReferenceState();
                reference.uniqueId = $"Tracking Reference {trackingReferences.Count}";

                if (inputManager.State.vr.trackingReferences == null)
                    inputManager.State.vr.trackingReferences = new List<TrackingReferenceState>();

                inputManager.State.vr.trackingReferences.Add(reference);

                var data = new TrackingReferenceData(index, reference);

                trackingReferences.Add(index, data);
            }
        }

        if (deviceClass == ETrackedDeviceClass.HMD && headIndex < 0)
            headIndex = index;
    }

    void InitHandData(VR_ControllerState controller)
    {
        var isLeft = controller.side == Chirality.Left;

        switch (controller)
        {
            case IndexControllerState index:
                if (isLeft)
                {
                    SteamVR_Actions.Knuckles.left_hand.SetSkeletalTransformSpace(EVRSkeletalTransformSpace.Model);
                    SteamVR_Actions.Knuckles.left_hand.SetRangeOfMotion(EVRSkeletalMotionRange.WithoutController);
                }
                else
                {
                    SteamVR_Actions.Knuckles.right_hand.SetSkeletalTransformSpace(EVRSkeletalTransformSpace.Model);
                    SteamVR_Actions.Knuckles.right_hand.SetRangeOfMotion(EVRSkeletalMotionRange.WithoutController);
                }
                break;

            case ViveControllerState vive:
                controller.hasBoundHand = DisableSkeletalModel;

                if (isLeft)
                {
                    controller.handPosition = new Vector3(-0.02f, 0f, -0.16f).ToRender();
                    controller.handRotation = Quaternion.Euler(140f, -90f, -90f).ToRender();

                    SteamVR_Actions.Vive.left_hand.SetSkeletalTransformSpace(EVRSkeletalTransformSpace.Model);
                    SteamVR_Actions.Vive.left_hand.SetRangeOfMotion(EVRSkeletalMotionRange.WithoutController);
                }
                else
                {
                    controller.handPosition = new Vector3(0.02f, 0f, -0.16f).ToRender();
                    controller.handRotation = Quaternion.Euler(40f, -90f, -90f).ToRender();

                    SteamVR_Actions.Vive.right_hand.SetSkeletalTransformSpace(EVRSkeletalTransformSpace.Model);
                    SteamVR_Actions.Vive.right_hand.SetRangeOfMotion(EVRSkeletalMotionRange.WithoutController);
                }

                controller.handRotation = (controller.handRotation.ToUnity() * Quaternion.Inverse(Quaternion.Euler(90, 90, 90))).ToRender();
                break;

            case TouchControllerState touch:
                controller.hasBoundHand = DisableSkeletalModel;

                if (isLeft)
                {
                    controller.handPosition = new Vector3(-0.04f, -0.025f, -0.1f).ToRender();
                    controller.handRotation = Quaternion.Euler(185f, -95f, -90f).ToRender();

                    SteamVR_Actions.OculusTouch.left_hand.SetSkeletalTransformSpace(EVRSkeletalTransformSpace.Model);
                    SteamVR_Actions.OculusTouch.left_hand.SetRangeOfMotion(EVRSkeletalMotionRange.WithoutController);
                }
                else
                {
                    controller.handPosition = new Vector3(0.04f, -0.025f, -0.1f).ToRender();
                    controller.handRotation = Quaternion.Euler(5f, -95f, -90f).ToRender();

                    SteamVR_Actions.OculusTouch.right_hand.SetSkeletalTransformSpace(EVRSkeletalTransformSpace.Model);
                    SteamVR_Actions.OculusTouch.right_hand.SetRangeOfMotion(EVRSkeletalMotionRange.WithoutController);
                }

                controller.handRotation = (controller.handRotation.ToUnity() * Quaternion.Inverse(Quaternion.Euler(90, 90, 90))).ToRender();
                break;

            case CosmosControllerState cosmos:
                controller.hasBoundHand = DisableSkeletalModel;

                if (isLeft)
                {
                    SteamVR_Actions.Cosmos.left_hand.SetSkeletalTransformSpace(EVRSkeletalTransformSpace.Model);
                    SteamVR_Actions.Cosmos.left_hand.SetRangeOfMotion(EVRSkeletalMotionRange.WithoutController);
                }
                else
                {
                    SteamVR_Actions.Cosmos.right_hand.SetSkeletalTransformSpace(EVRSkeletalTransformSpace.Model);
                    SteamVR_Actions.Cosmos.right_hand.SetRangeOfMotion(EVRSkeletalMotionRange.WithoutController);
                }

                controller.handPosition = Vector3.zero.ToRender();
                controller.handRotation = Quaternion.identity.ToRender();
                break;

            case HP_ReverbControllerState hpReverb:
                controller.hasBoundHand = true;

                if (isLeft)
                {
                    controller.handPosition = new Vector3(-0.028f, 0.0f, -0.18f).ToRender();
                    controller.handRotation = Quaternion.Euler(30, 5, 100).ToRender();

                    SteamVR_Actions.HPReverb.left_hand.SetSkeletalTransformSpace(EVRSkeletalTransformSpace.Model);
                    SteamVR_Actions.HPReverb.left_hand.SetRangeOfMotion(EVRSkeletalMotionRange.WithoutController);
                }
                else
                {
                    controller.handPosition = new Vector3(0.028f, 0.00f, -0.18f).ToRender();
                    controller.handRotation = Quaternion.Euler(30, -5, -100).ToRender();

                    SteamVR_Actions.HPReverb.right_hand.SetSkeletalTransformSpace(EVRSkeletalTransformSpace.Model);
                    SteamVR_Actions.HPReverb.right_hand.SetRangeOfMotion(EVRSkeletalMotionRange.WithoutController);
                }
                break;

            case WindowsMR_ControllerState winMR:
                controller.hasBoundHand = true;

                if (isLeft)
                {
                    controller.handPosition = new Vector3(-0.028f, 0.0f, -0.18f).ToRender();
                    controller.handRotation = Quaternion.Euler(30, 5, 100).ToRender();

                    SteamVR_Actions.WindowsMR.left_hand.SetSkeletalTransformSpace(EVRSkeletalTransformSpace.Model);
                    SteamVR_Actions.WindowsMR.left_hand.SetRangeOfMotion(EVRSkeletalMotionRange.WithoutController);
                }
                else
                {
                    controller.handPosition = new Vector3(0.028f, 0.00f, -0.18f).ToRender();
                    controller.handRotation = Quaternion.Euler(30, -5, -100).ToRender();

                    SteamVR_Actions.WindowsMR.right_hand.SetSkeletalTransformSpace(EVRSkeletalTransformSpace.Model);
                    SteamVR_Actions.WindowsMR.right_hand.SetRangeOfMotion(EVRSkeletalMotionRange.WithoutController);
                }
                break;

            case PicoNeo2ControllerState picoNeo2:
                controller.hasBoundHand = true;

                if (isLeft)
                {
                    controller.handPosition = new Vector3(-0.028f, 0.0f, -0.18f).ToRender();
                    controller.handRotation = Quaternion.Euler(30, 5, 100).ToRender();

                    SteamVR_Actions.OculusTouch.left_hand.SetSkeletalTransformSpace(EVRSkeletalTransformSpace.Model);
                    SteamVR_Actions.OculusTouch.left_hand.SetRangeOfMotion(EVRSkeletalMotionRange.WithoutController);
                }
                else
                {
                    controller.handPosition = new Vector3(0.028f, 0.00f, -0.18f).ToRender();
                    controller.handRotation = Quaternion.Euler(30, -5, -100).ToRender();

                    SteamVR_Actions.OculusTouch.right_hand.SetSkeletalTransformSpace(EVRSkeletalTransformSpace.Model);
                    SteamVR_Actions.OculusTouch.right_hand.SetRangeOfMotion(EVRSkeletalMotionRange.WithoutController);
                }
                break;

            case GenericControllerState generic:
                controller.hasBoundHand = true;

                if (isLeft)
                {
                    controller.handPosition = new Vector3(-0.02f, 0f, -0.16f).ToRender();
                    controller.handRotation = Quaternion.Euler(140f, -90f, -90f).ToRender();

                    SteamVR_Actions.Generic.LeftHand.SetSkeletalTransformSpace(EVRSkeletalTransformSpace.Model);
                    SteamVR_Actions.Generic.LeftHand.SetRangeOfMotion(EVRSkeletalMotionRange.WithoutController);
                }
                else
                {
                    controller.handPosition = new Vector3(0.02f, 0f, -0.16f).ToRender();
                    controller.handRotation = Quaternion.Euler(40f, -90f, -90f).ToRender();

                    SteamVR_Actions.Generic.RightHand.SetSkeletalTransformSpace(EVRSkeletalTransformSpace.Model);
                    SteamVR_Actions.Generic.RightHand.SetRangeOfMotion(EVRSkeletalMotionRange.WithoutController);
                }

                controller.handRotation = (controller.handRotation.ToUnity() * Quaternion.Inverse(Quaternion.Euler(90, 90, 90))).ToRender();
                break;

            default:
                throw new NotImplementedException($"Hand binding not implement for controller of type: {controller.GetType()}");
        }
    }

    VR_ControllerState CreateController(string uniqueId, string renderModel,
        Chirality controllerSide, BodyNode bodyNode)
    {
        var isLeft = controllerSide == Chirality.Left;

        VR_ControllerState controller;

        if (renderModel.Contains("knuckles") || renderModel.Contains("indexcontroller"))
        {
            SteamVR_Actions.Knuckles.Activate(SteamVR_Input_Sources.Any);

            var index = new IndexControllerState();

            controller = index;
        }
        else if (renderModel.Contains("controller_vive"))
        {
            SteamVR_Actions.Vive.Activate(SteamVR_Input_Sources.Any);

            var vive = new ViveControllerState();

            controller = vive;
        }
        else if (renderModel.Contains("oculus") ||
            (renderModel.Contains("vrlink") && renderModel.Contains("shuttlecock")) // SteamLink sometimes does this
            )
        {
            SteamVR_Actions.OculusTouch.Activate(SteamVR_Input_Sources.Any);

            var touch = new TouchControllerState();

            if (renderModel.Contains("rifts"))
                touch.model = TouchControllerModel.QuestAndRiftS;
            else
                touch.model = TouchControllerModel.CV1;

            controller = touch;
        }
        else if (renderModel.Contains("cosmos"))
        {
            SteamVR_Actions.Cosmos.Activate(SteamVR_Input_Sources.Any);

            var cosmos = new CosmosControllerState();

            controller = cosmos;
        }
        else if (renderModel.Contains("1642_1118"))
        {
            SteamVR_Actions.HPReverb.Activate(SteamVR_Input_Sources.Any);

            var hpReverb = new HP_ReverbControllerState();

            controller = hpReverb;
        }
        else if (renderModel.Replace('/', '\\').ToLower().Contains("Microsoft\\Windows\\OpenVR".ToLower()))
        {
            SteamVR_Actions.WindowsMR.Activate(SteamVR_Input_Sources.Any);

            var winMR = new WindowsMR_ControllerState();

            controller = winMR;
        }
        else if (renderModel.Contains("pico_neo2"))
        {
            SteamVR_Actions.OculusTouch.Activate(SteamVR_Input_Sources.Any);

            var picoNeo2 = new PicoNeo2ControllerState();

            controller = picoNeo2;
        }
        else
        {
            Debug.LogWarning($"Unknown Controller: " + renderModel);

            var generic = new GenericControllerState();

            controller = generic;
        }

        controller.side = controllerSide;
        controller.bodyNode = bodyNode;

        // Register it with the VR inputs
        controller.deviceID = uniqueId;
        controller.deviceModel = renderModel;

        // Add it to the list of the input state
        if (inputManager.State.vr.controllers == null)
            inputManager.State.vr.controllers = new List<VR_ControllerState>();

        inputManager.State.vr.controllers.Add(controller);

        return controller;
    }

    void RegisterController(int index, ETrackedControllerRole role, string renderModel, string uniqueId)
    {
        var system = OpenVR.System;

        var targetSide = (role == ETrackedControllerRole.LeftHand) ? Chirality.Left : Chirality.Right;
        var bodyNode = BodyNode.LeftController.GetSide(targetSide);
        var isLeft = targetSide == Chirality.Left;

        bool swapLeftAndRight = false;

        if (isLeft && LeftData != null)
        {
            var newLeftRole = system.GetControllerRoleForTrackedDeviceIndex((uint)LeftData.Index);

            if (newLeftRole == ETrackedControllerRole.RightHand)
            {
                Debug.Log($"Left controller {LeftData.Index} is now right.");
                swapLeftAndRight = true;
            }
        }

        if (!isLeft && RightData != null)
        {
            var newRightRole = system.GetControllerRoleForTrackedDeviceIndex((uint)RightData.Index);

            if (newRightRole == ETrackedControllerRole.LeftHand)
            {
                Debug.Log($"Right controller {RightData.Index} is now left.");
                swapLeftAndRight = true;
            }
        }

        if (swapLeftAndRight)
        {
            Debug.Log($"Swapping left and right");

            if (LeftData == null || RightData == null)
            {
                // One of the sides is not registered yet. This means we need to register the displaced
                // controller as a new one for the other side (which will create structures for the other side)
                // and then we re-use the already generated structures for this controller
                var displacedData = LeftData ?? RightData;
                var displacedIndex = displacedData.Index;
                var displacedUniqueId = displacedData.UniqueId;
                var displacedRenderModel = displacedData.RenderModel;

                Debug.Log($"The other side does not exist, re-registering {displacedIndex} as other side and mapping {index} to current one");

                // We saved the data. We need to swap out the indexes first, otherwise the registration will think
                // that the displaced index is already registered

                // We want this to register a new controller for the other side, so we remove this one
                mappedControllers.Remove(displacedIndex);
                // We also want it to map new data
                mappedData.Remove(displacedIndex);

                // Re-use the existing data for the controller that's being registered right now
                displacedData.Index = index;
                displacedData.RenderModel = renderModel;
                displacedData.UniqueId = uniqueId;

                // Map it to the new index
                mappedControllers[index] = displacedData.Controller;
                mappedData[index] = displacedData;

                // Register the displaced controller as the other side
                RegisterController(displacedIndex, SteamRole(displacedData.Side.GetOther()), displacedRenderModel, displacedUniqueId);

                // We are done here. We don't need to register new data - that's been done of the other controller
                return;
            }
            else
            {
                Debug.Log($"Both sides are allocated, swapping left {LeftData.Index} & right {RightData.Index}");

                // Both controllers are already registered, so we just swap the sides for them
                var _leftIndex = LeftData.Index;
                var _rightIndex = RightData.Index;

                LeftData.Index = _rightIndex;
                RightData.Index = _leftIndex;

                mappedData[LeftData.Index] = LeftData;
                mappedData[RightData.Index] = RightData;

                mappedControllers[LeftData.Index] = LeftData.Controller;
                mappedControllers[RightData.Index] = RightData.Controller;

                return;
            }
        }

        VR_ControllerState controller = null;

        // Check if we already have an existing controller mapped to this index
        mappedControllers.TryGetValue(index, out controller);

        // If we don't have a controller for this one yet, register it now
        if (controller == null)
        {
            controller = CreateController(uniqueId, renderModel, targetSide, bodyNode);

            Debug.Log("Registering New Controller: " + controller);

            // Store the mapped controller in case it needs to be remapped later
            mappedControllers.Add(index, controller);
        }

        mappedData.TryGetValue(index, out var data);

        // We already have mapped data for this
        if (data != null)
        {
            Debug.Log($"Device {index} is already registered. Setting as active.");

            if (data.Side != targetSide)
            {
                Debug.Log($"Controller {index} changed from {data.Side} to {targetSide}. Remapping");

                // Invalidate the existing data. We want to keep this data for the other controller
                data.Index = -1;
                data.Controller = null;
                mappedData.Remove(index);

                // Re-run the registration. This will create new data for the correct side now
                RegisterController(index, role, renderModel, uniqueId);

                return;
            }

            // Just assign it as active one
            SetDataAsActive(data, index);
            return;
        }

        InitHandData(controller);

        HandState fingerHand = null;

        var hand = new HandState();
        hand.uniqueId = uniqueId;
        hand.chirality = isLeft ? Chirality.Left : Chirality.Right;
        hand.tracksMetacarpals = true;
        hand.isTracking = false;

        if (inputManager.State.vr.hands == null)
            inputManager.State.vr.hands = new List<HandState>();

        inputManager.State.vr.hands.Add(hand);

        fingerHand = hand;

        data = new SteamControllerData(targetSide, index, renderModel, uniqueId);

        data.Controller = controller;
        data.Hand = fingerHand;

        // Register the data
        mappedData.Add(index, data);

        SetDataAsActive(data, index);
    }

    void SetDataAsActive(SteamControllerData data, int index)
    {
        // Reset the active status to make sure it's re-enabled
        data.ClearActiveStatus();

        if (data.Side == Chirality.Left)
        {
            LeftData?.ClearActiveStatus();

            LeftData = data;
        }
        else
        {
            RightData?.ClearActiveStatus();

            RightData = data;
        }
    }

    float GetBatteryLevel(uint index)
    {
        ETrackedPropertyError err = new ETrackedPropertyError();

        float f = OpenVR.System.GetFloatTrackedDeviceProperty((uint)index, ETrackedDeviceProperty.Prop_DeviceBatteryPercentage_Float, ref err);

        if (err == ETrackedPropertyError.TrackedProp_Success)
            return f;
        else
            return -1;
    }

    bool GetIsCharging(uint index)
    {
        ETrackedPropertyError err = new ETrackedPropertyError();

        bool charging = OpenVR.System.GetBoolTrackedDeviceProperty((uint)index, ETrackedDeviceProperty.Prop_DeviceIsCharging_Bool, ref err);

        if (err == ETrackedPropertyError.TrackedProp_Success)
            return charging;
        else
            return false;
    }

    string GetSerialNumber(int index)
    {
        var system = OpenVR.System;
        var error = default(ETrackedPropertyError);

        var capacity = system.GetStringTrackedDeviceProperty((uint)index, ETrackedDeviceProperty.Prop_SerialNumber_String, null, 0, ref error);

        if (capacity <= 1)
        {
            Debug.LogError("Failed to get serial number for tracked object " + index);
            return null;
        }

        var buffer = new System.Text.StringBuilder((int)capacity);
        system.GetStringTrackedDeviceProperty((uint)index, ETrackedDeviceProperty.Prop_SerialNumber_String, buffer, capacity, ref error);

        Debug.Log($"SerialNumber Result for {index}: {error}");

        var serialNumber = buffer.ToString();

        return serialNumber;
    }

    private void OnNewPoses(TrackedDevicePose_t[] poses)
    {
        var system = OpenVR.System;

        if (system != null)
        {
            if (LeftData == null || RightData == null)
            {
                for (int i = invalidRoleControllers.Count - 1; i >= 0; i--)
                {
                    var invalidIndex = invalidRoleControllers[i].index;

                    var role = system.GetControllerRoleForTrackedDeviceIndex((uint)invalidIndex);

                    if (role != ETrackedControllerRole.Invalid)
                    {
                        invalidRoleControllers.RemoveAt(i);
                        OnDeviceConnected(invalidIndex, true);
                    }
                }
            }
            else if (invalidRoleControllers.Count > 0)
            {
                foreach (var c in invalidRoleControllers)
                {
                    Debug.Log($"Mapping invalid role controller as tracker. Index: {c.index}, Serial: {c.serial}");

                    var serial = c.serial;

                    if (string.IsNullOrWhiteSpace(serial))
                        serial = GetSerialNumber(c.index);

                    TrackerConnected(c.index, serial, null);
                }

                invalidRoleControllers.Clear();
            }
        }

        if (headIndex >= 0)
            UpdateTrackedPose(headIndex, poses, head);

        foreach (var reference in trackingReferences)
            UpdateTrackedPose(reference.Key, poses, reference.Value.State);

        foreach (var tracker in trackers)
            UpdateTrackedPose(tracker.Value.deviceIndex, poses, tracker.Value.tracker);
    }

    void UpdateTrackedPose(int index, TrackedDevicePose_t[] poses, ITrackedDevice trackedObject)
    {
        if (trackedObject == null)
            return;

        if (index >= poses.Length)
        {
            trackedObject.IsTracking = false;
            return;
        }

        if (poses[index].bDeviceIsConnected && poses[index].bPoseIsValid)
        {
            var pose = new SteamVR_Utils.RigidTransform(poses[index].mDeviceToAbsoluteTracking);

            trackedObject.Position = pose.pos.ToRender();
            trackedObject.Rotation = pose.rot.ToRender();

            trackedObject.IsTracking = true;
        }
        else
            trackedObject.IsTracking = false;
    }

    // SPLIT TODO!!!
    //public void CollectDeviceInfos(DataTreeList list)
    //{
    //    var dict = new DataTreeDictionary();

    //    switch (Device)
    //    {
    //        case HeadOutputDevice.SteamVR:
    //            dict.Add("Name", "SteamVR");
    //            break;

    //        case HeadOutputDevice.WindowsMR:
    //            dict.Add("Name", "Windows MR");
    //            break;

    //        case HeadOutputDevice.Oculus:
    //            dict.Add("Name", "Oculus");
    //            break;
    //    }

    //    dict.Add("Type", "HMD");
    //    dict.Add("Model", SteamVR.instance?.hmd_ModelNumber);

    //    list.Add(dict);

    //    if (HasFingerTracking)
    //    {
    //        dict = new DataTreeDictionary();

    //        dict.Add("Name", "SteamVR Skeletal Model");
    //        dict.Add("Type", "Finger Tracking");
    //        dict.Add("Model", "SteamVR");

    //        list.Add(dict);
    //    }
    //}

    public override void Initialize(InputManager manager)
    {
        base.Initialize(manager);

        this.inputManager = manager;

        manager.OnVR_ActiveChanged += Manager_OnVR_ActiveChanged;

        Debug.Log("SteamVR Driver: Registering Events");

        var connectedAction = SteamVR_Events.DeviceConnectedAction(OnDeviceConnected);
        var newPosesAction = SteamVR_Events.NewPosesAction(OnNewPoses);

        connectedAction.Enable(true);
        newPosesAction.Enable(true);

        Debug.Log("Initializing SteamVR");

        SteamVR.Initialize(true);

        ETrackedPropertyError err = default;

        var name = SteamVR.instance?.hmd_TrackingSystemName;
        var serial = SteamVR.instance?.hmd_SerialNumber;
        var modelNumber = SteamVR.instance?.hmd_ModelNumber;
        var type = SteamVR.instance?.hmd_Type;
        var manufacturer = SteamVR.instance?.GetStringProperty(ETrackedDeviceProperty.Prop_ManufacturerName_String);
        var wirelessDongle = SteamVR.instance?.GetStringProperty(ETrackedDeviceProperty.Prop_ConnectedWirelessDongle_String);
        var isWireless = OpenVR.System.GetBoolTrackedDeviceProperty(0, ETrackedDeviceProperty.Prop_DeviceIsCharging_Bool, ref err);

        Debug.Log("Tracking system name: " + name + ", serial number: " + serial + ", model number: " + modelNumber
            + $", Type: {type}, Manufacturer: {manufacturer}, WirelessDongle: {wirelessDongle}, IsWireless: {isWireless}, Using native support: " + SteamVR.usingNativeSupport);

        name = name?.ToLower();
        serial = serial?.ToLower();
        modelNumber = modelNumber?.ToLower();

        if (name != null)
        {
            if (name.Contains("holographic"))
                Device = HeadOutputDevice.WindowsMR;
            else if (name.Contains("rift"))
                Device = HeadOutputDevice.Oculus;

            var _lower = name.ToLower();

            if (_lower.Contains("aapvr") || _lower.Contains("alvr") || _lower.Contains("ivry"))
                DisableSkeletalModel = true;
        }

        if (modelNumber != null)
        {
            if (modelNumber.Contains("oculusquest"))
                DisableSkeletalModel = true;
        }

        if (serial != null)
        {
            if (serial.Contains("oculusquest"))
                DisableSkeletalModel = true;
        }

        if (!DisableSkeletalModel)
        {
            foreach (var arg in System.Environment.GetCommandLineArgs())
                if (arg.ToLower().EndsWith("legacysteamvrinput"))
                {
                    DisableSkeletalModel = true;
                    break;
                }
        }

        Debug.Log("SteamVR Driver: Registering Inputs");

        // Assign the head state
        head = new HeadsetState();
        inputManager.State.vr.headsetState = head;

        head.connectionType = isWireless ? HeadsetConnection.WirelessGeneral : HeadsetConnection.Wired;

        if (string.Equals(serial, "VRLINKHMDQUESTPRO", StringComparison.OrdinalIgnoreCase))
            head.connectionType = HeadsetConnection.WirelessSteamLink;

        head.headsetManufacturer = manufacturer;
        head.headsetModel = modelNumber;

        SteamVR.settings.legacyMixedRealityCamera = false;
        SteamVR.settings.mixedRealityActionSetAutoEnable = false;

        SteamVR.settings.inputUpdateMode = SteamVR_UpdateModes.OnUpdate;
        SteamVR.settings.poseUpdateMode = SteamVR_UpdateModes.OnUpdate;

        Debug.Log("SteamVR Driver: Initialization Finished");

        SteamVR_Events.System(EVREventType.VREvent_DashboardActivated).Listen(OnDashboardActivated);
        SteamVR_Events.System(EVREventType.VREvent_DashboardDeactivated).Listen(OnDashboardDeactivated);
    }

    private void Manager_OnVR_ActiveChanged(bool vrActive)
    {
        SteamVR.instance?.compositor.SuspendRendering(!vrActive);
    }

    public override void UpdateState(InputState state)
    {
        var vr = state.vr;

        lock (actionQueue)
            while (actionQueue.Count > 0)
                actionQueue.Dequeue()();

        if (headIndex >= 0)
        {
            vr.headsetState.batteryCharging = GetIsCharging((uint)headIndex);
            vr.headsetState.batteryLevel = GetBatteryLevel((uint)headIndex);
        }

        var activityLevel = OpenVR.System.GetTrackedDeviceActivityLevel(OpenVR.k_unTrackedDeviceIndex_Hmd);

        // Update if the user is present in the headset
        state.vr.userPresentInHeadset = activityLevel == EDeviceActivityLevel.k_EDeviceActivityLevel_UserInteraction;

        if (activityLevel != _lastActivityLevel)
        {
            Debug.Log($"Device Activity Level changed from {_lastActivityLevel} to {activityLevel}");
            _lastActivityLevel = activityLevel;
        }

        if (LeftData?.Controller != null)
            UpdateController(LeftData.Controller, SteamVR_Input_Sources.LeftHand);
        if (RightData?.Controller != null)
            UpdateController(RightData.Controller, SteamVR_Input_Sources.RightHand);

        foreach (var tracker in trackers)
            if (tracker.Value.tracker.isTracking)
            {
                var index = (uint)tracker.Value.deviceIndex;

                tracker.Value.tracker.batteryLevel = GetBatteryLevel(index);
                tracker.Value.tracker.batteryCharging = GetIsCharging(index);
            }
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

        if (LeftData != null)
        {
            UpdateHapticPoint(leftData, SteamVR_Input_Sources.LeftHand, ref LeftData.HapticData);

            if (leftVibrateTime > 0)
                VibrateController(Chirality.Left, leftVibrateTime);
        }

        if (RightData != null)
        {
            UpdateHapticPoint(rightData, SteamVR_Input_Sources.RightHand, ref RightData.HapticData);

            if (rightVibrateTime > 0)
                VibrateController(Chirality.Right, rightVibrateTime);
        }
    }

    void UpdateHapticPoint(HapticPointState point, SteamVR_Input_Sources sources, ref HapticSimulationData hapticData)
    {
        try
        {
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

            if (Mathf.Approximately(intensity, 0))
                return;

            intensity /= weightSum;
            frequency /= weightSum;

            SteamVR_Actions.Generic.Haptic.Execute(0, 0.05f, frequency, intensity, sources);
        }
        catch (Exception ex)
        {
            Debug.LogError("Exception vibrating Vive Controller:\n" + ex);
        }
    }

    void VibrateController(Chirality side, double time)
    {
        try
        {
            SteamVR_Actions.Generic.Haptic.Execute(0, (float)time, 20, 1, side == Chirality.Left
                ? SteamVR_Input_Sources.LeftHand : SteamVR_Input_Sources.RightHand);
        }
        catch (Exception ex)
        {
            Debug.LogError("Exception vibrating Vive Controller:\n" + ex);
        }
    }

    void UpdateController(VR_ControllerState controller, SteamVR_Input_Sources source)
    {
        HandState fingerHand;

        if (source == SteamVR_Input_Sources.LeftHand)
            fingerHand = LeftData.Hand;
        else
            fingerHand = RightData.Hand;

        // It's always active from the renderer end. The main process can filter this depending on whether VR is active or not
        controller.isDeviceActive = true;

        if (SteamVR_Actions.Generic.Pose.GetPoseIsValid(source))
        {
            controller.position = SteamVR_Actions.Generic.Pose.GetLocalPosition(source).ToRender();
            controller.rotation = SteamVR_Actions.Generic.Pose.GetLocalRotation(source).ToRender();

            if (controller is TouchControllerState)
            {
                var rotation = controller.rotation.ToUnity() * _touchRotationOffset;

                controller.rotation = rotation.ToRender();

                var positionOffset = rotation * (source == SteamVR_Input_Sources.LeftHand ? _leftTouchOffset : _rightTouchOffset);
                var position = controller.position.ToUnity();

                controller.position = (position - positionOffset).ToRender();
            }

            controller.isTracking = true;
        }
        else
            controller.isTracking = false;

        switch (controller)
        {
            case IndexControllerState index:
                UpdateController(index, fingerHand, source);
                break;

            case ViveControllerState vive:
                UpdateController(vive, fingerHand, source);
                break;

            case WindowsMR_ControllerState winMR:
                UpdateController(winMR, fingerHand, source);
                break;

            case TouchControllerState touch:
                UpdateController(touch, fingerHand, source);
                break;

            case HP_ReverbControllerState hp:
                UpdateController(hp, fingerHand, source);
                break;

            case CosmosControllerState cosmos:
                UpdateController(cosmos, fingerHand, source);
                break;

            case PicoNeo2ControllerState piconeo2:
                UpdateController(piconeo2, fingerHand, source);
                break;

            case GenericControllerState generic:
                UpdateController(generic, fingerHand, source);
                break;
        }

        if (controller.isTracking)
        {
            var index = SteamVR_Actions.Generic.Pose.GetDeviceIndex(source);

            controller.batteryLevel = GetBatteryLevel(index);
            controller.batteryCharging = GetIsCharging(index);
        }
    }

    void UpdateController(IndexControllerState controller, HandState hand, SteamVR_Input_Sources source)
    {
        var index = SteamVR_Actions.Knuckles;

        controller.grip = index.grip.GetAxis(source);
        controller.gripTouch = index.grip_touch.GetState(source);
        controller.gripClick = index.grip_click.GetState(source);

        controller.buttonA = index.button_A.GetState(source);
        controller.buttonB = index.button_B.GetState(source);

        controller.buttonAtouch = index.button_A_touch.GetState(source);
        controller.buttonBtouch = index.button_B_touch.GetState(source);

        controller.trigger = index.trigger.GetAxis(source);
        controller.triggerTouch = index.trigger_touch.GetState(source);
        controller.triggerClick = index.trigger_click.GetState(source);

        controller.joystickRaw = index.joystick.GetAxis(source).ToRender();
        controller.joystickTouch = index.joystick_touch.GetState(source);
        controller.joystickClick = index.joystick_click.GetState(source);

        controller.touchpad = index.touchpad.GetAxis(source).ToRender();
        controller.touchpadTouch = index.touchpad_touch.GetState(source);
        controller.touchpadPress = index.touchpad_press.GetState(source);
        controller.touchpadForce = index.touchpad_force.GetAxis(source);

        if (hand != null)
            UpdateHand(hand, controller, (hand.chirality == Chirality.Left) ? index.left_hand : index.right_hand);
    }

    void UpdateController(ViveControllerState controller, HandState hand, SteamVR_Input_Sources source)
    {
        var vive = SteamVR_Actions.Vive;

        controller.grip = vive.grip.GetState(source);
        controller.app = vive.app.GetState(source);

        controller.triggerHair = vive.trigger_hair.GetState(source);
        controller.triggerClick = vive.trigger_click.GetState(source);
        controller.trigger = vive.trigger.GetAxis(source);

        controller.touchpadTouch = vive.touchpad_touch.GetState(source);
        controller.touchpadClick = vive.touchpad_click.GetState(source);
        controller.touchpad = vive.touchpad.GetAxis(source).ToRender();

        if (hand != null)
            UpdateHand(hand, controller, (hand.chirality == Chirality.Left) ? vive.left_hand : vive.right_hand);
    }

    void UpdateController(WindowsMR_ControllerState controller, HandState hand, SteamVR_Input_Sources source)
    {
        var mr = SteamVR_Actions.WindowsMR;

        controller.grip = mr.grip.GetState(source);
        controller.app = mr.app.GetState(source);

        controller.triggerHair = mr.trigger_hair.GetState(source);
        controller.triggerClick = mr.trigger_click.GetState(source);
        controller.trigger = mr.trigger.GetAxis(source);

        controller.touchpadTouch = mr.touchpad_touch.GetState(source);
        controller.touchpadClick = mr.touchpad_click.GetState(source);
        controller.touchpad = mr.touchpad.GetAxis(source).ToRender();

        controller.joystickRaw = mr.joystick.GetAxis(source).ToRender();

        if (hand != null)
            UpdateHand(hand, controller, (hand.chirality == Chirality.Left) ? mr.left_hand : mr.right_hand);
    }

    void UpdateController(TouchControllerState controller, HandState hand, SteamVR_Input_Sources source)
    {
        var touch = SteamVR_Actions.OculusTouch;

        controller.start = touch.start.GetState(source);

        controller.buttonYB = touch.button_YB.GetState(source);
        controller.buttonXA = touch.button_XA.GetState(source);

        controller.buttonYB_touch = touch.button_YB_touch.GetState(source);
        controller.buttonXA_touch = touch.button_XA_touch.GetState(source);

        controller.thumbrestTouch = touch.thumbrest_touch.GetState(source);

        controller.grip = touch.grip.GetAxis(source);
        controller.gripClick = touch.grip_click.GetState(source);

        controller.joystickRaw = touch.joystick.GetAxis(source).ToRender();
        controller.joystickTouch = touch.joystick_touch.GetState(source);
        controller.joystickClick = touch.joystick_click.GetState(source);

        controller.trigger = touch.trigger.GetAxis(source);
        controller.triggerTouch = touch.trigger_touch.GetState(source);
        controller.triggerClick = touch.trigger_click.GetState(source);

        if (hand != null)
            UpdateHand(hand, controller, (hand.chirality == Chirality.Left) ? touch.left_hand : touch.right_hand);
    }

    void UpdateController(HP_ReverbControllerState controller, HandState hand, SteamVR_Input_Sources source)
    {
        var hp = SteamVR_Actions.HPReverb;

        controller.appMenu = hp.appmenu.GetState(source);

        controller.buttonYB = hp.button_YB.GetState(source);
        controller.buttonXA = hp.button_XA.GetState(source);

        controller.grip = hp.grip.GetAxis(source);
        controller.gripTouch = hp.grip_touch.GetState(source);
        controller.gripClick = hp.grip_click.GetState(source);

        controller.joystickRaw = hp.joystick.GetAxis(source).ToRender();
        controller.joystickClick = hp.joystick_click.GetState(source);

        controller.trigger = hp.trigger.GetAxis(source);
        controller.triggerClick = hp.trigger_click.GetState(source);

        if (hand != null)
            UpdateHand(hand, controller, (hand.chirality == Chirality.Left) ? hp.left_hand : hp.right_hand);
    }

    void UpdateController(PicoNeo2ControllerState controller, HandState hand, SteamVR_Input_Sources source)
    {
        // Pico Neo controllers seem to report themselves as Oculus Touch, so using this instead for no
        var piconeo = SteamVR_Actions.OculusTouch;

        controller.app = piconeo.start.GetState(source);

        controller.buttonYB = piconeo.button_YB.GetState(source);
        controller.buttonXA = piconeo.button_XA.GetState(source);

        controller.gripClick = piconeo.grip_click.GetState(source);

        controller.joystick = piconeo.joystick.GetAxis(source).ToRender();
        controller.joystickTouch = piconeo.joystick_touch.GetState(source);
        controller.joystickClick = piconeo.joystick_click.GetState(source);

        controller.trigger = piconeo.trigger.GetAxis(source);
        controller.triggerClick = piconeo.trigger_click.GetState(source);

        if (hand != null)
            UpdateHand(hand, controller, (hand.chirality == Chirality.Left) ? piconeo.left_hand : piconeo.right_hand);
    }

    void UpdateController(CosmosControllerState controller, HandState hand, SteamVR_Input_Sources source)
    {
        var cosmos = SteamVR_Actions.Cosmos;

        controller.vive = cosmos.menu.GetState(source);

        controller.buttonAX = cosmos.buttonAX.GetState(source);
        controller.buttonBY = cosmos.buttonBY.GetState(source);

        controller.bumper = cosmos.bumper.GetState(source);

        controller.gripClick = cosmos.grip_click.GetState(source);

        controller.joystickRaw = cosmos.joystick.GetAxis(source).ToRender();
        controller.joystickTouch = cosmos.joystick_touch.GetState(source);
        controller.joystickClick = cosmos.joystick_click.GetState(source);

        controller.trigger = cosmos.trigger.GetAxis(source);
        controller.triggerTouch = cosmos.trigger_touch.GetState(source);
        controller.triggerClick = cosmos.trigger_click.GetState(source);

        if (hand != null)
            UpdateHand(hand, controller, (hand.chirality == Chirality.Left) ? cosmos.left_hand : cosmos.right_hand);
    }

    void UpdateController(GenericControllerState controller, HandState hand, SteamVR_Input_Sources source)
    {
        var generic = SteamVR_Actions.Generic;

        controller.strength = generic.Strength.GetAxis(source);
        controller.axis = generic.Axis.GetAxis(source).ToRender();

        controller.touchingStrength = generic.TouchingStrength.GetState(source);
        controller.touchingAxis = generic.TouchingAxis.GetState(source);

        controller.primary = generic.ActionPrimary.GetState(source);
        controller.menu = generic.ActionMenu.GetState(source);
        controller.grab = generic.ActionGrab.GetState(source);
        controller.secondary = generic.ActionSecondary.GetState(source);

        if (hand != null)
            UpdateHand(hand, controller, (hand.chirality == Chirality.Left) ? generic.LeftHand : generic.RightHand);
    }

    Quaternion _axisCompensation = Quaternion.AngleAxis(180, Vector3.up);

    Quaternion _fingerCompensationRight = Quaternion.LookRotation(new Vector3(1, 0, 0), new Vector3(0, 1, 0));
    Quaternion _wristCompensationRight = Quaternion.LookRotation(new Vector3(0, 0, 1), new Vector3(1, 0, 0));

    Quaternion _fingerCompensationLeft = Quaternion.LookRotation(new Vector3(-1, 0, 0), new Vector3(0, -1, 0));
    Quaternion _wristCompensationLeft = Quaternion.LookRotation(new Vector3(0, 0, 1), new Vector3(-1, 0, 0));

    void UpdateHand(HandState hand, VR_ControllerState controller, SteamVR_Action_Skeleton skeleton)
    {
        hand.isDeviceActive = controller.isDeviceActive;

        if (!skeleton.activeBinding || !skeleton.poseIsValid)
        {
            hand.isTracking = false;
            return;
        }

        if (!controller.isTracking)
            return;

        hand.isTracking = true;

        hand.confidence = 1;

        var controllerPos = controller.position.ToUnity();
        var controllerRot = controller.rotation.ToUnity();

        if (controller is TouchControllerState)
        {
            var positionOffset = controllerRot * (hand.chirality == Chirality.Left ? _leftTouchOffset : _rightTouchOffset);

            controllerRot = controllerRot * Quaternion.Inverse(_touchRotationOffset);
            controllerPos += positionOffset;
        }

        controllerRot = controllerRot * _axisCompensation;

        Quaternion wristCompensation;
        Quaternion fingerCompensation;

        if (hand.chirality == Chirality.Right)
        {
            wristCompensation = _wristCompensationRight;
            fingerCompensation = _fingerCompensationRight;
        }
        else
        {
            wristCompensation = _wristCompensationLeft;
            fingerCompensation = _fingerCompensationLeft;
        }

        Vector3 wristPos = skeleton.bonePositions[SteamVR_Skeleton_JointIndexes.wrist];
        Quaternion wristRot = skeleton.boneRotations[SteamVR_Skeleton_JointIndexes.wrist] * wristCompensation;

        if (hand.segmentPositions == null)
            hand.segmentPositions = new List<RenderVector3>();

        if (hand.segmentRotations == null)
            hand.segmentRotations = new List<RenderQuaternion>();

        while (hand.segmentPositions.Count < FingerHelper.FINGER_SEGMENT_COUNT)
            hand.segmentPositions.Add(default);

        while (hand.segmentRotations.Count < FingerHelper.FINGER_SEGMENT_COUNT)
            hand.segmentRotations.Add(default);

        for (int i = 0; i < skeleton.GetBoneCount(); i++)
        {
            var joint = (SteamVR_Skeleton_JointIndexEnum)i;

            if (joint == SteamVR_Skeleton_JointIndexEnum.root)
                continue;

            var pos = skeleton.bonePositions[i];
            var rot = skeleton.boneRotations[i];

            if (joint == SteamVR_Skeleton_JointIndexEnum.wrist)
            {
                pos = (controllerRot * pos) + controllerPos;
                rot = controllerRot * rot;

                rot = rot * wristCompensation;

                hand.wristPosition = pos.ToRender();
                hand.wristRotation = rot.ToRender();
            }
            else
            {
                pos = Quaternion.Inverse(wristRot) * (pos - wristPos);
                rot = Quaternion.Inverse(wristRot) * rot;

                rot = rot * fingerCompensation;

                var node = GetFingerSegment(joint);

                if (node != BodyNode.NONE)
                {
                    var index = node.FlatSegmentIndex();

                    hand.segmentPositions[index] = pos.ToRender();
                    hand.segmentRotations[index] = rot.ToRender();
                }
            }
        }
    }

    static BodyNode GetFingerSegment(SteamVR_Skeleton_JointIndexEnum joint)
    {
        switch (joint)
        {
            case SteamVR_Skeleton_JointIndexEnum.indexMetacarpal: return BodyNode.LeftIndexFinger_Metacarpal;
            case SteamVR_Skeleton_JointIndexEnum.indexProximal: return BodyNode.LeftIndexFinger_Proximal;
            case SteamVR_Skeleton_JointIndexEnum.indexMiddle: return BodyNode.LeftIndexFinger_Intermediate;
            case SteamVR_Skeleton_JointIndexEnum.indexDistal: return BodyNode.LeftIndexFinger_Distal;
            case SteamVR_Skeleton_JointIndexEnum.indexTip: return BodyNode.LeftIndexFinger_Tip;

            case SteamVR_Skeleton_JointIndexEnum.middleMetacarpal: return BodyNode.LeftMiddleFinger_Metacarpal;
            case SteamVR_Skeleton_JointIndexEnum.middleProximal: return BodyNode.LeftMiddleFinger_Proximal;
            case SteamVR_Skeleton_JointIndexEnum.middleMiddle: return BodyNode.LeftMiddleFinger_Intermediate;
            case SteamVR_Skeleton_JointIndexEnum.middleDistal: return BodyNode.LeftMiddleFinger_Distal;
            case SteamVR_Skeleton_JointIndexEnum.middleTip: return BodyNode.LeftMiddleFinger_Tip;

            case SteamVR_Skeleton_JointIndexEnum.ringMetacarpal: return BodyNode.LeftRingFinger_Metacarpal;
            case SteamVR_Skeleton_JointIndexEnum.ringProximal: return BodyNode.LeftRingFinger_Proximal;
            case SteamVR_Skeleton_JointIndexEnum.ringMiddle: return BodyNode.LeftRingFinger_Intermediate;
            case SteamVR_Skeleton_JointIndexEnum.ringDistal: return BodyNode.LeftRingFinger_Distal;
            case SteamVR_Skeleton_JointIndexEnum.ringTip: return BodyNode.LeftRingFinger_Tip;

            case SteamVR_Skeleton_JointIndexEnum.pinkyMetacarpal: return BodyNode.LeftPinky_Metacarpal;
            case SteamVR_Skeleton_JointIndexEnum.pinkyProximal: return BodyNode.LeftPinky_Proximal;
            case SteamVR_Skeleton_JointIndexEnum.pinkyMiddle: return BodyNode.LeftPinky_Intermediate;
            case SteamVR_Skeleton_JointIndexEnum.pinkyDistal: return BodyNode.LeftPinky_Distal;
            case SteamVR_Skeleton_JointIndexEnum.pinkyTip: return BodyNode.LeftPinky_Tip;

            case SteamVR_Skeleton_JointIndexEnum.thumbMetacarpal: return BodyNode.LeftThumb_Metacarpal;
            case SteamVR_Skeleton_JointIndexEnum.thumbMiddle: return BodyNode.LeftThumb_Proximal;
            case SteamVR_Skeleton_JointIndexEnum.thumbDistal: return BodyNode.LeftThumb_Distal;
            case SteamVR_Skeleton_JointIndexEnum.thumbTip: return BodyNode.LeftThumb_Tip;

            default: return BodyNode.NONE;
        }
    }
}
#endif