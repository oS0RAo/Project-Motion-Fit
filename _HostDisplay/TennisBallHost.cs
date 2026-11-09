using Fusion;
using UnityEngine;

public class TennisBallHost : NetworkBehaviour
{
    // [Networked] คือตัวแปรที่จะถูกซิงค์ข้อมูลให้ตรงกันทุกเครื่องอัตโนมัติ
    [Networked] public Vector3 NetworkPos { get; set; }   // ตำแหน่งของลูกบอลในระบบเครือข่าย
    [Networked] public Vector3 Velocity { get; set; }     // ความเร็วและทิศทางของลูกบอล
    [Networked] public NetworkBool IsMoving { get; set; } // สถานะว่าลูกบอลกำลังเคลื่อนที่อยู่หรือไม่
    [Networked] public NetworkBool IsScored { get; set; } // สถานะว่ามีการทำคะแนนไปแล้วหรือยังในรอบนี้
    [Networked] public int PlayerScore { get; set; }      // คะแนนของผู้เล่น
    [Networked] public int AIScore { get; set; }          // คะแนนของบอท (AI)
    [Networked] public int CurrentRally { get; set; }     // จำนวนครั้งที่โต้ลูกกันไปมา (ยิ่งเยอะลูกยิ่งเร็ว)
    [Networked] public float ServeCooldown { get; set; }  // เวลานับถอยหลังก่อนจะเริ่มเสิร์ฟลูกใหม่
    [Networked] public int ServingPlayerID { get; set; } = 1;  // ใครคือคนเสิร์ฟในรอบนี้ (1 หรือ 2)

    [Header("Tennis Physics")]
    public float gravity = 25.0f;        // แรงโน้มถ่วงที่ดึงลูกบอลลง (ค่าสูงกว่าปกติเพื่อให้ลูกตกเร็วขึ้น)
    public float bounceFactor = 0.65f;   // แรงกระดอนเมื่อกระทบพื้น (0.65 คือกระดอนขึ้น 65% ของความเร็วเดิม)
    public float groundFriction = 0.8f;  // แรงเสียดทานเมื่อกระทบพื้น (ทำให้ลูกเคลื่อนที่ช้าลงตามแกน X, Z)
    public float aiMissChance = 0.2f;    // โอกาสที่บอทจะรับลูกพลาด (20%)

    [Header("Court Rules")]
    public float courtZ_AI = 11.0f;      // ขอบสนามฝั่ง AI (แกน Z)
    public float courtZ_Player = -11.0f; // ขอบสนามฝั่งผู้เล่น (แกน Z)
    public float courtWidth = 5.5f;      // ความกว้างของสนาม (แกน X ทั้งซ้ายและขวา)
    public float netHeight = 1.2f;       // ความสูงของตาข่าย
    public float netClipMargin = 0.15f;  // ระยะเฉียดตาข่าย (ถ้าลูกลอยสูงระดับนี้จะถือเป็นลูกพลิกเน็ต)

    private TennisPlayerAvatar _cachedPlayer; // เก็บอ้างอิงถึงตัวละครผู้เล่นเพื่อไม่ต้องค้นหาบ่อยๆ
    private TrailRenderer _trail;             // เอฟเฟกต์เส้นตามหลังลูกบอล

    // Spawned คล้ายๆ Start() ของ Unity แต่จะทำงานเมื่อ Object นี้ถูกสร้างขึ้นมาในระบบเครือข่าย
    public override void Spawned()
    {
        _trail = GetComponent<TrailRenderer>(); // ดึงคอมโพเนนต์ TrailRenderer มาเก็บไว้
        NetworkPos = transform.position;        // ตั้งค่าตำแหน่งเริ่มต้นให้ตรงกับในฉาก
        IsScored = false;                       // รีเซ็ตสถานะการทำคะแนน
        ServeCooldown = 0f;                     // รีเซ็ตเวลาคูลดาวน์
    }

    // ฟังก์ชันนี้คล้าย FixedUpdate แต่ทำงานตามจังหวะ (Tick) ของเครือข่าย Fusion
    public override void FixedUpdateNetwork()
    {
        // หากเครื่องนี้ไม่มีสิทธิ์ควบคุม State (ไม่ใช่ Host หรือไม่ใช่คนที่ได้สิทธิ์) ให้ข้ามไปเลย
        if (!Object.HasStateAuthority) return;

        // ในฟังก์ชัน FixedUpdateNetwork ตอนลูกหยุดนิ่ง (รอเสิร์ฟ)
        if (!IsMoving)
        {
            // ค้นหาตัวละครผู้เล่นทั้งหมดในฉาก
            TennisPlayerAvatar p1 = null;
            TennisPlayerAvatar p2 = null;

            foreach (var avatar in FindObjectsByType<TennisPlayerAvatar>(FindObjectsSortMode.None))
            {
                if (avatar.PlayerID == 1) p1 = avatar;
                if (avatar.PlayerID == 2) p2 = avatar;
            }

            // เลือกตัวละครที่จะถือลูกเสิร์ฟตาม ServingPlayerID
            TennisPlayerAvatar activeServer = (ServingPlayerID == 2 && p2 != null) ? p2 : p1;
            if (activeServer == null) activeServer = p1; // Fallback

            if (activeServer != null)
            {
                // บังคับลูกบอลมาลอยอยู่ตรงหน้า "เฉพาะคนที่ได้สิทธิ์เสิร์ฟ"
                NetworkPos = activeServer.transform.position + new Vector3(0.8f, 1.8f, 0.5f);
                transform.position = NetworkPos;
            }
            return;
        }

        // ช่วงเวลาพักรอหลังมีคนได้แต้ม
        if (ServeCooldown > 0)
        {
            ServeCooldown -= Runner.DeltaTime; // ลดเวลาคูลดาวน์ลงตามเวลาที่ผ่านไปในเครือข่าย
            if (ServeCooldown <= 0)
            {
                IsMoving = false; // หมดเวลาพัก ล็อกลูกให้อยู่นิ่งเพื่อเตรียมเสิร์ฟ
                Velocity = Vector3.zero; // เคลียร์ความเร็ว
                RPC_ResetTrail(); // ลบเส้นเอฟเฟกต์หางลูกบอล
                return;
            }
        }

        // ระบบจำลองฟิสิกส์ของลูกบอล
        float previousZ = NetworkPos.z; // เก็บค่าตำแหน่ง Z ก่อนเคลื่อนที่เพื่อใช้เช็กการข้ามเน็ต

        // คำนวณความเร็วแนวตั้งที่โดนแรงโน้มถ่วงดึงลง (v = u + at)
        Velocity += Vector3.down * gravity * Runner.DeltaTime;
        // อัปเดตตำแหน่งของลูกบอลตามความเร็ว (s = vt)
        NetworkPos += Velocity * Runner.DeltaTime;

        // เช็กการข้ามขอบเน็ต (ตรวจสอบว่าลูกข้ามเส้น Z=0 หรือไม่)
        if ((previousZ < 0 && NetworkPos.z >= 0) || (previousZ > 0 && NetworkPos.z <= 0))
        {
            // ถ้าความสูงของลูกต่ำกว่าขอบเน็ต (ชนตาข่ายเต็มๆ)
            if (NetworkPos.y < netHeight - netClipMargin)
            {
                NetworkPos = new Vector3(NetworkPos.x, NetworkPos.y, 0f); // บังคับลูกหยุดที่แนวเน็ต
                // คำนวณแรงสะท้อนกลับไปด้านหลังและตกลงพื้น
                Velocity = new Vector3(Velocity.x * 0.2f, -Velocity.y * 0.5f, Velocity.z * -0.2f);

                if (!IsScored) // ถ้ายังไม่มีคนได้แต้ม
                {
                    bool playerHitNet = previousZ < 0; // เช็กว่าผู้เล่นหรือบอทเป็นคนตีติดเน็ต
                    ScorePoint(!playerHitNet, "HIT THE NET!"); // ให้คะแนนฝั่งตรงข้าม
                }
                RPC_PlayBounceEffect(); // เล่นเอฟเฟกต์ชน
                return;
            }
            // ถ้าลูกลอยอยู่ในระยะเฉียดขอบบนตาข่าย (ลูกพลิกเน็ต)
            else if (NetworkPos.y >= netHeight - netClipMargin && NetworkPos.y <= netHeight + netClipMargin)
            {
                NetworkPos = new Vector3(NetworkPos.x, netHeight, 0f); // จับลูกวางไว้บนขอบเน็ตชั่วคราว

                bool playerHit = previousZ < 0; // ใครเป็นคนตีมา
                ScorePoint(playerHit, "NET CORD! LUCKY POINT!"); // ให้แต้มคนตีทันที ถือเป็นลูกโชคดีรับยาก

                // บังคับลูกร่วงลงหน้าเน็ตฝั่งตรงข้ามแบบหยอดๆ
                float pushZ = playerHit ? 1.5f : -1.5f;
                Velocity = new Vector3(Random.Range(-1.5f, 1.5f), 2.5f, pushZ);
                IsMoving = true;

                RPC_ShowHitFeedback("NET CORD!", false); // โชว์ข้อความ
                RPC_PlayBounceEffect();
                return;
            }
        }

        // เช็กการกระดอนพื้น (ถ้าลูกสูงน้อยกว่า 0.2 และกำลังตกลง)
        if (NetworkPos.y <= 0.2f && Velocity.y < 0)
        {
            NetworkPos = new Vector3(NetworkPos.x, 0.2f, NetworkPos.z); // ดันลูกขึ้นมาเหนือพื้นนิดนึงไม่ให้จม
            // คำนวณความเร็วใหม่
            Velocity = new Vector3(Velocity.x * groundFriction, -Velocity.y * bounceFactor, Velocity.z * groundFriction);
            RPC_PlayBounceEffect();

            // เช็กลูกออก (Out)
            if (!IsScored)
            {
                // ถ้าตำแหน่ง X เกินความกว้างสนาม หรือ Z เกินความยาวสนาม
                if (Mathf.Abs(NetworkPos.x) > courtWidth || Mathf.Abs(NetworkPos.z) > courtZ_AI + 1f)
                {
                    bool playerHitLast = Velocity.z > 0; // เช็กว่าใครตีมาพ้นแดน
                    ScorePoint(!playerHitLast, "OUT!"); // ให้คะแนนฝั่งตรงข้ามคนที่ตีออก
                }
            }
        }

        // การรับลูก (เช็กเมื่อยังไม่มีใครได้แต้ม)
        if (!IsScored)
        {
            // เช็กว่าเป็นโหมดสู้กันเองหรือไม่ (โหมด 2 หรือ 3)
            bool isPvP = TennisNetworkBootstrapper.Instance != null && TennisNetworkBootstrapper.Instance.currentMode >= 2;

            // ถ้าลูกพุ่งไปฝั่ง AI หรือ P2 และใกล้ถึงท้ายคอร์ต
            if (NetworkPos.z > courtZ_AI - 0.5f && Velocity.z > 0)
            {
                if (!isPvP) // เล่นกับ AI (โหมด 0 หรือ 1)
                {
                    // ถ้าโต้กันเกิน 3 ครั้ง AI มีโอกาสสุ่มรับพลาด
                    if (CurrentRally >= 3 && Random.value < aiMissChance)
                        ScorePoint(true, "POINT! PLAYER SCORES!");
                    else
                        ReturnBallFromAI(); // ถ้าไม่พลาด ให้ AI ตีสวนกลับ
                }
                else // โหมด P1 vs P2 (โหมด 2 หรือ 3)
                {
                    // ปล่อยให้ลูกวิ่งไปหา P2 ถ้าเลยระยะรับลูกไปแล้ว ค่อยให้ P1 ได้แต้ม
                    if (NetworkPos.z > courtZ_AI + 1.0f)
                        ScorePoint(true, "P1 SCORES!");
                }
            }

            // ถ้าลูกพุ่งมาฝั่งผู้เล่น (P1 หรือ P1+P2) และเลยท้ายคอร์ตไปแล้ว (ผู้เล่นรับไม่ทัน)
            if (NetworkPos.z < courtZ_Player - 1.0f && Velocity.z < 0)
            {
                ScorePoint(false, isPvP ? "P2 SCORES!" : "BOT SCORES!"); // AI หรือ P2 ได้แต้ม
            }
        }
    }

    public override void Render()
    {
        // อัปเดต Transform ให้ตรงกับตำแหน่งที่คำนวณไว้ใน Network
        transform.position = NetworkPos;
    }

    // ฟังก์ชันเมื่อผู้เล่นตีลูก
    public void HitByPlayer(TennisGesture gesture, float power, float timingOffset, int playerID)
    {
        if (IsScored && ServeCooldown > 0) return; // ถ้าได้แต้มแล้วกำลังรอกลับไปเสิร์ฟ ห้ามตี
        IsScored = false;
        CurrentRally++; // เพิ่มจำนวนการโต้ลูก

        // สุ่มเป้าหมายแกน X ให้อยู่ในระยะสนาม
        float targetX = Random.Range(-courtWidth + 1.0f, courtWidth - 1.0f);
        float bounceZ = 0f;
        float speedMult = 0.06f; // ตัวคูณเวลา (ยิ่งน้อย ลูกยิ่งลอยถึงเป้าหมายเร็ว)

        // กำหนดระยะตกของลูกตามท่าทางการตี
        if (gesture == TennisGesture.Smash)
        {
            bounceZ = Random.Range(5.0f, courtZ_AI - 1.0f);
            speedMult = 0.07f;
        }
        else
        {
            int zone = Random.Range(0, 3);
            if (zone == 0) { bounceZ = Random.Range(1.5f, 4.0f); speedMult = 0.08f; } // หน้าเน็ต
            else if (zone == 1) { bounceZ = Random.Range(4.5f, 7.5f); speedMult = 0.065f; } // กลางคอร์ต
            else { bounceZ = Random.Range(8.0f, courtZ_AI - 0.5f); speedMult = 0.055f; } // ท้ายคอร์ต
        }

        // ตรวจสอบว่าคนตีคือทีม B หรือไม่ (P2 ในโหมดดวล หรือ P3, P4 ในโหมด 4 คน)
        int mode = TennisNetworkBootstrapper.Instance != null ? TennisNetworkBootstrapper.Instance.currentMode : 0;
        bool isTeamB = (playerID == 2 && mode == 2) || (playerID >= 3);

        // สลับฝั่งจุดตกเป้าหมาย (bounceZ) หากเป็นการตีจากทีม B
        // เพราะทีม B ต้องตีกลับไปตกฝั่งทีม A (แดนลบ)
        if (isTeamB)
        {
            bounceZ = -bounceZ;
        }

        // คำนวณระยะทางจากลูกบอลปัจจุบันไปยังจุดตก
        float distance = Mathf.Abs(NetworkPos.z - bounceZ);
        float timeToBounce = distance * speedMult;

        // ระบบเพิ่มความเร็วเมื่อโต้ลูกกันนานขึ้น
        float rallySpeedFactor = Mathf.Clamp(1.3f - (CurrentRally * 0.05f), 0.65f, 1.3f);

        if (gesture != TennisGesture.Smash)
        {
            timeToBounce *= rallySpeedFactor;
            timeToBounce = Mathf.Clamp(timeToBounce, 0.70f, 1.6f);
        }
        else
        {
            timeToBounce = Mathf.Clamp(timeToBounce, 0.65f, 0.85f);
        }

        // ยิงลูกไปยังตำแหน่งที่คำนวณไว้ โดยกำหนดเวลาลอยในอากาศ
        LaunchToTarget(new Vector3(targetX, 0.2f, bounceZ), timeToBounce);

        // บังคับทิศทางลูก (Velocity) ให้พุ่งถูกทางเสมอ
        if (!isTeamB)
        {
            // ทีม A ตีไปหาทีม B (พุ่งไปแดนบวก)
            Velocity = new Vector3(Velocity.x, Velocity.y, Mathf.Abs(Velocity.z));
        }
        else
        {
            // ทีม B ตีกลับไปหาทีม A (พุ่งไปแดนลบ)
            Velocity = new Vector3(Velocity.x, Velocity.y, -Mathf.Abs(Velocity.z));
        }

        // กำหนดข้อความ UI ตอนตีโดน
        string grade = Random.value > 0.5f ? "PERFECT!" : "GOOD!";
        if (gesture == TennisGesture.Smash) grade = "POWER SMASH!";
        RPC_ShowHitFeedback(grade, timeToBounce < 0.75f);
    }

    // ฟังก์ชันให้ AI ตีสวนกลับ
    private void ReturnBallFromAI()
    {
        float targetX = Random.Range(-courtWidth + 1.0f, courtWidth - 1.0f);
        float bounceZ = 0f;
        float speedMult = 0.06f;

        // AI สุ่มโซนที่จะตีตกฝั่งผู้เล่น (แกน Z ติดลบ)
        int zone = Random.Range(0, 3);
        if (zone == 0) { bounceZ = Random.Range(-4.0f, -1.5f); speedMult = 0.08f; }
        else if (zone == 1) { bounceZ = Random.Range(-7.5f, -4.5f); speedMult = 0.065f; }
        else { bounceZ = Random.Range(courtZ_Player + 0.5f, -8.0f); speedMult = 0.055f; }

        float distance = Mathf.Abs(NetworkPos.z - bounceZ);
        float rallySpeedFactor = Mathf.Clamp(1.3f - (CurrentRally * 0.05f), 0.65f, 1.3f);
        float timeToBounce = Mathf.Clamp(distance * speedMult * rallySpeedFactor, 0.70f, 1.6f);

        LaunchToTarget(new Vector3(targetX, 0.2f, bounceZ), timeToBounce);
    }

    // ฟังก์ชันจัดการคะแนนเมื่อมีการทำแต้มสำเร็จ
    private void ScorePoint(bool playerWon, string message)
    {
        IsScored = true;
        CurrentRally = 0; // รีเซ็ตจำนวนการโต้
        ServeCooldown = 2.5f; // ให้ลูกกลิ้งไป 2.5 วินาทีก่อนวาร์ปกลับไปเริ่มใหม่

        if (playerWon)
        {
            PlayerScore += 15;
            RPC_ShowScoreResult(message, true);
        }
        else
        {
            AIScore += 15;
            RPC_ShowScoreResult(message, false);
        }
    }

    // ฟังก์ชันคำนวณ Projectile เพื่อหาค่า Velocity ที่ต้องใช้ในการยิงให้ลงเป้าหมาย
    public void LaunchToTarget(Vector3 bounceTarget, float timeToBounce)
    {
        Vector3 displacement = bounceTarget - NetworkPos; // หาระยะกระจัดรวม (จุดหมาย - จุดเริ่มต้น)
        Vector3 horizontalDisp = new Vector3(displacement.x, 0, displacement.z); // แยกระยะกระจัดเฉพาะแนวนอน

        // ความเร็วแนวนอน = ระยะทางแนวนอน / เวลา
        Vector3 velXZ = horizontalDisp / timeToBounce;
        // ความเร็วแนวตั้ง = (ระยะทางแนวตั้ง + (0.5 * แรงโน้มถ่วง * เวลา^2)) / เวลา
        float velY = (displacement.y + 0.5f * gravity * timeToBounce * timeToBounce) / timeToBounce;

        // รวมความเร็วทั้ง 3 แกน
        Velocity = new Vector3(velXZ.x, velY, velXZ.z);
        IsMoving = true;

        RPC_ResetTrail(); // รีเซ็ตเส้นตามหลังลูก
    }

    // กลุ่มฟังก์ชัน [Rpc] คือคำสั่งที่จะถูกสั่งจากเครื่อง Host 
    // ให้ไปรันบนทุกๆ คนที่อยู่ในห้องเดียวกัน ใช้เพื่อซิงค์เอฟเฟกต์ / UI

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_ResetTrail() { if (_trail != null) _trail.Clear(); } // สั่งลบเส้นหางลูกบนทุกเครื่อง

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_ShowHitFeedback(string text, NetworkBool isHighPower)
    {
        // สั่งโชว์ Popup ระดับความแรงบนหน้าจอทุกคน
        if (TennisUIManager.Instance != null)
            TennisUIManager.Instance.ShowPopup(text, isHighPower ? new Color(1f, 0.6f, 0f) : Color.green);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_ShowScoreResult(string text, NetworkBool playerWon)
    {
        // สั่งโชว์ข้อความคนได้แต้มบนหน้าจอทุกคน
        if (TennisUIManager.Instance != null)
            TennisUIManager.Instance.ShowPopup(text, playerWon ? Color.cyan : Color.red);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_PlayBounceEffect()
    {
        // รอใส่ AudioSource, Partical Effect ตอนลูกตกพื้น
    }
}