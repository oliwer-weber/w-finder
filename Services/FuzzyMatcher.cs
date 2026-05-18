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
    /// Maximum usage bonus added to the raw text-match score. Keeps usage from
    /// dominating poor text matches — a brand-new strong match still wins over a
    /// well-worn weak one.
    /// </summary>
    private const int MaxUsageBonus = 10;

    /// <summary>
    /// Returns items that match the query, sorted best-match-first.
    /// Adds a small frecency bonus (capped) from UsageStore so frequently-used
    /// items rise within their text-match tier.
    /// </summary>
    public static List<BrowserItem> Match(List<BrowserItem> items, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return items;

        return MatchScored(items, query).Select(s => s.Item).ToList();
    }

    /// <summary>
    /// Same as <see cref="Match"/> but returns the score alongside each item so callers
    /// can rank groups by their best-scoring member instead of by Kind precedence.
    /// </summary>
    public static List<(BrowserItem Item, int Score)> MatchScored(List<BrowserItem> items, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return items.Select(i => (i, 0)).ToList();

        string queryLower = query.ToLowerInvariant();

        var scored = new List<(BrowserItem Item, int Score)>();

        foreach (var item in items)
        {
            int nameScore = ScoreMatch(item.Name, item.NameLower, queryLower);
            int categoryScore = ScoreMatch(item.Category, item.CategoryLower, queryLower);

            int best = Math.Max(nameScore, categoryScore / 2);
            if (best <= 0) continue;

            double usage = UsageStore.GetScore(item);
            if (usage > 0)
            {
                int bonus = (int)Math.Min(MaxUsageBonus, Math.Log(1 + usage) * 3);
                best += bonus;
            }

            scored.Add((item, best));
        }

        return scored
            .OrderByDescending(s => s.Score)
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

        // Exact prefix bonus, or contiguous-substring-anywhere bonus.
        // The substring bonus rewards "close in" appearing intact inside
        // "Close Inactive Views" even though it isn't at the start — keeping
        // it well above scattered-letter subsequence matches like
        // "Calloutsymbol detail number, drawing number".
        if (targetLower.StartsWith(queryLower))
            score += 10;
        else if (queryLower.Length > 1 && targetLower.Contains(queryLower))
            score += 8;

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
