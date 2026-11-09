using UnityEngine;
using TMPro;

public class TennisUIManager : MonoBehaviour
{
    public static TennisUIManager Instance;

    [Header("Gameplay HUD (จอหลัก)")]
    // ข้อความแสดงคะแนน
    public TextMeshProUGUI scoreText;
    // ข้อความแสดงสถิติย่อย เช่น จำนวนการตีโต้ และแคลอรีที่เผาผลาญ
    public TextMeshProUGUI statsText;
    // ข้อความป๊อปอัปเด้งเตือนชั่วคราวกลางจอ เช่น "PERFECT SMASH!", "OUT!"
    public TextMeshProUGUI popupFeedbackText;

    // ตัวจับเวลาสำหรับซ่อนข้อความป๊อปอัป
    private float _popupTimer;

    void Awake()
    {
        Instance = this;
    }

    // Update ทำงานทุกๆ เฟรมภาพ
    void Update()
    {
        // ระบบนับถอยหลังเพื่อซ่อนข้อความ Popup
        if (_popupTimer > 0)
        {
            _popupTimer -= Time.deltaTime; // ลดเวลาลงตามเวลาจริงที่ผ่านไป

            // เมื่อเวลาหมดให้ล้างข้อความทิ้ง
            if (_popupTimer <= 0 && popupFeedbackText != null)
                popupFeedbackText.text = "";
        }
    }

    // ฟังก์ชันสำหรับสั่งอัปเดตคะแนนบนจอ
    public void UpdateScoreBoard(int p1Score, int p2Score, int rally, float calories)
    {
        if (scoreText != null)
        {
            // กำหนดชื่อทีมเริ่มต้น
            string leftTeam = "A";
            string rightTeam = "B";

            // ตรวจสอบโหมดปัจจุบันเพื่อเปลี่ยนป้ายชื่อให้เหมาะสม
            if (TennisNetworkBootstrapper.Instance != null)
            {
                int mode = TennisNetworkBootstrapper.Instance.currentMode;
                if (mode == 0) { leftTeam = "Player"; rightTeam = "BOT"; }        // โหมด 0: 1v1 กับบอท
                else if (mode == 1) { leftTeam = "TEAM A"; rightTeam = "BOT TEAM"; }   // โหมด 1: 2v2 กับบอท
                else if (mode == 2) { leftTeam = "Player1"; rightTeam = "Player2"; }         // โหมด 2: 1v1 ดวลกันเอง
                else if (mode == 3) { leftTeam = "TEAM A"; rightTeam = "TEAM B"; }     // โหมด 3: 2v2 ดวลกันเอง
            }

            scoreText.text = $"{leftTeam}  {p1Score}  -  {p2Score}  {rightTeam}";
        }

        // จัดรูปแบบข้อความสถิติ โชว์จำนวนครั้งที่โต้ลูก และจำกัดจุดทศนิยมแคลอรี 2 ตำแหน่ง
        if (statsText != null)
            statsText.text = $"RALLY: {rally} Hits\nBURN: {calories:F2} kcal";
    }

    // ฟังก์ชันสำหรับแสดงข้อความป๊อปอัปกลางจอ
    public void ShowPopup(string message, Color color)
    {
        if (popupFeedbackText == null) return;

        popupFeedbackText.text = message;
        popupFeedbackText.color = color;

        // ตั้งเวลาให้แสดงค้างไว้ 1.5 วินาทีก่อนจะถูกฟังก์ชัน Update() ลบทิ้ง
        _popupTimer = 1.5f;
    }
}