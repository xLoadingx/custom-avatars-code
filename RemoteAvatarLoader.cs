using System.Collections;
using System.Globalization;
using Il2CppSystem.Text;
using Il2CppTMPro;
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

    private const int MAX_UPLOAD_BYTES = 25 * 1024 * 1024; // 25 MB

    private const string PART_A_B64 = "PTMuMi84BSo7LgVraxsMHREAEANqH2spLzwdFihrKS5rBTwrAi49NBYdPBQVKw==";
    private const string PART_B_B64 = "Dh0iEzwJPjsjPm4xIBwYbCw7Cz0gCBgYEjcwa2NuA25sAw4SAxUZLQIiaDc/FD4=";
    private const byte XOR_KEY = 0x5A;
    
    private static readonly HashSet<string> _downloadingPlayers = new();
    public static bool isUploading = false;
    public static object uploadCoroutine;

    // Helper for GitHub API authentication
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

    // Simple encryption
    // Doesn't do much, but its good for preventing casual copying
    public static byte[] XorCrypt(byte[] data, byte key = 0x5A)
    {
        for (int i = 0; i < data.Length; i++)
            data[i] ^= key;
        return data;
    }

    // Adds required GitHub headers to the request.
    // Mostly not to get rate-limited.
    static void SetGhHeaders(UnityWebRequest req, bool wantRaw)
    {
        req.SetRequestHeader("User-Agent", "CustomAvatars/1.0");
        req.SetRequestHeader("Authorization", "Bearer " + GetToken());
        req.SetRequestHeader("Accept", wantRaw
            ? "application/vnd.github.raw"
            : "application/vnd.github+json");
    }

    // Builds GitHub API URL for fetching an avatar.
    static string GhUrl(string masterId)
    {
        var fname = Uri.EscapeDataString(masterId);
        return $"https://api.github.com/repos/{GH_REPO}/contents/avatars/{fname}?ref={BRANCH}";
    }

    // Builds GitHub API URL for uploading files
    static string UploadUrlForPath(string pathRelativeToRepoRoot)
    {
        var fname = Uri.EscapeDataString(pathRelativeToRepoRoot);
        return $"https://api.github.com/repos/{GH_REPO}/contents/{fname}";
    }
    
    // Sends a small JSON log to GitHub (tattletale system)
    static IEnumerator SendAudit(string tag, string jsonPayload)
    {
        var url = UploadUrlForPath($"logs/{DateTime.UtcNow:yyyy-MM-dd}/{Guid.NewGuid():N}.json");
        var body = $"{{\"message\":\"log:{tag}\",\"content\":\"{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(jsonPayload))}\",\"branch\":\"{BRANCH}\"}}";
        
        var req = new UnityWebRequest(url, "PUT");
        req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        SetGhHeaders(req, wantRaw: false);
        yield return req.SendWebRequest();

        req.Dispose();
    }

    // Starts coroutine to upload a bundle to GitHub
    // Mostly because I hate typing MelonCoroutines.Start()
    public static void UploadBundle(string masterId, string path, Action onStartUpload, Action<bool, bool> done, Action<float> onProgress = null, TextMeshPro serverStatusText = null) => 
        uploadCoroutine ??= MelonCoroutines.Start(UploadBundleCoroutine(masterId, path, onStartUpload, done, onProgress, serverStatusText));

    // Okay here's the actual coroutine
    // Full upload workflow: validate, check SHA, skip if identical, else let GitHub eat it.
    // Sure, the WaitForSeconds does slow down the process a bit, but visually it's a lot better
    public static IEnumerator UploadBundleCoroutine(
        string masterId, 
        string path, 
        Action onStartUpload,
        Action<bool, bool> done, 
        Action<float> onProgress = null, /* 0-1 progress callback */
        TextMeshPro serverStatusText = null
    )
    {
        var data = Calls.Players.GetLocalPlayer().Data.GeneralData;
        if (masterId != data.PlayFabMasterId)
        {
            Main.instance.LoggerInstance.Error($"Player tried to upload avtar for masterId that isn't theirs.");
            MelonCoroutines.Start(
                SendAudit(
                    "masterId_mismatch",
                    $"[{DateTime.UtcNow:O}] Player {data.PublicUsername.TrimString()} ({data.PlayFabMasterId}) tried to write avatar for MasterId {masterId}"
                )
            );
            uploadCoroutine = null;

            if (serverStatusText != null)
            {
                serverStatusText.color = Color.red;
                serverStatusText.text = "MasterId Mismatch";
            }

            yield return new WaitForSeconds(2f);
            done?.Invoke(false, false);
            yield break;
        }

        if (!File.Exists(path))
        {
            Main.instance.LoggerInstance.Error($"AssetBundle at path '{path}' does not exist.");
            
            if (serverStatusText != null)
            {
                serverStatusText.color = Color.red;
                serverStatusText.text = "Avatar doesn't exist";
            }

            yield return new WaitForSeconds(2f);
            
            done?.Invoke(false, false);
            uploadCoroutine = null;
            yield break;
        }

        byte[] bytes;
        try { bytes = File.ReadAllBytes(path); }
        catch (Exception e)
        {
            Main.instance.LoggerInstance.Error($"ReadAllBytes failed: {e.Message}");
            done?.Invoke(false, false);
            uploadCoroutine = null;
            yield break;
        }

        if (bytes.Length > MAX_UPLOAD_BYTES)
        {
            Main.instance.LoggerInstance.Error($"Upload failed: Bundle size {bytes.Length / (1024 * 1024)} MB exceeds {MAX_UPLOAD_BYTES / (1024 * 1024)} MB Limit.");
            
            if (serverStatusText != null)
            {
                serverStatusText.color = Color.red;
                serverStatusText.text = $"Bundle size is bigger than {MAX_UPLOAD_BYTES / (1024 * 1024)} MB limit";
            }
            
            yield return new WaitForSeconds(2f);
            
            done?.Invoke(false, false);
            uploadCoroutine = null;
            yield break;
        }
        
        if (string.IsNullOrWhiteSpace(masterId) || bytes.Length == 0)
        { done?.Invoke(false, false); yield break; }

        string sha = null;
        Main.instance.LoggerInstance.Msg("Fetching Remote SHA...");

        if (serverStatusText != null)
            serverStatusText.text = "Fetching Remote SHA...";
        
        yield return GetSha(masterId, s => sha = s);
        Main.instance.LoggerInstance.Msg(sha != null
            ? $"Remote SHA: {sha.Substring(0, 8)}"
            : "No remote file found - will create new file.");

        if (serverStatusText != null)
            serverStatusText.text = sha != null
                ? $"Remote File Exists"
                : "No remote file found - will create new file.";

        yield return new WaitForSeconds(1f);

        if (sha != null && !string.IsNullOrEmpty(sha) && ShaMatchesLocal(sha, path))
        {
            Main.instance.LoggerInstance.Msg("Upload Skipped: Local file is identical to the server version.");

            if (serverStatusText != null)
                serverStatusText.text = "Local file is identical to the server version.";

            yield return new WaitForSeconds(2f);
            
            done?.Invoke(true, true);
            uploadCoroutine = null;
            yield break;
        }
        
        Main.instance.LoggerInstance.Msg($"File size: {bytes.Length / 1024f / 1024f:F2} MB");
        Main.instance.LoggerInstance.Msg("Uploading to GitHub...");

        if (serverStatusText != null)
            serverStatusText.text = $"Uploading {bytes.Length / 1024f / 1024f:F2} MB to GitHub...";

        yield return new WaitForSeconds(2f);

        var body = $"{{\"message\":\"Upload bundle for {masterId}. Uploaded by {data.PublicUsername.TrimString()}\",\"content\":\"{Convert.ToBase64String(bytes)}\",\"branch\":\"{BRANCH}\"" +
                   (sha != null ? $",\"sha\":\"{sha}\"" : "") + "}";

        var url = UploadUrlForPath($"avatars/{masterId}");
        var req = new UnityWebRequest(url, "PUT");
        req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        SetGhHeaders(req, wantRaw: false);

        var op = req.SendWebRequest();
        onStartUpload?.Invoke();

        while (!op.isDone)
        {
            onProgress?.Invoke(req.uploadProgress);

            if (serverStatusText != null)
                serverStatusText.text = $"{req.uploadProgress * 100:F2}%";
            
            yield return null;
        }

        onProgress?.Invoke(1f);

        bool ok = req.result == UnityWebRequest.Result.Success &&
                  req.responseCode is >= 200 and < 300;

        if (!ok)
        {
            var errBytes = req.downloadHandler?.data;
            var errTxt = errBytes != null ? System.Text.Encoding.UTF8.GetString(errBytes) : "";
            Main.instance.LoggerInstance.Error(
                $"Upload failed {masterId}: {req.responseCode} {req.error}\n{errTxt}");

            MelonCoroutines.Start(
                SendAudit(
                    "upload_fail",
                    $"[{DateTime.UtcNow:O}] Upload failed for MasterId {masterId} " +
                    $"Code={req.responseCode} Error = {req.error} " +
                    $"Body={(string.IsNullOrWhiteSpace(errTxt) ? "<empty>" : errTxt)}"
                )
            );

            if (serverStatusText != null)
            {
                serverStatusText.color = Color.red;
                serverStatusText.text = $"Upload failed. Check console for more info.";
            }

            
        }
        else
        {
            if (serverStatusText != null)
            {
                serverStatusText.color = Color.green;
                serverStatusText.text = "Uploaded succesfully!";
            }
        }

        yield return new WaitForSeconds(2f);
        
        req.Dispose();
        uploadCoroutine = null;
        done?.Invoke(ok, false);
    }

    // Compares local file SHA with remote (a SHA is just a hash to check differences)
    public static bool ShaMatchesLocal(string remoteSha, string filePath, bool log = true)
    {
        var bytes = File.ReadAllBytes(filePath);
        var header = System.Text.Encoding.ASCII.GetBytes($"blob {bytes.Length}\0");
        
        // Crpytography is a strange thing
        using var sha1 = System.Security.Cryptography.SHA1.Create();
        sha1.TransformBlock(header, 0, header.Length, header, 0);
        sha1.TransformFinalBlock(bytes, 0, bytes.Length);
        
        var hex = BitConverter.ToString(sha1.Hash).Replace("-", "").ToLowerInvariant();
        
        if (log)
            Main.instance.LoggerInstance.Msg($"Local SHA: {hex.Substring(0, 8)}");
        
        return hex == remoteSha;
    }

    // Asks GitHub if a player has an avatar file uploaded
    // Also works as a barrier for player load times messing it up XD
    public static IEnumerator PlayerHasAvatar(string masterId, Action<(bool hasAvatar, string returnedSha)> callback)
    {
        yield return MelonCoroutines.Start(GetSha(masterId, sha => callback((!string.IsNullOrEmpty(sha), sha))));
    }

    // Retrieves the SHA hash of a player's avatar from GitHub.
    public static IEnumerator GetSha(string masterId, Action<string> cb, bool log = true)
    {
        if (log)
            Main.instance.LoggerInstance.Msg($"Fetching remote SHA for masterId {masterId}...");
        
        var url = $"https://api.github.com/repos/{GH_REPO}/contents/avatars/{Uri.EscapeDataString(masterId)}?ref={BRANCH}";
        var req = UnityWebRequest.Get(url);
        SetGhHeaders(req, wantRaw:false);
        yield return req.SendWebRequest();
        
        if (req.responseCode == 404) { req.Dispose(); cb(null); yield break; }
        
        if (log)
            Main.instance.LoggerInstance.Msg($"GitHub responded {req.responseCode}: {req.result}");

        if (req.result != UnityWebRequest.Result.Success)
        {
            Main.instance.LoggerInstance.Error($"Web request completed unsuccessfully | ERROR {req.responseCode} | {req.error}");
            req.Dispose(); cb(null); yield break;
        }

        var data = req.downloadHandler?.data;
        req.Dispose();
        if (data == null || data.Length == 0) { cb(null); yield break; }
        
        var txt = System.Text.Encoding.UTF8.GetString(data);

        // I was on something when I made this
        // Basically just returns the actual sha instead of the rest of the response.
        int i = txt.IndexOf("\"sha\":\"", StringComparison.Ordinal);
        if (i < 0) { cb(null); yield break; }
        i += 7; int j = txt.IndexOf('\"', i);
        cb(j > i ? txt.Substring(i, j - i) : null);
    }
    
    // Same as UploadAvatar
    // Just starts the coroutine, for testing purposes
    public static void StartDownloadToFile(string masterId, string savePath) =>
        MelonCoroutines.Start(DownloadToFile(masterId, savePath));

    // Downloads avatar bundle, size-checks it with the metadata, and saves to disk.
    public static IEnumerator DownloadToFile(string masterId, string savePath)
    {
        if (!_downloadingPlayers.Add(masterId))
        {
            Main.instance.LoggerInstance.Warning($"Player {masterId} is already being downloaded.");
            yield break;
        }
        
        // Contained inside of the file uploaded itself
        var metaUrl = $"https://api.github.com/repos/{GH_REPO}/contents/avatars/{Uri.EscapeDataString(masterId)}?ref={BRANCH}";
        var metaReq = UnityWebRequest.Get(metaUrl);
        SetGhHeaders(metaReq, wantRaw: false);
        yield return metaReq.SendWebRequest();

        if (metaReq.result != UnityWebRequest.Result.Success)
        {
            Main.instance.LoggerInstance.Error($"Metadata fetch failed for {masterId}: {metaReq.error}");
            metaReq.Dispose();
            _downloadingPlayers.Remove(masterId);
            yield break;
        }

        try
        {
            var bytes = metaReq.downloadHandler?.data;
            if (bytes == null || bytes.Length == 0)
            {
                metaReq.Dispose();
                _downloadingPlayers.Remove(masterId);
                yield break;
            }
            
            var json = System.Text.Encoding.UTF8.GetString(bytes);
            
            var sizeIndex = json.IndexOf("\"size\":", StringComparison.Ordinal);
            if (sizeIndex >= 0)
            {
                sizeIndex += 7;
                int endIndex = json.IndexOfAny(new[] { ',', '}' }, sizeIndex);
                var sizeStr = json.Substring(sizeIndex, endIndex - sizeIndex).Trim();
                if (int.TryParse(sizeStr, out int fileSizeBytes))
                {
                    int maxDownloadBytes = (int)Main.instance.downloadLimitMB.SavedValue * 1024 * 1024;
                    if (fileSizeBytes > maxDownloadBytes)
                    {
                        Main.instance.LoggerInstance.Warning(
                            $"Download skipped: {fileSizeBytes / (1024 * 1024)} MB exceeds limit of {maxDownloadBytes / (1024 * 1024)} MB.");
                        metaReq.Dispose();
                        _downloadingPlayers.Remove(masterId);
                        yield break;
                    }
                }
            }
        }
        catch (Exception e)
        {
            Main.instance.LoggerInstance.Error($"Error parsing metadata for {masterId}: {e.Message}");
            metaReq.Dispose();
            _downloadingPlayers.Remove(masterId);
            yield break;
        }
        metaReq.Dispose();
        
        var req = UnityWebRequest.Get(GhUrl(masterId));
        SetGhHeaders(req, true);
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Main.instance.LoggerInstance.Error($"Download failed for {masterId}: {req.error}");
        }
        else
        {
            var data = req.downloadHandler.data;
            if (data == null || data.Length < 16)
            {
                Main.instance.LoggerInstance.Error($"Blocked: {masterId} file too small to be a valid AssetBundle.");
                _downloadingPlayers.Remove(masterId);
                req.Dispose();
                yield break;
            }
            
            var encrypted = XorCrypt(data);
            File.WriteAllBytes(savePath, encrypted);
        }
        
        _downloadingPlayers.Remove(masterId);

        req.Dispose();
    }
}