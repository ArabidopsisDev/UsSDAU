using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UsSDAU.Models;

namespace UsSDAU.Services;

public sealed class CacheService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
    private readonly string _root = ResolveRoot();

    private static string ResolveRoot()
    {
        var overridden = Environment.GetEnvironmentVariable("USSDAU_DATA_DIR");
        return string.IsNullOrWhiteSpace(overridden)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UsSDAU", "cache")
            : Path.Combine(Path.GetFullPath(overridden), "cache");
    }

    public async Task SaveCoursesAsync(string account, string term, IReadOnlyList<Course> value) =>
        await WriteAsync(PathFor("courses", account, term), value);
    public async Task<List<Course>> LoadCoursesAsync(string account, string term) =>
        await ReadAsync<List<Course>>(PathFor("courses", account, term)) ?? [];

    public async Task SaveScoresAsync(string account, string term, ScoreResult value) =>
        await WriteAsync(PathFor("scores", account, term), value);
    public async Task<ScoreResult?> LoadScoresAsync(string account, string term) =>
        await ReadAsync<ScoreResult>(PathFor("scores", account, term));

    public async Task<Dictionary<string, ScoreResult>> LoadScoresAsync(string account, IEnumerable<string> terms)
    {
        var result = new Dictionary<string, ScoreResult>();
        foreach (var term in terms.Distinct())
        {
            var cached = await LoadScoresAsync(account, term);
            if (cached is not null && cached.Records.Count > 0) result[term] = cached;
        }
        return result;
    }

    public async Task SavePublicCoursesAsync(string term, IReadOnlyList<PublicCourse> value) =>
        await WriteAsync(PathFor("public", "all", term), value);
    public async Task<List<PublicCourse>> LoadPublicCoursesAsync(string term) =>
        await ReadAsync<List<PublicCourse>>(PathFor("public", "all", term)) ?? [];

    private string PathFor(string kind, string account, string term)
    {
        Directory.CreateDirectory(_root);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{account}|{term}"))).ToLowerInvariant()[..20];
        return Path.Combine(_root, $"{kind}-{key}.json");
    }

    private static async Task WriteAsync<T>(string path, T value)
    {
        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(value, JsonOptions));
        File.Move(temp, path, true);
    }

    private static async Task<T?> ReadAsync<T>(string path)
    {
        try
        {
            if (!File.Exists(path)) return default;
            return JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path), JsonOptions);
        }
        catch { return default; }
    }
}
