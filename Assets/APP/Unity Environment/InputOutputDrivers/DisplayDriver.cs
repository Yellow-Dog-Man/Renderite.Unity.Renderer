using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using Renderite.Shared;
using Renderite.Unity;

#if UNITY_STANDALONE_WIN

public class DuplicableDisplay : IDisplayTextureSource
{
    uDesktopDuplication.Monitor _monitor;
    uWindowCapture.UwcWindow _window;

    enum State
    {
        DirectCapture,
        WaitingOnWindow,
        WaitingOnTexture,
        UsingWindowCapture
    }

    State currentState = State.DirectCapture;

    HashSet<Action> _requests = new HashSet<Action>();

    public Texture UnityTexture => _window?.texture ?? _monitor?.texture;

    public DuplicableDisplay(int index)
    {

    }

    void UpdateProperties(uDesktopDuplication.Monitor monitor, DisplayState state)
    {
        state.resolution = new RenderVector2i(monitor.width, monitor.height);
        state.refreshRate = -1;
        state.orientation = currentState == State.UsingWindowCapture ? RectOrientation.Default : ToEngine(monitor.rotation);
        state.dpi = new RenderVector2(monitor.dpiX, monitor.dpiY);
        state.offset = new RenderVector2i(monitor.left, monitor.top);
        state.isPrimary = monitor.isPrimary;
    }

    public void Update(uDesktopDuplication.Monitor monitor, DisplayState state)
    {
        if (_requests.Count == 0)
        {
            _monitor = null;
            _window = null;

            UpdateProperties(monitor, state);
            return;
        }

        bool changed = false;

        if (monitor != _monitor || (currentState != State.DirectCapture && currentState != State.UsingWindowCapture))
        {
            if(currentState != State.DirectCapture)
                Debug.Log($"Monitor {monitor.id}, name: {monitor.name}, state: {monitor.state}");

            if (monitor.state == uDesktopDuplication.DuplicatorState.Unsupported)
            {
                var dummy = uWindowCapture.UwcManager.instance;
                _window = uWindowCapture.UwcManager.Find(monitor.name, false);

                if (_window != null)
                {
                    currentState = State.WaitingOnTexture;

                    _window.captureMode = uWindowCapture.CaptureMode.BitBlt;
                    _window.cursorDraw = true;

                    Debug.Log("Using fallback window capture: " + _window?.id);
                }
                else
                    currentState = State.WaitingOnWindow;
            }
            else
            {
                currentState = State.DirectCapture;
                _window = null;
            }

            _monitor = monitor;
            changed = true;

            if (_requests.Count > 0 && _window == null && currentState == State.DirectCapture)
                _monitor.CreateTextureIfNeeded();
        }

        if (currentState != State.WaitingOnWindow)
        {
            if (_window != null)
                _window.RequestCapture();
            else
                _monitor.Render();
        }

        if(currentState == State.WaitingOnTexture && _window?.texture != null)
        {
            changed = true;
            currentState = State.UsingWindowCapture;
        }

        UpdateProperties(monitor, state);

        if (changed && (currentState == State.DirectCapture || currentState == State.UsingWindowCapture))
            foreach (var request in _requests)
                request();
    }

    static RectOrientation ToEngine(uDesktopDuplication.MonitorRotation rotation)
    {
        switch(rotation)
        {
            case uDesktopDuplication.MonitorRotation.Rotate90:
                return RectOrientation.Clockwise90;
            case uDesktopDuplication.MonitorRotation.Rotate180:
                return RectOrientation.UpsideDown180;
            case uDesktopDuplication.MonitorRotation.Rotate270:
                return RectOrientation.CounterClockwise90;
            default:
                return RectOrientation.Default;
        }
    }

    public void RegisterRequest(Action onTextureChanged)
    {
        if(_window != null && currentState == State.DirectCapture)
            _monitor?.CreateTextureIfNeeded();

        _requests.Add(onTextureChanged);
    }

    public void UnregisterRequest(Action onTextureChanged)
    {
        _requests.Remove(onTextureChanged);

        // Keep the texture around for now, there's only ever going to be a few of them and destroying and recreating it
        // causes issues
        /*if (_requests.Count == 0)
            _monitor?.DestroyTexture();*/
    }
}

#endif

public class DisplayDriver : DisplayInput
{
#if UNITY_STANDALONE_WIN

    List<DuplicableDisplay> _displays = new List<DuplicableDisplay>();

    void OnAwake()
    {
        uDesktopDuplication.Manager.CreateInstance();
    }

    public override IDisplayTextureSource TryGetDisplayTexture(int index)
    {
        if (index < 0)
            return null;

        if (index >= _displays.Count)
            return null;

        return _displays[index];
    }

    protected override void UpdateState(List<DisplayState> states)
    {
        var monitors = uDesktopDuplication.Manager.monitors;

        for (int i = 0; i < monitors.Count; i++)
        {
            var monitor = monitors[i];

            DisplayState state;

            if (states.Count == i)
            {
                state = new DisplayState();
                state.displayIndex = i;

                states.Add(state);
            }
            else
                state = states[i];

            DuplicableDisplay display;

            if (_displays.Count == i)
            {
                display = new DuplicableDisplay(i);

                _displays.Add(display);
            }
            else
                display = _displays[i];

            display.Update(monitor, state);
        }
    }
#else

    public override IDisplayTextureSource TryGetDisplayTexture(int index) => null;
    protected override void UpdateState(List<DisplayState> states) { }

#endif
}
