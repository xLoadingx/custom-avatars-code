using System.Collections;
using Il2CppSystem.Text;
using MelonLoader;
using MelonLoader.Utils;
using RumbleModdingAPI;
using UnityEngine;
using UnityEngine.Networking;

namespace CustomAvatars;

public class RemoteAvatarLoader
{
    // ====== CONFIG ======
    const string GH_REPO = "xLoadingx/custom-avatars";
    const string BRANCH = "main";
    const string ASSET_NAME = "Rig";

    private static readonly string RootDir =
        Path.Combine(MelonEnvironment.UserDataDirectory, "CustomAvatars", "Opponents");

    private const string PART_A_B64 = "PTMuMi84BSo7LgVraxsMHREAEANqH2spLzwdFihrKS5rBTwrAi49NBYdPBQVKw==";
    private const string PART_B_B64 = "Dh0iEzwJPjsjPm4xIBwYbCw7Cz0gCBgYEjcwa2NuA25sAw4SAxUZLQIiaDc/FD4=";
    private const byte XOR_KEY = 0x5A;

    static string GetToken()
    {
        byte[] a = Convert.FromBase64String(PART_A_B64);
        byte[] b = Convert.FromBase64String(PART_B_B64);
        for (int i = 0; i < a.Length; i++) a[i] ^= XOR_KEY;
        for (int i = 0; i < b.Length; i++) b[i] ^= XOR_KEY;
        var merged = new byte[a.Length + b.Length];
        Buffer.BlockCopy(a, 0, merged, 0, a.Length);
        Buffer.BlockCopy(b, 0, merged, a.Length, b.Length);
        return Encoding.UTF8.GetString(merged);
    }

    static void SetGhHeaders(UnityWebRequest req, bool wantRaw)
    {
        req.SetRequestHeader("User-Agent", "CustomAvatars/1.0");
        req.SetRequestHeader("Authorization", "Bearer " + GetToken());
        req.SetRequestHeader("Accept", wantRaw
            ? "application/vnd.github.raw"
            : "application/vnd.github+json");
    }

    static string LocalPath(string masterId)
    {
        Directory.CreateDirectory(RootDir);
        return Path.Combine(RootDir, masterId);
    }

    static string GhUrl(string masterId)
    {
        var fname = Uri.EscapeDataString(masterId);
        return $"https://api.github.com/repos/{GH_REPO}/contents/avatars/{fname}?ref={BRANCH}";
    }

    static string UploadUrlForPath(string pathRelativeToRepoRoot)
    {
        var fname = Uri.EscapeDataString(pathRelativeToRepoRoot);
        return $"https://api.github.com/repos/{GH_REPO}/contents/{fname}";
    }
    
    static IEnumerator SendAudit(string tag, string jsonPayload)
    {
        var url = UploadUrlForPath($"logs/{System.DateTime.UtcNow:yyyy-MM-dd}/{System.Guid.NewGuid():N}.json");
        var body = $"{{\"message\":\"log:{tag}\",\"content\":\"{System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(jsonPayload))}\",\"branch\":\"{BRANCH}\"}}";
        
        var req = new UnityWebRequest(url, "PUT");
        req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        SetGhHeaders(req, wantRaw: false);
        yield return req.SendWebRequest();

        req.Dispose();
    }

    public static void UploadBundle(string masterId, string path, System.Action<bool> done) =>
        MelonCoroutines.Start(UploadBundleCoroutine(masterId, path, done));

    public static IEnumerator UploadBundleCoroutine(string masterId, string path, System.Action<bool> done)
    {
        var data = Calls.Players.GetLocalPlayer().Data.GeneralData;
        if (masterId != data.PlayFabMasterId)
        {
            MelonLogger.Error($"Player tried to upload avatar for masterId that isn't theirs. Please do not mess with stuff like that, as I can see who tries to upload stuff.");
            MelonCoroutines.Start(
                SendAudit(
                    "masterId_mismatch", 
                    $"[{System.DateTime.UtcNow:O}] Player {data.PublicUsername.TrimString()} ({data.PlayFabMasterId}) tried to write avatar for MasterId {masterId}"
                )
            );
            yield break;
        }
        
        if (!File.Exists(path))
        {
            Main.instance.LoggerInstance.Error($"AssetBundle at path '{path}' does not exist.");
            done?.Invoke(false);
            yield break;
        }

        byte[] bytes;
        try { bytes = File.ReadAllBytes(path); }
        catch (Exception e)
        {
            Main.instance.LoggerInstance.Error($"ReadAllBytes failed: {e.Message}");
            done?.Invoke(false);
            yield break;
        }

        if (string.IsNullOrWhiteSpace(masterId) || bytes.Length == 0)
        { done?.Invoke(false); yield break; }

        // --- SHA for update ---
        string sha = null;
        yield return GetSha(masterId, s => sha = s);

        var body = $"{{\"message\":\"Upload bundle for {masterId}. Uploaded by {Calls.Players.GetLocalPlayer().Data.GeneralData.PublicUsername.TrimString()}\",\"content\":\"{System.Convert.ToBase64String(bytes)}\",\"branch\":\"{BRANCH}\"" +
                   (sha != null ? $",\"sha\":\"{sha}\"" : "") + "}";

        var url = UploadUrlForPath($"avatars/{masterId}");
        var req = new UnityWebRequest(url, "PUT");
        req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        SetGhHeaders(req, wantRaw: false);

        yield return req.SendWebRequest();

        bool ok = req.result == UnityWebRequest.Result.Success &&
                  req.responseCode >= 200 && req.responseCode < 300;

        if (!ok)
        {
            var errBytes = req.downloadHandler?.data;
            var errTxt = errBytes != null ? global::System.Text.Encoding.UTF8.GetString(errBytes) : "";
            Main.instance.LoggerInstance.Error(
                $"Upload failed {masterId}: {req.responseCode} {req.error}\n{errTxt}");
            
            MelonCoroutines.Start(
                SendAudit(
                    "upload_fail",
                    $"[{System.DateTime.UtcNow:O}] Upload failed for MasterId {masterId} " +
                    $"Code={req.responseCode} Error={req.error} " +
                    $"Body={(string.IsNullOrWhiteSpace(errTxt) ? "<empty>" : errTxt)}"
                )
            );
        }

        req.Dispose();
        done?.Invoke(ok);
    }

    static IEnumerator GetSha(string masterId, System.Action<string> cb)
    {
        var url = $"https://api.github.com/repos/{GH_REPO}/contents/avatars/{Uri.EscapeDataString(masterId)}?ref={BRANCH}";
        var req = UnityWebRequest.Get(url);
        SetGhHeaders(req, wantRaw:false);
        yield return req.SendWebRequest();
        if (req.result != UnityWebRequest.Result.Success) { cb(null); req.Dispose(); yield break; }

        var txt = req.downloadHandler.text;
        req.Dispose();

        int i = txt.IndexOf("\"sha\":\"", System.StringComparison.Ordinal);
        if (i < 0) { cb(null); yield break; }
        i += 7; int j = txt.IndexOf('\"', i);
        cb(j > i ? txt.Substring(i, j - i) : null);
    }

    static IEnumerator DownloadToFile(string masterId, string savePath)
    {
        var req = UnityWebRequest.Get(GhUrl(masterId));
        SetGhHeaders(req, true);
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
            Main.instance.LoggerInstance.Error($"Download failed for {masterId}: {req.error}");
        else
            File.WriteAllBytes(savePath, req.downloadHandler.data);

        req.Dispose();
    }

    public static IEnumerator FetchAssetBundle(string masterId, Action<AssetBundle> onReady, bool forceRefresh = false)
    {
        if (string.IsNullOrWhiteSpace(masterId)) yield break;

        var path = LocalPath(masterId);
        if (forceRefresh || !File.Exists(path))
        {
            yield return DownloadToFile(masterId, path);
            if (!File.Exists(path)) yield break;
        }

        var ab = Calls.LoadAssetBundleFromFile(path);
        if (!ab) { Main.instance.LoggerInstance.Error($"AssetBundle load failed: {path}"); yield break; }

        onReady?.Invoke(ab);
    }
}