//using System.Collections;
//using System.Collections.Generic;
//using FrooxEngine;
//using Elements.Core;
//using UnityEngine;
//using Valve.VR;

//public class TrackerPoser : MonoBehaviour
//{
//    public int Index = -1;

//    public float MotionSmoothingSpeed = 0.75f;

//    void Start()
//    {
//        var newPosesAction = SteamVR_Events.NewPosesAction(OnNewPoses);

//        newPosesAction.Enable(true);
//    }

//    void Update()
//    {
//    }

//    private void OnNewPoses(TrackedDevicePose_t[] poses)
//    {
//        var system = OpenVR.System;

//        if (system != null)
//        {
//            if (Index >= 0 && Index < poses.Length)
//            {
//                if (poses[Index].bPoseIsValid)
//                {
//                    var pose = new SteamVR_Utils.RigidTransform(poses[Index].mDeviceToAbsoluteTracking);

//                    transform.localPosition = Vector3.Lerp(transform.localPosition, pose.pos, MotionSmoothingSpeed);
//                    transform.localRotation = Quaternion.Slerp(transform.localRotation, pose.rot, MotionSmoothingSpeed);
//                }
//            }
//        }
//    }
//}
