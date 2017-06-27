using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class SampleFrame
{
    public RenderTexture Image;
    public Matrix4x4 Intrinsics;
    public Matrix4x4 CameraToWorld;
    
    ~SampleFrame()
    {
        if(Image)
        {
            Image = null;
        }
    }
}

[System.Serializable]
public class Keyframe
{
    public RenderTexture Image;
    public RenderTexture DepthImage;
    public RenderTexture CostVolume;
    public RenderTexture CostVolumeMinMax;
    public RenderTexture Regularizer;

    public RenderTexture NaiveDepthImage;

    public Matrix4x4 Intrinsics;
    public Matrix4x4 CameraToWorld;
    public int depth;
    public float invDepthMax;
    public float invDepthMin;

    public Texture2D QBuffer;

    public RenderTexture QImage;
    public RenderTexture DImage;
    public RenderTexture AImage;

    ~Keyframe()
    {
        Reset();
    }

    void Reset()
    {
        if (Image)
        {
            Image.Release();
            Image = null;
        }

        if (DepthImage)
        {
            DepthImage.Release();
            DepthImage = null;
        }

        if (CostVolume)
        {
            CostVolume.Release();
            CostVolume = null;
        }

        if(CostVolumeMinMax)
        {
            CostVolumeMinMax.Release();
            CostVolumeMinMax = null;
        }

        if (Regularizer)
        {
            Regularizer.Release();
            Regularizer = null;
        }
        
        if(NaiveDepthImage)
        {
            NaiveDepthImage.Release();
            NaiveDepthImage = null;
        }

        if(QImage)
        {
            QImage.Release();
            QImage = null;
        }

        if (DImage)
        {
            DImage.Release();
            DImage = null;
        }

        if (AImage)
        {
            AImage.Release();
            AImage = null;
        }
    }

    RenderTexture createRT(int width, int height, RenderTextureFormat format)
    {
        RenderTexture rt = new RenderTexture(width, height, 0, format, RenderTextureReadWrite.Linear);
        rt.enableRandomWrite = true;
        rt.filterMode = FilterMode.Point;
        rt.Create();
        return rt;
    }

    void initRegularizer(ComputeShader cs, float alpha, float beta)
    {
        int kernel = cs.FindKernel("CSMain");
        uint threadX, threadY, threadZ;
        cs.GetKernelThreadGroupSizes(kernel, out threadX, out threadY, out threadZ);
        cs.SetInt("_width", Image.width);
        cs.SetInt("_height", Image.height);
        cs.SetFloat("_alpha", alpha);
        cs.SetFloat("_beta", beta);
        cs.SetTexture(kernel, "Intensity", Image);
        cs.SetTexture(kernel, "Result", Regularizer);
        cs.Dispatch(kernel, Mathf.CeilToInt((float)Regularizer.width / (float)threadX), Mathf.CeilToInt((float)Regularizer.height / (float)threadY), (int)threadZ);
    }

    void resetCostVolume(ComputeShader cs)
    {
        int kernel = cs.FindKernel("CSMain");
        uint threadX, threadY, threadZ;
        cs.GetKernelThreadGroupSizes(kernel, out threadX, out threadY, out threadZ);
        cs.SetTexture(kernel, "Result", CostVolume);
        cs.Dispatch(kernel, Mathf.CeilToInt((float)CostVolume.width / (float)threadX), Mathf.CeilToInt((float)CostVolume.height / (float)threadY), (int)threadZ);
    }

    void updateCosts(SampleFrame frame, ComputeShader cs)
    {
        int kernel = cs.FindKernel("CSMain");
        uint threadX, threadY, threadZ;
        cs.GetKernelThreadGroupSizes(kernel, out threadX, out threadY, out threadZ);
        cs.SetInt("_width", Image.width);
        cs.SetInt("_height", Image.height);
        cs.SetInt("_depth", depth);
        cs.SetInt("_texWidth", CostVolume.width);
        cs.SetInt("_texHeight", CostVolume.height);
        cs.SetFloat("_invDepthMax", invDepthMax);
        cs.SetFloat("_invDepthMin", invDepthMin);

        Matrix4x4 keyframeToSample = frame.CameraToWorld.inverse * CameraToWorld;
        Matrix4x4 invIntrinsics = Intrinsics.inverse;
        cs.SetVector("_intrinsics", new Vector4(Intrinsics.m00, Intrinsics.m11, Intrinsics.m02, Intrinsics.m12));
        cs.SetVector("_invIntrinsics", new Vector4(invIntrinsics.m00, invIntrinsics.m11, invIntrinsics.m02, invIntrinsics.m12));
        cs.SetVector("_keyframeToSampleR0", keyframeToSample.GetRow(0));
        cs.SetVector("_keyframeToSampleR1", keyframeToSample.GetRow(1));
        cs.SetVector("_keyframeToSampleR2", keyframeToSample.GetRow(2));
        cs.SetTexture(kernel, "KeyframeImg", Image);
        cs.SetTexture(kernel, "SampleImg", frame.Image);
        cs.SetTexture(kernel, "Result", CostVolume);
        cs.Dispatch(kernel, Mathf.CeilToInt((float)CostVolume.width / (float)threadX), Mathf.CeilToInt((float)CostVolume.height / (float)threadY), (int)threadZ);
    }

    void updateCostsMinMax(ComputeShader cs)
    {
        int kernel = cs.FindKernel("CSMain");
        uint threadX, threadY, threadZ;
        cs.GetKernelThreadGroupSizes(kernel, out threadX, out threadY, out threadZ);
        cs.SetInt("_width", Image.width);
        cs.SetInt("_height", Image.height);
        cs.SetInt("_depth", depth);
        cs.SetInt("_texWidth", CostVolume.width);
        cs.SetInt("_texHeight", CostVolume.height);
        cs.SetTexture(kernel, "CostVolume", CostVolume);
        cs.SetTexture(kernel, "Result", CostVolumeMinMax);
        cs.Dispatch(kernel, Mathf.CeilToInt((float)CostVolumeMinMax.width / (float)threadX), Mathf.CeilToInt((float)CostVolumeMinMax.height / (float)threadY), (int)threadZ);
    }

    public void Initialize(SampleFrame frame, int numDepthValues,
        ComputeShader initCostShader, ComputeShader regularizerShader)
    {
        Reset();

        int width = frame.Image.width;
        int height = frame.Image.height;
        this.Intrinsics = frame.Intrinsics;
        this.CameraToWorld = frame.CameraToWorld;
        this.depth = numDepthValues;
        this.Image = frame.Image;
        this.DepthImage = createRT(width, height, RenderTextureFormat.ARGBFloat);
        this.NaiveDepthImage = createRT(width, height, RenderTextureFormat.ARGBFloat);
        this.Regularizer = createRT(width, height, RenderTextureFormat.ARGBFloat);

        this.QBuffer = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
        this.QBuffer.Apply();
        this.QImage = createRT(width, height, RenderTextureFormat.ARGBFloat);
        this.DImage = createRT(width, height, RenderTextureFormat.ARGBFloat);
        this.AImage = createRT(width, height, RenderTextureFormat.ARGBFloat);

        float maxDepthInMeters = 3;
        float minDepthInMeters = 0.5f;
        this.invDepthMax = Mathf.Max(1.0f / maxDepthInMeters, 1.0f / minDepthInMeters);
        this.invDepthMin = Mathf.Min(1.0f / maxDepthInMeters, 1.0f / minDepthInMeters);

        int numElems = (width * height * depth);
        int ideal2DPixels = Mathf.CeilToInt(Mathf.Sqrt((float)numElems));
        int texWidth = ideal2DPixels;
        int texHeight = ideal2DPixels;

        this.CostVolume = createRT(texWidth, texHeight, RenderTextureFormat.ARGBFloat);
        this.CostVolumeMinMax = createRT(width, height, RenderTextureFormat.ARGBFloat);
        frame.Image = null;

        resetCostVolume(initCostShader);
        initRegularizer(regularizerShader, 1, 2);
    }

    public void ComputeNaiveDepth(ComputeShader cs)
    {
        int kernel = cs.FindKernel("CSMain");
        uint threadX, threadY, threadZ;
        cs.GetKernelThreadGroupSizes(kernel, out threadX, out threadY, out threadZ);
        cs.SetInt("_width", NaiveDepthImage.width);
        cs.SetInt("_height", NaiveDepthImage.height);
        cs.SetInt("_depth", depth);
        cs.SetInt("_texWidth", CostVolume.width);
        cs.SetInt("_texHeight", CostVolume.height);
        cs.SetFloat("_invDepthMax", invDepthMax);
        cs.SetFloat("_invDepthMin", invDepthMin);
        cs.SetTexture(kernel, "CostVolume", CostVolume);
        cs.SetTexture(kernel, "Result", NaiveDepthImage);
        cs.Dispatch(kernel, Mathf.CeilToInt((float)NaiveDepthImage.width / (float)threadX), Mathf.CeilToInt((float)NaiveDepthImage.height / (float)threadY), (int)threadZ);
    }

    public void UpdateCostVolume(SampleFrame frame, ComputeShader updateCostShader, ComputeShader updateCostMinMaxShader)
    {
        updateCosts(frame, updateCostShader);
        updateCostsMinMax(updateCostMinMaxShader);
    }

    void initSolver(ComputeShader cs)
    {
        int kernel = cs.FindKernel("CSMain");
        uint threadX, threadY, threadZ;
        cs.GetKernelThreadGroupSizes(kernel, out threadX, out threadY, out threadZ);
        cs.SetInt("_width", AImage.width);
        cs.SetInt("_height", AImage.height);
        cs.SetInt("_depth", depth);
        cs.SetInt("_texWidth", CostVolume.width);
        cs.SetInt("_texHeight", CostVolume.height);
        cs.SetFloat("_invDepthMax", invDepthMax);
        cs.SetFloat("_invDepthMin", invDepthMin);
        cs.SetTexture(kernel, "CostVolume", CostVolume);
        cs.SetTexture(kernel, "ResultQ", QImage);
        cs.SetTexture(kernel, "ResultD", DImage);
        cs.SetTexture(kernel, "ResultA", AImage);
        cs.Dispatch(kernel, Mathf.CeilToInt((float)AImage.width / (float)threadX), Mathf.CeilToInt((float)AImage.height / (float)threadY), (int)threadZ);
    }

    void qStep(ComputeShader cs, float epsilon, float sigmaQ, ComputeShader csProj)
    {
        int kernel = cs.FindKernel("CSMain");
        uint threadX, threadY, threadZ;
        cs.GetKernelThreadGroupSizes(kernel, out threadX, out threadY, out threadZ);
        cs.SetInt("_width", QImage.width);
        cs.SetInt("_height", QImage.height);
        cs.SetFloat("_epsilon", epsilon);
        cs.SetFloat("_sigma_q", sigmaQ);
        cs.SetTexture(kernel, "Regularizer", Regularizer);
        cs.SetTexture(kernel, "ResultQ", QImage);
        cs.SetTexture(kernel, "ResultD", DImage);
        cs.Dispatch(kernel, Mathf.CeilToInt((float)QImage.width / (float)threadX), Mathf.CeilToInt((float)QImage.height / (float)threadY), (int)threadZ);
    }

    void dStep(ComputeShader cs, float sigmaD, float theta)
    {
        int kernel = cs.FindKernel("CSMain");
        uint threadX, threadY, threadZ;
        cs.GetKernelThreadGroupSizes(kernel, out threadX, out threadY, out threadZ);
        cs.SetInt("_width", DImage.width);
        cs.SetInt("_height", DImage.height);
        cs.SetFloat("_sigma_d", sigmaD);
        cs.SetFloat("_theta_n", theta);
        cs.SetTexture(kernel, "Regularizer", Regularizer);
        cs.SetTexture(kernel, "ResultQ", QImage);
        cs.SetTexture(kernel, "ResultD", DImage);
        cs.SetTexture(kernel, "ResultA", AImage);
        cs.Dispatch(kernel, Mathf.CeilToInt((float)DImage.width / (float)threadX), Mathf.CeilToInt((float)DImage.height / (float)threadY), (int)threadZ);
    }

    void aStep(ComputeShader cs, float lambda, float theta)
    {
        int kernel = cs.FindKernel("CSMain");
        uint threadX, threadY, threadZ;
        cs.GetKernelThreadGroupSizes(kernel, out threadX, out threadY, out threadZ);
        cs.SetInt("_width", AImage.width);
        cs.SetInt("_height", AImage.height);
        cs.SetInt("_depth", depth);
        cs.SetInt("_texWidth", CostVolume.width);
        cs.SetInt("_texHeight", CostVolume.height);
        cs.SetFloat("_invDepthMax", invDepthMax);
        cs.SetFloat("_invDepthMin", invDepthMin);
        cs.SetFloat("_lambda", lambda);
        cs.SetFloat("_theta_n", theta);
        cs.SetTexture(kernel, "CostVolume", CostVolume);
        cs.SetTexture(kernel, "CostVolumeMinMax", CostVolumeMinMax);
        cs.SetTexture(kernel, "ResultD", DImage);
        cs.SetTexture(kernel, "ResultA", AImage);
        cs.Dispatch(kernel, Mathf.CeilToInt((float)AImage.width / (float)threadX), Mathf.CeilToInt((float)AImage.height / (float)threadY), (int)threadZ);
    }

    public void SolveDepthMap(ComputeShader initSolveShader, ComputeShader updateQShader,
        ComputeShader updateDShader, ComputeShader updateAShader, ComputeShader regularizerShader, ComputeShader projectQShader, DTAMSettings settings)
    {
        initRegularizer(regularizerShader, settings.alpha, settings.beta);
        initSolver(initSolveShader);

        float theta_n = 0.2f;
        float theta_end = 1.0e-4f;
        float lambda = 1.0f / (1.0f + 0.5f * settings.minSceneDepth);// 1);// 1.0e-6f;
        float beta = 0.001f;
        int cur_iter = 1;
        int max_iters = 1000;
        float epsilon = settings.epsilon;
        float sigmaQ = settings.sigmaQ;//1.0e-2f;
        float sigmaD = settings.sigmaD;// 1.0e-2f;
        while (theta_n > theta_end && cur_iter < max_iters)
        {
            qStep(updateQShader, epsilon, sigmaQ, projectQShader);
            dStep(updateDShader, sigmaD, theta_n);
            aStep(updateAShader, lambda, theta_n);
            if(theta_n < 1.0e-3)
            {
                beta = 0.0001f;
            }
            theta_n *= (1.0f - beta * cur_iter);
            //sigmaQ *= (1.0f - beta * cur_iter);
            //sigmaD *= (1.0f - beta * cur_iter);
            cur_iter++;
        }
        Debug.Log("Num iterations: " + cur_iter);
    }
}

[System.Serializable]
public class DTAMSettings
{
    public float minSceneDepth = 1;
    public float sigmaQ = 1.0e-2f;
    public float sigmaD = 1.0e-2f;
    public float alpha = 1;
    public float beta = 2;
    public float epsilon = 1.0e-4f;
}