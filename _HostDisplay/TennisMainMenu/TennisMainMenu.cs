using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;

public enum TennisGameMode
{
    P1_vs_AI,
    P1_P2_vs_AI,
    P1_vs_P2,
    P1_P2_vs_P3_P4
}

public class TennisMainMenu : MonoBehaviour
{
    public static TennisGameMode CurrentMode = TennisGameMode.P1_vs_AI;

    [Header("Scene Configuration")]
    public bool isMobileScene = false;

    [Header("Host UI (PC/TV)")]
    public GameObject hostPanel;
    public TMP_Text selectedModeText;
    public TMP_InputField hostRoomCodeInput;
    public Toggle isPublicToggle; // Toggle สำหรับเลือกว่าเปิดสาธารณะหรือไม่

    [Header("Client UI (Mobile)")]
    public GameObject clientPanel;
    public TMP_InputField clientRoomCodeInput;
    public GameObject mobileJoinPanel;       // เพิ่มตัวแปรเก็บหน้า Join
    public GameObject mobileControllerPanel;

    [Header("References")]
    public TennisNetworkBootstrapper bootstrapper;

    void Start()
    {
        if (TennisNetworkBootstrapper.Instance != null)
            bootstrapper = TennisNetworkBootstrapper.Instance;

        if (isMobileScene)
        {
            if (hostPanel != null) hostPanel.SetActive(false);

            // เปิดหน้า Join และซ่อนหน้า Controller ตอนเริ่มแอป
            if (mobileJoinPanel != null) mobileJoinPanel.SetActive(true);
            if (mobileControllerPanel != null) mobileControllerPanel.SetActive(false);
        }
        else
        {
            if (clientPanel != null) clientPanel.SetActive(false);
            if (hostPanel != null) hostPanel.SetActive(true);

            if (hostRoomCodeInput != null)
                hostRoomCodeInput.text = "FIT" + Random.Range(1000, 9999).ToString();

            UpdateModeText();

            // ให้ PC เปิดห้องรอทันทีที่เปิดแอป เพื่อรอมือถือเข้ามาสั่งงาน
            if (bootstrapper != null)
            {
                bootstrapper.defaultRoomCode = hostRoomCodeInput.text;
                bootstrapper.StartHostScreen(true); // ให้เปิดห้องแบบ Public รอไว้เป็นค่าเริ่มต้น
            }
        }
    }

    public void SelectMode(int modeIndex)
    {
        if (isMobileScene) return;
        CurrentMode = (TennisGameMode)modeIndex;
        UpdateModeText();
    }

    private void UpdateModeText()
    {
        if (selectedModeText != null)
        {
            string modeName = CurrentMode.ToString().Replace("_", " ");
            selectedModeText.text = $"MODE: {modeName}";
        }
    }

    public void OnClickStartHost()
    {
        if (bootstrapper != null && !isMobileScene)
        {
            bootstrapper.defaultRoomCode = hostRoomCodeInput.text;

            // อ่านค่าจาก Toggle ถ้าไม่มีกำหนดไว้ให้ถือเป็น Public (true) ไว้ก่อน
            bool isPublicRoom = isPublicToggle != null ? isPublicToggle.isOn : true;

            // ส่งค่าบอก Bootstrapper ด้วยว่าเป็น Public หรือ Private
            bootstrapper.StartHostScreen(isPublicRoom);

            gameObject.SetActive(false);
        }
    }

    public void OnClickJoinClient()
    {
        if (bootstrapper != null && isMobileScene)
        {
            string roomCode = clientRoomCodeInput.text.ToUpper();

            // ซ่อนหน้า Join เพื่อป้องกันการกดปุ่มเบิ้ล
            if (mobileJoinPanel != null) mobileJoinPanel.SetActive(false);

            // สั่งเชื่อมต่อเครือข่าย
            bootstrapper.JoinFromMobile(roomCode);

            // เปิดหน้ารีโมต Controller ขึ้นมาแทนที่
            if (mobileControllerPanel != null) mobileControllerPanel.SetActive(true);
        }
    }
}