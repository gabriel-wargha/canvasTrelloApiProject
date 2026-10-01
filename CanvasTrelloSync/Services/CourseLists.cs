namespace CanvasTrelloSync.Services;

// Each course gets its own list on Trello, named after the course, like "Prompt Engineering".
public static class CourseLists
{
    // Long enough for "Code for Schools: Digitech Teacher Training", short enough to read on a Trello column
    private const int MaxNameLength = 45;

    // Words that read badly at the end of a cut name, like "Computer Science &…"
    private static readonly string[] DanglingWords = { "&", "and", "in", "of", "for", "the", "to", "using", "with", "-", ":" };

    // "PD-0141-ENHANCING-LEARNING-..." -> "PD-0141": keep the parts up to the first one with a number in it
    public static string ShortCode(string? courseCode)
    {
        if (string.IsNullOrWhiteSpace(courseCode))
            return "?";

        string[] parts = courseCode.Split('-');
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Any(char.IsDigit))
                return string.Join('-', parts.Take(i + 1));
        }

        // No number anywhere: the code is already the best name we have
        return courseCode;
    }

    // The course name, cut at a whole word if it's long. No name -> the short course code.
    public static string ListName(string? courseName, string? courseCode)
    {
        if (string.IsNullOrWhiteSpace(courseName))
            return ShortCode(courseCode);

        var words = courseName.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        string full = string.Join(' ', words);
        if (full.Length <= MaxNameLength)
            return full;

        // Keep whole words while they fit, leaving room for the "…"
        var kept = new List<string>();
        foreach (string word in words)
        {
            if (string.Join(' ', kept.Append(word)).Length > MaxNameLength - 1)
                break;
            kept.Add(word);
        }

        while (kept.Count > 1 && DanglingWords.Contains(kept[^1], StringComparer.OrdinalIgnoreCase))
            kept.RemoveAt(kept.Count - 1);

        // A single word longer than the limit: cut inside it
        if (kept.Count == 0)
            return full[..(MaxNameLength - 1)] + "…";

        return string.Join(' ', kept) + "…";
    }
}
