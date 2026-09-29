using CanvasTrelloSync.Interfaces;
using CanvasTrelloSync.Models;

namespace CanvasTrelloSync.Tests.Fakes;

// Pretend Trello board kept in memory. It counts every call that would change the real board.
public class FakeTaskBoard : ITaskBoard
{
    public Dictionary<string, string> Lists { get; } = new(StringComparer.OrdinalIgnoreCase) { ["Later"] = "list-later" };
    public List<TrelloCard> Cards { get; } = new();
    public int WriteCalls { get; private set; }

    // Makes the Nth CreateCardAsync call fail, like Trello going down halfway through a sync
    public int? FailOnCreateNumber { get; set; }
    private int _createCount;

    public List<TrelloCard> CardsIn(string listName) => Cards.Where(c => c.ListId == Lists[listName]).ToList();

    public Task<Dictionary<string, string>> GetListsAsync() =>
        Task.FromResult(new Dictionary<string, string>(Lists, StringComparer.OrdinalIgnoreCase));

    public Task<string> EnsureListAsync(string name)
    {
        if (!Lists.ContainsKey(name))
        {
            WriteCalls++;
            Lists[name] = $"list-{name.ToLower()}";
        }
        return Task.FromResult(Lists[name]);
    }

    public Task<List<TrelloCard>> GetCardsAsync(string listId) =>
        Task.FromResult(Cards.Where(c => c.ListId == listId).ToList());

    public async Task<TrelloCard> CreateCardAsync(string listId, Assignment assignment)
    {
        // Yield like a real network call, so two syncs at once can interleave
        await Task.Yield();

        _createCount++;
        if (_createCount == FailOnCreateNumber)
            throw new HttpRequestException("Trello returned 500 InternalServerError on POST cards");

        WriteCalls++;
        var card = new TrelloCard
        {
            Id = $"card-{assignment.Id}",
            Name = assignment.CardTitle,
            ListId = listId,
            Url = $"https://trello.test/c/{assignment.Id}",
        };
        Cards.Add(card);
        return card;
    }

    public async Task MoveCardAsync(string cardId, string listId)
    {
        await Task.Yield();
        WriteCalls++;
        Cards.Single(c => c.Id == cardId).ListId = listId;
    }
}
