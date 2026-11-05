using UnityEngine;
using TMPro;

public class TennisUIManager : MonoBehaviour
{
    public static TennisUIManager Instance;

    [Header("Connection Panels")]
    // หน้าต่างเริ่มต้นที่มีปุ่ม "สร้างห้อง" หรือ "เข้าห้อง"
    public GameObject connectionPanel;
    // หน้าต่างรีโมตบนมือถือ
    public GameObject mobileRemotePanel;
    // ช่องกรอกรหัสห้องบนหน้าจอมือถือ
    public TMP_InputField roomCodeInput;

    [Header("Host HUD (จอหลัก)")]
    // ข้อความแสดงสถานะระบบ
    public TextMeshProUGUI statusText;
    // ข้อความแสดงคะแนนของผู้เล่นเทียบกับบอท
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

        // ซ่อนหน้าจอกดรีโมตของมือถือไว้ก่อนตั้งแต่เริ่มเกม
        if (mobileRemotePanel != null) mobileRemotePanel.SetActive(false);
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

    // ฟังก์ชันถูกเรียกเมื่อกดปุ่ม "Start Host" บนหน้าจอหลัก
    public void OnClickStartHost(TennisNetworkBootstrapper bootstrapper)
    {
        // ซ่อนหน้าต่างเมนูเริ่มต้น
        connectionPanel.SetActive(false);
        // แสดงข้อความบอกให้รู้ว่าสร้างห้องชื่ออะไร และกำลังรอให้มือถือเข้ามาเชื่อมต่อ
        statusText.text = $"ROOM CODE: {bootstrapper.defaultRoomCode}\nWaiting for Mobile Controller...";

        // สั่งให้ Bootstrapper ทำการเปิดห้องในฐานะ Host
        bootstrapper.StartHostScreen();
    }

    // ฟังก์ชันถูกเรียกเมื่อกดปุ่ม "Join Room" บนหน้าจอมือถือ
    public void OnClickJoinMobile(TennisNetworkBootstrapper bootstrapper)
    {
        // ซ่อนหน้าต่างเมนูเริ่มต้น
        connectionPanel.SetActive(false);
        // เปิดหน้าต่างสำหรับให้มือถือใช้เป็นจอยสติ๊ก
        if (mobileRemotePanel != null) mobileRemotePanel.SetActive(true);

        // ดึงชื่อห้องจากช่องกรอก หากไม่ได้กรอกอะไรมา ให้ใช้ค่าเริ่มต้น (defaultRoomCode)
        string code = roomCodeInput != null && !string.IsNullOrEmpty(roomCodeInput.text)
                      ? roomCodeInput.text.ToUpper() // แปลงเป็นตัวพิมพ์ใหญ่ทั้งหมดป้องกันการพิมพ์ผิด
                      : bootstrapper.defaultRoomCode;

        // อัปเดตข้อความบนมือถือว่าเชื่อมต่อห้องสำเร็จแล้ว ให้จับมือถือแน่นๆ
        statusText.text = $"Connected to Room: {code}\nGrip your phone tightly!";

        // สั่งให้ Bootstrapper ทำการเชื่อมต่อเข้าห้องในฐานะ Client
        bootstrapper.JoinFromMobile(code);
    }

    // ฟังก์ชันสำหรับให้สคริปต์อื่น (เช่น TennisPlayerAvatar) สั่งอัปเดตคะแนนบนจอ
    public void UpdateScoreBoard(int playerScore, int aiScore, int rally, float calories)
    {
        // จัดรูปแบบข้อความคะแนนตรงกลางจอ
        if (scoreText != null)
            scoreText.text = $"PLAYER  {playerScore}  -  {aiScore}  BOT";

        // จัดรูปแบบข้อความสถิติ (โชว์จำนวนครั้งที่โต้ลูก และจำกัดจุดทศนิยมแคลอรี 2 ตำแหน่งด้วย :F2)
        if (statsText != null)
            statsText.text = $"RALLY: {rally} Hits\nBURN: {calories:F2} kcal";
    }

    // ฟังก์ชันสำหรับแสดงข้อความป๊อปอัปกลางจอ (รับคำสั่งมาจากลูกเทนนิสผ่านเครือข่าย)
    public void ShowPopup(string message, Color color)
    {
        if (popupFeedbackText == null) return; // ป้องกันบัคถ้าไม่ได้ลาก UI มาใส่

        popupFeedbackText.text = message; // เปลี่ยนข้อความ
        popupFeedbackText.color = color;  // เปลี่ยนสี (เช่น เขียว=Perfect, แดง=ออก, ฟ้า=ได้แต้ม)

        // ตั้งเวลาให้แสดงค้างไว้ 1.5 วินาทีก่อนจะถูกฟังก์ชัน Update() ลบทิ้ง
        _popupTimer = 1.5f;
    }
}