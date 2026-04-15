using UnityEngine;
using System.Collections;
using System;
using Renderite.Unity;
using Renderite.Shared;
using System.Text;
using EnumsNET;
using System.Collections.Generic;

public class KeyboardDriver : KeyboardInput
{
    StringBuilder typeDelta = new StringBuilder();

    IReadOnlyList<Key> _keys;

    bool _lastKeyboardActive;

    void Start()
    {
        UnityEngine.InputSystem.Keyboard.current.onTextInput += Current_onTextInput;

        _keys = Enums.GetValues<Key>();
    }

    void Current_onTextInput(char obj)
    {
        typeDelta.Append(obj);
    }

    bool GetKeyState(Key key)
    {
        // the keycodes are compatible for now
        var unity = ToUnity(key);

        if (unity == UnityEngine.InputSystem.Key.None)
            return false;

        return UnityEngine.InputSystem.Keyboard.current[unity].isPressed;
    }

    UnityEngine.InputSystem.Key ToUnity(Key key)
    {
        switch (key)
        {
            case Key.Space: return UnityEngine.InputSystem.Key.Space;
            case Key.Return: return UnityEngine.InputSystem.Key.Enter;
            case Key.Tab: return UnityEngine.InputSystem.Key.Tab;
            case Key.BackQuote: return UnityEngine.InputSystem.Key.Backquote;
            case Key.Quote: return UnityEngine.InputSystem.Key.Quote;
            case Key.Semicolon: return UnityEngine.InputSystem.Key.Semicolon;
            case Key.Comma: return UnityEngine.InputSystem.Key.Comma;
            case Key.Period: return UnityEngine.InputSystem.Key.Period;
            case Key.Slash: return UnityEngine.InputSystem.Key.Slash;
            case Key.Backslash: return UnityEngine.InputSystem.Key.Backslash;
            case Key.LeftBracket: return UnityEngine.InputSystem.Key.LeftBracket;
            case Key.RightBracket: return UnityEngine.InputSystem.Key.RightBracket;
            case Key.Minus: return UnityEngine.InputSystem.Key.Minus;
            case Key.Equals: return UnityEngine.InputSystem.Key.Equals;

            case Key.A: return UnityEngine.InputSystem.Key.A;
            case Key.B: return UnityEngine.InputSystem.Key.B;
            case Key.C: return UnityEngine.InputSystem.Key.C;
            case Key.D: return UnityEngine.InputSystem.Key.D;
            case Key.E: return UnityEngine.InputSystem.Key.E;
            case Key.F: return UnityEngine.InputSystem.Key.F;
            case Key.G: return UnityEngine.InputSystem.Key.G;
            case Key.H: return UnityEngine.InputSystem.Key.H;
            case Key.I: return UnityEngine.InputSystem.Key.I;
            case Key.J: return UnityEngine.InputSystem.Key.J;
            case Key.K: return UnityEngine.InputSystem.Key.K;
            case Key.L: return UnityEngine.InputSystem.Key.L;
            case Key.M: return UnityEngine.InputSystem.Key.M;
            case Key.N: return UnityEngine.InputSystem.Key.N;
            case Key.O: return UnityEngine.InputSystem.Key.O;
            case Key.P: return UnityEngine.InputSystem.Key.P;
            case Key.Q: return UnityEngine.InputSystem.Key.Q;
            case Key.R: return UnityEngine.InputSystem.Key.R;
            case Key.S: return UnityEngine.InputSystem.Key.S;
            case Key.T: return UnityEngine.InputSystem.Key.T;
            case Key.U: return UnityEngine.InputSystem.Key.U;
            case Key.V: return UnityEngine.InputSystem.Key.V;
            case Key.W: return UnityEngine.InputSystem.Key.W;
            case Key.X: return UnityEngine.InputSystem.Key.X;
            case Key.Y: return UnityEngine.InputSystem.Key.Y;
            case Key.Z: return UnityEngine.InputSystem.Key.Z;

            case Key.Alpha0: return UnityEngine.InputSystem.Key.Digit0;
            case Key.Alpha1: return UnityEngine.InputSystem.Key.Digit1;
            case Key.Alpha2: return UnityEngine.InputSystem.Key.Digit2;
            case Key.Alpha3: return UnityEngine.InputSystem.Key.Digit3;
            case Key.Alpha4: return UnityEngine.InputSystem.Key.Digit4;
            case Key.Alpha5: return UnityEngine.InputSystem.Key.Digit5;
            case Key.Alpha6: return UnityEngine.InputSystem.Key.Digit6;
            case Key.Alpha7: return UnityEngine.InputSystem.Key.Digit7;
            case Key.Alpha8: return UnityEngine.InputSystem.Key.Digit8;
            case Key.Alpha9: return UnityEngine.InputSystem.Key.Digit9;

            case Key.LeftShift: return UnityEngine.InputSystem.Key.LeftShift;
            case Key.RightShift: return UnityEngine.InputSystem.Key.RightShift;
            case Key.LeftAlt: return UnityEngine.InputSystem.Key.LeftAlt;
            case Key.RightAlt: return UnityEngine.InputSystem.Key.RightAlt;
            case Key.AltGr: return UnityEngine.InputSystem.Key.AltGr;
            case Key.LeftControl: return UnityEngine.InputSystem.Key.LeftCtrl;
            case Key.RightControl: return UnityEngine.InputSystem.Key.RightCtrl;
            case Key.LeftWindows: return UnityEngine.InputSystem.Key.LeftWindows;
            case Key.RightWindows: return UnityEngine.InputSystem.Key.RightWindows;

            case Key.Escape: return UnityEngine.InputSystem.Key.Escape;
            case Key.LeftArrow: return UnityEngine.InputSystem.Key.LeftArrow;
            case Key.RightArrow: return UnityEngine.InputSystem.Key.RightArrow;
            case Key.UpArrow: return UnityEngine.InputSystem.Key.UpArrow;
            case Key.DownArrow: return UnityEngine.InputSystem.Key.DownArrow;
            case Key.Backspace: return UnityEngine.InputSystem.Key.Backspace;
            case Key.PageDown: return UnityEngine.InputSystem.Key.PageDown;
            case Key.PageUp: return UnityEngine.InputSystem.Key.PageUp;
            case Key.Home: return UnityEngine.InputSystem.Key.Home;
            case Key.End: return UnityEngine.InputSystem.Key.End;
            case Key.Insert: return UnityEngine.InputSystem.Key.Insert;
            case Key.Delete: return UnityEngine.InputSystem.Key.Delete;
            case Key.CapsLock: return UnityEngine.InputSystem.Key.CapsLock;
            case Key.Numlock: return UnityEngine.InputSystem.Key.NumLock;
            case Key.Print: return UnityEngine.InputSystem.Key.PrintScreen;
            case Key.ScrollLock: return UnityEngine.InputSystem.Key.ScrollLock;
            case Key.Pause: return UnityEngine.InputSystem.Key.Pause;

            case Key.KeypadEnter: return UnityEngine.InputSystem.Key.NumpadEnter;
            case Key.KeypadDivide: return UnityEngine.InputSystem.Key.NumpadDivide;
            case Key.KeypadMultiply: return UnityEngine.InputSystem.Key.NumpadMultiply;
            case Key.KeypadPlus: return UnityEngine.InputSystem.Key.NumpadPlus;
            case Key.KeypadMinus: return UnityEngine.InputSystem.Key.NumpadMinus;
            case Key.KeypadPeriod: return UnityEngine.InputSystem.Key.NumpadPeriod;
            case Key.KeypadEquals: return UnityEngine.InputSystem.Key.NumpadEquals;

            case Key.Keypad0: return UnityEngine.InputSystem.Key.Numpad0;
            case Key.Keypad1: return UnityEngine.InputSystem.Key.Numpad1;
            case Key.Keypad2: return UnityEngine.InputSystem.Key.Numpad2;
            case Key.Keypad3: return UnityEngine.InputSystem.Key.Numpad3;
            case Key.Keypad4: return UnityEngine.InputSystem.Key.Numpad4;
            case Key.Keypad5: return UnityEngine.InputSystem.Key.Numpad5;
            case Key.Keypad6: return UnityEngine.InputSystem.Key.Numpad6;
            case Key.Keypad7: return UnityEngine.InputSystem.Key.Numpad7;
            case Key.Keypad8: return UnityEngine.InputSystem.Key.Numpad8;
            case Key.Keypad9: return UnityEngine.InputSystem.Key.Numpad9;

            case Key.F1: return UnityEngine.InputSystem.Key.F1;
            case Key.F2: return UnityEngine.InputSystem.Key.F2;
            case Key.F3: return UnityEngine.InputSystem.Key.F3;
            case Key.F4: return UnityEngine.InputSystem.Key.F4;
            case Key.F5: return UnityEngine.InputSystem.Key.F5;
            case Key.F6: return UnityEngine.InputSystem.Key.F6;
            case Key.F7: return UnityEngine.InputSystem.Key.F7;
            case Key.F8: return UnityEngine.InputSystem.Key.F8;
            case Key.F9: return UnityEngine.InputSystem.Key.F9;
            case Key.F10: return UnityEngine.InputSystem.Key.F10;
            case Key.F11: return UnityEngine.InputSystem.Key.F11;
            case Key.F12: return UnityEngine.InputSystem.Key.F12;

            default:
                return UnityEngine.InputSystem.Key.None;
        }
    }

    string GetTypeDelta()
    {
        // Optimization. Most of the time there won't be anything in the type delta, so we can just
        // return null to avoid unecessary processing
        if (typeDelta.Length == 0)
            return null;

        var str = typeDelta.ToString();
        typeDelta.Clear();
        return str;
    }

    protected override void UpdateState(KeyboardState state)
    {
        state.typeDelta = GetTypeDelta();

        // Collect the currently held keys
        if (state.heldKeys == null)
            state.heldKeys = new HashSet<Key>();

        state.heldKeys.Clear();

        foreach (var key in _keys)
            if (GetKeyState(key))
                state.heldKeys.Add(key);
    }

    public override void HandleOutputState(OutputState output)
    {
        if(output.keyboardInputActive != _lastKeyboardActive)
        {
            _lastKeyboardActive = output.keyboardInputActive;

            Input.imeCompositionMode = _lastKeyboardActive ? IMECompositionMode.On : IMECompositionMode.Off;
        }
    }
}
