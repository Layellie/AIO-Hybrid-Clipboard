using AIO_Hybrid_Clipboard.Models;
using AIO_Hybrid_Clipboard.Services;
using System.Linq;
using Xunit;

namespace AIO_Hybrid_Clipboard.Tests
{
    public class HistoryServiceTests
    {
        [Fact]
        public void InsertText_AddsNewEntryToTop()
        {
            var history = new HistoryService();
            history.InsertText("first");
            history.InsertText("second");

            Assert.Equal("second", history.Texts[0].Text);
            Assert.Equal("first",  history.Texts[1].Text);
        }

        [Fact]
        public void InsertText_IgnoresNullAndEmpty()
        {
            var history = new HistoryService();
            history.InsertText("");
            Assert.Empty(history.Texts);
        }

        [Fact]
        public void InsertText_PromotesDuplicateInsteadOfDuplicating()
        {
            var history = new HistoryService();
            history.InsertText("a");
            history.InsertText("b");
            history.InsertText("a");

            Assert.Equal(2, history.Texts.Count);
            Assert.Equal("a", history.Texts[0].Text);
        }

        [Fact]
        public void InsertText_NewEntriesLandBelowPinnedSection()
        {
            var history = new HistoryService();
            history.InsertText("pinned");
            history.TogglePin(history.Texts[0]);

            history.InsertText("fresh");

            Assert.Equal("pinned", history.Texts[0].Text);
            Assert.Equal("fresh",  history.Texts[1].Text);
        }

        [Fact]
        public void InsertText_EvictsOldestUnpinnedBeyondLimit()
        {
            var history = new HistoryService();
            history.InsertText("keep-me");
            history.TogglePin(history.Texts[0]);

            for (int i = 0; i < HistoryService.Limit + 5; i++)
                history.InsertText($"entry-{i}");

            Assert.Equal(HistoryService.Limit, history.Texts.Count);
            Assert.Contains(history.Texts, t => t.Text == "keep-me");
            Assert.DoesNotContain(history.Texts, t => t.Text == "entry-0");
        }

        [Fact]
        public void InsertText_AllPinnedNothingIsEvicted()
        {
            var history = new HistoryService();
            for (int i = 0; i < HistoryService.Limit; i++)
            {
                history.InsertText($"pin-{i}");
                history.TogglePin(history.Texts.First(t => t.Text == $"pin-{i}"));
            }

            history.InsertText("overflow");

            // The unpinned newcomer may exceed the cap because pins are never evicted.
            Assert.Equal(HistoryService.Limit + 1, history.Texts.Count);
            Assert.Contains(history.Texts, t => t.Text == "overflow");
        }

        [Fact]
        public void ConfigurableLimit_EnforcesCustomCapAndTrimsUnpinned()
        {
            var history = new HistoryService(maxItems: 20);
            for (int i = 0; i < 15; i++)
            {
                history.InsertText($"item-{i}");
                history.AddScreenshot(new ScreenshotModel { Name = $"s-{i}", Path = $"p-{i}" });
            }

            history.TogglePin(history.Texts.Last());
            history.TogglePin(history.Screenshots.Last());

            history.MaxItems = 10;
            var evictedShots = history.TrimToLimit();

            Assert.Equal(10, history.Texts.Count);
            Assert.Equal(10, history.Screenshots.Count);
            Assert.Equal(5, evictedShots.Count);
            Assert.Contains(history.Texts, t => t.IsPinned);
            Assert.Contains(history.Screenshots, s => s.IsPinned);
        }

        [Fact]
        public void TogglePin_MovesEntryToTopAndBackBelowPins()
        {
            var history = new HistoryService();
            history.InsertText("a");
            history.InsertText("b");
            var a = history.Texts[1];

            history.TogglePin(a);
            Assert.True(a.IsPinned);
            Assert.Equal("a", history.Texts[0].Text);

            history.TogglePin(a);
            Assert.False(a.IsPinned);
            Assert.Equal("a", history.Texts[0].Text); // top of unpinned section
        }

        [Fact]
        public void AddScreenshot_ReturnsEvictedEntryBeyondLimit()
        {
            var history = new HistoryService();
            for (int i = 0; i < HistoryService.Limit; i++)
                history.AddScreenshot(new ScreenshotModel { Name = $"shot-{i}", Path = $"p{i}" });

            var evicted = history.AddScreenshot(new ScreenshotModel { Name = "new", Path = "pn" });

            Assert.NotNull(evicted);
            Assert.Equal("shot-0", evicted!.Name);
            Assert.Equal(HistoryService.Limit, history.Screenshots.Count);
        }

        [Fact]
        public void AddScreenshot_PinnedEntriesSurviveEviction()
        {
            var history = new HistoryService();
            for (int i = 0; i < HistoryService.Limit; i++)
                history.AddScreenshot(new ScreenshotModel { Name = $"shot-{i}", Path = $"p{i}" });

            var oldest = history.Screenshots[^1];
            history.TogglePin(oldest);

            var evicted = history.AddScreenshot(new ScreenshotModel { Name = "new", Path = "pn" });

            Assert.NotNull(evicted);
            Assert.NotEqual(oldest, evicted);
            Assert.Contains(oldest, history.Screenshots);
        }

        [Fact]
        public void AddScreenshot_DeduplicatesBySha256HashAndPromotesExisting()
        {
            var history = new HistoryService();
            var first = new ScreenshotModel { Name = "s1", Path = "p1", ContentHash = "ABCD1234", OcrText = "saved ocr" };
            var other = new ScreenshotModel { Name = "s2", Path = "p2", ContentHash = "FFFF9999" };

            history.AddScreenshot(first, out bool dup1);
            history.AddScreenshot(other, out bool dup2);

            var duplicate = new ScreenshotModel { Name = "s1-copy", Path = "p1-new", ContentHash = "abcd1234" };
            var evicted   = history.AddScreenshot(duplicate, out bool dup3);

            Assert.False(dup1);
            Assert.False(dup2);
            Assert.True(dup3);
            Assert.Null(evicted);
            Assert.Equal(2, history.Screenshots.Count);
            Assert.Same(first, history.Screenshots[0]);
            Assert.Equal("saved ocr", history.Screenshots[0].OcrText);
        }

        [Fact]
        public void ClearUnpinned_RemovesOnlyUnpinnedEntriesAndReturnsRemovedScreenshots()
        {
            var history = new HistoryService();
            history.InsertText("pin-text");
            history.TogglePin(history.Texts[0]);
            history.InsertText("temp-text");

            var pinShot  = new ScreenshotModel { Name = "pin-shot",  Path = "pin.png" };
            var tempShot = new ScreenshotModel { Name = "temp-shot", Path = "temp.png" };
            history.AddScreenshot(pinShot);
            history.TogglePin(pinShot);
            history.AddScreenshot(tempShot);

            var removed = history.ClearUnpinned();

            Assert.Single(history.Texts);
            Assert.Equal("pin-text", history.Texts[0].Text);
            Assert.Single(history.Screenshots);
            Assert.Same(pinShot, history.Screenshots[0]);
            Assert.Single(removed);
            Assert.Same(tempShot, removed[0]);
        }

        [Fact]
        public void FilterTexts_IsCaseInsensitive()
        {
            var history = new HistoryService();
            history.InsertText("Hello World");
            history.InsertText("unrelated");

            var hits = history.FilterTexts("hello");

            Assert.Single(hits);
            Assert.Equal("Hello World", hits[0].Text);
        }

        [Fact]
        public void FilterTexts_SupportsMultiWordAndFuzzyTypoTolerance()
        {
            var history = new HistoryService();
            history.InsertText("Quarterly invoice summary 2026");
            history.InsertText("Random grocery list");

            Assert.Single(history.FilterTexts("quarterly 2026"));
            Assert.Single(history.FilterTexts("invocie summary")); // transposition typo
            Assert.Empty(history.FilterTexts("quarterly grocery"));
        }

        [Fact]
        public void FilterScreenshots_MatchesNameAndOcrText()
        {
            var history = new HistoryService();
            history.AddScreenshot(new ScreenshotModel { Name = "Capture - 10:00", OcrText = "invoice total 42" });
            history.AddScreenshot(new ScreenshotModel { Name = "Capture - 11:00", OcrText = "meeting notes" });

            Assert.Single(history.FilterScreenshots("invoice"));
            Assert.Single(history.FilterScreenshots("11:00"));
            Assert.Equal(2, history.FilterScreenshots("capture").Count);
            Assert.Single(history.FilterScreenshots("capture 42")); // multi-word across Name + OcrText
        }
    }
}
