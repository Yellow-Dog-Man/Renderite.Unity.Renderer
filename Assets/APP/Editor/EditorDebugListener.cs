using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.IO;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class EditorDebugListener
{
    static UdpClient udp;

    class StartData
    {
        public string queueName;
        public long queueCapacity;

        public StartData(string queueName, long queueCapacity)
        {
            this.queueName = queueName;
            this.queueCapacity = queueCapacity;
        }
    }

    static StartData _startData;


    static EditorDebugListener()
    {
        Debug.Log("Listening for main process connections");

        udp = new UdpClient(Renderite.Shared.Helper.EDITOR_PORT);

        Task.Run(ReceiverLogic);

        EditorApplication.update += Update;
    }

    static void Update()
    {
        if (_startData == null)
            return;

        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            var manager = root.gameObject.GetComponentInChildren<Renderite.Unity.RenderingManager>();

            if (manager == null)
                continue;

            manager.EditorQueueName = _startData.queueName;
            manager.EditorQueueCapacity = _startData.queueCapacity;

            EditorApplication.EnterPlaymode();

            break;
        }

        _startData = null;
    }

    static async Task ReceiverLogic()
    {
        for(; ; )
        {
            var packet = await udp.ReceiveAsync();

            Debug.Log($"Received {packet.Buffer.Length} bytes from: " + packet.RemoteEndPoint);

            try
            {
                var stream = new MemoryStream(packet.Buffer);
                var reader = new BinaryReader(stream);

                var queueName = reader.ReadString();
                var queueCapacity = reader.ReadInt64();

                Debug.Log($"Received queue name: {queueName}, Capacity: {queueCapacity}. Starting...");

                _startData = new StartData(queueName, queueCapacity);
            }
            catch(System.Exception ex)
            {
                Debug.LogError($"Exception parsing packet data: " + ex);
            }
        }
    }
}
