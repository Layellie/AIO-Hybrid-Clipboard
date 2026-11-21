using AIO_Hybrid_Clipboard.Models;
using Xunit;

namespace AIO_Hybrid_Clipboard.Tests
{
    public class ContentClassifierTests
    {
        [Theory]
        [InlineData("https://github.com/Layellie/AIO-Hybrid-Clipboard")]
        [InlineData("http://localhost:8080/api/health")]
        [InlineData("  https://example.com/path?a=1&b=2  ")]
        public void Classify_DetectsHttpUrls(string input)
        {
            var (kind, color) = ContentClassifier.Classify(input);
            Assert.Equal(ClipKind.Url, kind);
            Assert.Null(color);
        }

        [Theory]
        [InlineData("#fff", "#FFFFFF")]
        [InlineData("#5865f2", "#5865F2")]
        [InlineData("#805865F2", "#805865F2")]
        [InlineData("  #0aB  ", "#00AABB")]
        public void Classify_DetectsAndNormalizesHexColors(string input, string expectedHex)
        {
            var (kind, color) = ContentClassifier.Classify(input);
            Assert.Equal(ClipKind.Color, kind);
            Assert.Equal(expectedHex, color);
        }

        [Theory]
        [InlineData("#12345")]
        [InlineData("#GGG")]
        [InlineData("5865F2")]
        public void Classify_RejectsInvalidHexColors(string input)
        {
            var (kind, color) = ContentClassifier.Classify(input);
            Assert.NotEqual(ClipKind.Color, kind);
            Assert.Null(color);
        }

        [Theory]
        [InlineData("samet@example.com")]
        [InlineData("dev.team+alerts@sub.domain.org")]
        public void Classify_DetectsEmails(string input)
        {
            var (kind, _) = ContentClassifier.Classify(input);
            Assert.Equal(ClipKind.Email, kind);
        }

        [Theory]
        [InlineData(@"C:\Users\samet\source\repos\app.sln")]
        [InlineData(@"D:/Projects/AIO/Program.cs")]
        [InlineData(@"""C:\Program Files\My App\tool.exe""")]
        [InlineData(@"\\fileserver\shared\report.pdf")]
        public void Classify_DetectsWindowsFilePaths(string input)
        {
            var (kind, _) = ContentClassifier.Classify(input);
            Assert.Equal(ClipKind.FilePath, kind);
        }

        [Theory]
        [InlineData("git commit -m \"feat: v2\"")]
        [InlineData("dotnet test --configuration Release")]
        [InlineData("SELECT id, name FROM users WHERE active = 1;")]
        [InlineData("public static int Add(int a, int b) => a + b;")]
        [InlineData("{\"version\": \"2.0.0\"}")]
        public void Classify_DetectsCodeSnippets(string input)
        {
            var (kind, _) = ContentClassifier.Classify(input);
            Assert.Equal(ClipKind.Code, kind);
        }

        [Theory]
        [InlineData("Just a regular note for the meeting")]
        [InlineData("Check https://example.com later please")]
        [InlineData("")]
        public void Classify_FallsBackToPlainText(string input)
        {
            var (kind, _) = ContentClassifier.Classify(input);
            Assert.Equal(ClipKind.Text, kind);
        }

        [Fact]
        public void BuildPreview_CollapsesWhitespaceAndTruncatesWithEllipsis()
        {
            Assert.Equal("hello world again", ContentClassifier.BuildPreview("  hello\r\n\t  world   again  "));

            string longText = new string('a', 50);
            string preview  = ContentClassifier.BuildPreview(longText, maxChars: 10);
            Assert.Equal(10, preview.Length);
            Assert.EndsWith("…", preview);
        }

        [Fact]
        public void CountLines_CountsLineBreaksAccurately()
        {
            Assert.Equal(0, ContentClassifier.CountLines(""));
            Assert.Equal(1, ContentClassifier.CountLines("single line"));
            Assert.Equal(3, ContentClassifier.CountLines("a\nb\r\nc"));
        }
    }
}
