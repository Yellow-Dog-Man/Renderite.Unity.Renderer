using AmplifyOcclusion;
using Renderite.Shared;
using Renderite.Unity;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

public class CameraPostprocessingManager : MonoBehaviour
{
    static PostProcessResources _resources;
    static PostProcessProfile _baseProfile;

    public UnityEngine.Camera Camera { get; private set; }
    public bool IsPrimary { get; private set; }
    public bool IsVR { get; private set; }

    PostProcessLayer _postProcessing;

    MotionBlur _motionBlur;
    Bloom _bloom;
    AmplifyOcclusionEffect _ao;
    ScreenSpaceReflections _ssr;

    public void Initialize(UnityEngine.Camera camera, CameraSettings settings)
    {
        Camera = camera;
        IsPrimary = settings.IsPrimary;
        IsVR = settings.IsVR;

        InitializePostProcessing();

        // For primary camera, listen to the post-processing settings and update accordingly
        if (IsPrimary)
            RenderingManager.Instance.PostProcessingUpdated += UpdatePostProcessing;
        else
        {
            _motionBlur.enabled.value = settings.MotionBlur && !IsVR;
            _ssr.enabled.value = settings.ScreenSpaceReflection;

            UpdateAA(AntiAliasingMethod.SMAA);
        }
    }

    public void UpdatePostProcessing(bool enabled, bool motionBlur, bool screenspaceReflections)
    {
        if (_postProcessing == null)
            return;

        _postProcessing.enabled = enabled;

        if (_ao != null)
            _ao.enabled = enabled;

        if (enabled)
        {
            if (_motionBlur != null)
                _motionBlur.enabled.value = motionBlur;

            if (_ssr != null)
                _ssr.enabled.value = screenspaceReflections;
        }
    }

    public void UpdatePostProcessing(PostProcessingConfig settings)
    {
        // Update motion blur
        if (_motionBlur != null)
        {
            _motionBlur.enabled.value = !IsVR && !Mathf.Approximately(settings.motionBlurIntensity, 0f);
            _motionBlur.shutterAngle.value = settings.motionBlurIntensity * 360f;
        }

        // Update bloom
        if (_bloom != null)
        {
            _bloom.enabled.value = !Mathf.Approximately(settings.bloomIntensity, 0f);
            _bloom.intensity.value = settings.bloomIntensity;
        }

        // Update AO
        if (_ao != null)
        {
            _ao.enabled = !Mathf.Approximately(settings.ambientOcclusionIntensity, 0f);
            _ao.Intensity = settings.ambientOcclusionIntensity;
        }

        // Update Screenspace Reflections
        if (_ssr != null)
            _ssr.enabled.value = settings.screenSpaceReflections;

        UpdateAA(settings.antialiasing);
    }

    void InitializePostProcessing()
    {
        _postProcessing = gameObject.GetComponent<PostProcessLayer>();

        if (_postProcessing == null)
            AddPostProcessing();

        if (_ao == null)
            AddAO();

        _motionBlur = _postProcessing.defaultProfile.GetSetting<MotionBlur>();
        _bloom = _postProcessing.defaultProfile.GetSetting<Bloom>();
        _ssr = _postProcessing.defaultProfile.GetSetting<ScreenSpaceReflections>();
    }

    void AddPostProcessing()
    {
        if (_resources == null)
            _resources = Resources.Load<PostProcessResources>("PostProcessResources");

        _postProcessing = gameObject.AddComponent<PostProcessLayer>();
        _postProcessing.Init(_resources);

        if (_baseProfile == null)
            _baseProfile = Resources.Load<PostProcessProfile>("PostProcessing_V2");

        var newProfile = Instantiate(_baseProfile);
        newProfile.settings.Clear();

        foreach (var item in _baseProfile.settings)
        {
            // skip, it breaks things
            if (IsPrimary && item is ColorGrading)
                continue;

            newProfile.settings.Add(Instantiate(item));
        }

        _postProcessing.defaultProfile = newProfile;
    }

    void AddAO()
    {
        _ao = gameObject.AddComponent<AmplifyOcclusionEffect>();

        _ao.PerPixelNormals = AmplifyOcclusionEffect.PerPixelNormalSource.None;
        _ao.Radius = 4;
        _ao.PowerExponent = 0.6f;

        _ao.SampleCount = AmplifyOcclusion.SampleCountLevel.Low;

        if (!IsPrimary)
        {
            _ao.FilterEnabled = false;
            _ao.BlurPasses = 4;
            _ao.BlurRadius = 4;
            _ao.BlurSharpness = 3;
        }
        else
        {
            _ao.BlurPasses = 2;
            _ao.BlurSharpness = 3;
            _ao.BlurRadius = 4;

            _ao.FilterResponse = 0.9f;
            _ao.FilterBlending = 0.25f;
        }
    }

    void UpdateAA(AntiAliasingMethod method)
    {
        switch (method)
        {
            case AntiAliasingMethod.Off:
                _postProcessing.antialiasingMode = PostProcessLayer.Antialiasing.None;
                _postProcessing.finalBlitToCameraTarget = false;
                break;

            case AntiAliasingMethod.FXAA:
                _postProcessing.antialiasingMode = PostProcessLayer.Antialiasing.FastApproximateAntialiasing;
                _postProcessing.fastApproximateAntialiasing.keepAlpha = !IsPrimary;
                _postProcessing.finalBlitToCameraTarget = !IsPrimary;
                break;

            case AntiAliasingMethod.SMAA:
                _postProcessing.antialiasingMode = PostProcessLayer.Antialiasing.SubpixelMorphologicalAntialiasing;
                _postProcessing.subpixelMorphologicalAntialiasing.quality = SubpixelMorphologicalAntialiasing.Quality.High;
                _postProcessing.finalBlitToCameraTarget = !IsPrimary;
                break;

            case AntiAliasingMethod.TAA:
                if (IsVR)
                {
                    // This doesn't work right in VR, so don't even try
                    goto case AntiAliasingMethod.Off; // OOOooooOoooO! Evil goto!
                }

                _postProcessing.antialiasingMode = PostProcessLayer.Antialiasing.TemporalAntialiasing;
                _postProcessing.finalBlitToCameraTarget = !IsPrimary;
                break;
        }
    }

    public void RemovePostProcessing()
    {
        if (_postProcessing != null)
        {
            Destroy(_postProcessing.defaultProfile);
            Destroy(_postProcessing);

            _postProcessing = null;
        }

        if (_ao != null)
        {
            Destroy(_ao);
            _ao = null;
        }
    }

    void OnDestroy()
    {
        if(IsPrimary)
            RenderingManager.Instance.PostProcessingUpdated -= UpdatePostProcessing;

        RemovePostProcessing();
    }
}
