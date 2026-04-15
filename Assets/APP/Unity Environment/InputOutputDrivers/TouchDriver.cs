using Renderite.Shared;
using Renderite.Unity;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

public class TouchDriver : InputDriver
{
    void Awake()
    {
        EnhancedTouchSupport.Enable();
    }

    public override void UpdateState(InputState state)
    {
        if (state.touches == null)
            state.touches = new List<TouchState>();

        var activeTouches = Touch.activeTouches;

        while(activeTouches.Count > state.touches.Count)
            state.touches.Add(PackerMemoryPool.Instance.Borrow<TouchState>());

        while(activeTouches.Count < state.touches.Count)
        {
            PackerMemoryPool.Instance.Return(state.touches[state.touches.Count - 1]);
            state.touches.RemoveAt(state.touches.Count - 1);
        }

        for(int i = 0; i < activeTouches.Count; i++)
        {
            var touch = activeTouches[i];
            var touchState = state.touches[i];

            touchState.touchId = touch.touchId;
            touchState.position = touch.screenPosition.ToRender();

            touchState.isPressing = touch.phase == UnityEngine.InputSystem.TouchPhase.Began || 
                touch.phase == UnityEngine.InputSystem.TouchPhase.Moved ||
                touch.phase == UnityEngine.InputSystem.TouchPhase.Stationary;

            touchState.pressure = touch.pressure;
        }
    }
}
