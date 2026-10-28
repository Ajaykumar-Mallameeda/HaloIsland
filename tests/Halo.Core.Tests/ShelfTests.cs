using Halo.Core.Shelf;
using Xunit;

namespace Halo.Core.Tests;

/// <summary>H-F02: the user-controlled shelf — text, file references, limits, missing files, Unicode, persistence.</summary>
public class ShelfTests
{
    private readonly ManualClock _clock = new();

    [Fact]
    public void TextIsPinnedNewestFirstAndRepeatsMoveToTheTop()
    {
        var shelf = new ShelfList(_clock);
        var a = shelf.AddText("first")!;
        shelf.AddText("second");
        shelf.AddText("first");
        Assert.Equal(["first", "second"], shelf.Items.Select(i => i.Value));
        Assert.NotEqual(a.Id, shelf.Items[0].Id);
        Assert.Null(shelf.AddText("   "));
        Assert.Null(shelf.AddText(null));
    }

    [Fact]
    public void LongTextIsCutUnicodeIsKeptAndControlCharactersAreDropped()
    {
        var shelf = new ShelfList(_clock);
        Assert.Equal(ShelfList.MaxTextLength, shelf.AddText(new string('a', 50_000))!.Value.Length);
        var unicode = shelf.AddText("Grüße \u4F60\u597D \U0001F600\u0007\r\nline two\ttab")!;
        Assert.Equal("Grüße \u4F60\u597D \U0001F600\r\nline two\ttab", unicode.Value);
        Assert.Equal("Grüße \u4F60\u597D \U0001F600", unicode.Label); // first line only
    }

    [Fact]
    public void TheShelfIsBoundedAndCanBeCleared()
    {
        var shelf = new ShelfList(_clock);
        for (var i = 0; i < ShelfList.MaxItems + 10; i++)
        {
            shelf.AddText($"item {i}");
        }

        Assert.Equal(ShelfList.MaxItems, shelf.Items.Count);
        Assert.Equal($"item {ShelfList.MaxItems + 9}", shelf.Items[0].Value);
        Assert.True(shelf.Remove(shelf.Items[0].Id));
        shelf.Clear();
        Assert.Empty(shelf.Items);
    }

    [Fact]
    public void FilesAreReferencesAndMissingFilesAreReportedNotRecreated()
    {
        var dir = Path.Combine(Path.GetTempPath(), "halo-shelf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "report \u00e9t\u00e9.txt");
            File.WriteAllText(file, "content");
            var shelf = new ShelfList(_clock);
            var added = shelf.AddFiles([file, "relative\\path.txt", string.Empty, dir]);
            Assert.Equal(2, added.Count); // the relative and empty paths are refused
            var item = added[0];
            Assert.Equal((ShelfKind.File, file), (item.Kind, item.Value));
            Assert.StartsWith("report \u00e9t\u00e9.txt", item.Label, StringComparison.Ordinal);
            Assert.False(ShelfList.IsMissing(item));
            Assert.False(ShelfList.IsMissing(added[1])); // a folder

            File.Delete(file);
            Assert.True(ShelfList.IsMissing(item));
            Assert.False(File.Exists(file)); // checking never recreates it
            Assert.True(shelf.Remove(item.Id));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void PersistenceRoundTripsAndSkipsInvalidEntries()
    {
        var shelf = new ShelfList(_clock);
        shelf.AddText("snippet \u2603");
        shelf.AddFiles([@"C:\Users\Public\Documents\x.docx"]);
        var back = new ShelfList(_clock);
        Assert.True(back.Load(shelf.ToJson()));
        Assert.Equal(shelf.Items, back.Items);

        var junk = new ShelfList(_clock);
        Assert.True(junk.Load("{ \"items\": [ { \"id\": \"a\", \"kind\": \"file\", \"value\": \"relative.txt\", \"addedUtc\": \"2026-10-29T09:00:00Z\" }, { \"id\": \"b\", \"kind\": \"exe\", \"value\": \"x\", \"addedUtc\": \"2026-10-29T09:00:00Z\" }, { \"id\": \"c\", \"kind\": \"text\", \"value\": \"ok\", \"addedUtc\": \"2026-10-29T09:00:00Z\" } ] }"));
        Assert.Equal("ok", Assert.Single(junk.Items).Value);
    }
}
