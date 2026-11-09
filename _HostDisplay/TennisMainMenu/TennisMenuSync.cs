using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

public class TennisMenuSync : NetworkBehaviour
{
    // ใช้ RpcSources.All เพื่อปลดล็อกให้ลูกข่าย (มือถือ) กดส่งคำสั่งหา Host ได้
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_SetMode(int newMode)
    {
        // อัปเดตโหมดใน Bootstrapper (เพื่อให้เกมรู้ว่าต้องเสกตัวละครกี่ตัว/แบบไหน)
        if (TennisNetworkBootstrapper.Instance != null)
        {
            TennisNetworkBootstrapper.Instance.currentMode = newMode;
        }

        // อัปเดต UI ข้อความบนหน้าจอ Monitor (PC)
        var mainMenu = FindAnyObjectByType<TennisMainMenu>();
        if (mainMenu != null)
        {
            mainMenu.SelectMode(newMode);
        }
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_SetPublic(bool isPublic)
    {
        var mainMenu = FindAnyObjectByType<TennisMainMenu>();
        if (mainMenu != null && mainMenu.isPublicToggle != null)
        {
            mainMenu.isPublicToggle.isOn = isPublic;
        }

        // อัปเดตสถานะห้องในระบบเครือข่ายจริงๆ
        if (Runner.IsServer && Runner.SessionInfo != null)
        {
            Runner.SessionInfo.IsVisible = isPublic;
        }
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_StartMatch()
    {
        if (Runner.IsServer)
        {
            Debug.Log("[PC] ได้รับคำสั่ง Start แล้ว! กำลังโหลดเข้า Scene Index 1 ทั้งเครือข่าย...");
            // ให้ Host สั่งเครือข่ายโหลด Index 1
            Runner.LoadScene(SceneRef.FromIndex(1));
        }
    }
}