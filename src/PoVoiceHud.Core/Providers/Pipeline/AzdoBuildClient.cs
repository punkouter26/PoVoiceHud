using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace PoVoiceHud.Core.Providers.Pipeline;

/// <summary>Azure DevOps Build REST API (YAML pipelines) over a PAT: the builds list, plus the timeline of a failed run.</summary>
public sealed class AzdoBuildClient(HttpClient http, Uri organization, string project, string pat) : IBuildClient
{
    private const string ApiVersion = "7.1";

    private readonly string _project = $"{organization.ToString().TrimEnd('/')}/{Uri.EscapeDataString(project)}";
    private readonly AuthenticationHeaderValue _auth = new("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes(":" + pat)));

    public async Task<IReadOnlyList<BuildRun>> GetRecentBuildsAsync(string branch, int top, CancellationToken ct)
    {
        var builds = await GetAsync(
            $"{_project}/_apis/build/builds?branchName={Uri.EscapeDataString("refs/heads/" + branch)}&$top={top}&queryOrder=queueTimeDescending&api-version={ApiVersion}", ct);

        var runs = new List<BuildRun>();
        if (!builds.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array) return runs;
        foreach (var build in value.EnumerateArray())
        {
            var id = build.GetProperty("id").GetInt32();
            var result = Text(build, "result");
            string? failedStage = null;
            if (result == "failed")
            {
                var timeline = await GetAsync($"{_project}/_apis/build/builds/{id}/timeline?api-version={ApiVersion}", ct);
                if (timeline.TryGetProperty("records", out var records) && records.ValueKind == JsonValueKind.Array)
                    failedStage = records.EnumerateArray()
                        .Where(r => Text(r, "result") == "failed" && Text(r, "type") is "Stage" or "Job")
                        .OrderBy(r => r.TryGetProperty("order", out var order) && order.ValueKind == JsonValueKind.Number ? order.GetInt32() : int.MaxValue)
                        .Select(r => Text(r, "name"))
                        .FirstOrDefault();
            }
            var web = build.TryGetProperty("_links", out var links) && links.TryGetProperty("web", out var link) && Text(link, "href") is { } href
                ? new Uri(href)
                : new Uri($"{_project}/_build/results?buildId={id}");
            var definition = build.TryGetProperty("definition", out var d) ? Text(d, "name") : null;
            runs.Add(new BuildRun(id, Text(build, "buildNumber") ?? id.ToString(CultureInfo.InvariantCulture), definition ?? "Pipeline",
                Text(build, "status") ?? "unknown", result, Text(build, "sourceBranch") ?? "", Text(build, "sourceVersion"),
                Time(build, "startTime"), Time(build, "finishTime"), web, failedStage));
        }
        return runs;
    }

    private async Task<JsonElement> GetAsync(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = _auth;
        using var response = await http.SendAsync(request, ct);
        // A bad or expired PAT is answered with 203 and the HTML sign-in page rather than 401.
        if (response.StatusCode == HttpStatusCode.NonAuthoritativeInformation)
            throw new HttpRequestException("Azure DevOps asked for sign-in.", null, HttpStatusCode.Unauthorized);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static DateTimeOffset? Time(JsonElement element, string name) =>
        Text(element, name) is { } text && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time) ? time : null;
}
