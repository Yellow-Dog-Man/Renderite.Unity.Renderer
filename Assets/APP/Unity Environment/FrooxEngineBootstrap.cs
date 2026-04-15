//using System.Collections;
//using System.Collections.Generic;
//using UnityEngine;
//using FrooxEngine;
//using UnityFrooxEngineRunner;
//using Elements.Core;
//using System.Threading;
//using SkyFrost.Base;
//using UnityEngine.XR;
//using System.Threading.Tasks.Dataflow;
//using System.Threading.Tasks;

//public class FrooxEngineBootstrap : MonoBehaviour
//{
//#if UNITY_EDITOR
//    public HeadOutputDevice EditorOutputDevice;
//#endif

//    public UnityEngine.Camera OverlayCamera;
//    public EngineLoadProgress Progress;

//    public static bool IsInitialized { get; private set; }
//    static List<System.Action> deferredInitializationActions = new List<System.Action>();

//    public static ISystemInfo SystemInfo;
//    public static ActionBlock<LogData> Logger;
//    public static System.IO.StreamWriter LogStream;

//    public readonly struct LogData
//    {
//        public readonly string message;
//        public readonly TaskCompletionSource<bool> task;

//        public LogData(string msg)
//        {
//            message = msg;
//            task = null;
//        }

//        public LogData(TaskCompletionSource<bool> task)
//        {
//            this.message = null;
//            this.task = task;
//        }

//        public static implicit operator LogData(string msg) => new LogData(msg);
//        public static implicit operator LogData(TaskCompletionSource<bool> task) => new LogData(task);
//    }

//    List<string> deferredLogMessages = new List<string>();

//    public static void RunPostInitAction(System.Action action)
//    {
//        if (IsInitialized)
//        {
//            action();
//            return;
//        }

//        lock (deferredInitializationActions)
//        {
//            // It's possible it has initialized in the meanwhile
//            if (IsInitialized)
//            {
//                action();
//                return;
//            }

//            deferredInitializationActions.Add(action);
//        }
//    }

//    void OnAwake()
//    {
//        Screen.sleepTimeout = SleepTimeout.NeverSleep;

//#if !UNITY_WINRT
//        if (string.IsNullOrEmpty(Thread.CurrentThread.Name))
//            Thread.CurrentThread.Name = "UnityThread";

//        System.Threading.Thread.CurrentThread.Priority = System.Threading.ThreadPriority.Highest;

//        System.AppDomain.CurrentDomain.UnhandledException += (sender, e) => UniLog.Error("UNHANDLED EXCEPTION:\n" + e.ExceptionObject
//            + "\n\nSender: " + sender);
//#endif
//    }

//    void StartupLog(string message) => deferredLogMessages.Add(message);

//    static string GetLinuxTempPath()
//    {
//        var xdg_cache = System.Environment.GetEnvironmentVariable("XDG_CACHE_HOME");

//        if (!string.IsNullOrEmpty(xdg_cache))
//            return System.IO.Path.Combine(xdg_cache, Application.productName);

//        var home = System.Environment.GetEnvironmentVariable("HOME");

//        if (!string.IsNullOrEmpty(home))
//            return System.IO.Path.Combine(home, ".cache", Application.productName);

//        return Application.temporaryCachePath;
//    }

//    IEnumerator Start()
//    {
//        Progress?.SetFixedPhase("Initializing Startup");

//#if !UNITY_WINRT
//        var args = System.Environment.GetCommandLineArgs();
//#else
//        var args = new string[0];
//#endif

//        // Setup initial startup log, buffering the messages, because we haven't created the log file yet
//        UniLog.OnLog += StartupLog;
//        UniLog.OnWarning += StartupLog;
//        UniLog.OnError += StartupLog;

//        var launchOptions = UnityLaunchOptions.GetLaunchOptions(args);

//        if (launchOptions.DataDirectory == null)
//            launchOptions.DataDirectory = Application.persistentDataPath;

//        if (launchOptions.CacheDirectory == null)
//        {
//#if UNITY_STANDALONE_LINUX || UNITY_ANDROID
//            launchOptions.CacheDirectory = GetLinuxTempPath();
//#else
//            launchOptions.CacheDirectory = Application.temporaryCachePath;
//#endif
//        }

//        if (launchOptions.LogsDirectory == null)
//            launchOptions.LogsDirectory = "Logs";

//        UniLog.MessagePrefix += () =>
//        {
//            var fpsStr = (SystemInfo?.FPS ?? -1).ToString("F0").PadLeft(3);
//            return System.DateTime.Now.ToMillisecondTimeString() + $" ({fpsStr} FPS)" + "\t";
//        };

//#if UNITY_STANDALONE
//        if (!System.IO.Directory.Exists(launchOptions.LogsDirectory))
//            System.IO.Directory.CreateDirectory(launchOptions.LogsDirectory);

//        int attempt = 0;

//        do
//        {
//            if(attempt == 100)
//            {
//                // just give up
//                Application.Quit();
//                yield break;
//            }

//            try
//            {
//                var logName = UniLog.GenerateLogName(Engine.VersionNumber, attempt > 0 ? $"-{attempt}" : "");

//                LogStream = System.IO.File.CreateText(System.IO.Path.Combine(launchOptions.LogsDirectory, logName));
//            }
//            catch (System.Exception ex)
//            {

//            }

//            attempt++;

//        } while (LogStream == null);

//        Logger = new ActionBlock<LogData>(data =>
//        {
//            if(data.message != null)
//                LogStream.WriteLine(data.message);
//            else
//            {
//                LogStream.Flush();
//                data.task.SetResult(true);
//            }
//        }, new ExecutionDataflowBlockOptions()
//        {
//            EnsureOrdered = true,
//            MaxDegreeOfParallelism = 1
//        });

//        UniLog.OnLog += str => Logger.Post(str);
//        UniLog.OnWarning += str => Logger.Post(str);
//        UniLog.OnError += str => Logger.Post(str);
//        UniLog.OnFlush += () =>
//        {
//            var task = new TaskCompletionSource<bool>();
//            Logger.Post(task);
//            task.Task.Wait();
//        };
//#else
//        UniLog.OnLog += str => Debug.Log(str);
//        UniLog.OnWarning += str => Debug.LogWarning(str);
//        UniLog.OnError += str => Debug.LogError(str);
//#endif

//        // Process any startup logs
//        UniLog.OnLog -= StartupLog;
//        UniLog.OnWarning -= StartupLog;
//        UniLog.OnError -= StartupLog;

//        foreach (var msg in deferredLogMessages)
//            Logger.Post(msg);

//        deferredLogMessages = null;

//        if (launchOptions.ForceScreenMode)
//            launchOptions.OutputDevice = HeadOutputDevice.Screen;

//#if UNITY_EDITOR
//        launchOptions.OutputDevice = EditorOutputDevice;
//#endif

//        Progress?.SetFixedPhase("Detecting output device");

//        PostProcessingInterface.SetupCamera = CameraInitializer.SetupCamera;

//        // Initialize Steam API early, so we can check remote play
//        SteamConnector.InitializeSteamAPI();

//        if (launchOptions.OutputDevice != HeadOutputDevice.Screen && SteamConnector.IsRemotePlayActive())
//        {
//            UniLog.Log("Steam Remote Play is active, defaulting to Screen mode");
//            launchOptions.OutputDevice = HeadOutputDevice.Screen;
//        }

//        if (launchOptions.OutputDevice == HeadOutputDevice.Autodetect)
//            yield return AutodetectOutputDevice(launchOptions);

//        yield return LoadOutputDevice(launchOptions.OutputDevice);

//        Progress?.SetFixedPhase("Creating FrooxEngineRunner");

//#if UNITY_EDITOR
//        string appPath = @".\";
//#else
//        string appPath = System.IO.Path.GetDirectoryName(Application.dataPath);
//#endif

//        var runner = gameObject.AddComponent<UnityFrooxEngineRunner.FrooxEngineRunner>();

//        runner.OnShutdownRequested = OnShutdownRequested;
//        runner.OnFinalizeShutdown = OnFinalizeShutdown;

//        runner.OverlayCamera = OverlayCamera;
//        bool isAot = false;

//#if ENABLE_IL2CPP
//        isAot = true;
//#endif

//        var systemInfoConnector = new SystemInfoConnector(launchOptions.OutputDevice, isAot);

//        yield return runner.Initialize(appPath, launchOptions, isAot, Progress, RegisterDrivers);

//        if (runner.Engine == null)
//            yield break;

//        SystemInfo = runner.Engine.SystemInfo;

//        if ((launchOptions.OutputDevice == HeadOutputDevice.StaticCamera360 ||
//            launchOptions.OutputDevice == HeadOutputDevice.Screen360) &&
//            launchOptions.CubemapResolution > 0)
//        {
//            var resolution = MathX.CeilToPowerOfTwo(launchOptions.CubemapResolution);

//            UniLog.Log($"Overriding cubemap resolution for 360 camera to: " + resolution);

//            runner.ScreenOutput.GetComponentInChildren<Camera360>().CubemapSize = resolution;
//        }

//        yield return new WaitForEndOfFrame();

//        lock (deferredInitializationActions)
//        {
//            IsInitialized = true;

//            foreach (var action in deferredInitializationActions)
//                action();

//            deferredInitializationActions.Clear();
//        }

//        Destroy(this);
//    }

//    void RegisterDrivers(Engine engine, UnityLaunchOptions options)
//    {
//        var keyboard = gameObject.AddComponent<KeyboardDriver>();
//        var mouse = new MouseDriver();
//        var gamepad = new GamepadDriver();
//        var touch = new TouchDriver();

//        engine.InputInterface.RegisterKeyboardDriver(keyboard);
//        engine.InputInterface.RegisterMouseDriver(mouse);
//        engine.InputInterface.RegisterInputDriver(gamepad);

//        engine.InputInterface.KeyboardActivated += () => Input.imeCompositionMode = IMECompositionMode.On;
//        engine.InputInterface.KeyboardDeactivated += () => Input.imeCompositionMode = IMECompositionMode.Off;

//#if UNITY_STANDALONE_WIN
//        engine.InputInterface.RegisterDragAndDropInterface(new WindowsDragAndDropDriver());

//        // Setup leap motion driver, even if it's not present, it handles the rest

//        if (!Engine.Config.DisableDesktop)
//        {
//            // Setup touch injection driver
//            engine.InputInterface.RegisterInputDriver(gameObject.AddComponent<TouchInjectionDriver>());
//            engine.InputInterface.RegisterInputDriver(new GameObject("Displays").AddComponent<DisplayDriver>());
//        }


//        if (options.OutputDevice == HeadOutputDevice.SteamVR)
//        {
//            engine.InputInterface.RegisterInputDriver(gameObject.AddComponent<ViveHandTrackingDriver>());
//        }
//#endif

//#if UNITY_ANDROID
//        engine.InputInterface.RegisterDragAndDropInterface(new TouchScreenKeyboardDriver());
//#endif

//    }

//    IEnumerator AutodetectOutputDevice(LaunchOptions launchOptions)
//    {
//#if !UNITY_ANDROID
//        var devices = new List<string>();

//#if UNITY_STANDALONE_WIN
//        devices.Add("oculus");
//#endif
//        devices.Add("openvr");
//        devices.Add("none");

//#if UNITY_STANDALONE_WIN
//        if (System.Diagnostics.Process.GetProcessesByName("vrcompositor").Length > 0 &&
//            System.Diagnostics.Process.GetProcessesByName("vrmonitor").Length > 0)
//        {
//            UniLog.Log("Detected SteamVR running, skipping Oculus Runtime initialization.");
//            devices.Remove("oculus");
//        }
//#endif

//        XRSettings.LoadDeviceByName(devices.ToArray());

//        yield return null;

//        XRSettings.enabled = true;
//#endif

//        switch (Application.platform)
//        {
//            case RuntimePlatform.Android:
//                if (XRDevice.isPresent)
//                    launchOptions.OutputDevice = HeadOutputDevice.OculusQuest;
//                else
//                    launchOptions.OutputDevice = HeadOutputDevice.Screen;
//                break;

//            // assume a PC platform
//            default:
//                if (XRDevice.isPresent)
//                {
//                    if (XRSettings.loadedDeviceName.ToLower().Contains("oculus"))
//                        launchOptions.OutputDevice = HeadOutputDevice.Oculus;
//                    else
//                        launchOptions.OutputDevice = HeadOutputDevice.SteamVR;
//                }
//                else
//                    launchOptions.OutputDevice = HeadOutputDevice.Screen;
//                break;
//        }

//        UniLog.Log("Autodetected device: " + launchOptions.OutputDevice);
//    }

//    IEnumerator LoadOutputDevice(HeadOutputDevice device)
//    {
//        switch (device)
//        {
//            case HeadOutputDevice.Oculus:
//                yield return LoadDevice("oculus");
//                break;

//            case HeadOutputDevice.OculusQuest:
//                // do not do anything for these, as they implicitly use the VR mode
//                break;

//            case HeadOutputDevice.SteamVR:
//            case HeadOutputDevice.WindowsMR:
//                yield return LoadDevice("openvr");
//                break;
//        }
//    }

//    IEnumerator LoadDevice(string newDevice)
//    {
//        if (string.Compare(UnityEngine.XR.XRSettings.loadedDeviceName, newDevice, true) != 0)
//        {
//            XRSettings.LoadDeviceByName(newDevice);
//            yield return null;
//            XRSettings.enabled = true;
//        }
//    }

//    static void OnShutdownRequested()
//    {
//#if !UNITY_EDITOR
//        Valve.VR.OpenVR.System?.AcknowledgeQuit_Exiting();
//#endif
//    }

//    static void OnFinalizeShutdown()
//    {
//        Debug.Log("OnFinalizeShutdown");

//        Logger.Post("<<< LOG END >>>");

//        Logger.Complete();
//        Logger.Completion.Wait();

//        LogStream.Flush();
//        LogStream.Close();

//        Debug.Log("OnFinalizeShutdown DONE");
//    }
//}
