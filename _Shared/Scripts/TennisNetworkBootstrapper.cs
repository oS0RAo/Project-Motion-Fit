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
    public NetworkObject aiAvatarPrefab;
    public NetworkObject playerAvatarPrefab;       // ตัวละครผู้เล่นที่จะถูกสร้างเมื่อมือถือเชื่อมต่อสำเร็จ
    public NetworkObject tennisBallPrefab;         // ลูกเทนนิสที่จะถูกสร้างโดย Host
    public string defaultRoomCode = "FIT1";           // ชื่อห้องเริ่มต้นสำหรับให้เชื่อมต่อหากัน

    [Header("Lobby Setup")]
    public NetworkObject lobbyPlayerPrefab; // ลาก Prefab ของ LobbyPlayerSync มาใส่

    // Dictionary ไว้เก็บข้อมูลผู้เล่นใน Lobby
    public Dictionary<PlayerRef, LobbyPlayerSync> LobbyPlayers = new Dictionary<PlayerRef, LobbyPlayerSync>();

    public NetworkObject menuSyncPrefab;

    [Header("Mobile Reference (ใส่เฉพาะฝั่งมือถือ)")]
    public MobileTennisSensor mobileSensor;           // อ้างอิงไปยังสคริปต์อ่านค่าเซ็นเซอร์ Gyroscope บนมือถือ

    private NetworkRunner _runner;                    // ตัวแปรเก็บ Instance ของเครือข่ายที่กำลังรันอยู่
    private Dictionary<PlayerRef, NetworkObject> _spawnedPlayers = new Dictionary<PlayerRef, NetworkObject>(); // ดิกชันนารีเก็บรายชื่อผู้เล่นและตัวละครที่ถูกเสกขึ้นมา
    private static TennisNetworkBootstrapper _instance;
    public int currentMode = 0;  // เพิ่มตัวแปรเก็บโหมด (0=1vAI, 1=2vAI, 2=1v1, 3=1v1)

    // ให้สคริปต์อื่น (เช่น TennisMainMenu) เรียกใช้ตัวหลักที่รอดจากการถูกทำลายได้
    public static TennisNetworkBootstrapper Instance => _instance;

    // รีเซ็ตค่า static ทุกครั้งที่กด Play (กันกรณีปิด Domain Reload ใน Enter Play Mode Options
    // ซึ่งจะทำให้ _instance ค้างข้ามรอบ แล้ว Bootstrapper ตัวใหม่ถูก Destroy ทิ้งทุกครั้ง)
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _instance = null;
    }

    private void Awake()
    {
        if (_instance == null)
        {
            // ถ้ายังไม่มี Bootstrapper ในเกม ให้ตั้งตัวนี้เป็นตัวหลัก
            _instance = this;
            // สั่ง Unity ว่าห้ามทำลายออบเจกต์นี้ทิ้งเด็ดขาดแม้จะเปลี่ยน Scene
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            // ถ้าผู้เล่นกดย้อนกลับมาหน้า Main Menu แล้วเจอ Bootstrapper ตัวเก่าอยู่ ให้ทำลายตัวที่เกิดใหม่ทิ้ง
            Destroy(gameObject);
        }
    }

    // ปุ่ม "Start Host" บนหน้าจอ
    public async void StartHostScreen(bool isPublic = true)
    {
        // เช็ค Prefab ก่อนสร้าง Runner เพื่อไม่ให้มี NetworkRunnerPrefab(Clone) ค้างอยู่ใน Hierarchy เมื่อ error
        if (menuSyncPrefab == null)
        {
            Debug.LogError("MenuSyncPrefab ขาดหายไป (Null) กรุณาลากใส่ใน Inspector ใหม่");
            return;
        }

        // ถ้าเปิดห้องไว้แล้ว (เช่น เปิดรอตอน Start() แล้วมากดปุ่ม Start Host อีกที)
        // ไม่ต้องสร้าง Runner ซ้ำ ให้แค่อัปเดตสถานะ Public/Private ของห้องเดิม
        if (_runner != null)
        {
            if (_runner.IsRunning && _runner.IsServer && _runner.SessionInfo != null)
                _runner.SessionInfo.IsVisible = isPublic;
            return;
        }

        _runner = Instantiate(runnerPrefab); // สร้างระบบเครือข่ายขึ้นมาในฉาก
        _runner.AddCallbacks(this);          // ลงทะเบียนให้สคริปต์นี้รับฟังเหตุการณ์จาก _runner
        _runner.ProvideInput = false;        // จอหลัก(Host) แค่ประมวลผลและแสดงภาพ ไม่ได้ส่งค่าการควบคุม

        // เริ่มต้นห้องในฐานะ Host แบบ Asynchronous
        var result = await _runner.StartGame(new StartGameArgs()
        {
            GameMode = GameMode.Host,        // เป็นเจ้าของห้อง
            SessionName = defaultRoomCode,   // ชื่อห้อง
            IsVisible = isPublic, // true = โชว์ใน Lobby Hub, false = ซ่อนเป็น Private
            IsOpen = true,        // ให้คนอื่น Join ได้
            SceneManager = _runner.gameObject.AddComponent<NetworkSceneManagerDefault>() // ตัวจัดการซิงค์โหลดฉาก
        });

        // ถ้าเปิดห้องไม่สำเร็จ ให้ล้าง Runner ทิ้ง เพื่อให้กดเปิดใหม่ได้
        if (!result.Ok)
        {
            Debug.LogError($"เปิดห้องไม่สำเร็จ: {result.ShutdownReason}");
            if (_runner != null) Destroy(_runner.gameObject);
            _runner = null;
            return;
        }

        // เมื่อสร้างห้องสำเร็จ ให้ Host เป็นคนสร้างลูกเทนนิสลงสนาม
        if (_runner.IsServer)
        {
            // เสกลูกเทนนิส
            //_runner.Spawn(tennisBallPrefab, new Vector3(0, 1.5f, -6.5f), Quaternion.identity);
            _runner.Spawn(menuSyncPrefab, Vector3.zero, Quaternion.identity);
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
        if (runner.IsServer)
        {
            // เครื่อง PC (Host ตัวจริงของ Server) เป็นแค่จอมอนิเตอร์ ไม่ต้องเสกตัวละครใน Lobby ให้ตัวเอง
            if (player == runner.LocalPlayer) return;

            int slot = LobbyPlayers.Count + 1; // นับลำดับเฉพาะมือถือที่เข้ามา

            NetworkObject obj = runner.Spawn(lobbyPlayerPrefab, Vector3.zero, Quaternion.identity, player);
            LobbyPlayerSync lobbyPlayer = obj.GetComponent<LobbyPlayerSync>();

            // ให้มือถือเครื่องแรกสุด (Slot 1) เป็น Room Leader (P1) ที่คุมเกมได้
            bool isRoomLeader = (slot == 1);

            lobbyPlayer.PlayerName = isRoomLeader ? "Host Player" : $"Player {slot}";
            lobbyPlayer.SlotIndex = slot;
            lobbyPlayer.IsHost = isRoomLeader; // ตอนนี้มือถือเครื่องแรกจะกลายเป็น Host แล้ว!
            lobbyPlayer.IsReady = isRoomLeader;

            LobbyPlayers.Add(player, lobbyPlayer);
        }
    }
    // Callback ทำงานเมื่อมีผู้เล่นกดออกหรือหลุดการเชื่อมต่อ
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        if (runner.IsServer && LobbyPlayers.TryGetValue(player, out LobbyPlayerSync lobbyPlayer))
        {
            runner.Despawn(lobbyPlayer.Object);
            LobbyPlayers.Remove(player);
        }
    }

    // Callback ทำงานทุกจังหวะ Tick (บนมือถือ) เพื่อรวบรวมค่าเซ็นเซอร์ส่งเข้าเครือข่าย
    public void OnInput(NetworkRunner runner, NetworkInput input)
    {
        // ตรวจสอบว่าใส่สคริปต์เซ็นเซอร์มาหรือยัง
        if (mobileSensor == null)
        {
            mobileSensor = FindAnyObjectByType<MobileTennisSensor>();
            // ถ้าหาไม่เจอจริงๆ (เช่นกำลังโหลดฉาก หรือไม่ได้วางสคริปต์ไว้) ให้แสดงแค่ LogWarning แล้วข้ามไปก่อน
            if (mobileSensor == null)
            {
                Debug.LogWarning("[Bootstrapper] ยังหา MobileSensor ในฉากไม่เจอ รอสักครู่...");
                return;
            }
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
        if (_spawnedPlayers.ContainsKey(controllerPlayer)) return;

        int assignedPlayerID = _spawnedPlayers.Count + 1;
        Vector3 spawnPos = new Vector3(0f, 1f, -8f);
        Quaternion spawnRot = Quaternion.identity;

        // กำหนดจุดเกิดและการหันหน้าตามโหมด
        if (currentMode == 1) // 1=P1+P2 vs AI
        {
            if (assignedPlayerID == 1) spawnPos = new Vector3(-2.5f, 1f, -8f);
            else if (assignedPlayerID == 2) spawnPos = new Vector3(2.5f, 1f, -8f);
        }
        else if (currentMode == 2) // 2=P1 vs P2
        {
            if (assignedPlayerID == 1) spawnPos = new Vector3(0f, 1f, -8f);
            else if (assignedPlayerID == 2) { spawnPos = new Vector3(0f, 1f, 8f); spawnRot = Quaternion.Euler(0, 180, 0); }
        }
        else if (currentMode == 3) // 3=P1+P2 vs P3+4 (โหมด 2v2)
        {
            if (assignedPlayerID == 1) spawnPos = new Vector3(-2.5f, 1f, -8f); // ทีม A ซ้าย
            else if (assignedPlayerID == 2) spawnPos = new Vector3(2.5f, 1f, -8f); // ทีม A ขวา
            else if (assignedPlayerID == 3) { spawnPos = new Vector3(2.5f, 1f, 8f); spawnRot = Quaternion.Euler(0, 180, 0); } // ทีม B ซ้าย(หันกลับ)
            else if (assignedPlayerID == 4) { spawnPos = new Vector3(-2.5f, 1f, 8f); spawnRot = Quaternion.Euler(0, 180, 0); } // ทีม B ขวา(หันกลับ)
        }

        NetworkObject avatar = runner.Spawn(playerAvatarPrefab, spawnPos, spawnRot, controllerPlayer);

        TennisPlayerAvatar avatarScript = avatar.GetComponent<TennisPlayerAvatar>();
        if (avatarScript != null)
        {
            avatarScript.PlayerID = assignedPlayerID;
            avatarScript.activeBall = FindAnyObjectByType<TennisBallHost>();
        }

        _spawnedPlayers.Add(controllerPlayer, avatar);
    }

    // Callback ด้านล่างนี้บังคับต้องมีตาม Interface INetworkRunnerCallbacks 
    // แต่ไม่ได้เขียนลอจิกอะไรไว้ (ปล่อยว่าง)

    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }

    // เมื่อเครือข่ายปิดตัวลง ให้เคลียร์ _runner เพื่อให้เปิดห้องใหม่ได้
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        if (_runner == runner) _runner = null;
    }

    public void OnSceneLoadDone(NetworkRunner runner)
    {
        // เช็กว่าตอนนี้ Host โหลดเข้า Scene "Tennis_Gameplay" เสร็จหรือยัง
        if (runner.IsServer)
        {
            int currentSceneBuildIndex = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;
            int gameplaySceneIndex = UnityEngine.SceneManagement.SceneUtility.GetBuildIndexByScenePath("Tennis_Gameplay");

            // ถ้าโหลดมาถึงหน้า Gameplay แล้ว ค่อยเสกลูกบอล
            if (currentSceneBuildIndex == gameplaySceneIndex)
            {
                // ตรวจสอบก่อนว่าลูกบอลยังไม่ถูกเสก เพื่อป้องกันการเสกซ้ำ
                var existingBall = FindAnyObjectByType<TennisBallHost>();
                if (existingBall == null)
                {
                    runner.Spawn(tennisBallPrefab, new Vector3(0, 1.5f, -6.5f), Quaternion.identity);
                }

                // เคลียร์รายชื่อตัวละครเก่าก่อนเริ่มเกมใหม่
                _spawnedPlayers.Clear();

                // เสกตัวละครให้มือถือทุกเครื่องที่เข้ามาในห้องแล้ว
                foreach (var player in runner.ActivePlayers)
                {
                    // ข้าม Host ไป เพราะ Host ไม่มีตัวละครให้เล่น
                    if (player == runner.LocalPlayer && runner.GameMode == GameMode.Host) continue;

                    SpawnAvatarForPlayer(runner, player);
                }

                // เสก AI ลงสนามตามโหมดที่เลือก (ทำต่อจากเสกผู้เล่นเสร็จแล้วในหน้า Gameplay)
                if (currentMode == 0) // 1v1 กับบอท (บอท 1 ตัว)
                {
                    NetworkObject ai = runner.Spawn(aiAvatarPrefab, new Vector3(0, 1f, 8f), Quaternion.Euler(0, 180, 0));
                    if (ai != null) ai.GetComponent<TennisAIAvatar>().AIIndex = 1;
                }
                else if (currentMode == 1) // โหมดคู่หู 2v2 กับบอท (บอท 2 ตัว)
                {
                    NetworkObject ai1 = runner.Spawn(aiAvatarPrefab, new Vector3(-2.5f, 1f, 8f), Quaternion.Euler(0, 180, 0));
                    if (ai1 != null) ai1.GetComponent<TennisAIAvatar>().AIIndex = 1; // บอทคุมซ้าย

                    NetworkObject ai2 = runner.Spawn(aiAvatarPrefab, new Vector3(2.5f, 1f, 8f), Quaternion.Euler(0, 180, 0));
                    if (ai2 != null) ai2.GetComponent<TennisAIAvatar>().AIIndex = 2; // บอทคุมขวา
                }
            }
        }
    }

    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ReadOnlySpan<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
}