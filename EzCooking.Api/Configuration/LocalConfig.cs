namespace EzCooking.Api.Configuration;

public static class LocalConfig
{
    public static void Load(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        foreach (var line in File.ReadLines(path))
        {
            var value = line.Trim();
            if (value.Length == 0 || value.StartsWith('#'))
            {
                continue;
            }

            var separator = value.IndexOf('=');
            if (separator <= 0)
            {
                throw new InvalidOperationException("Invalid entry in .env.");
            }

            var key = value[..separator].Trim();
            var text = value[(separator + 1)..].Trim();
            if (text.Length >= 2 && ((text[0] == '"' && text[^1] == '"') || (text[0] == '\'' && text[^1] == '\'')))
            {
                text = text[1..^1];
            }

            if (Environment.GetEnvironmentVariable(key) is null)
            {
                Environment.SetEnvironmentVariable(key, text);
            }
        }
    }
}

public sealed record AppPaths(string Root)
{
    public string Media => Path.Combine(Root, "media");
    public string For(Guid id) => Path.Combine(Media, id.ToString("N"));
}
