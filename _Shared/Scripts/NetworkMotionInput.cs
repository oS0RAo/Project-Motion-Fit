using Fusion;
using UnityEngine;

public enum TennisGesture : byte
{
    None = 0,
    Forehand = 1,   // เหวี่ยงจากขวาไปซ้าย
    Backhand = 2,   // เหวี่ยงจากซ้ายไปขวา
    Smash = 3       // เหวี่ยงสับลงจากเหนือหัว
}

public struct NetworkMotionInput : INetworkInput
{
    public TennisGesture Gesture;
    public float SwingPower;         // ความแรงในการเหวี่ยง (0.0f - 1.0f)
    public Quaternion DeviceRot;     // องศามือถือ ใช้หมุนไม้เทนนิสบนจอแบบ Real-time
    public NetworkBool ResetGyro;    // ปุ่มกดรีเซ็ตองศาไม้เทนนิส
}