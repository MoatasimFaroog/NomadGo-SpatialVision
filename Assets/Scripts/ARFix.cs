using UnityEngine;

public class ARFix
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        Camera cam = Camera.main;

        if (cam != null)
        {
            cam.clearFlags = CameraClearFlags.Depth;
            cam.backgroundColor = Color.black;
        }
    }
}
