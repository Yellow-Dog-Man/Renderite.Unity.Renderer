using Renderite.Shared;
using Renderite.Unity;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

public class GamepadDriver : InputDriver
{
    public override void UpdateState(InputState state)
    {
        var unityGamepad = UnityEngine.InputSystem.Gamepad.current;

        if (unityGamepad == null)
            return;

        if(state.gamepads == null)
            state.gamepads = new List<GamepadState>();

        var gamepad = state.gamepads.FirstOrDefault(g => g.displayName == unityGamepad.displayName);

        if (gamepad == null)
        {
            gamepad = new GamepadState();
            state.gamepads.Add(gamepad);
        }

        gamepad.displayName = unityGamepad.displayName;

        gamepad.a = unityGamepad.aButton.isPressed;
        gamepad.b = unityGamepad.bButton.isPressed;
        gamepad.x = unityGamepad.xButton.isPressed;
        gamepad.y = unityGamepad.yButton.isPressed;

        gamepad.leftBumper = unityGamepad.leftShoulder.isPressed;
        gamepad.rightBumper = unityGamepad.rightShoulder.isPressed;

        gamepad.leftThumbstick = unityGamepad.leftStick.ReadValue().ToRender();
        gamepad.leftThumbstickClick = unityGamepad.leftStickButton.isPressed;

        gamepad.rightThumbstick = unityGamepad.rightStick.ReadValue().ToRender();
        gamepad.rightThumbstickClick = unityGamepad.rightStickButton.isPressed;

        gamepad.leftTrigger = unityGamepad.leftTrigger.ReadValue();
        gamepad.rightTrigger = unityGamepad.rightTrigger.ReadValue();

        gamepad.dPadUp = unityGamepad.dpad.up.isPressed;
        gamepad.dPadRight = unityGamepad.dpad.right.isPressed;
        gamepad.dPadDown = unityGamepad.dpad.down.isPressed;
        gamepad.dPadLeft = unityGamepad.dpad.left.isPressed;

        gamepad.menu = unityGamepad.selectButton.isPressed;
        gamepad.start = unityGamepad.startButton.isPressed;
    }
}
