using System.Globalization;
using System.Net.Http.Json;
using CanvasTrelloSync.Interfaces;
using CanvasTrelloSync.Models;
using CanvasTrelloSync.Services;

namespace CanvasTrelloSync.Clients;

public class TrelloClient : ITaskBoard
{
    private readonly HttpClient _http;
    private readonly string _auth;
    private readonly string _boardId;

    public TrelloClient(string apiKey, string apiToken, string boardId)
    {
        _http = new HttpClient { BaseAddress = new Uri("https://api.trello.com/1/") };
        _auth = $"key={Uri.EscapeDataString(apiKey)}&token={Uri.EscapeDataString(apiToken)}";
        _boardId = boardId;
    }

    public async Task<string> GetBoardNameAsync()
    {
        var board = await SendAsync<TrelloBoard>(HttpMethod.Get, $"boards/{_boardId}", "fields=name");
        return board?.Name ?? "?";
    }

    public async Task<Dictionary<string, string>> GetListsAsync()
    {
        // Case-insensitive, so "done" and "Done" count as the same list
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var list in await GetBoardListsAsync())
            result.TryAdd(list.Name, list.Id);

        return result;
    }

    public async Task<string> EnsureListAsync(string name)
    {
        var lists = await GetBoardListsAsync();
        var existing = lists.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            return existing.Id;

        var created = await SendAsync<TrelloList>(HttpMethod.Post, "lists",
            $"name={Uri.EscapeDataString(name)}&idBoard={_boardId}&pos={NewListPosition(lists, name)}");
        return created!.Id;
    }

    // New course lists go just before Done, so Done stays the last column. Done itself (or any list when
    // there is no Done yet) goes at the end.
    private static string NewListPosition(List<TrelloList> lists, string name)
    {
        var done = lists.FirstOrDefault(l => string.Equals(l.Name, SyncService.DoneList, StringComparison.OrdinalIgnoreCase));
        if (done is null || string.Equals(name, SyncService.DoneList, StringComparison.OrdinalIgnoreCase))
            return "bottom";

        // Halfway between Done and the list before it
        double before = lists.Where(l => l.Pos < done.Pos).Select(l => l.Pos).DefaultIfEmpty(0).Max();
        return ((before + done.Pos) / 2).ToString(CultureInfo.InvariantCulture);
    }

    private async Task<List<TrelloList>> GetBoardListsAsync() =>
        await SendAsync<List<TrelloList>>(HttpMethod.Get, $"boards/{_boardId}/lists", "fields=name,pos") ?? new List<TrelloList>();

    public async Task<List<TrelloCard>> GetCardsAsync(string listId) =>
        await SendAsync<List<TrelloCard>>(HttpMethod.Get, $"lists/{listId}/cards", "") ?? new List<TrelloCard>();

    public async Task<TrelloCard> CreateCardAsync(string listId, Assignment assignment)
    {
        // canvas-id is a backup link from the card to its assignment, in case sync-state.json is lost
        string desc = $"Course: {assignment.CourseCode}\n{assignment.Url}\n\ncanvas-id:{assignment.Id}";

        string query = $"idList={listId}&pos=bottom" +
                       $"&name={Uri.EscapeDataString(assignment.CardTitle)}" +
                       $"&desc={Uri.EscapeDataString(desc)}";

        // Canvas Network assignments usually have no due date, so only send one when it exists
        if (assignment.DueAt is DateTimeOffset due)
            query += $"&due={Uri.EscapeDataString(due.UtcDateTime.ToString("o"))}";

        return (await SendAsync<TrelloCard>(HttpMethod.Post, "cards", query))!;
    }

    public async Task MoveCardAsync(string cardId, string listId) =>
        await SendAsync<TrelloCard>(HttpMethod.Put, $"cards/{cardId}", $"idList={listId}&pos=top");

    // Not part of ITaskBoard on purpose: the sync engine must never be able to delete cards
    public async Task DeleteCardAsync(string cardId) =>
        await SendAsync<object>(HttpMethod.Delete, $"cards/{cardId}", "");

    // Every Trello call goes through here, so auth and error handling live in one place
    private async Task<T?> SendAsync<T>(HttpMethod method, string path, string query)
    {
        string url = $"{path}?{_auth}" + (query == "" ? "" : $"&{query}");

        using var request = new HttpRequestMessage(method, url);
        using var response = await _http.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            // Only the path goes in the message: the full URL contains the token
            throw new HttpRequestException(
                $"Trello returned {(int)response.StatusCode} {response.StatusCode} on {method} {path}",
                null, response.StatusCode);
        }

        return await response.Content.ReadFromJsonAsync<T>();
    }
}
