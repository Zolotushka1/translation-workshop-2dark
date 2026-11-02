using System.Text;
using System.Text.RegularExpressions;

namespace Workshop.Core;

public static class GameProfile
{
    public static string Prepare(string runtimeRoot)
    {
        string appData = Path.Combine(runtimeRoot, "profile");
        string state = Path.Combine(appData, "Gloomywood", "2Dark", ".monkeystate");
        Directory.CreateDirectory(Path.GetDirectoryName(state)!);
        string identity = Guid.NewGuid().ToString("N").ToUpperInvariant();
        string text = File.Exists(state) ? File.ReadAllText(state, new UTF8Encoding(false, true))
            : $"DARKDATA\t2\tconfigs/systemconfig\t1\t[CONFIG]\nS:CurrentPlayerID={identity}\n\tplayers/{identity.ToLowerInvariant()}/config\t1\t[CONFIG]\nS:AnonymousId={identity}\nS:PlayerName=Workshop\n";
        const string marker = "configs/systemconfig\t1\t[CONFIG]";
        int index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (!text.StartsWith("DARKDATA\t", StringComparison.Ordinal) || index < 0) throw new InvalidDataException("Неизвестный формат тестового профиля 2Dark.");
        int contentStart = index + marker.Length;
        int next = text.IndexOf('\t', contentStart);
        if (next < 0) next = text.Length;
        string system = text[contentStart..next];
        system = Regex.Replace(system, @"(?m)^[IB]:FULLSCREEN=[^\r\n]*(?:\r?\n|$)", "");
        text = text[..contentStart] + "\nI:FULLSCREEN=0\n" + system.TrimStart('\r', '\n') + text[next..];
        var player = Regex.Match(text, @"(?m)^S:CurrentPlayerID=([^\r\n\t]+)");
        if (player.Success)
        {
            string playerMarker = "players/" + player.Groups[1].Value.ToLowerInvariant() + "/config\t1\t[CONFIG]";
            int playerIndex = text.IndexOf(playerMarker, StringComparison.OrdinalIgnoreCase);
            if (playerIndex >= 0)
            {
                int begin = playerIndex + playerMarker.Length;
                int end = text.IndexOf('\t', begin); if (end < 0) end = text.Length;
                if (!Regex.IsMatch(text[begin..end], @"(?m)^S:PlayerName=.+"))
                    text = text[..begin] + "\nS:PlayerName=Workshop\n" + text[begin..end].TrimStart('\r', '\n') + text[end..];
            }
        }
        BuildService.WriteAtomic(state, Encoding.UTF8.GetBytes(text));
        return appData;
    }
}
