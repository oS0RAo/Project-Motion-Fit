using UnityEngine;
using TMPro;

public class MobileTennisSensor : MonoBehaviour
{
    [Header("Swing Sensitivity")]
    // กำหนดค่าความไวของการแกว่ง (ถ้าความเร็วเชิงมุมน้อยกว่านี้ จะไม่ถือว่าเป็นการตี)
    public float swingThreshold = 3.0f;
    // ความเร็วสูงสุดที่ถือว่าตีแรงที่สุด (ใช้คำนวณเปอร์เซ็นต์ความแรง)
    public float maxSwingSpeed = 12.0f;
    // ระยะเวลาหน่วงหลังตีเสร็จ เพื่อป้องกันการจับการสวิงซ้ำซ้อนรัวๆ (คูลดาวน์ 0.4 วินาที)
    public float cooldownTime = 0.4f;
    // กำหนดว่าผู้เล่นถนัดขวาหรือไม่ (มีผลต่อการอ่านทิศทาง Forehand/Backhand)
    public bool isRightHanded = true;

    [Header("Debug UI")]
    // เอาไว้แสดงค่า Gyroscope บนหน้าจอมือถือเพื่อใช้ทดสอบ
    public TMP_Text debugText;

    private float _lastSwingTime; // เก็บเวลาที่ตีลูกครั้งล่าสุด เพื่อเอาไว้เช็กคูลดาวน์
    // เก็บค่าการหมุนเริ่มต้นเพื่อให้หันหน้าไปทางไหนก็ได้ตอนเริ่มเกม
    private Quaternion _calibrationOffset = Quaternion.identity;

    // Properties เก็บค่าท่าทางและความแรงที่เพิ่งตรวจจับได้ (รอให้ Bootstrapper มาดึงไปส่งผ่าน Network)
    public TennisGesture PendingGesture { get; private set; }
    public float PendingPower { get; private set; }

    // ฟังก์ชันแปลงระบบพิกัดการหมุนจาก Gyroscope (มือขวา) ให้ตรงกับระบบของ Unity (มือซ้าย)
    private static Quaternion GyroToUnity(Quaternion q)
    {
        // ต้องกลับด้านแกน Z และ W เพื่อให้การหมุนซ้าย-ขวา, ก้ม-เงย ตรงกับในเกม
        return new Quaternion(q.x, q.y, -q.z, -q.w);
    }

    // Property สำหรับอ่านค่าการหมุนปัจจุบันที่หักลบกับค่าตอนคาลิเบรตแล้ว
    public Quaternion CalibratedRotation
    {
        get
        {
            if (!Input.gyro.enabled) return Quaternion.identity; // ถ้าไม่มี Gyro ให้คืนค่าเป็นศูนย์

            // อ่านค่าท่าทางปัจจุบันของมือถือ แล้วแปลงเป็น Unity Space
            Quaternion current = GyroToUnity(Input.gyro.attitude);
            // เอาค่าเริ่มต้นที่เซ็ตไว้มาหักล้าง กับค่าปัจจุบัน เพื่อให้จุดที่เซ็ตเริ่มต้นคือหน้าตรงเสมอ
            return Quaternion.Inverse(_calibrationOffset) * current;
        }
    }

    void Start()
    {
        // ตรวจสอบว่ามือถือเครื่องนี้มีเซ็นเซอร์ Gyroscope หรือไม่
        if (SystemInfo.supportsGyroscope)
        {
            Input.gyro.enabled = true; // เปิดใช้งาน Gyroscope
            // สั่งหน่วงเวลา 0.5 วินาทีก่อนเรียกฟังก์ชัน RecenterGyro (ให้เวลาเซ็นเซอร์เริ่มทำงานก่อน)
            Invoke(nameof(RecenterGyro), 0.5f);
        }
        else
        {
            Debug.LogWarning("[MobileTennisSensor] อุปกรณ์นี้ไม่รองรับ Gyroscope");
        }
    }

    void Update()
    {
        if (!SystemInfo.supportsGyroscope) return; // ถ้าไม่มีเซ็นเซอร์ ไม่ต้องทำอะไรต่อ

        // อ่านค่าความเร็วในการหมุน
        Vector3 rotRate = Input.gyro.rotationRateUnbiased;
        // หาผลรวมความเร็วในการหมุนทั้ง 3 แกน
        float totalAngularSpeed = rotRate.magnitude;

        // อัปเดตข้อความบนหน้าจอมือถือสำหรับทดสอบ
        if (debugText != null)
        {
            Quaternion r = CalibratedRotation;
            debugText.text =
                $"Rate X:{rotRate.x:F2} Y:{rotRate.y:F2} Z:{rotRate.z:F2}\n" +
                $"Speed: {totalAngularSpeed:F2}\n" +
                $"Rot: {r.eulerAngles}";
        }

        // ถ้าเพิ่งตีไป ยังไม่พ้นระยะเวลาคูลดาวน์ ให้ออกจากการตรวจจับทันที
        if (Time.time - _lastSwingTime < cooldownTime) return;

        // ถ้าความเร็วการหมุนมากกว่าเกณฑ์ที่ตั้งไว้ (ผู้เล่นเริ่มเหวี่ยงแขน)
        if (totalAngularSpeed > swingThreshold)
            DetectSwingType(rotRate, totalAngularSpeed); // ส่งไปวิเคราะห์ว่าเป็นท่าตีแบบไหน
    }

    // ฟังก์ชันวิเคราะห์ท่าทางการตีจากความเร็วแต่ละแกน
    private void DetectSwingType(Vector3 rotRate, float speed)
    {
        // เช็กว่าเป็นการตบลง หรือไม่ โดยดูว่าการหมุนแกน Z เยอะกว่าแกนอื่นๆไหม
        bool isPitching = Mathf.Abs(rotRate.z) > Mathf.Abs(rotRate.y) && Mathf.Abs(rotRate.z) > Mathf.Abs(rotRate.x);

        // คำนวณความแรงเป็นเปอร์เซ็นต์ (0.0 ถึง 1.0) เทียบระหว่างเกณฑ์ต่ำสุดและสูงสุด
        float power = Mathf.Clamp01((speed - swingThreshold) / (maxSwingSpeed - swingThreshold));

        if (isPitching) // ถ้าเป็นการเหวี่ยงในแนวก้ม/เงย
        {
            // เช็กทิศทางว่าเหวี่ยงลงหรือเหวี่ยงขึ้น (ถ้าถนัดขวา แกน Z เป็นบวกคือสับข้อมือลง)
            bool isSwingingDown = isRightHanded ? (rotRate.z > 0.5f) : (rotRate.z < -0.5f);
            if (isSwingingDown)
            {
                PendingGesture = TennisGesture.Smash; // กำหนดท่าเป็นลูกตบ
                PendingPower = power;
                _lastSwingTime = Time.time; // รีเซ็ตเวลาคูลดาวน์
#if UNITY_ANDROID || UNITY_IOS
                Handheld.Vibrate(); // สั่งให้มือถือสั่นเพื่อเป็น Feedback ว่าตีโดนแล้ว
#endif
            }
        }
        else // ถ้าไม่ใช่การก้มเงย แปลว่าเหวี่ยงออกข้าง (ซ้าย-ขวา)
        {
            PendingPower = power;
            // เช็กว่าเหวี่ยงไปทางไหนเพื่อแยก Forehand กับ Backhand
            PendingGesture = (rotRate.y > 0.5f || rotRate.x < -0.5f) ? TennisGesture.Backhand : TennisGesture.Forehand;
            _lastSwingTime = Time.time;
#if UNITY_ANDROID || UNITY_IOS
            Handheld.Vibrate();
#endif
        }
    }

    // ฟังก์ชันจำลองการตีด้วยปุ่มกด
    public void TriggerSimulatedSwing(TennisGesture gesture, float power)
    {
        PendingGesture = gesture;
        PendingPower = power;
        _lastSwingTime = Time.time;
    }

    // ฟังก์ชันล้างค่าที่ค้างอยู่ถูกเรียกโดย Bootstrapper หลังจากส่งข้อมูลผ่าน Network ไปแล้ว
    public void ClearPendingSwing()
    {
        PendingGesture = TennisGesture.None;
        PendingPower = 0f;
    }

    // ฟังก์ชันสำหรับตั้งศูนย์ (Calibrate) มือถือใหม่ (รีเซ็ตให้ทิศที่ชี้อยู่ปัจจุบันคือทิศตรงหน้า)
    public void RecenterGyro()
    {
        if (SystemInfo.supportsGyroscope)
            _calibrationOffset = GyroToUnity(Input.gyro.attitude);
    }
}