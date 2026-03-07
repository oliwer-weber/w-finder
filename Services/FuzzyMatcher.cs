using w_finder.Models;

namespace w_finder.Services;

/// <summary>
/// Scores and ranks BrowserItems against a search query.
/// Uses subsequence matching with bonuses for consecutive chars,
/// word-boundary matches, and exact prefix matches.
/// </summary>
public static class FuzzyMatcher
{
    /// <summary>
    /// Returns items that match the query, sorted best-match-first.
    /// </summary>
    public static List<BrowserItem> Match(List<BrowserItem> items, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return items;

        // Compute queryLower once — not per item.
        string queryLower = query.ToLowerInvariant();

        var scored = new List<(BrowserItem Item, int Score)>();

        foreach (var item in items)
        {
            // Use pre-lowercased fields to avoid per-item allocations.
            int nameScore = ScoreMatch(item.Name, item.NameLower, queryLower);
            int categoryScore = ScoreMatch(item.Category, item.CategoryLower, queryLower);

            // Name matches are weighted higher than category matches
            int best = Math.Max(nameScore, categoryScore / 2);

            if (best > 0)
                scored.Add((item, best));
        }

        return scored
            .OrderByDescending(s => s.Score)
            .Select(s => s.Item)
            .ToList();
    }

    /// <summary>
    /// Scores how well the query matches the target string.
    /// Single-pass: subsequence check and scoring are merged together.
    /// Returns 0 if the query is not a subsequence of the target.
    /// </summary>
    private static int ScoreMatch(string target, string targetLower, string queryLower)
    {
        if (targetLower.Length == 0 || queryLower.Length == 0)
            return 0;

        int score = 0;
        int qi = 0;
        bool prevMatched = false;

        for (int ti = 0; ti < targetLower.Length && qi < queryLower.Length; ti++)
        {
            if (targetLower[ti] == queryLower[qi])
            {
                score += 1;

                // Consecutive match bonus
                if (prevMatched)
                    score += 2;

                // Start-of-string bonus
                if (ti == 0)
                    score += 5;

                // Word boundary bonus (char after space, dash, underscore, case change)
                // Uses the original target so PascalCase boundaries are detected correctly.
                if (ti > 0 && IsWordBoundary(target, ti))
                    score += 3;

                prevMatched = true;
                qi++;
            }
            else
            {
                prevMatched = false;
            }
        }

        // If we didn't consume the full query it's not a subsequence — no match.
        if (qi < queryLower.Length)
            return 0;

        // Exact prefix bonus
        if (targetLower.StartsWith(queryLower))
            score += 10;

        return score;
    }

    private static bool IsWordBoundary(string s, int index)
    {
        char prev = s[index - 1];
        char curr = s[index];
        return prev == ' ' || prev == '-' || prev == '_' || prev == ':'
            || (char.IsLower(prev) && char.IsUpper(curr));
    }
}
