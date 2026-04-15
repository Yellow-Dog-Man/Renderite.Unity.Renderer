#if UMP_SUPPORTED
using Renderite.Shared;
using Renderite.Unity;
using SharpDX.Direct3D11;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using UMP;
using UnityEngine;
using UnityEngine.Experimental.Audio;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

public class UMPVideoTextureBehaviour : MonoBehaviour, IVideoPlaybackInstance
{
    const float MAX_DEVIATION = 2f;

    VideoTextureAsset asset;

    MediaPlayer mediaPlayer;

    MediaPlayerStandalone standalonePlayer;

    string dataSource;

    public UnityEngine.Texture Texture => _texture;

    UnityEngine.Texture _texture;

    public bool IsLoaded { get; private set; }

    public bool HasAlpha { get; private set; }
    public Vector2Int Size { get; private set; }
    public double Length { get; private set; }

    public float CurrentClockError { get; private set; }

    public static string EngineName => "libVLC";

    int sampleRate;

    volatile bool _initialized;

    float playCooloff;
    float pauseCooloff;

    double lastReportedPosition;
    float lastReportedPositionTime;

    double lastReportedBeforeSeek;
    float lastReportedPositionTimeBeforeSeek;

    VideoTextureUpdate _update;
    VideoTextureProperties _properties;

    List<VideoAudioTrack> _audioTracks;

    AudioBufferHandler _bufferHandler;

    double EstimatedPositionBeforeSeek => lastReportedBeforeSeek + (Time.time - lastReportedPositionTimeBeforeSeek);
    double EstimatedPosition => lastReportedPosition + (Time.time - lastReportedPositionTime);

    bool seeking;
    bool firstReportAfterSeek;

    double lastSeekError;

    int? _setAudioTrack;

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

    void SendOnLoaded()
    {
        if (_initialized)
            asset.TextureChanged();
        else
            _initialized = true;
    }

    public IEnumerator Setup(VideoTextureAsset asset, string dataSource, int audioSystemSampleRate)
    {
        Debug.Log("Preparing UMP: " + dataSource + $", AssetId: {asset.AssetId}");

        sampleRate = audioSystemSampleRate;

        InitializePlayer(audioSystemSampleRate);

        this.asset = asset;
        this.dataSource = dataSource;

        if (mediaPlayer == null)
            throw new InvalidOperationException("MediaPlayer is null! Cannot Setup playback");

        mediaPlayer.DataSource = dataSource;
        mediaPlayer.Mute = true; // start muted
        mediaPlayer.Play();

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        while (!_initialized)
        {
            if (stopwatch.Elapsed.TotalSeconds > 10)
            {
                Debug.LogWarning("UnityVideoTexture Init Timeout" + $", AssetId: {asset.AssetId}");
                break;
            }

            yield return new WaitForSecondsRealtime(0.1f);
        }
    }

    void InitializePlayer(int outputSampleRate)
    {
        PlayerOptions options = new PlayerOptions(null);

        switch (UMPSettings.RuntimePlatform)
        {
            case UMPSettings.Platforms.Win:
            case UMPSettings.Platforms.Mac:
            case UMPSettings.Platforms.Linux:

                var winOptions = new PlayerOptionsStandalone(null)
                {
                    FixedVideoSize = Vector2.zero,
                    HardwareDecoding = PlayerOptions.States.Disable,
                    //AudioOutputSources = null,
                    //HardwareDecoding = PlayerOptions.State.Disable,
                    FlipVertically = true,
                    UseTCP = false,
                    FileCaching = 300,
                    LiveCaching = 300,
                    DiskCaching = 300,
                    NetworkCaching = 300,
                    LogDetail = LogLevels.Disable,
                    //LogListener = log => UniLog.Log($"libVLC - {log.Level}:\t{log.Message}")

                };

                options = winOptions;
                break;

            case UMPSettings.Platforms.Android:
                var androidOptions = new PlayerOptionsAndroid(null)
                {
                    FixedVideoSize = Vector2.zero,
                    //HardwareDecoding = PlayerOptions.State.Enable,
                    PlayInBackground = false,
                    UseTCP = false,
                    //FileCaching = 300,
                    //LiveCaching = 300,
                    //DiscCaching = 300,
                    NetworkCaching = 300
                };

                options = androidOptions;
                break;

            case UMPSettings.Platforms.iOS:
                var iphoneOptions = new PlayerOptionsIPhone(null)
                {
                    FixedVideoSize = Vector2.zero,
                    VideoToolbox = true,
                    VideoToolboxFrameWidth = 4096,
                    VideoToolboxAsync = false,
                    VideoToolboxWaitAsync = true,
                    PlayInBackground = false,
                    UseTCP = false,
                    PacketBuffering = true,
                    MaxBufferSize = 15 * 1024 * 1024,
                    MinFrames = 50000,
                    Infbuf = false,
                    Framedrop = 0,
                    MaxFps = 31
                };

                options = iphoneOptions;
                break;
        }

        mediaPlayer = new MediaPlayer(this, null as GameObject[], options, outputSampleRate);

        standalonePlayer = (MediaPlayerStandalone)mediaPlayer.Player;

        standalonePlayer.OnPlaySamples += StandalonePlayer_OnPlaySamples;
        standalonePlayer.OnFlushSamples += StandalonePlayer_OnFlushSamples;
        standalonePlayer.OnPause += StandalonePlayer_OnPause;
        standalonePlayer.OnResume += StandalonePlayer_OnResume;

        mediaPlayer.EventManager.PlayerPositionChangedListener += PositionChanged;
        mediaPlayer.EventManager.PlayerImageReadyListener += OnTextureCreated;
        mediaPlayer.EventManager.PlayerEndReachedListener += EndReached;

        mediaPlayer.EventManager.PlayerEncounteredErrorListener += EventManager_PlayerEncounteredErrorListener;
        mediaPlayer.EventManager.PlayerPreparedListener += EventManager_PlayerPreparedListener;
    }

    private void EventManager_PlayerEncounteredErrorListener()
    {
        Debug.Log("UMP Player Encountered Error. LastError: " + standalonePlayer?.GetLastError() + $", AssetId: {asset.AssetId}");

        StartCoroutine(DelayFail());
    }

    IEnumerator DelayFail()
    {
        yield return new WaitForSecondsRealtime(5);

        SendOnLoaded();
    }

    private void PositionChanged(float normalizedPos)
    {
        //Debug.Log("PositionChanged");

        if (firstReportAfterSeek)
        {
            firstReportAfterSeek = false;
            return;
        }

        double pos = normalizedPos * Length;

        if (seeking)
        {
            if (Math.Abs(EstimatedPositionBeforeSeek - pos) > MAX_DEVIATION)
            {
                lastSeekError = lastSeekError + EstimatedPosition - pos;
                /*Debug.Log("Seek error: " + lastSeekError + ", estimated: " + EstimatedPosition
                    + ", reported: " + pos + ", estimated before seek: " + EstimatedPositionBeforeSeek);*/
            }
            seeking = false;
        }

        lastReportedPosition = pos;
        lastReportedPositionTime = Time.time;
    }

    private void EndReached()
    {
        mediaPlayer.Stop(false);
        seeking = false;
    }

    private void OnTextureCreated(UnityEngine.Texture2D obj)
    {
        //UniLog.Log($"TextureCreated {obj}, length: " + Length + ", ableToPlay: " + mediaPlayer.AbleToPlay);

        _audioTracks = new List<VideoAudioTrack>();

        if (mediaPlayer.SpuTracks != null && mediaPlayer.SpuTracks.Length > 0)
        {
            foreach (var track in mediaPlayer.SpuTracks)
                if (track.Id < 0)
                {
                    mediaPlayer.SpuTrack = track;
                    break;
                }
        }

        for (int i = 0; i < mediaPlayer.AudioTracks.Length; i++)
        {
            var track = mediaPlayer.AudioTracks[i];

            _audioTracks.Add(new VideoAudioTrack()
            {
                index = i,
                channelCount = 2,
                name = track.Name,
                sampleRate = sampleRate
            });
        }

        // update data
        if (mediaPlayer.Length == 0 && mediaPlayer.AbleToPlay)
            Length = double.PositiveInfinity;
        else
            Length = mediaPlayer.Length / 1000.0;

        Size = new Vector2Int(mediaPlayer.VideoWidth, mediaPlayer.VideoHeight);
        HasAlpha = false;

        _texture = obj;

        IsLoaded = true;

        SendOnLoaded();
    }

    private void EventManager_PlayerPreparedListener(int arg1, int arg2)
    {
        //UniLog.Log("Player Prepared: " + arg1 + ", " + arg2);

        if (!_initialized)
            OnTextureCreated(null);
    }

    void Update()
    {
        if (!_initialized)
            return;

        bool isStream = double.IsPositiveInfinity(Length);

        //if (connector.world.Focus == World.WorldFocus.Background)
        //{
        //    if (mediaPlayer.IsPlaying && !inBackground)
        //    {
        //        mediaPlayer.Mute = true;

        //        if (!isStream)
        //            mediaPlayer.Pause();

        //        seeking = false;
        //        inBackground = true;
        //    }

        //    return;
        //}

        var _tex = Texture;

        if (_tex != null)
        {
            var properties = Interlocked.Exchange(ref _properties, null);

            if (properties != null)
            {
                if (properties.filterMode == TextureFilterMode.Anisotropic)
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

        if(_setAudioTrack != null)
        {
            var tracks = mediaPlayer.AudioTracks;
            mediaPlayer.AudioTrack = tracks[_setAudioTrack.Value];

            _setAudioTrack = null;
        }

        // Fetch latest update
        var update = Interlocked.Exchange(ref _update, null);

        if (update != null)
        {
            ApplyUpdate(update);
            PackerMemoryPool.Instance.Return(update);
        }
    }

    void ApplyUpdate(VideoTextureUpdate update)
    {
        bool isStream = double.IsPositiveInfinity(Length);

        mediaPlayer.Mute = !update.play;
        mediaPlayer.Volume = 100;

        if (isStream)
        {
            // the source is a stream, don't do traditional seeking
            if (update.play)
            {
                if (!mediaPlayer.IsPlaying && playCooloff <= 0f)
                {
                    mediaPlayer.Play();
                    mediaPlayer.Position = 0;
                    playCooloff = 2f;
                    pauseCooloff = 0f;
                }
            }
            else
            {
                //if (mediaPlayer.IsPlaying && pauseCooloff <= 0f)
                //{
                //    mediaPlayer.Pause();
                //    pauseCooloff = 2f;
                //    playCooloff = 0f;
                //}
            }

            playCooloff -= Time.deltaTime;
            pauseCooloff -= Time.deltaTime;
        }
        else
        {
            // compute estimated mediaPlayer position 
            var adjustedPosition = update.AdjustedPosition;

            if (update.play)
            {
                pauseCooloff = 0f;

                // compute position error
                double positionError = Math.Abs(adjustedPosition - EstimatedPosition);

                /*UniLog.Log("Position error: " + positionError + ", fixing (EstimatedPlayerPosition: " + EstimatedPosition
+ " LastPlayerPosition: " + lastReportedPosition + ", "
+ "Target Position: " + playback.position + ", IsPlaying: " + mediaPlayer.IsPlaying
+ ", IsAble: " + mediaPlayer.AbleToPlay + ", IsReady: " + mediaPlayer.IsReady + " seeking: " + seeking);*/

                if (!mediaPlayer.IsPlaying && playCooloff <= 0f)
                {
                    mediaPlayer.Time = (long)((adjustedPosition) * 1000);

                    PositionChanged((float)(adjustedPosition / Length));
                    mediaPlayer.Play();
                    //UniLog.Log("Running Play()");
                    playCooloff = 2f;

                }
                else if (positionError > MAX_DEVIATION && !seeking)
                {
                    lastReportedBeforeSeek = lastReportedPosition;
                    lastReportedPositionTimeBeforeSeek = lastReportedPositionTime;

                    mediaPlayer.Time = (long)((adjustedPosition + lastSeekError) * 1000);

                    // immediately set new last reported position, otherwise this will run repeatedly
                    // until it actually reports one
                    PositionChanged((float)(adjustedPosition / Length));

                    seeking = true;
                    firstReportAfterSeek = true;
                }

                playCooloff -= Time.deltaTime;
            }
            else
            {
                playCooloff = 0f;

                seeking = false;

                if (mediaPlayer.IsPlaying &&/* mediaPlayer.Position < 0.9f &&*/ pauseCooloff <= 0f)
                {
                    pauseCooloff = 2f;
                    mediaPlayer.Pause();
                    //UniLog.Log("Running Pause()");
                }

                pauseCooloff -= Time.deltaTime;

                //mediaPlayer.Position = (long)(playback.position * 1000);
            }

            var playerTime = mediaPlayer.Time * 0.001;
            CurrentClockError = (float)(playerTime - adjustedPosition);

            RenderingManager.Instance.Results.UpdateVideoClockError(asset.AssetId, CurrentClockError);
        }
    }

    void DisposeAudioBufferHandler()
    {
        var buffer = _bufferHandler;
        _bufferHandler = null;
        buffer?.Dispose();
    }

    void OnDestroy()
    {
        Debug.Log($"Destroying Video AssetId {asset?.AssetId}");

        var _mediaPlayer = mediaPlayer;

        DisposeAudioBufferHandler();

        // make sure that any audio callbacks won't be fullfilled
        // the lock makes sure we don't release the mediaplayer in the middle of audio reading
        mediaPlayer = null;
        standalonePlayer = null;
        _initialized = false;

        _mediaPlayer.Release();
    }

    public void StartAudio(VideoTextureStartAudioTrack audioTrack)
    {
        _setAudioTrack = audioTrack.audioTrackIndex;

        // Dispose any previous one
        DisposeAudioBufferHandler();

        var audioWriter = new VideoTextureAudioWriter(audioTrack.queueName, audioTrack.queueCapacity);

        _bufferHandler = new AudioBufferHandler(audioWriter, asset.AssetId);

        PackerMemoryPool.Instance.Return(audioTrack);
    }

    void StandalonePlayer_OnPlaySamples(Span<float> samples, long pts)
    {
        _bufferHandler?.Write(samples, pts);
    }

    void StandalonePlayer_OnResume(long pts)
    {
        _bufferHandler?.Resume(pts);
    }

    void StandalonePlayer_OnPause(long pts)
    {
        _bufferHandler?.Pause(pts);
    }

    void StandalonePlayer_OnFlushSamples(long pts)
    {
        _bufferHandler?.Flush(pts);
    }
}

class AudioBufferHandler : IDisposable
{
    readonly struct BufferRecord
    {
        public readonly int start;
        public readonly int count;
        public readonly float delay;

        public BufferRecord(int start, int count, float delay)
        {
            this.start = start;
            this.count = count;
            this.delay = delay;
        }
    }

    public int FreeCapacity => rawSampleData.Length - usedCapacity;

    VideoTextureAudioWriter _writer;

    CancellationTokenSource _cancellationSource;

    // 2 seconds should be enough
    float[] rawSampleData = new float[48000 * 4];
    Queue<BufferRecord> records = new Queue<BufferRecord>();

    Thread _thread;

    long? lastPts;

    int usedCapacity;

    int sampleHead = 0;

    object _lock = new object();

    bool _playing = true;

    int _associatedAssetId;

    public AudioBufferHandler(VideoTextureAudioWriter writer, int associatedAssetId)
    {
        _associatedAssetId = associatedAssetId;

        _writer = writer;

        _cancellationSource = new CancellationTokenSource();

        _thread = new Thread(PushLoop);
        _thread.IsBackground = true;
        _thread.Priority = System.Threading.ThreadPriority.Highest;

        _thread.Start();
    }

    public void Flush(long pts)
    {
        lock(_lock)
        {
            records.Clear();
            usedCapacity = 0;
            sampleHead = 0;

            lastPts = null;
        }
    }

    public void Pause(long pts)
    {
        lastPts = null;

        _playing = false;
    }

    public void Resume(long pts)
    {
        lastPts = null;

        _playing = true;
    }

    public void Write(Span<float> buffer, long pts)
    {
        lock(_lock)
        {
            // Just dequeue a bunch of them until we have enough space
            // this should ideally never happen
            while (FreeCapacity < buffer.Length)
            {
                Debug.LogWarning($"Not enough free capacity! Skipping. FreeCapacity: {FreeCapacity}, Entries: {records.Count}, Length: {buffer.Length}, Pts: {pts}" + $", AssetId: {_associatedAssetId}");
                Dequeue();
            }

            // Compute time delay
            int start = sampleHead;
            int count = buffer.Length;

            float delay;

            if (lastPts == null)
                delay = 0;
            else
            {
                var delta = pts - lastPts.Value;
                delay = delta * 0.001f * 0.001f;
            }

            lastPts = pts;

            records.Enqueue(new BufferRecord(start, count, delay));

            WriteRaw(buffer);
        }
    }

    void PushLoop()
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        double nextUpdate = 0;

        float[] readBuffer = null;

        while(!_cancellationSource.IsCancellationRequested)
        {
            lock(_lock)
            {
                if (_playing)
                {
                    var record = Dequeue();

                    if (record.count == 0)
                        nextUpdate += 0.01f; // Wait 10 ms for another chunk of data
                    else
                    {
                        if (readBuffer == null || readBuffer.Length < record.count)
                            readBuffer = new float[record.count];

                        var data = readBuffer.AsSpan(0, record.count);

                        ReadRaw(record.start, data);

                        _writer.Write(data);

                        nextUpdate += record.delay;
                    }
                }
                else
                    nextUpdate += 0.01f; // wait a bit before resuming
            }

            var timeToNextUpdate = nextUpdate - stopwatch.Elapsed.TotalSeconds;
            timeToNextUpdate = Math.Max(0, timeToNextUpdate);

            System.Threading.Thread.Sleep(TimeSpan.FromSeconds(timeToNextUpdate));
        }

        // Dispose of the writer once we're done
        _writer.Dispose();
    }

    void WriteRaw(Span<float> buffer)
    {
        usedCapacity += buffer.Length;

        var toCopy = rawSampleData.Length - sampleHead;
        toCopy = Math.Min(toCopy, buffer.Length);

        buffer.Slice(0, toCopy).CopyTo(rawSampleData.AsSpan().Slice(sampleHead));

        sampleHead += toCopy;

        // Wrap around
        if (sampleHead == rawSampleData.Length)
            sampleHead = 0;

        buffer = buffer.Slice(toCopy);

        // Check if we still have data to copy
        if (buffer.Length == 0)
            return;

        // We don't need to slice the raw sample data because we just wrapped around
        buffer.CopyTo(rawSampleData.AsSpan());
        sampleHead += buffer.Length;
    }

    void ReadRaw(int start, Span<float> buffer)
    {
        var toRead = rawSampleData.AsSpan(start);

        if(toRead.Length > buffer.Length)
            toRead = toRead.Slice(0, buffer.Length);

        toRead.CopyTo(buffer);

        buffer = buffer.Slice(toRead.Length);

        if (buffer.Length > 0)
            rawSampleData.AsSpan(0, buffer.Length).CopyTo(buffer);
    }

    BufferRecord Dequeue()
    {
        if (records.Count == 0)
            return default;

        var record = records.Dequeue();

        usedCapacity -= record.count;

        return record;
    }

    public void Dispose()
    {
        _cancellationSource.Cancel();
    }
}
#endif