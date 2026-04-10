// =============================================================================
// Cena Platform -- Knowledge/Content REST Endpoints (STB-08 Phase 1)
// Concepts and learning path endpoints (stub data)
// =============================================================================

using System.Security.Claims;
using Cena.Api.Contracts.Content;
using Cena.Infrastructure.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cena.Api.Host.Endpoints;

public static class KnowledgeEndpoints
{
    // Hard-coded list of ~12 math concepts (Algebra I basics)
    private static readonly ConceptSummary[] StubConcepts =
    {
        new("concept_linear_eq", "Linear Equations", "Mathematics", "Algebra", "beginner", "mastered"),
        new("concept_inequalities", "Inequalities", "Mathematics", "Algebra", "beginner", "mastered"),
        new("concept_systems_linear", "Systems of Linear Equations", "Mathematics", "Algebra", "intermediate", "in-progress"),
        new("concept_quadratic_eq", "Quadratic Equations", "Mathematics", "Algebra", "intermediate", "available"),
        new("concept_factoring", "Factoring Polynomials", "Mathematics", "Algebra", "intermediate", "available"),
        new("concept_exp_rules", "Exponent Rules", "Mathematics", "Algebra", "beginner", "mastered"),
        new("concept_radicals", "Radicals and Rational Exponents", "Mathematics", "Algebra", "intermediate", "locked"),
        new("concept_functions", "Introduction to Functions", "Mathematics", "Algebra", "intermediate", "locked"),
        new("concept_quad_functions", "Quadratic Functions", "Mathematics", "Algebra", "advanced", "locked"),
        new("concept_polynomials", "Polynomial Operations", "Mathematics", "Algebra", "intermediate", "locked"),
        new("concept_rational", "Rational Expressions", "Mathematics", "Algebra", "advanced", "locked"),
        new("concept_seq_series", "Sequences and Series", "Mathematics", "Algebra", "advanced", "locked")
    };

    private static readonly Dictionary<string, ConceptDetailDto> ConceptDetails = new()
    {
        ["concept_linear_eq"] = new ConceptDetailDto(
            "concept_linear_eq", "Linear Equations", 
            "Learn to solve equations of the form ax + b = c. Master isolating variables and checking solutions.",
            "Mathematics", "Algebra", "beginner", "mastered", 0.95,
            Array.Empty<string>(),
            new[] { "concept_inequalities", "concept_systems_linear" },
            30, 25),
        ["concept_inequalities"] = new ConceptDetailDto(
            "concept_inequalities", "Inequalities",
            "Solve and graph linear inequalities. Understand compound inequalities and interval notation.",
            "Mathematics", "Algebra", "beginner", "mastered", 0.88,
            new[] { "concept_linear_eq" },
            new[] { "concept_systems_linear" },
            35, 30),
        ["concept_systems_linear"] = new ConceptDetailDto(
            "concept_systems_linear", "Systems of Linear Equations",
            "Solve systems using substitution and elimination methods. Graph systems and interpret solutions.",
            "Mathematics", "Algebra", "intermediate", "in-progress", 0.45,
            new[] { "concept_linear_eq", "concept_inequalities" },
            new[] { "concept_functions" },
            45, 40),
        ["concept_quadratic_eq"] = new ConceptDetailDto(
            "concept_quadratic_eq", "Quadratic Equations",
            "Solve quadratics by factoring, completing the square, and using the quadratic formula.",
            "Mathematics", "Algebra", "intermediate", "available", null,
            new[] { "concept_factoring" },
            new[] { "concept_quad_functions" },
            50, 45),
        ["concept_factoring"] = new ConceptDetailDto(
            "concept_factoring", "Factoring Polynomials",
            "Factor trinomials, difference of squares, and perfect square trinomials.",
            "Mathematics", "Algebra", "intermediate", "available", null,
            new[] { "concept_exp_rules" },
            new[] { "concept_quadratic_eq", "concept_polynomials" },
            40, 35),
        ["concept_exp_rules"] = new ConceptDetailDto(
            "concept_exp_rules", "Exponent Rules",
            "Master product, quotient, and power rules for exponents. Work with negative and zero exponents.",
            "Mathematics", "Algebra", "beginner", "mastered", 0.92,
            Array.Empty<string>(),
            new[] { "concept_factoring", "concept_radicals" },
            25, 20),
        ["concept_radicals"] = new ConceptDetailDto(
            "concept_radicals", "Radicals and Rational Exponents",
            "Simplify radical expressions and convert between radical and rational exponent forms.",
            "Mathematics", "Algebra", "intermediate", "locked", null,
            new[] { "concept_exp_rules" },
            new[] { "concept_quad_functions" },
            35, 30),
        ["concept_functions"] = new ConceptDetailDto(
            "concept_functions", "Introduction to Functions",
            "Understand function notation, domain, range, and evaluate functions.",
            "Mathematics", "Algebra", "intermediate", "locked", null,
            new[] { "concept_systems_linear" },
            new[] { "concept_quad_functions" },
            40, 35),
        ["concept_quad_functions"] = new ConceptDetailDto(
            "concept_quad_functions", "Quadratic Functions",
            "Graph parabolas, find vertex and axis of symmetry, and solve real-world problems.",
            "Mathematics", "Algebra", "advanced", "locked", null,
            new[] { "concept_quadratic_eq", "concept_functions", "concept_radicals" },
            new[] { "concept_rational" },
            55, 50),
        ["concept_polynomials"] = new ConceptDetailDto(
            "concept_polynomials", "Polynomial Operations",
            "Add, subtract, multiply, and divide polynomials.",
            "Mathematics", "Algebra", "intermediate", "locked", null,
            new[] { "concept_factoring" },
            Array.Empty<string>(),
            35, 30),
        ["concept_rational"] = new ConceptDetailDto(
            "concept_rational", "Rational Expressions",
            "Simplify, multiply, divide, add, and subtract rational expressions.",
            "Mathematics", "Algebra", "advanced", "locked", null,
            new[] { "concept_quad_functions", "concept_polynomials" },
            new[] { "concept_seq_series" },
            50, 45),
        ["concept_seq_series"] = new ConceptDetailDto(
            "concept_seq_series", "Sequences and Series",
            "Work with arithmetic and geometric sequences and series.",
            "Mathematics", "Algebra", "advanced", "locked", null,
            new[] { "concept_functions", "concept_rational" },
            Array.Empty<string>(),
            45, 40)
    };

    public static IEndpointRouteBuilder MapKnowledgeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("")
            .RequireAuthorization();

        // Content/Concepts endpoints
        group.MapGet("/api/content/concepts", GetConcepts).WithName("GetConcepts").WithTags("Content");
        group.MapGet("/api/content/concepts/{id}", GetConceptDetail).WithName("GetConceptDetail").WithTags("Content");

        // Knowledge path endpoint
        group.MapGet("/api/knowledge/path", GetKnowledgePath).WithName("GetKnowledgePath").WithTags("Knowledge");

        return app;
    }

    // GET /api/content/concepts — returns list of concepts
    private static IResult GetConcepts(HttpContext ctx)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        // Phase 1: Return hardcoded concept list
        var dto = new ConceptListDto(Items: StubConcepts);
        return Results.Ok(dto);
    }

    // GET /api/content/concepts/{id} — returns concept detail
    private static IResult GetConceptDetail(HttpContext ctx, string id)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        // Phase 1: Return hardcoded concept detail or 404
        if (!ConceptDetails.TryGetValue(id, out var detail))
        {
            return Results.NotFound(new { Error = "Concept not found" });
        }

        return Results.Ok(detail);
    }

    // GET /api/knowledge/path?from={conceptA}&to={conceptB} — returns learning path
    private static IResult GetKnowledgePath(
        HttpContext ctx,
        string? from = null,
        string? to = null)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        // Phase 1: Validate input and return stub path
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
        {
            return Results.BadRequest(new { Error = "Both 'from' and 'to' concept IDs are required" });
        }

        // Return a sample path from linear equations to quadratic functions
        var nodes = new[]
        {
            new PathNode("concept_linear_eq", "Linear Equations", 1, "mastered"),
            new PathNode("concept_inequalities", "Inequalities", 2, "mastered"),
            new PathNode("concept_systems_linear", "Systems of Linear Equations", 3, "in-progress"),
            new PathNode("concept_exp_rules", "Exponent Rules", 4, "mastered"),
            new PathNode("concept_factoring", "Factoring Polynomials", 5, "available"),
            new PathNode("concept_quadratic_eq", "Quadratic Equations", 6, "available"),
            new PathNode("concept_functions", "Introduction to Functions", 7, "locked"),
            new PathNode("concept_quad_functions", "Quadratic Functions", 8, "locked")
        };

        var edges = new[]
        {
            new PathEdge("concept_linear_eq", "concept_inequalities", "dependency"),
            new PathEdge("concept_linear_eq", "concept_systems_linear", "dependency"),
            new PathEdge("concept_inequalities", "concept_systems_linear", "dependency"),
            new PathEdge("concept_systems_linear", "concept_functions", "dependency"),
            new PathEdge("concept_exp_rules", "concept_factoring", "dependency"),
            new PathEdge("concept_factoring", "concept_quadratic_eq", "prerequisite"),
            new PathEdge("concept_quadratic_eq", "concept_quad_functions", "prerequisite"),
            new PathEdge("concept_functions", "concept_quad_functions", "prerequisite")
        };

        var dto = new PathDto(
            FromConceptId: from,
            ToConceptId: to,
            Nodes: nodes,
            Edges: edges,
            TotalSteps: nodes.Length,
            EstimatedMinutes: nodes.Sum(n => 40)); // ~40 min per concept

        return Results.Ok(dto);
    }

    private static string? GetStudentId(ClaimsPrincipal user)
    {
        return user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value;
    }
}
