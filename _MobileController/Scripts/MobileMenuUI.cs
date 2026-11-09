using UnityEngine;
using UnityEngine.UI;
using Fusion;
using System.Linq;
using TMPro;

[System.Serializable]
public class LobbySlotUI
{
    public TextMeshProUGUI nameText;    // ข้อความแสดงชื่อ
    public TextMeshProUGUI statusText;  // ข้อความสถานะ เช่น "READY", "WAITING"
    public Image statusBackground;      // พื้นหลังของปุ่มสถานะ (เพื่อเปลี่ยนสี)
}

public class MobileMenuUI : MonoBehaviour
{
    public Toggle mobilePublicToggle;

    [Header("Lobby Controls")]
    public Button startMatchButton; // ปุ่ม CREATE ROOM / START MATCH (แสดงเฉพาะ P1)
    public Button readyButton;      // ปุ่ม I'M READY (แสดงเฉพาะ P2, P3, P4)

    [Header("Lobby Slots (ใส่ UI ทั้ง 4 ช่อง)")]
    public LobbySlotUI[] playerSlots = new LobbySlotUI[4];

    [Header("Status Colors")]
    public Color readyColor = new Color(0.85f, 1f, 0.85f);     // สีเขียวอ่อน
    public Color notReadyColor = new Color(1f, 0.95f, 0.75f);  // สีเหลืองอ่อน
    public Color waitingColor = new Color(0.95f, 0.95f, 0.95f); // สีเทาอ่อน

    [Header("Mode Selection UI")]
    public GameObject modeSelectionPanel; // ลาก Panel ที่คลุมปุ่มโหมดทั้งหมดมาใส่ (เพื่อซ่อน/โชว์)
    public Button[] modeButtons = new Button[4]; // ใส่ปุ่มโหมด 0=1vAI, 1=2vAI, 2=1v1, 3=2v2

    public Color selectedModeColor = new Color(0.4f, 0.8f, 1f); // สีฟ้า (โหมดที่ถูกเลือก)
    public Color lockedModeColor = new Color(0.8f, 0.8f, 0.8f, 0.5f); // สีเทาจาง (ถูกล็อก)
    public Color normalModeColor = Color.white; // สีขาว (ปกติ กดได้)
    private int localCurrentMode = 0; // เก็บโหมดที่เลือกไว้ในฝั่งมือถือ

    void Start()
    {
        if (mobilePublicToggle != null)
        {
            mobilePublicToggle.onValueChanged.AddListener(OnTogglePublic);
        }
    }

    void Update()
    {
        UpdateLobbyUI();
    }

    // ฟังก์ชันอัปเดตหน้าจอ UI ให้ตรงกับสถานะล่าสุด สลับปุ่มซ่อน/โชว์อัตโนมัติ
    private void UpdateLobbyUI()
    {
        var allPlayers = FindObjectsByType<LobbyPlayerSync>(FindObjectsSortMode.None);
        bool allReady = true;
        LobbyPlayerSync myLocalPlayer = null;
        int validPlayerCount = 0; // ตัวแปรนับจำนวนคนในห้องที่ Spawn เสร็จสมบูรณ์แล้ว

        // ล้างข้อมูลทุก Slot ให้กลับเป็นค่าเริ่มต้น (Open slot) ก่อน
        for (int i = 0; i < 4; i++)
        {
            if (playerSlots[i].nameText != null) playerSlots[i].nameText.text = "Open slot";
            if (playerSlots[i].statusText != null) playerSlots[i].statusText.text = "WAITING";
            if (playerSlots[i].statusBackground != null) playerSlots[i].statusBackground.color = waitingColor;
        }

        // เช็กสถานะของทุกคนในห้อง และวาดรายชื่อลง UI
        foreach (var p in allPlayers)
        {
            // ป้องกันการอ่านค่าตัวแปร [Networked] ตอนที่โหลดฉาก หรือยัง Spawn ไม่เสร็จ
            if (p.Object == null || !p.Object.IsValid) continue;

            validPlayerCount++; // นับจำนวนคนที่อยู่ในห้อง

            if (!p.IsReady && !p.IsHost) allReady = false; // Host พร้อมเสมอ ไม่ต้องเช็ก

            // หาตัวแทนของมือถือเครื่องนี้ (เครื่องที่ถือ InputAuthority)
            if (p.Object.HasInputAuthority) myLocalPlayer = p;

            int slotIndex = p.SlotIndex - 1; // ลบ 1 เพราะ Array เริ่มที่ 0

            if (slotIndex >= 0 && slotIndex < 4)
            {
                // ตกแต่งข้อความต่อท้ายชื่อ
                string suffix = "";
                if (p.IsHost) suffix += " · host";
                if (p.Object.HasInputAuthority) suffix += " · you";

                // ใส่ชื่อลง UI
                if (playerSlots[slotIndex].nameText != null)
                    playerSlots[slotIndex].nameText.text = p.PlayerName.ToString() + suffix;

                // อัปเดตข้อความและสีสถานะ
                if (playerSlots[slotIndex].statusText != null && playerSlots[slotIndex].statusBackground != null)
                {
                    if (p.IsHost)
                    {
                        playerSlots[slotIndex].statusText.text = "READY";
                        playerSlots[slotIndex].statusBackground.color = readyColor;
                    }
                    else
                    {
                        playerSlots[slotIndex].statusText.text = p.IsReady ? "READY" : "NOT READY";
                        playerSlots[slotIndex].statusBackground.color = p.IsReady ? readyColor : notReadyColor;
                    }
                }
            }
        }

        // ซ่อน/โชว์ Panel เลือกโหมด (โชว์เฉพาะ Host เท่านั้น)
        if (modeSelectionPanel != null)
        {
            modeSelectionPanel.SetActive(myLocalPlayer != null && myLocalPlayer.IsHost);
        }

        // จัดการสิทธิ์การแสดงปุ่ม เริ่มเกม/เตรียมพร้อม
        if (myLocalPlayer != null)
        {
            if (myLocalPlayer.IsHost) // ถ้าเป็น P1 (Host)
            {
                if (startMatchButton != null)
                {
                    startMatchButton.gameObject.SetActive(true);
                    startMatchButton.interactable = allReady; // จะกดเริ่มได้ก็ต่อเมื่อทุกคน Ready
                }
                if (readyButton != null) readyButton.gameObject.SetActive(false);

                // จัดการสีและการล็อกของปุ่มโหมดทั้ง 4 (ทำงานเฉพาะเครื่อง Host)
                ManageModeButtonsLogic(validPlayerCount);
            }
            else // ถ้าเป็น P2, P3, P4
            {
                if (startMatchButton != null) startMatchButton.gameObject.SetActive(false);
                if (readyButton != null) readyButton.gameObject.SetActive(true);
            }
        }
    }

    // ฟังก์ชันสำหรับล็อกโหมด เปลี่ยนสีปุ่ม และสลับโหมดอัตโนมัติตามจำนวนคน
    private void ManageModeButtonsLogic(int playerCount)
    {
        bool modeChanged = false;

        // กฎการสลับโหมดอัตโนมัติ
        if (playerCount == 2 && localCurrentMode == 0) // ถ้ามี 2 คน และเลือก 1vAI อยู่
        {
            localCurrentMode = 2; // สลับไป 1v1 ให้เลย
            modeChanged = true;
        }
        else if (playerCount >= 4 && localCurrentMode != 3) // ถ้ามี 4 คน และไม่ได้เลือก 2v2 อยู่
        {
            localCurrentMode = 3; // สลับไป 2v2 ให้เลย
            modeChanged = true;
        }

        // ถ้ามีการบังคับเปลี่ยนโหมด ให้ส่งข้อมูลบอก Server ด้วย
        if (modeChanged)
        {
            var menuSync = GetMenuSync();
            if (menuSync != null) menuSync.RPC_SetMode(localCurrentMode);
        }

        // วาดสีปุ่มโหมดและล็อกปุ่ม
        for (int i = 0; i < 4; i++)
        {
            if (i >= modeButtons.Length || modeButtons[i] == null) continue;

            bool isSelected = (i == localCurrentMode);
            bool isLocked = false;

            // กฎการล็อก
            if (playerCount == 2 && i == 0) isLocked = true; // 2 คน ล็อก P1 vs AI
            if (playerCount == 3) isLocked = true; // 3 คน ล็อกทุกปุ่ม
            if (playerCount >= 4 && i != 3) isLocked = true; // 4 คน ล็อกทุกปุ่มยกเว้น 2v2

            // ทำให้ปุ่มกดไม่ได้ถ้ามันถูกเลือกอยู่แล้ว หรือถูกล็อก
            modeButtons[i].interactable = !isSelected && !isLocked;

            // เปลี่ยนสีปุ่ม (ดึง Image ของปุ่มมาเปลี่ยนสี)
            Image btnImage = modeButtons[i].GetComponent<Image>();
            if (btnImage != null)
            {
                if (isSelected) btnImage.color = selectedModeColor; // สีฟ้า
                else if (isLocked) btnImage.color = lockedModeColor; // สีเทา
                else btnImage.color = normalModeColor; // สีขาว
            }
        }
    }

    // ค้นหา TennisMenuSync ในเครือข่าย
    private TennisMenuSync GetMenuSync()
    {
        var menuSync = FindAnyObjectByType<TennisMenuSync>();
        if (menuSync == null)
        {
            Debug.LogWarning("[MobileMenuUI] ยังหา TennisMenuSync ไม่พบในเครือข่าย กำลังรอการซิงค์...");
        }
        return menuSync;
    }

    // ฟังก์ชันช่วยเหลือ: เช็กว่ามือถือเครื่องนี้เป็น Host (P1) หรือไม่
    private bool IsLocalPlayerHost()
    {
        var allPlayers = FindObjectsByType<LobbyPlayerSync>(FindObjectsSortMode.None);
        var myPlayer = allPlayers.FirstOrDefault(p => p.Object != null && p.Object.IsValid && p.Object.HasInputAuthority);
        return myPlayer != null && myPlayer.IsHost;
    }

    public void OnTogglePublic(bool isPublic)
    {
        // ป้องกันไม่ให้คนที่ไม่ได้เป็น Host แอบเปลี่ยนสถานะห้อง
        if (!IsLocalPlayerHost()) return;

        var menuSync = GetMenuSync();
        if (menuSync != null)
        {
            menuSync.RPC_SetPublic(isPublic);
        }
    }

    public void OnClickChangeMode(int modeIndex)
    {
        // บล็อกการเปลี่ยนโหมด ถ้าผู้เล่นคนนี้ไม่ใช่ P1 (Host)
        if (!IsLocalPlayerHost()) return;

        // เปลี่ยนโหมดในมือถือแล้วค่อยส่ง RPC
        localCurrentMode = modeIndex;
        UpdateLobbyUI();

        var menuSync = GetMenuSync();
        if (menuSync != null)
        {
            menuSync.RPC_SetMode(modeIndex);
        }
    }

    public void OnClickStartMatch()
    {
        // ป้องกันไม่ให้ลูกเรือเผลอกดเริ่มเกม
        if (!IsLocalPlayerHost()) return;

        var menuSync = FindAnyObjectByType<TennisMenuSync>();
        if (menuSync != null)
        {
            menuSync.RPC_StartMatch(); // สั่งให้ Host พาเข้าฉากเลย
        }
    }

    // สำหรับให้ P2-P4 กดปุ่ม I'm Ready
    public void OnClickReady()
    {
        var allPlayers = FindObjectsByType<LobbyPlayerSync>(FindObjectsSortMode.None);
        var myPlayer = allPlayers.FirstOrDefault(p => p.Object != null && p.Object.IsValid && p.Object.HasInputAuthority);

        if (myPlayer != null)
        {
            myPlayer.RPC_SetReady(!myPlayer.IsReady); // สลับสถานะ Ready / Not Ready
        }
    }
}