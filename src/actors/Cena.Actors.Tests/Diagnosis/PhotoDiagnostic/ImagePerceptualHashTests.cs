// =============================================================================
// Cena Platform — ImagePerceptualHash tests (EPIC-PRR-J PRR-404)
//
// Locks the three invariants the gate depends on:
//   1. Identical inputs → Hamming 0 (stability).
//   2. Near-identical inputs (single-pixel flip, gentle compression) →
//      Hamming ≤ 4 (noise tolerance).
//   3. Completely different inputs → Hamming ≥ 16 (discriminating power).
//
// The 16-bit floor for "different" is the gap between our default
// rejection threshold (10) and the point where two genuinely different
// pages start colliding. The test picks images from opposite corners of
// the intensity distribution so the floor is comfortably clear.
// =============================================================================

using Cena.Actors.Diagnosis.PhotoDiagnostic;
using Xunit;

namespace Cena.Actors.Tests.Diagnosis.PhotoDiagnostic;

public class ImagePerceptualHashTests
{
    // 16×16 grayscale canvases. Small enough to write literally, big enough
    // that the 8×8 downscale samples a meaningful region of each cell.
    private const int W = 16;
    private const int H = 16;

    private static byte[] SolidGrey(byte value)
    {
        var buf = new byte[W * H];
        for (int i = 0; i < buf.Length; i++) buf[i] = value;
        return buf;
    }

    private static byte[] LeftWhiteRightBlack()
    {
        // Left half white (255), right half black (0). Classic
        // high-contrast boundary — aHash should map roughly half of
        // the 8×8 cells above mean, half below, yielding a stable
        // hash with ~32 bits set.
        var buf = new byte[W * H];
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                buf[y * W + x] = x < W / 2 ? (byte)255 : (byte)0;
            }
        }
        return buf;
    }

    private static byte[] TopWhiteBottomBlack()
    {
        // Top half white, bottom half black — a horizontal flip of the
        // left/right case. Mean identical, but which cells are above/below
        // is totally different. This is our "visually different page".
        var buf = new byte[W * H];
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                buf[y * W + x] = y < H / 2 ? (byte)255 : (byte)0;
            }
        }
        return buf;
    }

    private static byte[] Checkerboard(int cellSize, byte dark = 0, byte light = 255)
    {
        var buf = new byte[W * H];
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                var cx = x / cellSize;
                var cy = y / cellSize;
                buf[y * W + x] = ((cx + cy) & 1) == 0 ? light : dark;
            }
        }
        return buf;
    }

    [Fact]
    public void IdenticalInputsProduceZeroHammingDistance()
    {
        var a = LeftWhiteRightBlack();
        var b = LeftWhiteRightBlack();
        var ha = ImagePerceptualHash.Compute(a, W, H);
        var hb = ImagePerceptualHash.Compute(b, W, H);
        Assert.Equal(0, ImagePerceptualHash.HammingDistance(ha, hb));
    }

    [Fact]
    public void ComputeIsDeterministicAcrossCalls()
    {
        var a = Checkerboard(4);
        var h1 = ImagePerceptualHash.Compute(a, W, H);
        var h2 = ImagePerceptualHash.Compute(a, W, H);
        var h3 = ImagePerceptualHash.Compute(a, W, H);
        Assert.Equal(h1, h2);
        Assert.Equal(h2, h3);
    }

    [Fact]
    public void SinglePixelFlipKeepsDistanceSmall()
    {
        // Flip one pixel in an otherwise-identical image. aHash sees the
        // new pixel only if it falls on one of the 8×8 sample coords AND
        // crosses the mean boundary. Either way, distance is tiny.
        var baseline = LeftWhiteRightBlack();
        var tweaked = (byte[])baseline.Clone();
        tweaked[5 * W + 5] = (byte)(tweaked[5 * W + 5] == 0 ? 255 : 0);

        var hb = ImagePerceptualHash.Compute(baseline, W, H);
        var ht = ImagePerceptualHash.Compute(tweaked, W, H);
        var dist = ImagePerceptualHash.HammingDistance(hb, ht);
        Assert.True(dist <= 4, $"single-pixel flip yielded Hamming {dist}, expected ≤ 4");
    }

    [Fact]
    public void LightReEncodingNoiseKeepsDistanceSmall()
    {
        // Simulate a re-encode / re-shoot: perturb every pixel by ±3,
        // which is well inside the jitter a second phone photo of the
        // same page would introduce. Boundary cells should almost
        // never cross the mean.
        var baseline = LeftWhiteRightBlack();
        var noisy = (byte[])baseline.Clone();
        for (int i = 0; i < noisy.Length; i++)
        {
            int delta = (i % 2 == 0) ? 3 : -3;
            int v = noisy[i] + delta;
            noisy[i] = (byte)Math.Clamp(v, 0, 255);
        }
        var hb = ImagePerceptualHash.Compute(baseline, W, H);
        var hn = ImagePerceptualHash.Compute(noisy, W, H);
        var dist = ImagePerceptualHash.HammingDistance(hb, hn);
        Assert.True(dist <= 4, $"light noise yielded Hamming {dist}, expected ≤ 4");
    }

    [Fact]
    public void VisuallyDifferentInputsHaveLargeHammingDistance()
    {
        // Left-white/right-black vs top-white/bottom-black: both have the
        // same mean (127) and the same number of bright cells, but the
        // geometry is orthogonal. aHash must register this as far apart.
        var lr = LeftWhiteRightBlack();
        var tb = TopWhiteBottomBlack();
        var hlr = ImagePerceptualHash.Compute(lr, W, H);
        var htb = ImagePerceptualHash.Compute(tb, W, H);
        var dist = ImagePerceptualHash.HammingDistance(hlr, htb);
        Assert.True(dist >= 16,
            $"different images yielded Hamming {dist}, expected ≥ 16 " +
            $"(must clear the default rejection threshold of 10 with margin)");
    }

    [Fact]
    public void CheckerboardVsSolidIsAlsoFarApart()
    {
        // Two obviously-different "pages" — a tight checker vs mid-grey.
        var checker = Checkerboard(2);
        var solid = SolidGrey(127);
        var hc = ImagePerceptualHash.Compute(checker, W, H);
        var hs = ImagePerceptualHash.Compute(solid, W, H);
        var dist = ImagePerceptualHash.HammingDistance(hc, hs);
        Assert.True(dist >= 16, $"checker vs solid yielded Hamming {dist}, expected ≥ 16");
    }

    [Fact]
    public void RejectsInputLengthMismatch()
    {
        byte[] tooShort = new byte[W * H - 1];
        Assert.Throws<ArgumentException>(() =>
            ImagePerceptualHash.Compute(tooShort, W, H));
    }

    [Fact]
    public void RejectsNonPositiveDimensions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ImagePerceptualHash.Compute(new byte[0], 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ImagePerceptualHash.Compute(new byte[0], 1, 0));
    }

    [Fact]
    public void HammingDistanceIsPopCountOfXor()
    {
        // Sanity: 0 vs ulong.MaxValue = 64 (all bits differ).
        Assert.Equal(0, ImagePerceptualHash.HammingDistance(0UL, 0UL));
        Assert.Equal(64, ImagePerceptualHash.HammingDistance(0UL, ulong.MaxValue));
        Assert.Equal(1, ImagePerceptualHash.HammingDistance(0UL, 1UL));
        Assert.Equal(2, ImagePerceptualHash.HammingDistance(0b1010UL, 0UL));
    }
}
