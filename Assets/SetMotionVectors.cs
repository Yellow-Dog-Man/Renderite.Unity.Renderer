using UnityEngine;
using System.Collections;

public class SetMotionVectors : MonoBehaviour
{
    void Start()
    {
        var cam = this.GetComponent<Camera>();

        cam.depthTextureMode |= DepthTextureMode.MotionVectors;
    }
}
