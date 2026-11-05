using System;
using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using UnityEngine;

// INetworkRunnerCallbacks ทำให้สคริปต์นี้รับเหตุการณ์ต่างๆ จากเครือข่ายได้ (เช่น มีคนเข้าห้อง, หลุดเชื่อมต่อ, รับค่า Input)
public class TennisNetworkBootstrapper : MonoBehaviour, INetworkRunnerCallbacks
{
    [Header("Network Setup")]
    public NetworkRunner runnerPrefab;                // Prefab ของ NetworkRunner ที่เป็นหัวใจหลักในการคุมเครือข่าย
    public NetworkPrefabRef playerAvatarPrefab;       // ตัวละครผู้เล่นที่จะถูกสร้างเมื่อมือถือเชื่อมต่อสำเร็จ
    public NetworkPrefabRef tennisBallPrefab;         // ลูกเทนนิสที่จะถูกสร้างโดย Host
    public string defaultRoomCode = "FIT1";           // ชื่อห้องเริ่มต้นสำหรับให้เชื่อมต่อหากัน

    [Header("Mobile Reference (ใส่เฉพาะฝั่งมือถือ)")]
    public MobileTennisSensor mobileSensor;           // อ้างอิงไปยังสคริปต์อ่านค่าเซ็นเซอร์ Gyroscope บนมือถือ

    private NetworkRunner _runner;                    // ตัวแปรเก็บ Instance ของเครือข่ายที่กำลังรันอยู่
    private Dictionary<PlayerRef, NetworkObject> _spawnedPlayers = new Dictionary<PlayerRef, NetworkObject>(); // ดิกชันนารีเก็บรายชื่อผู้เล่นและตัวละครที่ถูกเสกขึ้นมา

    // ปุ่ม "Start Host" บนหน้าจอ
    public async void StartHostScreen()
    {
        _runner = Instantiate(runnerPrefab); // สร้างระบบเครือข่ายขึ้นมาในฉาก
        _runner.AddCallbacks(this);          // ลงทะเบียนให้สคริปต์นี้รับฟังเหตุการณ์จาก _runner
        _runner.ProvideInput = false;        // จอหลัก(Host) แค่ประมวลผลและแสดงภาพ ไม่ได้ส่งค่าการควบคุม

        // เริ่มต้นห้องในฐานะ Host แบบ Asynchronous
        await _runner.StartGame(new StartGameArgs()
        {
            GameMode = GameMode.Host,        // เป็นเจ้าของห้อง
            SessionName = defaultRoomCode,   // ชื่อห้อง
            SceneManager = _runner.gameObject.AddComponent<NetworkSceneManagerDefault>() // ตัวจัดการซิงค์โหลดฉาก
        });

        // เมื่อสร้างห้องสำเร็จ ให้ Host เป็นคนสร้างลูกเทนนิสลงสนาม
        if (_runner.IsServer)
        {
            // เสกลูกเทนนิส
            _runner.Spawn(tennisBallPrefab, new Vector3(0, 1.5f, -6.5f), Quaternion.identity);
        }
    }

    // ผูกปุ่ม "Connect Remote" บนจอมือถือ
    public async void JoinFromMobile(string roomCode)
    {
        _runner = Instantiate(runnerPrefab);
        _runner.AddCallbacks(this);
        _runner.ProvideInput = true; // ฝั่งมือถือต้องส่งค่า Input เพราะทำหน้าที่เป็นจอยควบคุม

        // เข้าห้องในฐานะ Client
        await _runner.StartGame(new StartGameArgs()
        {
            GameMode = GameMode.Client,
            SessionName = string.IsNullOrEmpty(roomCode) ? defaultRoomCode : roomCode, // ถ้าไม่กรอกชื่อห้องมา ให้ใช้ห้องเริ่มต้น
            SceneManager = _runner.gameObject.AddComponent<NetworkSceneManagerDefault>()
        });
    }

    // Callback ทำงานเมื่อมีผู้เล่นใหม่เชื่อมต่อเข้ามาในห้อง
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        if (!runner.IsServer) return;           // ให้เฉพาะ Host เป็นคนจัดการเสกตัวละคร
        if (player == runner.LocalPlayer) return; // ไม่ต้องเสกตัวละครให้ตัวเอง (Host ไม่มีตัวละคร)

        // ถ้าคนที่เข้าห้องมาเป็น Client (มือถือ) ให้เสกตัวละครให้
        SpawnAvatarForPlayer(runner, player);
    }


    // Callback ทำงานทุกจังหวะ Tick (บนมือถือ) เพื่อรวบรวมค่าเซ็นเซอร์ส่งเข้าเครือข่าย
    public void OnInput(NetworkRunner runner, NetworkInput input)
    {
        // ตรวจสอบว่าใส่สคริปต์เซ็นเซอร์มาหรือยัง
        if (mobileSensor == null)
        {
            Debug.LogWarning("[Bootstrapper] mobileSensor เป็น null: ลืมลากใส่ใน Inspector ฝั่งมือถือ?");
            return;
        }

        // สร้างโครงสร้างข้อมูล Input แบบกำหนดเอง (NetworkMotionInput น่าจะถูกนิยามไว้ในสคริปต์อื่น)
        var motionData = new NetworkMotionInput
        {
            Gesture = mobileSensor.PendingGesture,       // ท่าทางการตีที่กำลังจะเกิดขึ้น
            SwingPower = mobileSensor.PendingPower,      // ความแรงที่เหวี่ยงมือถือ
            DeviceRot = mobileSensor.CalibratedRotation  // องศาการหมุนของมือถือที่ผ่านการคาลิเบรตแล้ว
        };

        input.Set(motionData);             // ยัดข้อมูลใส่ตัวแปร input เพื่อส่งผ่านเครือข่ายไปยัง Host
        mobileSensor.ClearPendingSwing();  // ล้างค่ารอตีทิ้ง เพื่อเตรียมรับการเหวี่ยงครั้งใหม่
    }

    // ฟังก์ชันจัดการเสกตัวละครให้กับมือถือ
    private void SpawnAvatarForPlayer(NetworkRunner runner, PlayerRef controllerPlayer)
    {
        // ล้างตัวละครเก่าที่มีอยู่ออกไปก่อน (เผื่อหลุดแล้วต่อใหม่)
        foreach (var kvp in _spawnedPlayers) runner.Despawn(kvp.Value);
        _spawnedPlayers.Clear();

        Vector3 spawnPos = new Vector3(0f, 1f, -8f); // ตำแหน่งยืนของผู้เล่น
        // เสกตัวละครพร้อมกำหนดให้มือถือ (controllerPlayer) เป็นผู้มีสิทธิ์ควบคุม (Input Authority)
        NetworkObject avatar = runner.Spawn(playerAvatarPrefab, spawnPos, Quaternion.identity, controllerPlayer);

        // ดึงสคริปต์ของตัวละครมาเพื่อผูกข้อมูลลูกเทนนิสเข้าไปให้รู้จัก
        TennisPlayerAvatar avatarScript = avatar.GetComponent<TennisPlayerAvatar>();
        avatarScript.activeBall = FindFirstObjectByType<TennisBallHost>(); // หาบอลที่มีอยู่ในฉากไปใส่ให้

        // บันทึกลงดิกชันนารีว่าผู้เล่นคนนี้ ใช้ตัวละครตัวไหน
        _spawnedPlayers.Add(controllerPlayer, avatar);
    }

    // Callback ทำงานเมื่อมีผู้เล่นกดออกหรือหลุดการเชื่อมต่อ
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        // ค้นหาในดิกชันนารีว่าผู้เล่นที่หลุดไปมีตัวละครค้างอยู่ไหม
        if (_spawnedPlayers.TryGetValue(player, out NetworkObject obj))
        {
            runner.Despawn(obj);            // ลบตัวละครออกจากเครือข่าย
            _spawnedPlayers.Remove(player); // ลบข้อมูลออกจากดิกชันนารี
        }
    }

    // Callback ด้านล่างนี้บังคับต้องมีตาม Interface INetworkRunnerCallbacks 
    // แต่ไม่ได้เขียนลอจิกอะไรไว้ (ปล่อยว่าง)

    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ReadOnlySpan<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnSceneLoadDone(NetworkRunner runner) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
}