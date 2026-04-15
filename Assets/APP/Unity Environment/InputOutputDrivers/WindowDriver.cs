using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Renderite.Shared;
using Renderite.Unity;

public class WindowDriver : WindowInput
{
#if UNITY_STANDALONE_WIN
    B83.Win32.UnityWindowsExtensions _hook;

    DragAndDropEvent _stagedEvent;

    public override void UpdateState(WindowState state)
    {
        base.UpdateState(state);

        state.dragAndDropEvent = _stagedEvent;
        _stagedEvent = null;
    }

    void Awake()
    {
        _hook = new B83.Win32.UnityWindowsExtensions();
        _hook.InstallHook();
        _hook.OnDroppedFiles += OnDroppedFiles;
    }

    void OnDroppedFiles(List<string> aPathNames, B83.Win32.POINT aDropPoint)
    {
        try
        {
            _stagedEvent = new DragAndDropEvent
            {
                paths = aPathNames,
                dropPoint = new RenderVector2i(aDropPoint.x, aDropPoint.y)
            };
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Exception dropping files:\n{ex}");
        }
    }
#endif
}
