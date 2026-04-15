using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PreRenderHeadTracker : MonoBehaviour
{
    public System.Action<Vector3, Quaternion> OnNewPose;

    private void OnPreRender()
    {
        OnNewPose?.Invoke(transform.localPosition, transform.localRotation);
    }
}
