using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace ViveHandTracking {

// Enum for selecting computation backend.
public enum GestureBackend {
  Auto = 0,  // default backend, use GPU on PC and CPU on Android, Recommended
  CPU = 1,   // use CPU, not supported on PC
  GPU = 2,   // use GPU, supported on PC/Android
}

// Enum for detection mode. Larger mode return more info, but runs more slowly. If a mode is not
// supported on a device, will fallback to previous supported mode.
public enum GestureMode {
  Point2D = 0,   // Fastest mode, return one 2d point for hand, supported on all devices
  Point3D = 1,   // Return one 3d point for hand, supported on dual camera devices
  Skeleton = 2,  // Return skeleton (21 points) for hand, supported on all devices
}

[Serializable]
[StructLayout(LayoutKind.Sequential)]
public class GestureOption {
  public GestureBackend backend = GestureBackend.Auto;
  public GestureMode mode = GestureMode.Skeleton;
  [Range(15, 90)]
  [Tooltip(
      "Limit max fps of raw detection. This has negative impact on latency. Only use it when VR application fps slows down due to hand tracking.")]
  public int maxFPS = 90;  // limit max fps of detection
}

// Enum for predefined gesture classification
public enum GestureType {
  Unknown = 0,  // All other gestures not in predefined set
  Point = 1,
  Fist = 2,
  OK = 3,
  Like = 4,
  Five = 5,
  Victory = 6,
}

// Struct containing information of pinch
[StructLayout(LayoutKind.Sequential)]
public struct PinchInfo {
  // Returns pinch level of the hand, within [0, 1], higher means more possible to pinch.
  // If you only need a boolean value for pinch or not, you can use isPinching instead.
  public float pinchLevel;

  // Returns if currently pinching or not.
  // If you need a range value within [0, 1], you can use pinchLevel instead.
  public bool isPinching {
    get { return pinchLevel > 0.7f; }
  }

  // Returns start position of the pinch ray.
  public Vector3 pinchStart;

  // Returns direction of the pinch ray.
  public Vector3 pinchDirection;

  // Returns rotation of the pinch ray.
  // If only need a forward direction, you can use pinchDirection instead.
  public Quaternion pinchRotation {
    get { return Quaternion.FromToRotation(Vector3.forward, pinchDirection); }
  }
}

// Class containing detection result for one hand
[StructLayout(LayoutKind.Sequential)]
public class GestureResult {
  // Returns if this hand is left/right
  public bool isLeft { get; private set; }

  // Returns position of the hand joints. This field is guaranteed to be not null.
  // Meaning of this field is different based on actual GestureMode.
  // Point2D & Point3D: Only first point is used as the position of hand.
  // Skeleton: The points is a 21-sized array with all the keypoints of the hand.
  public Vector3[] points { get; private set; }

  // Returns rotation of the hand joints. This field is guaranteed to be not null.
  // Meaning of this field is different based on actual GestureMode.
  // Point2D & Point3D: Only first element is used as the rotation of hand.
  // Skeleton: A 21-sized array of rotation of all the keypoints.
  // Identity rotation (assume hand is five gesture): palm face front and fingers point upward.
  public Quaternion[] rotations { get; private set; }

  // Returns pre-defined gesture type.
  public GestureType gesture { get; private set; }

  // Returns confidence of the hand, within [0, 1].
  public float confidence { get; private set; }

  // Returns information of pinch (index and thumb) finger, includig pinch level and directions.
  public PinchInfo pinch { get; private set; }

  // Returns position of palm center, use this if only need hand position instead of 21 joints.
  public Vector3 position { get; private set; }

  // Returns rotation of palm center.
  public Quaternion rotation {
    get { return rotations[0]; }
  }

  internal GestureResult(GestureResultRaw raw) {
    SetRaw(raw);
    pinch = raw.pinch;
  }

  internal void Update(GestureResultRaw raw) {
    SetRaw(raw);
    // lerp pinch direction for stability
    var newPinch = raw.pinch;
    newPinch.pinchDirection =
        Vector3.Lerp(pinch.pinchDirection, newPinch.pinchDirection, 5f * Time.deltaTime);
    pinch = newPinch;
  }

  private void SetRaw(GestureResultRaw raw) {
    isLeft = raw.isLeft;
    gesture = raw.gesture;
    points = raw.points;
    rotations = raw.rotations;
    confidence = raw.confidence;
    position = raw.position;
  }
}

[StructLayout(LayoutKind.Sequential)]
internal class GestureResultRaw {
  [MarshalAs(UnmanagedType.I1)]
  internal bool isLeft;

  internal Vector3 position;

  [MarshalAs(UnmanagedType.ByValArray, SizeConst = 21)]
  internal Vector3[] points;

  [MarshalAs(UnmanagedType.ByValArray, SizeConst = 21)]
  internal Quaternion[] rotations;

  internal GestureType gesture;

  internal float confidence;

  internal PinchInfo pinch;
}

// Enum for possible errors in gesture detection
public enum GestureFailure {
  None = 0,        // No error occurs
  OpenCL = -1,     // (Only on Windows) OpenCL is not supported on the machine
  Camera = -2,     // Start camera failed
  Internal = -10,  // Internal errors
  CPUOnPC = -11,   // CPU backend is not supported on Windows
}
;

// Enum for possible status in gesture detection
public enum GestureStatus {
  NotStarted = 0,  // Detection is not started or stopped
  Starting = 1,    // Detection is started, but first result is not returned yet
  Running = 2,     // Detection is running and updates result regularly
  Error = 3,       // Detection failed to start, or error occured during detection
}

static class GestureInterface {
  private const string DLLPath = "aristo_interface";

  [DllImport(DLLPath)]
  internal static extern GestureFailure StartGestureDetection([In, Out] GestureOption option);

  [DllImport(DLLPath)]
  internal static extern void StopGestureDetection();

  [DllImport(DLLPath)]
  internal static extern int GetGestureResult(out IntPtr points, out int frameIndex);

  [DllImport(DLLPath)]
  internal static extern void UseExternalTransform([MarshalAs(UnmanagedType.I1)] bool value);

  [DllImport(DLLPath)]
  internal static extern void SetCameraTransform(Vector3 position, Quaternion rotation);
}

}
