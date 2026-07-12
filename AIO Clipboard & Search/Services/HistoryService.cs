using AIO_Hybrid_Clipboard.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace AIO_Hybrid_Clipboard.Services
{
    /// <summary>
    /// Owns the text and screenshot history collections: insertion order,
    /// pinned-first sections, de-duplication and size-capped eviction.
    /// UI-independent so the rules can be unit tested.
    /// </summary>
    internal sealed class HistoryService
    {
        public const int Limit = 15;

        public ObservableCollection<ClipItem>        Texts       { get; } = new();
        public ObservableCollection<ScreenshotModel> Screenshots { get; } = new();

        /// <summary>
        /// Inserts or promotes a text entry. Pinned entries stay on top and are
        /// never auto-evicted; new entries land at the top of the unpinned section.
        /// </summary>
        public void InsertText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            var existing = Texts.FirstOrDefault(c => c.Text == text);
            bool wasPinned = existing?.IsPinned ?? false;
            if (existing != null) Texts.Remove(existing);

            var entry = new ClipItem(text) { IsPinned = wasPinned };
            Texts.Insert(wasPinned ? 0 : Texts.Count(c => c.IsPinned), entry);

            // Evict oldest unpinned entries — but never the one just inserted, so a
            // fresh copy always lands in history even when everything else is pinned
            // (matching screenshot behavior, where pins may exceed the cap).
            while (Texts.Count > Limit)
            {
                var victim = Texts.LastOrDefault(c => !c.IsPinned && c != entry);
                if (victim == null) break;
                Texts.Remove(victim);
            }
        }

        /// <summary>
        /// Adds a screenshot below the pinned section. Returns the entry evicted to
        /// stay within the limit — the caller owns deleting its cached file — or null.
        /// </summary>
        public ScreenshotModel? AddScreenshot(ScreenshotModel model)
        {
            ScreenshotModel? evicted = null;
            if (Screenshots.Count >= Limit)
            {
                evicted = Screenshots.LastOrDefault(s => !s.IsPinned);
                if (evicted != null) Screenshots.Remove(evicted);
            }
            Screenshots.Insert(Screenshots.Count(s => s.IsPinned), model);
            return evicted;
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

        public IReadOnlyList<ClipItem> FilterTexts(string query) =>
            Texts.Where(t => t.Text.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        public IReadOnlyList<ScreenshotModel> FilterScreenshots(string query) =>
            Screenshots.Where(s => s.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                   s.OcrText.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
    }
}
