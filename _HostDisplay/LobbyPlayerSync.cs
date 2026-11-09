using Fusion;
using UnityEngine;
using System;

public class LobbyPlayerSync : NetworkBehaviour
{
    // [Networked] ทำให้ข้อมูลพวกนี้ซิงค์ตรงกันทุกหน้าจอ
    [Networked] public NetworkString<_32> PlayerName { get; set; }
    [Networked] public NetworkBool IsReady { get; set; }
    [Networked] public int SlotIndex { get; set; } // 1=P1, 2=P2, 3=P3, 4=P4
    [Networked] public NetworkBool IsHost { get; set; } // ใครเป็นคนสร้างห้อง/ควบคุมห้อง

    // ประกาศ Event ไว้ให้ UI รอรับการแจ้งเตือนเวลาข้อมูลเปลี่ยน
    public static event Action OnLobbyDataChanged;

    public override void Spawned()
    {
        // แจ้ง UI ให้อัปเดตเมื่อมีคนใหม่เกิดในห้อง
        OnLobbyDataChanged?.Invoke();
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        // แจ้ง UI ให้อัปเดตเมื่อมีคนออก
        OnLobbyDataChanged?.Invoke();
    }

    // ฟังก์ชันให้มือถือส่งคำสั่งมาขอเปลี่ยนสถานะ Ready (ผ่าน RPC)
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_SetReady(NetworkBool readyState)
    {
        IsReady = readyState;
        OnLobbyDataChanged?.Invoke();
    }
}