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

    private const int MAX_UPLOAD_BYTES = 25 * 1024 * 1024; // 25 MB

    private const string PART_A_B64 = "PTMuMi84BSo7LgVraxsMHREAEANqH2spLzwdFihrKS5rBTwrAi49NBYdPBQVKw==";
    private const string PART_B_B64 = "Dh0iEzwJPjsjPm4xIBwYbCw7Cz0gCBgYEjcwa2NuA25sAw4SAxUZLQIiaDc/FD4=";
    private const byte XOR_KEY = 0x5A;
    
    private static readonly HashSet<string> _downloadingPlayers = new();

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

    public static void UploadBundle(string masterId, string path, System.Action<bool, bool> done) =>
        MelonCoroutines.Start(UploadBundleCoroutine(masterId, path, done));

    public static IEnumerator UploadBundleCoroutine(string masterId, string path, System.Action<bool, bool> done)
    {
        var data = Calls.Players.GetLocalPlayer().Data.GeneralData;
        if (masterId != data.PlayFabMasterId)
        {
            MelonLogger.Error($"Player tried to upload avatar for masterId that isn't theirs. Please do not mess with stuff like that. It benefits no one.");
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
            done?.Invoke(false, false);
            yield break;
        }

        byte[] bytes;
        try { bytes = File.ReadAllBytes(path); }
        catch (Exception e)
        {
            Main.instance.LoggerInstance.Error($"ReadAllBytes failed: {e.Message}");
            done?.Invoke(false, false);
            yield break;
        }

        if (bytes.Length > MAX_UPLOAD_BYTES)
        {
            Main.instance.LoggerInstance.Error($"Upload failed: Bundle size {bytes.Length / (1024 * 1024)} MB exceeds {MAX_UPLOAD_BYTES / (1024 * 1024)} MB Limit.");
            done.Invoke(false, false);
            yield break;
        }

        if (string.IsNullOrWhiteSpace(masterId) || bytes.Length == 0)
        { done?.Invoke(false, false); yield break; }

        // SHA for updating files
        string sha = null;
        Main.instance.LoggerInstance.Msg("Fetching remote SHA...");
        yield return GetSha(masterId, s => sha = s);
        Main.instance.LoggerInstance.Msg(sha != null
            ? $"Remote SHA: {sha.Substring(0, 8)}"
            : "No remote file found - will create new file.");

        if (sha != null && !string.IsNullOrEmpty(sha) && ShaMatchesLocal(sha, path))
        {
            Main.instance.LoggerInstance.Msg("Upload skipped: Local file is identical to the server version.");
            done?.Invoke(true, true);
            yield break;
        }
        
        Main.instance.LoggerInstance.Msg($"File size: {bytes.Length / 1024f / 1024f:F2} MB");
        
        Main.instance.LoggerInstance.Msg("Uploading to GitHub...");

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
        done?.Invoke(ok, false);
    }

    static bool ShaMatchesLocal(string remoteSha, string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        var header = System.Text.Encoding.ASCII.GetBytes($"blob {bytes.Length}\0");
        
        using var sha1 = System.Security.Cryptography.SHA1.Create();
        sha1.TransformBlock(header, 0, header.Length, header, 0);
        sha1.TransformFinalBlock(bytes, 0, bytes.Length);
        
        var hex = BitConverter.ToString(sha1.Hash).Replace("-", "").ToLowerInvariant();
        Main.instance.LoggerInstance.Msg($"Local SHA: {hex.Substring(0, 8)}");
        return hex == remoteSha;
    }

    public static IEnumerator PlayerHasAvatar(string masterId, Action<bool> callback)
    {
        if (File.Exists(Path.Combine(MelonEnvironment.UserDataDirectory, "CustomAvatars", "Opponents", masterId))) callback(true);
        yield return MelonCoroutines.Start(GetSha(masterId, sha => callback(!string.IsNullOrEmpty(sha))));
    }

    static IEnumerator GetSha(string masterId, System.Action<string> cb)
    {
        var url = $"https://api.github.com/repos/{GH_REPO}/contents/avatars/{Uri.EscapeDataString(masterId)}?ref={BRANCH}";
        var req = UnityWebRequest.Get(url);
        SetGhHeaders(req, wantRaw:false);
        yield return req.SendWebRequest();
        
        if ((long)req.responseCode == 404) { req.Dispose(); cb(null); yield break; }
        MelonLogger.Msg($"GitHub responded {req.responseCode}: {req.result}");

        if (req.result != UnityWebRequest.Result.Success)
        {
            Main.instance.LoggerInstance.Error($"Web request completed unsuccessfully | ERROR {req.responseCode} | {req.error}");
            req.Dispose(); cb(null); yield break;
        }

        var data = req.downloadHandler?.data;
        req.Dispose();
        if (data == null || data.Length == 0) { cb(null); yield break; }
        
        var txt = System.Text.Encoding.UTF8.GetString(data);

        int i = txt.IndexOf("\"sha\":\"", System.StringComparison.Ordinal);
        if (i < 0) { cb(null); yield break; }
        i += 7; int j = txt.IndexOf('\"', i);
        cb(j > i ? txt.Substring(i, j - i) : null);
    }

    public static IEnumerator DownloadToFile(string masterId, string savePath)
    {
        if (!_downloadingPlayers.Add(masterId))
        {
            Main.instance.LoggerInstance.Warning($"Player {masterId} is already being downloaded.");
            yield break;
        }
        
        var metaUrl = $"https://api.github.com/repos/{GH_REPO}/contents/avatars/{Uri.EscapeDataString(masterId)}?ref={BRANCH}";
        var metaReq = UnityWebRequest.Get(metaUrl);
        SetGhHeaders(metaReq, wantRaw: false);
        yield return metaReq.SendWebRequest();

        if (metaReq.result != UnityWebRequest.Result.Success)
        {
            Main.instance.LoggerInstance.Error($"Metadata fetch failed for {masterId}: {metaReq.error}");
            metaReq.Dispose();
            yield break;
        }

        try
        {
            var bytes = metaReq.downloadHandler?.data;
            if (bytes == null || bytes.Length == 0)
            {
                metaReq.Dispose();
                yield break;
            }
            
            var json = System.Text.Encoding.UTF8.GetString(bytes);
            
            var sizeIndex = json.IndexOf("\"size\":", StringComparison.Ordinal);
            if (sizeIndex >= 0)
            {
                sizeIndex += 7;
                int endIndex = json.IndexOfAny(new char[] { ',', '}' }, sizeIndex);
                var sizeStr = json.Substring(sizeIndex, endIndex - sizeIndex).Trim();
                if (int.TryParse(sizeStr, out int fileSizeBytes))
                {
                    int maxDownloadBytes = (int)Main.instance.downloadLimitMB.SavedValue * 1024 * 1024;
                    if (fileSizeBytes > maxDownloadBytes)
                    {
                        Main.instance.LoggerInstance.Warning(
                            $"Download skipped: {fileSizeBytes / (1024 * 1024)} MB exceeds limit of {maxDownloadBytes / (1024 * 1024)} MB.");
                        metaReq.Dispose();
                        yield break;
                    }
                }
            }
        }
        catch (Exception e)
        {
            Main.instance.LoggerInstance.Error($"Error parsing metadata for {masterId}: {e.Message}");
            metaReq.Dispose();
            yield break;
        }
        metaReq.Dispose();
        
        var req = UnityWebRequest.Get(GhUrl(masterId));
        SetGhHeaders(req, true);
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
            Main.instance.LoggerInstance.Error($"Download failed for {masterId}: {req.error}");
        else
            File.WriteAllBytes(savePath, req.downloadHandler.data);

        _downloadingPlayers.Remove(masterId);

        req.Dispose();
    }
}