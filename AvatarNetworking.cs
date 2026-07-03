using Il2CppExitGames.Client.Photon;
using Il2CppPhoton.Pun;
using Il2CppPhoton.Realtime;

namespace CustomAvatars;

public static class AvatarNetworking
{
    public static void UpdateLocalParams()
    {
        if (!PhotonNetwork.InRoom) return;

        var props = new Hashtable();

        props[CAParams.Visibility.Key] = Main.instance.LetOthersSeeMyAvatar.Value;
        props[CAParams.Version.Key] = BuildInfo.Version;
        
        // Avatar param bitmask
        int mask = 0;

        var rig = Main.LocalPlayer.Controller.GetComponent<CustomRig>();

        if (rig != null && rig.config != null)
        {
            var parameters = rig.config.parameters;

            for (int i = 0; i < parameters.Count; i++)
            {
                var param = parameters[i];

                if (!param.networked) continue;

                // Will figure this out later
                bool value = param.defaultToggle;

                if (value)
                    mask |= (1 << i);
            }
        }

        props[CAParams.Params.Key] = mask;
        
        // ------------------------------------------------
        
        PhotonNetwork.LocalPlayer.SetCustomProperties(props);
    }
}

public static class CAParams
{
    public static readonly Param<bool> Visibility = new("CA:visibility", false);
    public static readonly Param<string> Version = new("CA:version", null);
    public static readonly Param<int> Params = new("CA:params", 0);
}

public class Param<T>(string key, T def)
{
    public string Key = key;
    public T Default = def;

    public T Get(Player player)
    {
        if (player?.CustomProperties.TryGetValue(Key, out var val) ?? false)
        {
            if (val is T t)
                return t;

            if (typeof(T) == typeof(string))
                return (T)(object)val.ToString();
        }

        return Default;
    }
}