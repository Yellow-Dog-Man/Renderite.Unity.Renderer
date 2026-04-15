//using System.Collections;
//using System.Collections.Generic;
//using UnityEngine;
//using Elements.Core;
//using FrooxEngine;

//public class StaticCameraDriver : MonoBehaviour, IInputDriver
//{
//    public UnityEngine.Camera[] cameras;
//    public StaticCameraType CameraType;

//    public int UpdateOrder { get { return 0; } }

//    StaticCamera staticCamera;

//    public void CollectDeviceInfos(DataTreeList list)
//    {
//        var dict = new DataTreeDictionary();

//        dict.Add("Name", "StaticCamera");
//        dict.Add("Type", "Camera");

//        list.Add(dict);
//    }

//    public void RegisterInputs(InputInterface inputInterface)
//    {
//        staticCamera = inputInterface.CreateDevice<StaticCamera>(CameraType.ToString());
//        staticCamera.SetCameraType(CameraType);
//        staticCamera.UpdateAspectRatio(1f);
//        staticCamera.FieldOfView = 60f;
//    }

//    public void UpdateInputs(float deltaTime)
//    {
//        for (int i = 0; i < cameras.Length; i++)
//            cameras[i].fieldOfView = staticCamera.FieldOfView;

//        if(cameras.Length > 0)
//            staticCamera.UpdateAspectRatio(cameras[0].aspect);
//    }
//}
