using Fusion;
using UnityEngine;

public class TennisPlayerAvatar : NetworkBehaviour
{
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

        // อัปเดตข้อความบนหน้าจอหลักว่ามีมือถือเชื่อมต่อแล้ว พร้อมสอนวิธีเริ่มเกม (ให้ทำท่า Smash เพื่อเสิร์ฟ)
        if (TennisUIManager.Instance != null && TennisUIManager.Instance.statusText != null)
            TennisUIManager.Instance.statusText.text = "MOBILE CONNECTED!\nSmash down to Serve!";
    }

    // ฟังก์ชันทำงานตามจังหวะเวลาของเครือข่าย ใช้สำหรับประมวลผล Input และอัปเดตสถานะต่างๆ
    public override void FixedUpdateNetwork()
    {
        // 1. ดึงข้อมูล Input ที่ส่งมาจากมือถือ (NetworkMotionInput)
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

        // 2. ระบบวิ่งดักทางลูกแบบอัตโนมัติ (Auto-Movement)

        // ถ้ายังหาลูกเทนนิสไม่เจอ ให้ค้นหาจากในฉากก่อน
        if (activeBall == null || activeBall.Object == null || !activeBall.Object.IsValid)
        {
            activeBall = FindFirstObjectByType<TennisBallHost>();
            return;
        }

        // ถ้ารอบนั้นจบแล้ว (มีคนได้แต้ม) หรือลูกหยุดนิ่ง (กำลังรอเสิร์ฟ)
        if (activeBall.IsScored || !activeBall.IsMoving)
        {
            // ให้ตัวละครเดินกลับไปยืนรอตรงกลางเส้นหลังสนาม (ค่อยๆ เดินกลับไปด้วยความเร็วครึ่งหนึ่ง)
            Vector3 centerPos = new Vector3(0, transform.position.y, baseLineZ);
            transform.position = Vector3.Lerp(transform.position, centerPos, (autoMoveSpeed / 2) * Runner.DeltaTime);
        }
        // ถ้าลูกกำลังพุ่งมาทางฝั่งผู้เล่น (Velocity.z < 0)
        else if (activeBall.Velocity.z < 0)
        {
            // เป้าหมายที่ตัวละครจะวิ่งไปคือตำแหน่งแกน X ของลูก (วิ่งตามซ้ายขวา)
            float targetX = activeBall.transform.position.x;
            float targetZ = baseLineZ; // ยืนรอที่เส้นหลังเป็นหลัก

            // แต่ถ้าลูกมาใกล้เกินไป (z > -4) และลูกกำลังตกลงพื้น (Velocity.y < 0) แปลว่าเป็นลูกหยอด
            if (activeBall.transform.position.z > -4.0f && activeBall.Velocity.y < 0)
            {
                // ขยับเป้าหมายการยืนขึ้นมาข้างหน้าเป็นระยะ z: -6.5 เพื่อเตรียมรับลูกหน้าเน็ต
                targetZ = -6.5f;
            }

            // สั่งให้ตัวละครวิ่งไปดักลูกที่ตำแหน่งเป้าหมายด้วยความเร็ว autoMoveSpeed
            Vector3 targetPos = new Vector3(targetX, transform.position.y, targetZ);
            transform.position = Vector3.Lerp(transform.position, targetPos, autoMoveSpeed * Runner.DeltaTime);
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
            // ผู้เล่นต้องทำท่าตบ (Smash) เท่านั้นถึงจะถือว่าเป็นการเสิร์ฟ
            if (gesture == TennisGesture.Smash)
            {
                // สั่งตีลูกด้วยท่า Smash บังคับความแรงขั้นต่ำที่ 0.7 
                activeBall.HitByPlayer(TennisGesture.Smash, Mathf.Max(0.7f, power), 0f);
            }
            return;
        }

        // การตีโต้ (ลูกกำลังพุ่งมาฝั่งผู้เล่น)
        if (activeBall.IsMoving && activeBall.Velocity.z < 0)
        {
            // คำนวณระยะห่างระหว่างตัวละครกับลูกบอล
            float diffZ = activeBall.transform.position.z - transform.position.z;
            float diffX = Mathf.Abs(activeBall.transform.position.x - transform.position.x);

            // ถ้าลูกเข้ามาอยู่ในวงสวิง (hitRange)
            if (Mathf.Abs(diffZ) <= hitRangeZ && diffX <= hitRangeX)
            {
                // ใช้ระยะ Z ที่ห่างกันมาเป็น timingOffset
                float timingOffset = diffZ;

                // สั่งให้ลูกกระดอนกลับไปยังฝั่งตรงข้าม
                activeBall.HitByPlayer(gesture, power, timingOffset);
            }
        }
    }
}