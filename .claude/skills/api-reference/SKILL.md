---
name: api-reference
description: Canvas LMS and Trello REST API reference for this repo, covering endpoints, auth, pagination, response fields, and account-specific gotchas. Use when writing, changing, or debugging code that calls Canvas or Trello, or when a request returns an unexpected status or shape.
---

# Canvas + Trello API reference

This is the single source of truth for how CanvasTrelloSync talks to both APIs. If code and this file disagree, check the live API and then fix whichever one is wrong.

## Canvas (Canvas Network)

- **Base:** `{Canvas:BaseUrl}/api/v1/`, where the base URL is `https://learn.canvas.net`.
- **Auth:** header `Authorization: Bearer {Canvas:Token}`, plus a `User-Agent` such as `CanvasTrelloSync/1.0`.

| Call | Purpose |
|---|---|
| `GET courses?enrollment_state=active&per_page=50` | my active courses |
| `GET courses/{id}/assignments?include[]=submission&per_page=50` | assignments plus **my** submission |

**Fields used.** Course: `id`, `name`, `course_code`. Assignment: `id`, `name`, `due_at`, `html_url`, `points_possible`, `submission.workflow_state`.

**Pagination.** Results come in pages. Keep following the `Link` response header's `rel="next"` URL until there is none. The URL is absolute, so request it as-is. Format: `<https://…&page=2>; rel="next", <https://…>; rel="last"`.

**Gotchas**
- Canvas Network courses are self-paced, so `due_at` is always `null`. The app therefore syncs by submission state, not by date.
- Treat `workflow_state` values `submitted`, `graded`, and `pending_review` as submitted. `unsubmitted` means todo.
- Some courses refuse assignment access (401/403). Skip that one course with a warning and keep syncing the rest.
- `401` on `courses` means a bad or expired token.

## Trello

- **Base:** `https://api.trello.com/1/`.
- **Auth:** query string `key={Trello:ApiKey}&token={Trello:ApiToken}` on every call. The token is in the URL, so log only the path, never the full URL.

| Call | Purpose |
|---|---|
| `GET boards/{boardId}/lists` | lists: `id`, `name` |
| `POST lists?name=Done&idBoard={boardId}&pos=bottom` | create the Done list when missing |
| `GET lists/{listId}/cards` | cards on a list (board mirror) |
| `POST cards?idList=…&name=…&desc=…&pos=bottom[&due=ISO-8601 UTC]` | create a card; the response includes `id`, `shortUrl` |
| `PUT cards/{cardId}?idList={doneListId}&pos=top` | move a card to Done |
| `DELETE cards/{cardId}` | remove a card (test cleanup only) |

**Card fields used:** `id`, `name`, `desc`, `idList`, `shortUrl`, `due`.

**Gotchas**
- `Uri.EscapeDataString` every `name`/`desc` value. Assignment names contain `:`, `&`, `#`.
- Match list names case-insensitively. The board is "My Trello board" (`689380436f565a02948248f1`), and its lists are `Today`, `This Week`, `Later`. `Done` is created by the app.
- Only send `due` when the assignment has one.
- Put `canvas-id:{assignmentId}` in the card `desc`. It is a backup link from a card to its assignment if `sync-state.json` is lost.
- The rate limit is 100 requests per 10 s per token, and going over returns `429`. A full sync of about 20 cards stays under it. Add a short delay only if 429s appear.
- `401 invalid token` means a bad key/token pair. `404` on a board or list means a wrong id.

## Errors in this app

Both clients throw `HttpRequestException` with the status code and a message like `Trello returned 401 Unauthorized`. The UI catches it and shows a red panel. The web API returns `502` with the message.
