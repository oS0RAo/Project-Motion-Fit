using Fusion;
using UnityEngine;

public class TennisAIAvatar : NetworkBehaviour
{
    [Networked] public int AIIndex { get; set; } // 1 = คุมซ้าย, 2 = คุมขวา

    public float moveSpeed = 4.5f;
    public float baseLineZ = 9.5f; // AI จะยืนอยู่แดนบวกเสมอ

    private TennisBallHost activeBall;

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority) return; // ให้ Server (Host) เป็นคนคุมบอทเท่านั้น

        if (activeBall == null || activeBall.Object == null || !activeBall.Object.IsValid)
        {
            activeBall = FindAnyObjectByType<TennisBallHost>();
            return;
        }

        int mode = TennisNetworkBootstrapper.Instance != null ? TennisNetworkBootstrapper.Instance.currentMode : 0;
        bool isCoop = (mode == 1); // เช็กว่าเป็นโหมด P1+P2 vs AI 2 ตัว หรือไม่

        // ถ้ารอบนี้จบแล้ว หรือลูกกำลังพุ่งไปหาผู้เล่น (Z < 0) แปลว่า AI ไม่ต้องดักลูก
        if (activeBall.IsScored || !activeBall.IsMoving || activeBall.Velocity.z < 0)
        {
            float homeX = 0f;
            if (isCoop) homeX = (AIIndex == 1) ? -2.0f : 2.0f; // แบ่งโซนซ้าย-ขวา

            Vector3 centerPos = new Vector3(homeX, transform.position.y, baseLineZ);
            transform.position = Vector3.Lerp(transform.position, centerPos, (moveSpeed / 2) * Runner.DeltaTime);
        }
        else // ลูกกำลังพุ่งมาหา AI (Z > 0)
        {
            float targetX = activeBall.transform.position.x;
            float targetZ = baseLineZ;

            // เช็กลูกหยอด
            if (activeBall.transform.position.z < 4.0f && activeBall.Velocity.y < 0)
                targetZ = 6.5f;

            targetZ = Mathf.Clamp(targetZ, 0.5f, baseLineZ); // ล็อกไม่ให้ AI วิ่งทะลุเน็ตมาฝั่งเรา

            // ล็อกเขตการวิ่งซ้าย-ขวา หากมี AI 2 ตัว
            if (isCoop)
            {
                if (AIIndex == 1) targetX = Mathf.Min(targetX, -0.5f); // AI ตัวที่ 1 วิ่งได้แค่ซ้าย
                if (AIIndex == 2) targetX = Mathf.Max(targetX, 0.5f);  // AI ตัวที่ 2 วิ่งได้แค่ขวา
            }

            Vector3 targetPos = new Vector3(targetX, transform.position.y, targetZ);
            transform.position = Vector3.Lerp(transform.position, targetPos, moveSpeed * Runner.DeltaTime);
        }
    }
}