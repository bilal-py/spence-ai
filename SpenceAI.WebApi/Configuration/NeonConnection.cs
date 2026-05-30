namespace SpenceAI.WebApi.Configuration;

public static class NeonConnection
{
    /// <summary>
    /// Resolves the Postgres connection string from configuration or Neon-style DATABASE_URL.
    /// </summary>
    public static string? Resolve(IConfiguration configuration)
    {
        var fromConfig = configuration.GetConnectionString("DefaultConnection");
        if (!string.IsNullOrWhiteSpace(fromConfig))
        {
            return fromConfig;
        }

        var databaseUrl = configuration["DATABASE_URL"]
            ?? Environment.GetEnvironmentVariable("DATABASE_URL");

        if (string.IsNullOrWhiteSpace(databaseUrl))
        {
            return null;
        }

        return ParseDatabaseUrl(databaseUrl);
    }

    /// <summary>
    /// Converts postgresql://user:pass@host/db?sslmode=require into an Npgsql connection string.
    /// </summary>
    public static string ParseDatabaseUrl(string databaseUrl)
    {
        var uri = new Uri(databaseUrl);
        var userInfo = uri.UserInfo.Split(':', 2);
        var username = Uri.UnescapeDataString(userInfo[0]);
        var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty;
        var database = uri.AbsolutePath.TrimStart('/');
        var port = uri.Port > 0 ? uri.Port : 5432;
        var sslMode = "Require";

        if (!string.IsNullOrEmpty(uri.Query))
        {
            foreach (var segment in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = segment.Split('=', 2);
                if (parts.Length != 2)
                {
                    continue;
                }

                if (!parts[0].Equals("sslmode", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                sslMode = parts[1].ToLowerInvariant() switch
                {
                    "require" => "Require",
                    "verify-full" => "VerifyFull",
                    "prefer" => "Prefer",
                    "disable" => "Disable",
                    _ => "Require",
                };
            }
        }

        return
            $"Host={uri.Host};Port={port};Database={database};Username={username};Password={password};SSL Mode={sslMode}";
    }
}
