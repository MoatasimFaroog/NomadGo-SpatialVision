using UnityEngine;

public class ARFix : MonoBehaviour
{
    void Start()
    {
        Camera cam = Camera.main;

        if (cam != null)
        {
            // أهم سطر لإزالة الشاشة الزرقاء
            cam.clearFlags = CameraClearFlags.Depth;

            // تأكيد الخلفية شفافة
            cam.backgroundColor = Color.black;
        }
    }
}
