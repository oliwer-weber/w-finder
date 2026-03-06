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

        var scored = new List<(BrowserItem Item, int Score)>();

        foreach (var item in items)
        {
            int nameScore = ScoreMatch(item.Name, query);
            int categoryScore = ScoreMatch(item.Category, query);

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
    /// Returns 0 if the query is not a subsequence of the target.
    /// </summary>
    private static int ScoreMatch(string target, string query)
    {
        if (target.Length == 0 || query.Length == 0)
            return 0;

        string targetLower = target.ToLowerInvariant();
        string queryLower = query.ToLowerInvariant();

        // Quick check: is query a subsequence of target?
        int qi = 0;
        for (int ti = 0; ti < targetLower.Length && qi < queryLower.Length; ti++)
        {
            if (targetLower[ti] == queryLower[qi])
                qi++;
        }
        if (qi < queryLower.Length)
            return 0; // not a subsequence

        // Score the match
        int score = 0;
        qi = 0;
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

                // Word boundary bonus (char after space, dash, underscore, or case change)
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
