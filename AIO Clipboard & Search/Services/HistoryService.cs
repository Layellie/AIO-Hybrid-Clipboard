using AIO_Hybrid_Clipboard.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace AIO_Hybrid_Clipboard.Services
{
    public enum ClipFilterCategory
    {
        All,
        Pinned,
        Links,
        Code,
        Colors
    }

    /// <summary>
    /// Owns the text and screenshot history collections: insertion order,
    /// pinned-first sections, SHA-256 &amp; text de-duplication, configurable eviction,
    /// and multi-word fuzzy search. UI-independent so all rules are unit tested.
    /// </summary>
    internal sealed class HistoryService
    {
        public const int MinLimit = 10;
        public const int MaxLimit = 200;
        public const int Limit    = 50;

        private int _maxItems = Limit;

        public HistoryService(int maxItems = Limit)
        {
            _maxItems = ClampLimit(maxItems);
        }

        public int MaxItems
        {
            get => _maxItems;
            set => _maxItems = ClampLimit(value);
        }

        public ObservableCollection<ClipItem>        Texts       { get; } = new();
        public ObservableCollection<ScreenshotModel> Screenshots { get; } = new();

        public static int ClampLimit(int value) => Math.Clamp(value, MinLimit, MaxLimit);

        /// <summary>
        /// Inserts or promotes a text entry. Pinned entries stay on top and are
        /// never auto-evicted; new entries land at the top of the unpinned section.
        /// Preserves existing pin state and user-defined alias on duplicate promotion.
        /// </summary>
        public void InsertText(string text, string? sourceApp = null, string? alias = null)
        {
            if (string.IsNullOrEmpty(text)) return;

            var existing = Texts.FirstOrDefault(c => c.Text == text);
            bool   wasPinned   = existing?.IsPinned ?? false;
            string keptAlias   = !string.IsNullOrWhiteSpace(alias) ? alias : (existing?.Alias ?? string.Empty);
            string keptSource  = !string.IsNullOrWhiteSpace(sourceApp) ? sourceApp : (existing?.SourceApp ?? string.Empty);
            if (existing != null) Texts.Remove(existing);

            var entry = new ClipItem(text, sourceApp: keptSource, alias: keptAlias) { IsPinned = wasPinned };
            Texts.Insert(wasPinned ? 0 : Texts.Count(c => c.IsPinned), entry);

            // Evict oldest unpinned entries — but never the one just inserted, so a
            // fresh copy always lands in history even when everything else is pinned.
            while (Texts.Count > _maxItems)
            {
                var victim = Texts.LastOrDefault(c => !c.IsPinned && c != entry);
                if (victim == null) break;
                Texts.Remove(victim);
            }
        }

        /// <summary>
        /// Adds a screenshot below the pinned section (or promotes an existing identical
        /// capture matched by SHA-256 <see cref="ScreenshotModel.ContentHash"/>).
        /// Returns any entry evicted to stay within the limit — the caller owns deleting
        /// its cached file — or null. When <paramref name="wasDuplicate"/> is true, the
        /// caller should delete the newly written redundant <paramref name="model"/> file.
        /// </summary>
        public ScreenshotModel? AddScreenshot(ScreenshotModel model, out bool wasDuplicate)
        {
            wasDuplicate = false;

            if (!string.IsNullOrEmpty(model.ContentHash))
            {
                var existing = Screenshots.FirstOrDefault(
                    s => string.Equals(s.ContentHash, model.ContentHash, StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    wasDuplicate = true;
                    existing.CapturedAt = model.CapturedAt;
                    if (string.IsNullOrWhiteSpace(existing.OcrText) && !string.IsNullOrWhiteSpace(model.OcrText))
                        existing.OcrText = model.OcrText;

                    Screenshots.Remove(existing);
                    Screenshots.Insert(existing.IsPinned ? 0 : Screenshots.Count(s => s.IsPinned), existing);
                    return null;
                }
            }

            ScreenshotModel? evicted = null;
            if (Screenshots.Count >= _maxItems)
            {
                evicted = Screenshots.LastOrDefault(s => !s.IsPinned);
                if (evicted != null) Screenshots.Remove(evicted);
            }

            Screenshots.Insert(Screenshots.Count(s => s.IsPinned), model);
            return evicted;
        }

        /// <summary>Backward-compatible overload when caller does not track duplicates separately.</summary>
        public ScreenshotModel? AddScreenshot(ScreenshotModel model) => AddScreenshot(model, out _);

        /// <summary>
        /// Trims unpinned items after <see cref="MaxItems"/> is lowered.
        /// Returns evicted screenshots so the caller can delete their cached PNG files.
        /// </summary>
        public IReadOnlyList<ScreenshotModel> TrimToLimit()
        {
            while (Texts.Count > _maxItems)
            {
                var victim = Texts.LastOrDefault(c => !c.IsPinned);
                if (victim == null) break;
                Texts.Remove(victim);
            }

            var removedShots = new List<ScreenshotModel>();
            while (Screenshots.Count > _maxItems)
            {
                var victim = Screenshots.LastOrDefault(s => !s.IsPinned);
                if (victim == null) break;
                Screenshots.Remove(victim);
                removedShots.Add(victim);
            }

            return removedShots;
        }

        /// <summary>
        /// Removes all unpinned texts and screenshots, keeping every pinned entry intact.
        /// Returns removed screenshots so the caller can delete their cached PNG files.
        /// </summary>
        public IReadOnlyList<ScreenshotModel> ClearUnpinned()
        {
            for (int i = Texts.Count - 1; i >= 0; i--)
            {
                if (!Texts[i].IsPinned)
                    Texts.RemoveAt(i);
            }

            var removedShots = new List<ScreenshotModel>();
            for (int i = Screenshots.Count - 1; i >= 0; i--)
            {
                if (!Screenshots[i].IsPinned)
                {
                    removedShots.Add(Screenshots[i]);
                    Screenshots.RemoveAt(i);
                }
            }

            return removedShots;
        }

        /// <summary>
        /// Removes unpinned text and screenshot entries older than <paramref name="retentionDays"/>.
        /// Pinned entries are never auto-expired. Returns removed screenshots for disk cleanup.
        /// </summary>
        public IReadOnlyList<ScreenshotModel> PurgeExpired(int retentionDays, DateTime? now = null)
        {
            if (retentionDays <= 0)
                return Array.Empty<ScreenshotModel>();

            DateTime cutoff = (now ?? DateTime.Now).AddDays(-retentionDays);

            for (int i = Texts.Count - 1; i >= 0; i--)
            {
                if (!Texts[i].IsPinned && Texts[i].CreatedAt < cutoff)
                    Texts.RemoveAt(i);
            }

            var removedShots = new List<ScreenshotModel>();
            for (int i = Screenshots.Count - 1; i >= 0; i--)
            {
                if (!Screenshots[i].IsPinned && Screenshots[i].CapturedAt < cutoff)
                {
                    removedShots.Add(Screenshots[i]);
                    Screenshots.RemoveAt(i);
                }
            }

            return removedShots;
        }

        /// <summary>Toggles the pin and moves the entry to the top of its new section.</summary>
        public void TogglePin(ClipItem item)
        {
            if (!Texts.Contains(item)) return;
            item.IsPinned = !item.IsPinned;
            Texts.Remove(item);
            Texts.Insert(item.IsPinned ? 0 : Texts.Count(c => c.IsPinned), item);
        }

        /// <summary>Toggles the pin and moves the screenshot to the top of its new section.</summary>
        public void TogglePin(ScreenshotModel shot)
        {
            if (!Screenshots.Contains(shot)) return;
            shot.IsPinned = !shot.IsPinned;
            Screenshots.Remove(shot);
            Screenshots.Insert(shot.IsPinned ? 0 : Screenshots.Count(s => s.IsPinned), shot);
        }

        public IReadOnlyList<ClipItem> FilterTexts(string query, ClipFilterCategory category = ClipFilterCategory.All) =>
            Texts.Where(t => MatchesText(t, query, category)).ToList();

        public IReadOnlyList<ScreenshotModel> FilterScreenshots(string query, ClipFilterCategory category = ClipFilterCategory.All) =>
            Screenshots.Where(s => MatchesScreenshot(s, query, category)).ToList();

        public static bool MatchesText(ClipItem item, string? query, ClipFilterCategory category = ClipFilterCategory.All)
        {
            bool categoryMatch = category switch
            {
                ClipFilterCategory.All    => true,
                ClipFilterCategory.Pinned => item.IsPinned,
                ClipFilterCategory.Links  => item.Kind == ClipKind.Url,
                ClipFilterCategory.Code   => item.Kind == ClipKind.Code,
                ClipFilterCategory.Colors => item.Kind == ClipKind.Color,
                _                         => true
            };
            if (!categoryMatch) return false;

            if (string.IsNullOrWhiteSpace(query))
                return true;

            string[] tokens = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            foreach (string token in tokens)
            {
                if (!TokenMatches(item.Text, token) &&
                    !TokenMatches(item.Alias, token) &&
                    !TokenMatches(item.SourceApp, token))
                    return false;
            }
            return true;
        }

        public static bool MatchesScreenshot(ScreenshotModel shot, string? query, ClipFilterCategory category = ClipFilterCategory.All)
        {
            if (category == ClipFilterCategory.Pinned && !shot.IsPinned)
                return false;

            if (string.IsNullOrWhiteSpace(query))
                return true;

            string[] tokens = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            foreach (string token in tokens)
            {
                if (!TokenMatches(shot.Name, token) &&
                    !TokenMatches(shot.OcrText, token) &&
                    !TokenMatches(shot.SourceApp, token))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Splits query into whitespace-separated terms; every term must match either
        /// as a case-insensitive substring or (for 4+ char terms) within edit distance 1
        /// of any word in the target text.
        /// </summary>
        public static bool MatchesMultiWordFuzzy(string? text, string? query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return true;
            if (string.IsNullOrEmpty(text))
                return false;

            string[] tokens = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            foreach (string token in tokens)
            {
                if (!TokenMatches(text, token))
                    return false;
            }
            return true;
        }

        private const int MaxFuzzyScanChars = 262_144; // Skip per-word fuzzy scan on >256K texts to keep typing 60fps.

        private static bool TokenMatches(string? target, string token)
        {
            if (string.IsNullOrEmpty(target))
                return false;

            if (target.Contains(token, StringComparison.OrdinalIgnoreCase))
                return true;

            // Typo-tolerant fallback for terms of length >= 4: check word-level Damerau-Levenshtein <= 1.
            if (token.Length < 4 || target.Length > MaxFuzzyScanChars)
                return false;

            ReadOnlySpan<char> targetSpan = target.AsSpan();
            ReadOnlySpan<char> tokenSpan  = token.AsSpan();
            int i = 0;
            while (i < targetSpan.Length)
            {
                while (i < targetSpan.Length && !char.IsLetterOrDigit(targetSpan[i])) i++;
                int start = i;
                while (i < targetSpan.Length && char.IsLetterOrDigit(targetSpan[i])) i++;
                int wordLen = i - start;

                if (wordLen >= 3 && Math.Abs(wordLen - tokenSpan.Length) <= 1 &&
                    IsWithinEditDistanceOne(targetSpan.Slice(start, wordLen), tokenSpan))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Zero-allocation check for Damerau-Levenshtein distance &lt;= 1 (case-insensitive):
        /// handles 1 substitution, 1 insertion, 1 deletion, or 1 adjacent transposition.
        /// </summary>
        private static bool IsWithinEditDistanceOne(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
        {
            int lenA = a.Length;
            int lenB = b.Length;
            int diff = lenA - lenB;
            if (diff < -1 || diff > 1) return false;

            int i = 0, j = 0;
            bool edited = false;

            while (i < lenA && j < lenB)
            {
                if (char.ToUpperInvariant(a[i]) == char.ToUpperInvariant(b[j]))
                {
                    i++;
                    j++;
                    continue;
                }

                if (edited) return false;
                edited = true;

                if (diff == 0)
                {
                    // Adjacent transposition (e.g. "invocie" vs "invoice")
                    if (i + 1 < lenA &&
                        char.ToUpperInvariant(a[i]) == char.ToUpperInvariant(b[j + 1]) &&
                        char.ToUpperInvariant(a[i + 1]) == char.ToUpperInvariant(b[j]))
                    {
                        i += 2;
                        j += 2;
                        continue;
                    }
                    // Single substitution
                    i++;
                    j++;
                }
                else if (diff > 0)
                {
                    // Extra char in a
                    i++;
                }
                else
                {
                    // Extra char in b
                    j++;
                }
            }

            return true;
        }
    }
}
