using Renderite.Shared;
using Renderite.Unity;
using System;
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;

public class MouseDriver : MouseInput
{
    public static bool BlockMouseButtons;

#if UNITY_STANDALONE_WIN
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetCursorPos(int x, int y);
#endif

    bool _lastMouseLocked;
    Vector2Int? _lastLockPosition;

    Vector2 WarpCursor(Vector2 pos) => new Vector2(pos.x, Screen.height - pos.y);

    protected override void UpdateState(MouseState state)
    {
        var currentMouse = UnityEngine.InputSystem.Mouse.current;

        if (currentMouse == null)
        {
            state.isActive = false;
            return;
        }

        state.isActive = true;

        var blockMovement = UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches.Count > 0;
        var blockButtons = BlockMouseButtons || blockMovement;

        state.leftButtonState = currentMouse.leftButton.isPressed && !blockButtons;
        state.rightButtonState = currentMouse.rightButton.isPressed && !blockButtons;
        state.middleButtonState = currentMouse.middleButton.isPressed && !blockButtons;
        state.button4State = currentMouse.backButton.isPressed && !blockButtons;
        state.button5State = currentMouse.forwardButton.isPressed && !blockButtons;

        state.directDelta = blockMovement ? default : currentMouse.delta.ReadValue().ToRender();

        var scroll = currentMouse.scroll.ReadValue();

#if UNITY_STANDALONE_LINUX
        scroll *= 100;
#endif

        state.scrollWheelDelta = scroll.ToRender();

        var position = currentMouse.position.ReadValue();
        position = WarpCursor(position);
        state.windowPosition = position.ToRender();

#if UNITY_STANDALONE_WIN
        GetCursorPos(out POINT point);
        state.desktopPosition = new RenderVector2(point.X, point.Y);
#endif
    }

    public override void HandleStateUpdate(OutputState state)
    {
        var currentMouse = UnityEngine.InputSystem.Mouse.current;

        if (currentMouse == null)
            return;

        if (state.lockCursorPosition != null)
            currentMouse.WarpCursorPosition(WarpCursor(state.lockCursorPosition.Value.ToUnity()));

        var lockPosition = state.lockCursorPosition?.ToUnity();

        if (state.lockCursor != _lastMouseLocked || lockPosition != _lastLockPosition)
        {
            var position = _lastLockPosition;

            _lastMouseLocked = state.lockCursor;
            _lastLockPosition = lockPosition;

            if (state.lockCursor)
            {
                if (_lastLockPosition != null)
                    Cursor.lockState = CursorLockMode.Confined;
                else
                    Cursor.lockState = CursorLockMode.Locked;

                Cursor.visible = false;
            }
            else
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = false;

                if (position != null)
                    currentMouse.WarpCursorPosition(WarpCursor(position.Value));
            }
        }
    }
}
