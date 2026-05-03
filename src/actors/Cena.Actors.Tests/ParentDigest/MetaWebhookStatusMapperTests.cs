// =============================================================================
// Cena Platform — MetaWebhookStatusMapper tests (PRR-437)
//
// Every row in the PRR-437 §3 status-to-outcome table must round-trip
// through here. Drift in the mapper → drift in the admin ops queue
// grouping → lost debuggability on the hottest incident surface
// (failed parent notifications). Lock the table.
// =============================================================================

using Cena.Actors.ParentDigest;
using Xunit;

namespace Cena.Actors.Tests.ParentDigest;

public class MetaWebhookStatusMapperTests
{
    [Fact]
    public void Sent_maps_to_Breadcrumb()
    {
        var d = MetaWebhookStatusMapper.Map("sent", null);
        Assert.Equal(MetaWebhookActionKind.Breadcrumb, d.Action);
        Assert.Null(d.ReasonCode);
    }

    [Fact]
    public void Delivered_maps_to_Delivered_action()
    {
        var d = MetaWebhookStatusMapper.Map("delivered", null);
        Assert.Equal(MetaWebhookActionKind.Delivered, d.Action);
    }

    [Fact]
    public void Read_maps_to_Read_action()
    {
        var d = MetaWebhookStatusMapper.Map("read", null);
        Assert.Equal(MetaWebhookActionKind.Read, d.Action);
    }

    [Fact]
    public void Failed_with_code_131047_maps_to_re_engagement_window_expired()
    {
        var d = MetaWebhookStatusMapper.Map("failed", 131047);
        Assert.Equal(MetaWebhookActionKind.DeadLetter, d.Action);
        Assert.Equal("re_engagement_window_expired", d.ReasonCode);
        Assert.Equal(131047, d.MetaCode);
    }

    [Fact]
    public void Failed_with_code_131050_maps_to_SenderQualityRed()
    {
        var d = MetaWebhookStatusMapper.Map("failed", 131050);
        Assert.Equal(MetaWebhookActionKind.SenderQualityRed, d.Action);
        Assert.Equal("sender_quality_red", d.ReasonCode);
    }

    [Theory]
    [InlineData(132000)]
    [InlineData(132001)]
    [InlineData(132500)]
    [InlineData(132999)]
    public void Failed_with_code_in_template_range_maps_to_TemplateFailure(int code)
    {
        var d = MetaWebhookStatusMapper.Map("failed", code);
        Assert.Equal(MetaWebhookActionKind.TemplateFailure, d.Action);
        Assert.Equal("template_not_approved", d.ReasonCode);
        Assert.Equal(code, d.MetaCode);
    }

    [Theory]
    [InlineData(131999)]
    [InlineData(133000)]
    [InlineData(400)]
    public void Failed_with_code_outside_named_ranges_maps_to_generic_DeadLetter(int code)
    {
        var d = MetaWebhookStatusMapper.Map("failed", code);
        Assert.Equal(MetaWebhookActionKind.DeadLetter, d.Action);
        Assert.Equal($"meta-code:{code}", d.ReasonCode);
    }

    [Fact]
    public void Failed_without_code_maps_to_generic_DeadLetter()
    {
        var d = MetaWebhookStatusMapper.Map("failed", null);
        Assert.Equal(MetaWebhookActionKind.DeadLetter, d.Action);
        Assert.Equal("meta_failed_no_code", d.ReasonCode);
    }

    [Fact]
    public void Unknown_status_string_maps_to_Unknown()
    {
        var d = MetaWebhookStatusMapper.Map("tomorrow_was_yesterday", null);
        Assert.Equal(MetaWebhookActionKind.Unknown, d.Action);
    }

    [Fact]
    public void Null_status_maps_to_Unknown()
    {
        Assert.Equal(
            MetaWebhookActionKind.Unknown,
            MetaWebhookStatusMapper.Map(null, null).Action);
        Assert.Equal(
            MetaWebhookActionKind.Unknown,
            MetaWebhookStatusMapper.Map("   ", null).Action);
    }

    [Fact]
    public void Status_match_is_case_insensitive_and_trims()
    {
        // Meta emits lowercase today but the mapper shouldn't depend
        // on that; a proxy / log-replay tool may normalise case.
        Assert.Equal(MetaWebhookActionKind.Delivered,
            MetaWebhookStatusMapper.Map("DELIVERED", null).Action);
        Assert.Equal(MetaWebhookActionKind.Read,
            MetaWebhookStatusMapper.Map("  Read  ", null).Action);
    }
}
