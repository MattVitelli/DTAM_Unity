using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Debug3DMapper : MonoBehaviour {
    public Keyframe keyframe;
    public AnimationClip clip;
    public int width = 320;
    public int height = 240;
    public int depth = 512;
    public ComputeShader initCostShader;
    public ComputeShader updateCostShader;
    public ComputeShader updateCostMinMaxShader;
    public ComputeShader regularizeShader;
    public ComputeShader naiveDepthShader;
    public RenderTexture inverseDepthGT;

    public ComputeShader initSolverShader;
    public ComputeShader updateQShader;
    public ComputeShader projectQShader;
    public ComputeShader updateDShader;
    public ComputeShader updateAShader;
    public DTAMSettings settings = new DTAMSettings();

    RenderTexture createRT(int width, int height, RenderTextureFormat format)
    {
        RenderTexture rt = new RenderTexture(width, height, 24, format, RenderTextureReadWrite.Linear);
        rt.enableRandomWrite = true;
        rt.filterMode = FilterMode.Point;
        rt.Create();
        return rt;
    }

    Matrix4x4 toNDC(Matrix4x4 src, int width, int height, float znear, float zfar)
    {
        Matrix4x4 dst = Matrix4x4.zero;
        dst.m00 = 2.0f * src.m00 / width;
        dst.m02 = -(src.m02 / width) + 0.5f;
        dst.m11 = 2.0f * src.m11 / height;
        dst.m12 = -(src.m12 / height) + 0.5f;
        dst.m22 = -(znear + zfar) / (zfar - znear);
        dst.m23 = -2.0f * znear * zfar / (zfar - znear);
        dst.m32 = -1.0f;
        return dst;
    }

    void Start () {

        Camera camera = this.GetComponent<Camera>();
        float length = clip.length;
        float fps = 5.0f;
        float sampleTimeOffset = 1.0f / fps;
        clip.SampleAnimation(this.gameObject, 0);

        float aspectRatio = (float)width / (float)height;
        Matrix4x4 intrinsics = Matrix4x4.identity;
        intrinsics.m00 = (width * 0.5f) / Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        intrinsics.m02 = width * 0.5f;
        intrinsics.m11 = (height * 0.5f) / Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        intrinsics.m12 = height * 0.5f;

        Matrix4x4 ndcIntrinsics = toNDC(intrinsics, width, height, camera.nearClipPlane, camera.farClipPlane);
        camera.projectionMatrix = ndcIntrinsics;

        SampleFrame frame = new SampleFrame();
        frame.CameraToWorld = camera.transform.localToWorldMatrix;
        frame.Intrinsics = intrinsics;
        frame.Image = createRT(width, height, RenderTextureFormat.ARGBFloat);
        camera.targetTexture = frame.Image;
        camera.Render();
        keyframe = new Keyframe();
        keyframe.Initialize(frame, depth, initCostShader, regularizeShader);

        int numExecuted = 0;
        for (float t = sampleTimeOffset; t < length; t += sampleTimeOffset)
        {
            numExecuted++;
            clip.SampleAnimation(this.gameObject, t);
            SampleFrame newFrame = new SampleFrame();
            newFrame.CameraToWorld = camera.transform.localToWorldMatrix;
            newFrame.Intrinsics = intrinsics;
            newFrame.Image = createRT(width, height, RenderTextureFormat.ARGBFloat);
            camera.targetTexture = newFrame.Image;
            camera.Render();
            keyframe.UpdateCostVolume(newFrame, updateCostShader, updateCostMinMaxShader);
        }

        keyframe.ComputeNaiveDepth(naiveDepthShader);

        clip.SampleAnimation(this.gameObject, 0);
        inverseDepthGT = createRT(width, height, RenderTextureFormat.ARGBFloat);
        camera.targetTexture = inverseDepthGT;
        camera.RenderWithShader(Shader.Find("Custom/InverseDepth"), string.Empty);
        camera.targetTexture = null;

        keyframe.SolveDepthMap(initSolverShader, updateQShader, updateDShader, updateAShader, regularizeShader, projectQShader, settings);
    }

    public void Solve()
    {
        keyframe.SolveDepthMap(initSolverShader, updateQShader, updateDShader, updateAShader, regularizeShader, projectQShader, settings);
    }

    void OnGUI()
    {
        GUI.DrawTexture(new Rect(0, 0, 640/2, 480/2), inverseDepthGT, ScaleMode.ScaleToFit, false);
        GUI.DrawTexture(new Rect(640/2, 0, 640/2, 480/2), keyframe.NaiveDepthImage, ScaleMode.ScaleToFit, false);
        GUI.DrawTexture(new Rect(640, 0, 640/2, 480/2), keyframe.AImage, ScaleMode.ScaleToFit, false);
        GUI.DrawTexture(new Rect(0, 480/2, 640 / 2, 480 / 2), keyframe.DImage, ScaleMode.ScaleToFit, false);
    }
}
