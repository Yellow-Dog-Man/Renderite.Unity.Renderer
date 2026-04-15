using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VideoPlaybackManager : Renderite.Unity.VideoPlaybackManager
{
    void Awake()
    {
        RegisterPlaybackEngine(new Renderite.Unity.VideoPlaybackEngine(
            UnityVideoTextureBehavior.EngineName, go => go.AddComponent<UnityVideoTextureBehavior>(), 1));

#if UMP_SUPPORTED
        RegisterPlaybackEngine(new Renderite.Unity.VideoPlaybackEngine(
            UMPVideoTextureBehaviour.EngineName, go => go.AddComponent<UMPVideoTextureBehaviour>(), 5));
#endif
    }
}
