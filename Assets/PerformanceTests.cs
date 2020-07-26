using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Networking;
using UnityEngine.Rendering;

public class PerformanceTests : MonoBehaviour
{
    private string logBuffer = "";
    public List<string> urls;

    public class ABReqInfo
    {
        public string url;
        public UnityWebRequest request;
        public AsyncOperation requestAsyncOp;
        public AssetBundle ab;

        public List<AssetReqInfo> assetsInfo = new List<AssetReqInfo>();

        public class AssetReqInfo
        {
            public string assetName;
            public AssetBundleRequest loadOp;
        }
    }

    private List<ABReqInfo> reqs = new List<ABReqInfo>();

    [ContextMenu("Load Urls")]
    public void LoadUrls()
    {
        string urlListText = File.ReadAllText("D:/url_list.txt");

        string[] rawUrls = urlListText.Split('\n');

        urls = rawUrls.Take(20).ToList();
    }

    IEnumerator SendAllRequests()
    {
        foreach (string s in urls)
        {
            var req = UnityWebRequestAssetBundle.GetAssetBundle(s);
            var asyncOp = req.SendWebRequest();
            //Debug.Log("Downloading... " + s);
            ABReqInfo reqInfo = new ABReqInfo() {url = s, request = req, requestAsyncOp = asyncOp};
            reqs.Add(reqInfo);
        }

        foreach (var req in reqs)
        {
            yield return req.requestAsyncOp;
            //Debug.Log("Download finished for: " + req.url);
        }

        reqs = reqs.Where((x) => !x.request.isHttpError && !x.request.isNetworkError).ToList();
    }

    IEnumerator TestLoadAsset()
    {
        foreach (var reqInfo in reqs)
        {
            var ab = DownloadHandlerAssetBundle.GetContent(reqInfo.request);

            var assets = ab.GetAllAssetNames();

            foreach (var a in assets)
            {
                float time = Time.realtimeSinceStartup;
                ab.LoadAsset(a);
                time = Time.realtimeSinceStartup - time;
                logBuffer += $"LoadAsset() {a}... time: {time * 1000}ms\n";
                yield return null;
            }
        }

        yield return Resources.UnloadUnusedAssets();
        Caching.ClearCache();
    }

    IEnumerator TestLoadAllAssetsAsync()
    {
        foreach (var reqInfo in reqs)
        {
            var ab = DownloadHandlerAssetBundle.GetContent(reqInfo.request);

            var assets = ab.GetAllAssetNames();

            reqInfo.ab = ab;
            reqInfo.assetsInfo.Clear();

            float time = Time.realtimeSinceStartup;
            var loadOp = ab.LoadAllAssetsAsync();
            reqInfo.assetsInfo.Add(new ABReqInfo.AssetReqInfo {assetName = ab.name, loadOp = loadOp});
            time = Time.realtimeSinceStartup - time;
            logBuffer += $"LoadAllAssetsAsync() {ab.name}... time: {time * 1000}ms\n";
        }

        yield return AsyncLoadAllAssets();

        yield return Resources.UnloadUnusedAssets();
        Caching.ClearCache();
    }

    IEnumerator TestLoadAssetAsync()
    {
        foreach (var reqInfo in reqs)
        {
            var ab = DownloadHandlerAssetBundle.GetContent(reqInfo.request);
            var assets = ab.GetAllAssetNames();

            reqInfo.ab = ab;
            reqInfo.assetsInfo.Clear();

            foreach (var a in assets)
            {
                float time = Time.realtimeSinceStartup;
                var loadOp = ab.LoadAssetAsync(a);
                reqInfo.assetsInfo.Add(new ABReqInfo.AssetReqInfo {assetName = ab.name, loadOp = loadOp});
                time = Time.realtimeSinceStartup - time;
                logBuffer += $"LoadAssetAsync() {ab.name}... time: {time * 1000}ms\n";
            }
        }

        yield return AsyncLoadAllAssets();

        yield return Resources.UnloadUnusedAssets();
        Caching.ClearCache();
    }


    IEnumerator AsyncLoadAllAssets()
    {
        foreach (var reqInfo in reqs)
        {
            foreach (var assetInfo in reqInfo.assetsInfo)
            {
                float frames = Time.frameCount;
                List<float> dtList = new List<float>();

                dtList.Add(Time.unscaledDeltaTime);

                float time = Time.realtimeSinceStartup;
                while (!assetInfo.loadOp.isDone)
                {
                    yield return null;
                    dtList.Add(Time.unscaledDeltaTime);
                }

                float median = dtList[dtList.Count / 2] * 1000;
                time = (Time.realtimeSinceStartup - time) * 1000;

                frames = Time.frameCount - frames;
                logBuffer += $"wait for AssetBundleRequest {assetInfo.assetName}... total time: {time}ms... median dt: {median}ms. frames elapsed...{frames}.\n";
            }
        }

        yield return null;
    }


    IEnumerator Start()
    {
        Caching.ClearCache();
        yield return new WaitForSeconds(2.0f);
        logBuffer += "Frametime baseline test...\n";
        var coroutine = StartCoroutine(HiccupDetector());

        for (int i = 0; i < 30; i++)
            yield return null;

        StopCoroutine(coroutine);
        logBuffer += "Sending all requests...\n";
        yield return SendAllRequests();

        Debug.Log(logBuffer);
        logBuffer = "";
        List<float> frames = new List<float>();

        logBuffer += "LoadAsset() test...\n";
        frames.Clear();

        coroutine = StartCoroutine(FrameCounter(frames));

        float time = Time.realtimeSinceStartup;
        yield return TestLoadAsset();
        time = (Time.realtimeSinceStartup - time) * 1000;

        StopCoroutine(coroutine);

        logBuffer += $"LoadAsset() min {GetMin(frames)}ms\n";
        logBuffer += $"LoadAsset() max {GetMax(frames)}ms\n";
        logBuffer += $"LoadAsset() median {GetMedian(frames)}ms\n";

        logBuffer += $"LoadAsset() total time... {time}ms\n";

        Debug.Log(logBuffer);
        logBuffer = "";

        logBuffer += "LoadAssetAsync() test...\n";

        coroutine = StartCoroutine(FrameCounter(frames));

        time = Time.realtimeSinceStartup;
        yield return TestLoadAssetAsync();
        time = (Time.realtimeSinceStartup - time) * 1000;

        StopCoroutine(coroutine);

        logBuffer += $"LoadAssetAsync() min {GetMin(frames)}ms\n";
        logBuffer += $"LoadAssetAsync() max {GetMax(frames)}ms\n";
        logBuffer += $"LoadAssetAsync() median {GetMedian(frames)}ms\n";

        logBuffer += $"LoadAssetAsync() total time... {time}ms\n";

        Debug.Log(logBuffer);
        logBuffer = "";

        logBuffer += "LoadAllAssetsAsync() test...\n";

        coroutine = StartCoroutine(FrameCounter(frames));

        time = Time.realtimeSinceStartup;
        yield return TestLoadAllAssetsAsync();
        time = (Time.realtimeSinceStartup - time) * 1000;

        StopCoroutine(coroutine);

        logBuffer += $"LoadAllAssetsAsync() total time...{time}ms\n";

        logBuffer += $"LoadAllAssetsAsync() min {GetMin(frames)}ms\n";
        logBuffer += $"LoadAllAssetsAsync() max {GetMax(frames)}ms\n";
        logBuffer += $"LoadAllAssetsAsync() median {GetMedian(frames)}ms\n";

        Debug.Log(logBuffer);
        logBuffer = "";
    }

    IEnumerator FrameCounter(List<float> frames)
    {
        frames.Clear();
        while (true)
        {
            yield return null;
            frames.Add(Time.unscaledDeltaTime);
        }
    }

    public float GetMax(List<float> frames)
    {
        return frames.Max() * 1000.0f;
    }

    public float GetMin(List<float> frames)
    {
        return frames.Min() * 1000.0f;
    }

    public float GetMedian(List<float> frames)
    {
        if (frames.Count == 1)
            return frames[0] * 1000.0f;

        return frames[frames.Count / 2] * 1000.0f;
    }

    IEnumerator HiccupDetector()
    {
        while (true)
        {
            yield return null;
            logBuffer += $"Frame time = {Time.unscaledDeltaTime * 1000}ms\n";
        }
    }
}