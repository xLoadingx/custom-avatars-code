using System;
using System.Collections;
using System.Text;
using Il2CppRUMBLE.Players;
using Il2CppSystem.IO;
using MelonLoader;
using UnityEngine.Networking;
using File = System.IO.File;

namespace CustomAvatars;

public class RemoteAvatarNetworking
{
    private static MelonLogger.Instance logger => Melon<Main>.Logger;

    private const string T1 = "Z2l0aHViX3BhdF8xMUFWR0taSlkwcTRPTXRzNFQzWm1mX3QxbjNxQVpGbQ==";
    private const string T2 = "anJNNUtPTXVoM3lhNjlablJCVmdZYzl0ZzFDYk9yUzRWRkhFQURNUUg1QXdJVWkzMk4=";
    public const string KEY = "otc9jahbpt";
    
    // Helpers
    public static string GetUrlForID(string id) => $"https://raw.githubusercontent.com/xLoadingx/custom-avatars/main/avatars/{id}.rumbleavatar";

    public static void SetRequest(UnityWebRequest req)
    {
        req.SetRequestHeader("User-Agent", "CustomAvatars");
        req.SetRequestHeader("Authorization", $"Bearer {TKN()}");
    }

    public static string Xor(string input, string key)
    {
        var output = new char[input.Length];

        for (int i = 0; i < input.Length; i++)
            output[i] = (char)(input[i] ^ key[i % key.Length]);

        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(output));
        return Uri.EscapeDataString(b64);
    }

    private static string TKN()
    {
        string str1 = Encoding.UTF8.GetString(Convert.FromBase64String(T1));
        string str2 = Encoding.UTF8.GetString(Convert.FromBase64String(T2));
        return str1 + str2;
    }
    
    // ----------------------------------------------------------
    
    public static IEnumerator RemoteAvatarExists(string masterId, Action<bool> callback)
    {
        masterId = Xor(masterId, KEY);
        
        string url = GetUrlForID(masterId);

        var req = UnityWebRequest.Head(url);
        SetRequest(req);
        
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            // 404 = 'File Doesn't Exist'
            if (req.responseCode != 404)
                logger.Warning($"AvatarExists error: {req.error}");
            
            callback?.Invoke(false);
            yield break;
        }

        callback?.Invoke(true);
        req.Dispose();
    }
    
    public static IEnumerator GetAvatarAsset(
        string masterId, 
        Action<byte[]> callback,
        Action<float> onProgress = null,
        Func<bool> isCancelled = null)
    {
        masterId = Xor(masterId, KEY);
        string url = GetUrlForID(masterId);

        var headReq = UnityWebRequest.Head(url);
        SetRequest(headReq);

        yield return headReq.SendWebRequest();

        if (headReq.result == UnityWebRequest.Result.Success)
        {
            string lengthHeader = headReq.GetResponseHeader("Content-Length");
            if (long.TryParse(lengthHeader, out var size))
            {
                if (size > Main.instance.MaxFileDownloadSize.Value * 1024f * 1024f)
                {
                    logger.Warning($"Avatar too large for player {masterId} ({size} bytes)");
                    callback?.Invoke(null);
                    yield break;
                }
            }
        }

        headReq.Dispose();

        var req = UnityWebRequest.Get(url);
        SetRequest(req);
        
        var downloadReq = req.SendWebRequest();

        while (!downloadReq.isDone)
        {
            if (isCancelled?.Invoke() == true)
            {
                req.Abort();
                callback?.Invoke(null);
                yield break;
            }
            
            onProgress?.Invoke(downloadReq.progress);
            yield return null;
        }

        if (req.result != UnityWebRequest.Result.Success)
        {
            logger.Msg($"No avatar for {masterId} ({req.responseCode}");
            callback?.Invoke(null);
            yield break;
        }

        byte[] data = req.downloadHandler.data;

        if (data == null || data.Length == 0)
        {
            logger.Warning("Downloaded empty avatar");
            callback?.Invoke(null);
            yield break;
        }
        
        logger.Msg($"Downloaded avatar for {masterId} ({data.Length} bytes)");

        callback?.Invoke(data);

        req.Dispose();
    }
}