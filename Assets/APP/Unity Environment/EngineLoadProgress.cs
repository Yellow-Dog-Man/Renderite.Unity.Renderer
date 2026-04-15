using Renderite.Shared;
using Renderite.Unity;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EngineLoadProgress : EngineInitProgress
{
    public UnityEngine.Transform VisualRoot;
    public UnityEngine.Transform SplashRoot;
    public UnityEngine.Transform ProgressBarRoot;
    public UnityEngine.Transform ProgressBar;
    public UnityEngine.TextMesh Text;
    public UnityEngine.Material Material;
    public UnityEngine.Material SplashMaterial;
    public UnityEngine.Video.VideoPlayer VideoPlayer;
    public UnityEngine.Video.VideoClip FadeOutVideo;

    Texture2D _customSplash;

    bool initStarted;

    Vector3 originalScale;

    string _showSubphase;

    int renderedFrames = 0;
    bool videoActivated;

    public bool Done;

    DateTime? _fadeoutInitiatedOn;

    RendererSplashScreenOverride _screenOverride;

    public float LoadProgress { get; private set; }

    public override void InitStarted()
    {
        initStarted = true;
    }

    public override void InitCompleted()
    {
        Done = true;

        Debug.Log($"InitCompleted signalled. Hiding loading bar");
    }

    public override void UpdateProgress(Renderite.Shared.RendererInitProgressUpdate update)
    {
        LoadProgress = update.progress;

        if (update.forceShow)
            _showSubphase = update.subPhase;
        else
            _showSubphase = null;
    }

    void Awake()
    {
        ProgressBar.localScale = new Vector3(LoadProgress, 1, 1);

        originalScale = ProgressBarRoot.localScale;
        ProgressBarRoot.localScale = Vector3.zero;
    }

    void ApplySplash()
    {
        Debug.Log($"Applying custom splash screen: {_screenOverride.textureSize.x}x{_screenOverride.textureSize.y}, " +
            $"Relative size: {_screenOverride.textureRelativeScreenSize}, Loading Bar Pos: {_screenOverride.loadingBarOffset}");

        _customSplash = new Texture2D(
            _screenOverride.textureSize.x,
            _screenOverride.textureSize.y,
            UnityEngine.TextureFormat.BGRA32, true, false);

        var data = RenderingManager.Instance.SharedMemory.AccessData(_screenOverride.textureData);

        unsafe
        {
            fixed (void* ptr = data)
                _customSplash.LoadRawTextureData(new IntPtr(ptr), data.Length);
        }

        // Apply the data
        _customSplash.Apply(false, true);

        SplashMaterial.mainTexture = _customSplash;

        var height = _screenOverride.textureRelativeScreenSize * 3.1f;
        var width = _screenOverride.textureSize.x / (float)_screenOverride.textureSize.y;
        width *= height;

        SplashRoot.localScale = new Vector3(width, height, 1);
        ProgressBarRoot.localPosition = new Vector3(_screenOverride.loadingBarOffset.x, _screenOverride.loadingBarOffset.y, 0) * 3.1f * 0.5f;
        ProgressBarRoot.localScale = new Vector3(originalScale.x, originalScale.y, originalScale.z);

        Destroy(VideoPlayer);
        Destroy(FadeOutVideo);

        VideoPlayer = null;
        FadeOutVideo = null;

        // Clear it out because we applied it
        _screenOverride = null;
    }

    void Update()
    {
        renderedFrames++;

        if (_screenOverride != null)
            ApplySplash();

        if (!initStarted)
            return;

        ProgressBar.localPosition = new Vector3((1 - LoadProgress) * -0.5f, 0, 0);
        ProgressBar.localScale = new Vector3(LoadProgress, 1, 1);

        Text.gameObject.SetActive(_showSubphase != null);

        if (_showSubphase != null)
            Text.text = _showSubphase;

        if (VideoPlayer != null)
        {
            if (!videoActivated && renderedFrames >= 10)
            {
                videoActivated = true;
                VideoPlayer.Play();
            }

            var videoLerp = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((float)(VideoPlayer.time / VideoPlayer.length)));

            if (Done)
            {
                if (_fadeoutInitiatedOn == null)
                {
                    VideoPlayer.clip = FadeOutVideo;
                    VideoPlayer.time = 0;

                    videoLerp = 0;

                    VideoPlayer.Play();

                    _fadeoutInitiatedOn = DateTime.UtcNow;
                }

                if (videoLerp >= 0.9999f || (DateTime.UtcNow - _fadeoutInitiatedOn.Value).TotalSeconds > 30)
                {
                    // cleanup
                    if (!Application.isEditor)
                        Destroy(VideoPlayer.targetTexture);

                    Destroy(gameObject);
                    return;
                }

                videoLerp = 1 - videoLerp;
            }

            ProgressBarRoot.localScale = new Vector3(originalScale.x * videoLerp, originalScale.y, originalScale.z);

            Material.SetTextureScale("_MainTex", new Vector2(LoadProgress * videoLerp, 1f));
        }
        else
        {
            if(Done)
            {
                Destroy(gameObject);
                return;
            }

            Material.SetTextureScale("_MainTex", new Vector2(LoadProgress, 1f));
        }

        Material.SetTextureOffset("_MainTex", new Vector2(Time.time, 0f));

        var h = Time.time * 0.2f;
        var s = 0.15f;
        var v = 0.333f;
        var a = 0.5f;

        var c = Color.HSVToRGB(h, s, v);
        c.a = a;

        Material.SetColor("_TintColor", c);
    }

    public override void ApplySplashScreenOverride(RendererSplashScreenOverride splashScreen)
    {
        if (splashScreen == null)
            throw new ArgumentNullException(nameof(splashScreen));

        _screenOverride = splashScreen;
    }
}
