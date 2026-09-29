using CanvasTrelloSync.Models;

namespace CanvasTrelloSync.Interfaces;

// Anything we can put task cards on (Trello today, a fake in tests).
public interface ITaskBoard
{
    Task<string> GetBoardNameAsync();
    Task<Dictionary<string, string>> GetListsAsync();          // list name -> list id
    Task<string> EnsureListAsync(string name);                   // returns the list id, creating the list if missing
    Task<List<TrelloCard>> GetCardsAsync(string listId);
    Task<TrelloCard> CreateCardAsync(string listId, Assignment assignment);
    Task MoveCardAsync(string cardId, string listId);
}
