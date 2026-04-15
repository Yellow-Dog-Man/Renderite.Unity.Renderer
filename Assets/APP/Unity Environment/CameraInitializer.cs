using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;
using Renderite.Unity;

public class CameraInitializer : Renderite.Unity.CameraInitializer
{
    public override void RemovePostProcessing(Camera camera)
    {
        var manager = camera.gameObject.GetComponent<CameraPostprocessingManager>();
        manager.RemovePostProcessing();

        Destroy(manager);
    }

    public override void SetupPostprocessing(Camera camera, CameraSettings settings)
    {
        var manager = camera.gameObject.GetComponent<CameraPostprocessingManager>();

        if (settings.SetupPostProcessing)
        {
            if (manager == null)
            {
                manager = camera.gameObject.AddComponent<CameraPostprocessingManager>();
                manager.Initialize(camera, settings);
            }
            else
                manager.UpdatePostProcessing(settings.SetupPostProcessing, settings.MotionBlur, settings.ScreenSpaceReflection);
        }
        else if (manager != null)
            Destroy(manager);
            
    }
}
