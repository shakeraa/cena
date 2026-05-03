// =============================================================================
// Cena Platform — Image Upload Feature Flag Filter (RDY-001)
// Returns 503 Service Unavailable when CENA_IMAGE_UPLOAD_ENABLED is false.
// =============================================================================

namespace Cena.Student.Api.Host.Endpoints;

public static class ImageUploadFeatureFilter
{
    public static TBuilder RequireImageUploadEnabled<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var config = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
            if (!config.GetValue<bool>("CENA_IMAGE_UPLOAD_ENABLED", false))
            {
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
            return await next(context);
        });
    }
}
