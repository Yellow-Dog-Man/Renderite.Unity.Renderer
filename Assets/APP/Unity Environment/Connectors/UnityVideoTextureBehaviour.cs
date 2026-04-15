using Renderite.Shared;
using Renderite.Unity;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Experimental.Audio;
using UnityEngine.Experimental.Video;
using UnityEngine.PlayerLoop;
using UnityEngine.UIElements;
using VideoPlayer = UnityEngine.Video.VideoPlayer;

public class UnityVideoTextureBehavior : MonoBehaviour, IVideoPlaybackInstance
{
    // See the comment where this is used for details on why we need a "buffer" for the buffer and not read all the
    // samples all the way to 0.
    // From testing, even 1 works to leave in the buffer, but 8 is a "nicer number"
    // DON'T use 64. 64 crashes Unity. Seriously. If we leave 64 samples exactly in the audio buffer when reading them
    // Unity WILL crash. I don't know why. I don't even know if I want to know why. It just is. I wasted enough sanity on this. ;_;
    public const int BUFFER_BUFFER = 8; 

    public const int START_READ_BUFFER_SIZE = 4096;

    public bool IsLoaded { get; private set; }

    public Texture Texture => videoPlayer?.texture ?? Texture2D.blackTexture;
    public bool HasAlpha => false;

    public double Length
    {
        get
        {
            if (videoPlayer == null || !_initialized)
                return 0;

            return videoPlayer.frameCount / (double)videoPlayer.frameRate;
        }
    }

    public float CurrentClockError { get; private set; }

    public Vector2Int Size
    {
        get
        {
            if (videoPlayer?.texture == null || !_initialized)
                return Vector2Int.zero;

            return new Vector2Int(videoPlayer.texture.width, videoPlayer.texture.height);
        }
    }

    public static string EngineName => "Unity Native";

    bool _initialized;

    bool _isSeeking;

    Texture _lastTexture;

    VideoPlayer videoPlayer;
    VideoTextureAsset asset;

    VideoTextureUpdate _update;
    VideoTextureProperties _properties;

    List<VideoAudioTrack> _audioTracks;

    AudioSampleProvider _sampleProvider;
    VideoTextureAudioWriter _audioWriter;
    CancellationTokenSource _audioCancellationToken;

    bool playing;

    public IEnumerator Setup(VideoTextureAsset asset, string dataSource, int audioSystemSampleRate)
    {
        Debug.Log("Preparing UnityVideoTexture: " + dataSource + $", AssetId: {asset.AssetId}");

        try
        {
            this.asset = asset;

            videoPlayer = gameObject.AddComponent<VideoPlayer>();

            videoPlayer.playOnAwake = false;
            videoPlayer.renderMode = UnityEngine.Video.VideoRenderMode.APIOnly;
            videoPlayer.audioOutputMode = UnityEngine.Video.VideoAudioOutputMode.APIOnly;
            videoPlayer.source = UnityEngine.Video.VideoSource.Url;

            videoPlayer.skipOnDrop = true;
            videoPlayer.timeReference = UnityEngine.Video.VideoTimeReference.ExternalTime;

            videoPlayer.url = dataSource;

            videoPlayer.prepareCompleted += VideoPlayer_prepareCompleted;
            videoPlayer.errorReceived += VideoPlayer_errorReceived;

            videoPlayer.Prepare();
        }
        catch (Exception ex)
        {
            Debug.LogError("Exception initializing UnityVideoTexture:\n" + ex + $"\nAssetId: {asset.AssetId}");
            yield break;
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        while (!_initialized)
        {
            if(stopwatch.Elapsed.TotalSeconds > 10)
            {
                Debug.LogWarning("UnityVideoTexture Init Timeout" + $", AssetId: {asset.AssetId}");
                break;
            }

            yield return new WaitForSecondsRealtime(0.1f);
        }
    }

    void VideoPlayer_errorReceived(VideoPlayer source, string message)
    {
        Debug.LogWarning("UnityVideoTexture Error: " + message + $", AssetId: {asset.AssetId}");
        _initialized = true;
    }

    void Update()
    {
        if (!_initialized)
            return;

        var _tex = videoPlayer.texture;

        if (_lastTexture != _tex)
        {
            asset.TextureChanged();
            _lastTexture = _tex;
        }

        if (_tex != null)
        {
            var properties = Interlocked.Exchange(ref _properties, null);

            if (properties != null)
            {
                if(properties.filterMode == TextureFilterMode.Anisotropic)
                {
                    _tex.filterMode = FilterMode.Trilinear;
                    _tex.anisoLevel = properties.anisoLevel;
                }
                else
                {
                    _tex.filterMode = properties.filterMode.ToUnity();
                    _tex.anisoLevel = 0;
                }

                _tex.wrapModeU = properties.wrapU.ToUnity();
                _tex.wrapModeV = properties.wrapV.ToUnity();

                PackerMemoryPool.Instance.Return(properties);
            }
        }

        // Fetch latest update
        var update = Interlocked.Exchange(ref _update, null);

        if(update != null)
        {
            var adjustedPosition = update.AdjustedPosition;

            videoPlayer.isLooping = update.loop;
            videoPlayer.externalReferenceTime = adjustedPosition;

            playing = update.play;

            if (playing != videoPlayer.isPlaying)
            {
                if (playing)
                {
                    videoPlayer.time = adjustedPosition;
                    videoPlayer.Play();
                }
                else
                    videoPlayer.Pause();
            }

            CurrentClockError = (float)(videoPlayer.clockTime - adjustedPosition);

            RenderingManager.Instance.Results.UpdateVideoClockError(asset.AssetId, CurrentClockError);

            // Try to put the update back. This is done so it can be re-used for the next frame in case another update
            // doesn't come in, because we need to keep updating the state continuously for this video playback, otherwise
            // it'll keep resetting back to original position
            // If there's already another update ready, then we can return this one back to the pool
            if(Interlocked.CompareExchange(ref _update, update, null) != null)
                PackerMemoryPool.Instance.Return(update);
        }
    }

    void VideoPlayer_prepareCompleted(VideoPlayer source)
    {
        IsLoaded = true;

        if (source.audioTrackCount > 0)
        {
            _audioTracks = new List<VideoAudioTrack>();

            for(int i = 0; i < source.audioTrackCount; i++)
            {
                var track = new VideoAudioTrack();

                track.index = i;
                track.channelCount = source.GetAudioChannelCount((ushort)i);
                track.sampleRate = (int)source.GetAudioSampleRate((ushort)i);
                track.languageCode = source.GetAudioLanguageCode((ushort)i);

                _audioTracks.Add(track);
            }
        }

        _lastTexture = videoPlayer?.texture;

        videoPlayer.Play();

        _initialized = true;
    }

    void OnDestroy()
    {
        Debug.Log($"Destroying Video AssetId {asset?.AssetId}");

        DisposeAudioWriter();

        _initialized = false;

        if (videoPlayer != null)
        {
            Destroy(videoPlayer);
            videoPlayer = null;
        }

        videoPlayer = null;
        asset = null;
    }

    public void HandleUpdate(VideoTextureUpdate update)
    {
        var previous = Interlocked.Exchange(ref _update, update);

        if (previous != null)
            PackerMemoryPool.Instance.Return(previous);
    }

    public void HandleProperties(VideoTextureProperties properties)
    {
        var previous = Interlocked.Exchange(ref _properties, properties);

        if (previous != null)
            PackerMemoryPool.Instance.Return(previous);
    }

    // We null the tracks, because the VideoAudioTrack will get pooled after
    public List<VideoAudioTrack> GetTracks() => Interlocked.Exchange(ref _audioTracks, null);

    public void StartAudio(VideoTextureStartAudioTrack audioTrack)
    {
        // Dispose any previous writer
        DisposeAudioWriter();

        try
        {
            if (videoPlayer.controlledAudioTrackCount <= audioTrack.audioTrackIndex)
                videoPlayer.controlledAudioTrackCount = (ushort)(audioTrack.audioTrackIndex + 1);

            Debug.Log($"Starting Audio Track for Video {audioTrack.assetId}: {audioTrack.audioTrackIndex}, " +
                $"Audio Track Count: {videoPlayer.audioTrackCount}, Controlled: {videoPlayer.controlledAudioTrackCount}" + $", AssetId: {asset.AssetId}");

            _audioWriter = new VideoTextureAudioWriter(audioTrack.queueName, audioTrack.queueCapacity);

            try
            {
                _sampleProvider = videoPlayer.GetAudioSampleProvider((ushort)audioTrack.audioTrackIndex);
            }
            catch (InvalidOperationException ex)
            {
                Debug.LogWarning($"Failed to initialize audio track index {audioTrack.audioTrackIndex}, falling back to first track. Exception:\n" +
                    ex);

                _sampleProvider = videoPlayer.GetAudioSampleProvider(0);
            }

            _sampleProvider.enableSilencePadding = false;

            videoPlayer.EnableAudioTrack((ushort)audioTrack.audioTrackIndex, true);
        }
        catch(Exception ex)
        {
            Debug.LogError($"Exception enabling audio track {audioTrack.audioTrackIndex} for video {audioTrack.assetId}");
            return;
        }

        _audioCancellationToken = new CancellationTokenSource();

        var thread = new Thread(() => AudioReaderThread(_sampleProvider, _audioWriter, _audioCancellationToken.Token));
        thread.IsBackground = true;
        thread.Priority = System.Threading.ThreadPriority.Highest;

        thread.Start();

        PackerMemoryPool.Instance.Return(audioTrack);
    }

    void DisposeAudioWriter()
    {
        if (_audioCancellationToken == null)
            return;

        // This will kill the thread and dispose of the writer when it's done
        _audioCancellationToken.Cancel();
        _audioCancellationToken = null;

        _audioWriter = null;
    }

    void AudioReaderThread(AudioSampleProvider provider, VideoTextureAudioWriter writer, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var availableFrames = provider.availableSampleFrameCount;

                if (availableFrames < BUFFER_BUFFER)
                    continue;

                // IMPORTANT!!! For some reason, when the buffer is read all the way to 0, it will mess up the buffer and
                // generate some bogus samples for some reason. So we always leave a little bit in, which prevents this from
                // happening. Without this, if video is 44.1 kHz, it would generate about 45.5 kHz samples per second and make
                // very weird glitchy audio
                var toConsume = availableFrames - BUFFER_BUFFER;

                using (var buffer = new NativeArray<float>((int)(toConsume * provider.channelCount),
                    Allocator.TempJob, NativeArrayOptions.UninitializedMemory))
                {
                    var consumed = provider.ConsumeSampleFrames(buffer) * provider.channelCount;

                    if (consumed > 0)
                    {
                        unsafe
                        {
                            var span = new Span<float>(buffer.GetUnsafeReadOnlyPtr(), (int)consumed);

                            // Note that we don't super care if this fails because the buffer is full, we just drop the data
                            // If the playback is struggling to pull the data out
                            if (!writer.Write(span))
                                Debug.Log($"[{DateTime.UtcNow.ToLongTimeString()}] Shared Memory Queue is full, dropping {toConsume} frames of audio data for video {asset.AssetId}.");
                        }
                    }
                }

                Thread.Sleep(10);
            }
        }
        catch(Exception ex)
        {
            Debug.LogError($"Exception reading audio data for video {asset?.AssetId}:\n{ex}");
        }
        finally
        {
            // Dispose the writer once we're done with the loop
            writer.Dispose();
        }
    }
}
