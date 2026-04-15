using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Reflection;

public class MonoVersion : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        Type type = Type.GetType("Mono.Runtime");
        if (type != null)
        {
            MethodInfo displayName = type.GetMethod("GetDisplayName", BindingFlags.NonPublic | BindingFlags.Static);
            if (displayName != null)
                Debug.Log("MonoRuntime: " + displayName.Invoke(null, null));
        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
