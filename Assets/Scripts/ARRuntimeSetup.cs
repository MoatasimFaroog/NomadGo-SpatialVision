using UnityEngine;
using UnityEngine.XR.ARFoundation;
using Unity.XR.CoreUtils;

public static class ARRuntimeSetup
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Setup()
    {
        GameObject arSession = GameObject.Find("AR Session");
        if (arSession != null && arSession.GetComponent<ARSession>() == null)
        {
            arSession.AddComponent<ARSession>();
        }

        GameObject originObj = GameObject.Find("AR Session Origin");
        if (originObj != null && originObj.GetComponent<XROrigin>() == null)
        {
            originObj.AddComponent<XROrigin>();
        }

        Camera cam = Camera.main;
        if (cam != null)
        {
            if (cam.GetComponent<ARCameraManager>() == null)
                cam.gameObject.AddComponent<ARCameraManager>();

            if (cam.GetComponent<ARCameraBackground>() == null)
                cam.gameObject.AddComponent<ARCameraBackground>();

            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
        }
    }
}
