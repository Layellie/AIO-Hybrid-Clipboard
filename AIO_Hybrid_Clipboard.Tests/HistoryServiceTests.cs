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
        public void FilterScreenshots_MatchesNameAndOcrText()
        {
            var history = new HistoryService();
            history.AddScreenshot(new ScreenshotModel { Name = "Capture - 10:00", OcrText = "invoice total 42" });
            history.AddScreenshot(new ScreenshotModel { Name = "Capture - 11:00", OcrText = "meeting notes" });

            Assert.Single(history.FilterScreenshots("invoice"));
            Assert.Single(history.FilterScreenshots("11:00"));
            Assert.Equal(2, history.FilterScreenshots("capture").Count);
        }
    }
}
