using System.Net.Http.Headers;
using System.Net.Http.Json;
using CanvasTrelloSync.Interfaces;
using CanvasTrelloSync.Models;

namespace CanvasTrelloSync.Clients;

public class CanvasClient : ITaskSource
{
    private readonly HttpClient _http;

    public CanvasClient(string baseUrl, string token)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/api/v1/") };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("CanvasTrelloSync/1.0");
    }

    public Task<List<Course>> GetCoursesAsync() =>
        GetAllPagesAsync<Course>("courses?enrollment_state=active&per_page=50");

    public async Task<List<Assignment>> GetAssignmentsAsync(Course course)
    {
        // include[]=submission adds MY submission status to each assignment
        var assignments = await GetAllPagesAsync<Assignment>(
            $"courses/{course.Id}/assignments?include[]=submission&per_page=50");

        foreach (var assignment in assignments)
        {
            assignment.CourseCode = course.CourseCode ?? course.Id.ToString();
            assignment.CourseName = course.Name;
        }

        return assignments;
    }

    // Canvas splits long results into pages, so keep following the "next" link until there is none
    private async Task<List<T>> GetAllPagesAsync<T>(string url)
    {
        var results = new List<T>();
        string? next = url;

        while (next != null)
        {
            using var response = await _http.GetAsync(next);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"Canvas returned {(int)response.StatusCode} {response.StatusCode}", null, response.StatusCode);

            var page = await response.Content.ReadFromJsonAsync<List<T>>() ?? new List<T>();
            results.AddRange(page);

            next = GetNextLink(response);
        }

        return results;
    }

    // The Link header looks like: <https://...&page=2>; rel="next", <https://...>; rel="last"
    private static string? GetNextLink(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Link", out var values))
            return null;

        foreach (string part in string.Join(",", values).Split(','))
        {
            if (part.Contains("rel=\"next\""))
            {
                int start = part.IndexOf('<') + 1;
                int end = part.IndexOf('>');
                return part[start..end];
            }
        }

        return null;
    }
}
