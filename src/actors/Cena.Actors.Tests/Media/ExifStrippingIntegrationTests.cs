// =============================================================================
// Cena Platform — EXIF stripping integration tests (prr-001)
//
// Goal
// ----
// prr-001's DoD #3 — "Integration test with the committed EXIF-laden
// fixture passes: upload → response asserts ExifStripped=true → read
// persisted bytes → MetadataExtractor finds zero GPS/Make/Model/owner tags."
//
// The earlier Phase-2 test in src/api/Cena.Student.Api.Host.Tests covers
// the endpoint shape on a round-trippable 2x2 PNG but does NOT prove
// that metadata survives → is then absent. This test does, using the
// committed `tests/fixtures/exif/exif-laden-sample.jpg` fixture carrying
// known GPS / Make / Model / DateTime tags, and validates with
// MetadataExtractor (a codec-independent library, so we're not grading
// ImageSharp's encoder with itself).
//
// Shape — ExifStripper is invoked directly on the fixture bytes, matching
// the endpoint's seam. The fixture and the assertions are the point.
// =============================================================================

using Cena.Infrastructure.Media;
using MetadataExtractor.Formats.Exif;
using MetaDirectory = MetadataExtractor.Directory;
using MetadataReader = MetadataExtractor.ImageMetadataReader;

namespace Cena.Actors.Tests.Media;

public sealed class ExifStrippingIntegrationTests
{
    private const string FixtureRelPath = "tests/fixtures/exif/exif-laden-sample.jpg";

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CLAUDE.md"))) return dir.FullName;
            if (Directory.Exists(Path.Combine(dir.FullName, "src", "actors", "Cena.Actors")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Repo root not found.");
    }

    private static byte[] LoadFixture()
    {
        var path = Path.Combine(FindRepoRoot(), FixtureRelPath);
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"prr-001 fixture missing at {path}. The committed EXIF-laden " +
                "JPEG is required for integration proof — regenerate it with " +
                "scripts/make-exif-fixture.py if it was accidentally deleted.");
        return File.ReadAllBytes(path);
    }

    [Fact]
    public void Fixture_HasExpectedEXIFTags_Baseline()
    {
        // Sanity: the committed fixture must actually carry the tags we
        // are going to strip. If this assertion fails, the test would
        // succeed trivially — the fixture is the whole point.
        var bytes = LoadFixture();
        var directories = MetadataReader.ReadMetadata(new MemoryStream(bytes));

        var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
        Assert.NotNull(ifd0);
        Assert.True(ifd0!.ContainsTag(ExifDirectoryBase.TagMake),
            "Fixture should carry Make tag (it's supposed to be EXIF-laden).");
        Assert.True(ifd0.ContainsTag(ExifDirectoryBase.TagModel),
            "Fixture should carry Model tag.");

        var gps = directories.OfType<GpsDirectory>().FirstOrDefault();
        Assert.NotNull(gps);
        Assert.True(gps!.ContainsTag(GpsDirectory.TagLatitude),
            "Fixture should carry GPS Latitude (student's home geolocation risk).");
    }

    [Fact]
    public void Strip_RemovesAllGPSMakeModelAndTimestampTags()
    {
        var stripper = new ExifStripper();
        var input = LoadFixture();

        var result = stripper.Strip(input);

        Assert.True(
            result.Success,
            $"ExifStripper failed on the committed fixture: {result.FailureReason}. " +
            "The fixture must be strippable or it can't serve as a regression test.");
        Assert.NotEmpty(result.Scrubbed);

        var dirs = MetadataReader.ReadMetadata(new MemoryStream(result.Scrubbed)).ToList();

        // prr-001 non-negotiables — every one of these must be gone.
        AssertTagAbsent(dirs, ExifDirectoryBase.TagMake,            "Make (camera vendor)");
        AssertTagAbsent(dirs, ExifDirectoryBase.TagModel,           "Model (camera model)");
        AssertTagAbsent(dirs, ExifDirectoryBase.TagDateTime,        "DateTime (capture time)");
        AssertTagAbsent(dirs, ExifDirectoryBase.TagDateTimeOriginal,"DateTimeOriginal");

        // GPS — the worst leak shape (minor's home coordinates).
        var gps = dirs.OfType<GpsDirectory>().FirstOrDefault();
        if (gps is not null)
        {
            Assert.False(gps.ContainsTag(GpsDirectory.TagLatitude),
                "prr-001: GPS Latitude survived the strip — student geolocation leaked.");
            Assert.False(gps.ContainsTag(GpsDirectory.TagLongitude),
                "prr-001: GPS Longitude survived the strip — student geolocation leaked.");
            Assert.False(gps.ContainsTag(GpsDirectory.TagLatitudeRef),
                "prr-001: GPS LatitudeRef survived the strip.");
            Assert.False(gps.ContainsTag(GpsDirectory.TagLongitudeRef),
                "prr-001: GPS LongitudeRef survived the strip.");
        }
    }

    [Fact]
    public void Strip_EmptyInput_FailsWithExplicitReason()
    {
        var stripper = new ExifStripper();

        var result = stripper.Strip(Array.Empty<byte>());

        Assert.False(result.Success);
        Assert.Equal("empty_input", result.FailureReason);
        Assert.Empty(result.Scrubbed);
    }

    [Fact]
    public void Strip_GarbageInput_FailsAndReturnsEmptyBuffer()
    {
        var stripper = new ExifStripper();
        // Random bytes that do not match any supported image format.
        var garbage = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE, 0xBA, 0xBE };

        var result = stripper.Strip(garbage);

        Assert.False(result.Success);
        Assert.NotNull(result.FailureReason);
        // Per prr-001 DoD #4: on strip-failure we MUST persist nothing —
        // the empty Scrubbed buffer is the contract the endpoint relies
        // on to know "do not forward to OCR / moderation / persistence".
        Assert.Empty(result.Scrubbed);
    }

    private static void AssertTagAbsent(
        IReadOnlyCollection<MetaDirectory> dirs,
        int tagType,
        string humanName)
    {
        foreach (var dir in dirs)
        {
            if (dir.ContainsTag(tagType))
            {
                var description = dir.GetDescription(tagType) ?? "<binary>";
                Assert.Fail(
                    $"prr-001: {humanName} tag survived the strip in {dir.Name} " +
                    $"(value: {description}). The scrubbed bytes must not carry " +
                    "any EXIF / IPTC / XMP / GPS metadata — the response's " +
                    "ExifStripped=true label would be a lie.");
            }
        }
    }
}
