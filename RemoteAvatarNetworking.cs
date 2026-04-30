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
    
    private const long RELEASE_ID = 314323506;

    private const string TOKEN = "Token moment";

    public static IEnumerator GetAvatarAsset(Player player, Action<AvatarAsset> callback)
    {
        string masterId = player.Data.GeneralData.PlayFabMasterId;
        string targetName = $"{masterId}.rumbleavatar";

        string url = $"https://api.github.com/repos/xLoadingx/custom-avatars/releases/{RELEASE_ID}/assets";

        var req = UnityWebRequest.Get(url);
        req.SetRequestHeader("User-Agent", "CustomAvatars");
        req.SetRequestHeader("Authorization", $"Bearer {TOKEN}");
        
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            logger.Error($"GetAvatarAsset failed: {req.error}");
            callback?.Invoke(new AvatarAsset { Exists = false });
            yield break;
        }

        string json = req.downloadHandler.text;
        
        logger.Msg($"Json: {json}");

        int nameIndex = json.IndexOf($"\"name\":\"{targetName}\"", StringComparison.Ordinal);
        if (nameIndex == -1)
        {
            callback?.Invoke(new AvatarAsset { Exists = false });
            req.Dispose();
            yield break;
        }
        
        int idIndex = json.LastIndexOf("\"id\":", nameIndex, StringComparison.Ordinal);
        if (idIndex == -1)
        {
            callback?.Invoke(new AvatarAsset { Exists = false });
            req.Dispose();
            yield break;
        }

        idIndex += 5;
        int idEnd = json.IndexOf(',', idIndex);

        string idStr = json.Substring(idIndex, idEnd - idIndex).Trim();

        long id = long.Parse(idStr);

        callback?.Invoke(new AvatarAsset
        {
            Exists = true,
            Id = id,
            Name = targetName
        });

        req.Dispose();
    }
    
    public struct AvatarAsset
    {
        public bool Exists;
        public long Id;
        public string Name;
    }
}