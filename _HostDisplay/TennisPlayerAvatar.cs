using Fusion;
using UnityEngine;

public class TennisPlayerAvatar : NetworkBehaviour
{
    [Networked] public int PlayerID { get; set; } // 1 สำหรับ P1, 2 สำหรับ P2
    // ตัวแปรเก็บลูกเทนนิสที่ใช้ในการเล่น เพื่อใช้อ้างอิงระยะห่างตอนตีและการดักทางลูก
    public TennisBallHost activeBall;

    // จุดหมุนของไม้เทนนิส ใช้สำหรับรับค่าการหมุนจากมือถือ
    public Transform racketPivot;

    [Header("Movement")]
    public float autoMoveSpeed = 4.5f; // ความเร็วในการวิ่งไปดักลูกแบบอัตโนมัติ
    public float baseLineZ = -9.5f;    // ตำแหน่งเส้น Baseline ที่ตัวละครจะไปยืนรอประจำ

    // ระยะที่ถือว่าลูกอยู่ในวงสวิง (ถ้าลูกอยู่นอกระยะนี้ จะตีไม่โดน)
    public float hitRangeZ = 3.5f;     // ระยะตีโดนในแนวลึก
    public float hitRangeX = 4.0f;     // ระยะตีโดนในแนวซ้าย-ขวา

    // แคลอรีที่เผาผลาญจากการเหวี่ยงมือถือ (ซิงค์ผ่านเครือข่ายเพื่อให้มือถือและหน้าจอหลักเห็นตรงกัน)
    [Networked] public float CaloriesBurned { get; set; }

    // องศาการหมุนของมือถือที่ส่งผ่านเครือข่าย เพื่อให้ไม้เทนนิสหมุนตาม
    [Networked] public Quaternion NetworkRacketRot { get; set; }

    // ฟังก์ชันนี้ถูกเรียกเมื่อตัวละครถูกเสกขึ้นมาในห้อง (หลังจากมือถือเชื่อมต่อสำเร็จ)
    public override void Spawned()
    {
        NetworkRacketRot = Quaternion.identity; // รีเซ็ตการหมุนไม้เทนนิสให้เป็นปกติ
    }

    // ฟังก์ชันทำงานตามจังหวะเวลาของเครือข่าย ใช้สำหรับประมวลผล Input และอัปเดตสถานะต่างๆ
    public override void FixedUpdateNetwork()
    {
        // ดึงข้อมูล Input ที่ส่งมาจากมือถือ (NetworkMotionInput)
        if (GetInput(out NetworkMotionInput input))
        {
            // ตรวจสอบว่ามีข้อมูลการหมุนส่งมาหรือไม่ (ถ้าไม่เป็น 0 หมด แสดงว่ามีข้อมูล)
            if (input.DeviceRot.w != 0 || input.DeviceRot.x != 0 || input.DeviceRot.y != 0 || input.DeviceRot.z != 0)
            {
                // นำค่าการหมุนของมือถือไปเก็บไว้ในตัวแปร NetworkRacketRot
                NetworkRacketRot = input.DeviceRot;
            }

            // ถ้ามีการออกท่าทางการตี (เหวี่ยงมือถือ)
            if (input.Gesture != TennisGesture.None)
                ProcessSwing(input.Gesture, input.SwingPower); // ส่งไปคำนวณว่าตีโดนลูกหรือไม่
        }

        // ระบบวิ่งดักทางลูกแบบอัตโนมัติ (Auto-Movement)
        if (activeBall == null || activeBall.Object == null || !activeBall.Object.IsValid)
        {
            activeBall = FindAnyObjectByType<TennisBallHost>();
            return;
        }

        // กำหนดตำแหน่ง BaseLine ตามโหมดและฝั่งผู้เล่น
        float currentBaseLineZ = baseLineZ; // พื้นฐานคือยืนฝั่ง Z ติดลบ

        // ถ้าเป็นโหมดสู้กันเอง (>= 2) และเป็นผู้เล่นคนที่ 2 ให้กลับไปยืนฝั่ง Z บวก
        if (TennisNetworkBootstrapper.Instance != null && TennisNetworkBootstrapper.Instance.currentMode >= 2 && PlayerID == 2)
        {
            currentBaseLineZ = -baseLineZ;
        }

        bool isCoop = (TennisNetworkBootstrapper.Instance != null && TennisNetworkBootstrapper.Instance.currentMode == 1);

        if (activeBall.IsScored || !activeBall.IsMoving)
        {
            // ยืนประจำตำแหน่งแยกโซนซ้าย-ขวาอย่างเป็นธรรมชาติ (P1 ซ้าย, P2 ขวา)
            float homeX = 0f;
            if (isCoop)
            {
                homeX = (PlayerID == 1) ? -2.0f : 2.0f;
            }
            Vector3 centerPos = new Vector3(homeX, transform.position.y, currentBaseLineZ);
            transform.position = Vector3.Lerp(transform.position, centerPos, (autoMoveSpeed / 2) * Runner.DeltaTime);
        }
        else
        {
            // ตรวจสอบว่าลูกวิ่งมาหาฝั่งตัวละครตัวนี้หรือไม่
            bool isBallComingToMe = false;
            if (TennisNetworkBootstrapper.Instance != null && TennisNetworkBootstrapper.Instance.currentMode >= 2)
            {
                // โหมด PvP สู้กันเอง: P1 อยู่ฝั่งลบ (รอรับ Z < 0), P2 อยู่ฝั่งบวก (รอรับ Z > 0)
                isBallComingToMe = (PlayerID == 1 && activeBall.Velocity.z < 0) ||
                                   (PlayerID == 2 && activeBall.Velocity.z > 0);
            }
            else
            {
                // โหมด P1 vs AI หรือ Co-op: ผู้เล่นทุกคนยืนอยู่ฝั่งเดียวกัน (รอรับ Z < 0 เท่านั้น)
                // ถ้าลูกพุ่งไปฝั่งบวก แปลว่าพุ่งไปหา AI ไม่มีใครต้องวิ่งดัก
                isBallComingToMe = (activeBall.Velocity.z < 0);
            }

            if (isBallComingToMe)
            {
                float targetX = activeBall.transform.position.x;
                float targetZ = currentBaseLineZ;

                // เช็กลูกหยอด โดยแยกตามฝั่ง
                if (PlayerID == 1 && activeBall.transform.position.z > -4.0f && activeBall.Velocity.y < 0)
                    targetZ = -6.5f;
                else if (PlayerID == 2 && activeBall.transform.position.z < 4.0f && activeBall.Velocity.y < 0)
                    targetZ = 6.5f;

                // ล็อกเขตแดน (Clamp Z)
                // ไม่ว่าจะเป็นการดักลูกปกติ หรือดักลูกหยอด ตัวละครฝั่งแดนลบ (Z < 0) ต้องไม่วิ่งข้ามไปแดนบวก
                // และตัวละครฝั่งแดนบวก (โหมด PvP) ต้องไม่วิ่งข้ามมาแดนลบ
                if (currentBaseLineZ < 0)
                {
                    targetZ = Mathf.Clamp(targetZ, baseLineZ, -0.5f); // ห้ามเลยเส้นเน็ตไปทางบวก
                }
                else
                {
                    targetZ = Mathf.Clamp(targetZ, 0.5f, currentBaseLineZ); // ห้ามเลยเส้นเน็ตมาทางลบ
                }

                // สำหรับโหมด P1 P2 vs AI ให้วิ่งกลับไปเส้นหลังตัวเอง (ไม่วิ่งเข้าหากลางสนามทับซ้อนกัน)
                if (TennisNetworkBootstrapper.Instance != null && TennisNetworkBootstrapper.Instance.currentMode == 1)
                {
                    // จำกัดเขตให้ P1 ยืนซ้าย P2 ยืนขวาเวลาดักลูก
                    if (PlayerID == 1) targetX = Mathf.Min(targetX, -1.0f);
                    if (PlayerID == 2) targetX = Mathf.Max(targetX, 1.0f);
                }

                Vector3 targetPos = new Vector3(targetX, transform.position.y, targetZ);
                transform.position = Vector3.Lerp(transform.position, targetPos, autoMoveSpeed * Runner.DeltaTime);
            }

            else
            {
                // ถ้าลูกไม่ได้วิ่งมาหาฝั่งตัวเอง ให้ค่อยๆ เดินกลับไปตั้งหลักที่ BaseLine
                Vector3 centerPos = new Vector3(0, transform.position.y, currentBaseLineZ);

                // ในโหมดช่วยกัน ให้กลับไปยืนโซนใครโซนมัน (X: -2.5 และ 2.5) ไม่กลับไปกองตรงกลาง
                if (TennisNetworkBootstrapper.Instance != null && TennisNetworkBootstrapper.Instance.currentMode == 1)
                {
                    centerPos.x = (PlayerID == 1) ? -2.5f : 2.5f;
                }

                transform.position = Vector3.Lerp(transform.position, centerPos, (autoMoveSpeed / 2) * Runner.DeltaTime);
            }
        }
    }

    public override void Render()
    {
        // หมุนไม้เทนนิสตามค่า NetworkRacketRot (ค่าจากมือถือ) โดยใช้ Slerp เพื่อให้หมุนอย่างนุ่มนวล
        if (racketPivot != null && NetworkRacketRot != new Quaternion(0, 0, 0, 0))
        {
            racketPivot.localRotation = Quaternion.Slerp(
                racketPivot.localRotation,
                NetworkRacketRot,
                1f - Mathf.Exp(-15f * Time.deltaTime));
        }

        // อัปเดตคะแนนและแคลอรีที่เผาผลาญไปยังหน้าจอ UI ตลอดเวลา
        if (activeBall != null && activeBall.Object != null && activeBall.Object.IsValid && TennisUIManager.Instance != null)
        {
            TennisUIManager.Instance.UpdateScoreBoard(
                activeBall.PlayerScore,
                activeBall.AIScore,
                activeBall.CurrentRally,
                CaloriesBurned
            );
        }
    }

    // ฟังก์ชันตรวจสอบและประมวลผลเมื่อผู้เล่นเหวี่ยงมือถือ
    private void ProcessSwing(TennisGesture gesture, float power)
    {
        // เฉพาะเครื่อง Host เท่านั้นที่มีสิทธิ์ตัดสินว่าตีโดนหรือไม่
        if (!Object.HasStateAuthority) return;
        if (activeBall == null || !activeBall.Object.IsValid) return;

        // คำนวณแคลอรีที่เผาผลาญจากการเหวี่ยงและสะสมยอด
        CaloriesBurned += 0.08f * (1f + power);

        // การเสิร์ฟลูก (ลูกหยุดนิ่ง และหมดเวลาคูลดาวน์แล้ว)
        if (!activeBall.IsMoving && activeBall.ServeCooldown <= 0f)
        {
            // เช็กสิทธิ์ ว่าเป็นตาของ PlayerID นี้ที่จะต้องเสิร์ฟหรือไม่
            if (activeBall.ServingPlayerID == PlayerID)
            {
                // ผู้เล่นต้องทำท่าตบ (Smash) เท่านั้นถึงจะถือว่าเป็นการเสิร์ฟ
                if (gesture == TennisGesture.Smash)
                {
                    activeBall.HitByPlayer(TennisGesture.Smash, Mathf.Max(0.7f, power), 0f, PlayerID);
                }
            }
            return; // ถ้าลูกหยุดนิ่งอยู่ ใครที่ไม่ได้เสิร์ฟเหวี่ยงมือถือมาก็จะถูก Return ทิ้งตรงนี้ (ไม่ไปตีโต้)
        }

        // ตรวจสอบว่าลูกวิ่งมาหาฝั่งตัวละครตัวนี้หรือไม่ (ตอนตีโต้)
        bool isBallComingToMe = false;
        if (TennisNetworkBootstrapper.Instance != null && TennisNetworkBootstrapper.Instance.currentMode >= 2)
        {
            // โหมด PvP สู้กันเอง: P1 อยู่ฝั่งลบ (รอรับ Z < 0), P2 อยู่ฝั่งบวก (รอรับ Z > 0)
            isBallComingToMe = (PlayerID == 1 && activeBall.Velocity.z < 0) ||
                               (PlayerID == 2 && activeBall.Velocity.z > 0);
        }
        else
        {
            // โหมด P1 vs AI หรือ Co-op: ผู้เล่นทุกคนยืนอยู่ฝั่งเดียวกัน (รอรับ Z < 0 เท่านั้น)
            // ถ้าลูกพุ่งไปฝั่งบวก แปลว่าพุ่งไปหา AI ไม่มีใครต้องวิ่งดัก
            isBallComingToMe = (activeBall.Velocity.z < 0);
        }

        // การตีโต้ (ลูกกำลังพุ่งมา และพุ่งมาถูกฝั่ง)
        if (activeBall.IsMoving && isBallComingToMe)
        {
            // การตรวจสอบ HitRange ทำเหมือนเดิมได้ เพราะ Mathf.Abs ช่วยจัดการค่าติดลบให้แล้ว
            float diffZ = activeBall.transform.position.z - transform.position.z;
            float diffX = Mathf.Abs(activeBall.transform.position.x - transform.position.x);

            if (Mathf.Abs(diffZ) <= hitRangeZ && diffX <= hitRangeX)
            {
                float timingOffset = (PlayerID == 1) ? diffZ : -diffZ; // ปรับค่า Timing Offset ให้ถูกต้องตามฝั่ง

                // ส่ง PlayerID ไปให้ลูกบอลรู้ด้วยว่าใครตี
                activeBall.HitByPlayer(gesture, power, timingOffset, PlayerID);
            }
        }
    }
}