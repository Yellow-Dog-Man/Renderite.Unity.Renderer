using Renderite.Shared;
using Renderite.Unity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using ViveHandTracking;

public class ViveHandTrackingDriver : InputDriver, IOutputDriver
{
    public bool TrackingEnabled { get; private set; }

    bool providerAdded;
    bool registered;

    public override void UpdateState(InputState state)
    {
        if (!registered)
        {
            if (!TrackingEnabled)
                return;

            if (!providerAdded)
            {
                Debug.Log($"Initializing Vive Finger Tracking.");

                gameObject.AddComponent<GestureProvider>();
                providerAdded = true;
            }

            if (GestureProvider.Status != GestureStatus.Running)
                return;

            Debug.Log($"Registering Vive Finger Tracking. Mode: {GestureProvider.Mode}");

            registered = true;
        }

        if (!registered)
            return;

        if (state.vr.viveHandTracking == null)
            state.vr.viveHandTracking = new ViveHandTrackingInputState();

        var tracking = state.vr.viveHandTracking;

        tracking.isTracking = GestureProvider.Status == GestureStatus.Running && TrackingEnabled;

        if(tracking.isTracking)
        {
            UpdateHand(ref tracking.left, GestureProvider.LeftHand);
            UpdateHand(ref tracking.right, GestureProvider.RightHand);
        }
    }

    void UpdateHand(ref ViveHandState state, GestureResult viveHand)
    {
        if (viveHand == null)
        {
            state = null;
            return;
        }

        if (state == null)
            state = new ViveHandState();

        state.confidence = viveHand.confidence;
        state.pinchStrength = viveHand.pinch.pinchLevel;

        state.position = viveHand.position.ToRender();
        state.rotation = viveHand.rotation.ToRender();

        if (state.points == null)
            state.points = new List<RenderVector3>();

        for(int i = 0; i < viveHand.points.Length; i++)
        {
            if (i == state.points.Count)
                state.points.Add(viveHand.points[i].ToRender());
            else
                state.points[i] = viveHand.points[i].ToRender();
        }
    }

    public void HandleOutputState(OutputState state)
    {
        TrackingEnabled = state.vr?.useViveHandTracking ?? false;
    }
}
