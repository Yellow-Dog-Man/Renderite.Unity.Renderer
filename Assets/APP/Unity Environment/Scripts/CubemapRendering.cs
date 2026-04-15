using UnityEngine;
using System.Collections;

public class CubemapRendering : MonoBehaviour
{
    public int CubemapSize = 512;

    RenderTexture tex;
    Camera camera;

    public Material SetTexture;

    public Camera displayCam;
    
    void Awake()
    {
        tex = new RenderTexture(CubemapSize, CubemapSize, 16);
        tex.dimension = UnityEngine.Rendering.TextureDimension.Cube;

        SetTexture.SetTexture("_Cube", tex);

        camera = this.GetComponent<Camera>();
        camera.enabled = false;
    }

    void LateUpdate()
    {
        SetTexture.SetMatrix("_Rotation", Matrix4x4.TRS(Vector3.zero, transform.rotation, Vector3.one));
        camera.RenderToCubemap(tex);
    }
}
