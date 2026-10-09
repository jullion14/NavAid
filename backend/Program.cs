using NavAid.Api.Services;
using NavAid.Api.Models;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
var frontendOrigin = "http://localhost:5173"; // Vite's default dev port
builder.Services.AddOpenApi();
builder.Services.AddScoped<SpatialAnalysisService>();
builder.Services.AddScoped<IPriorityScoringService, PriorityScoringService>();
builder.Services.AddScoped<ISensitivityService, SensitivityService>();
builder.Services.AddScoped<IExplanationPayloadBuilder, ExplanationPayloadBuilder>();

builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(
        new NetTopologySuite.IO.Converters.GeoJsonConverterFactory());
    options.JsonSerializerOptions.Converters.Add(
        new System.Text.Json.Serialization.JsonStringEnumConverter());
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendDev", policy =>
    {
        policy.WithOrigins(frontendOrigin)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});
builder.Services.AddDbContext<NavAidDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("Default"),
        o => o.UseNetTopologySuite()    
    )
);

builder.Services.Configure<GeminiOptions>(
    builder.Configuration.GetSection(GeminiOptions.SectionName));

builder.Services.AddHttpClient<IAIExplanationService, AIExplanationService>();

var app = builder.Build();
app.UseCors("FrontendDev");

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast =  Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");

// Debug endpoints for development and testing. Not intended for production use.
// =============================================================================

app.MapGet("/api/_debug/payload/{id:int}", async (
    int id,
    IExplanationPayloadBuilder builder,
    CancellationToken ct) =>
{
    var payload = await builder.BuildAsync(ExplanationMode.Area, id, null, null, ct);
    return payload is null ? Results.NotFound() : Results.Ok(payload);
});

app.MapGet("/api/_debug/explain/{id:int}", async (
    int id,
    IExplanationPayloadBuilder builder,
    CancellationToken ct) =>
{
    var payload = await builder.BuildAsync(ExplanationMode.Area, id, null, null, ct);
    if (payload is null) return Results.NotFound();

    var result = TemplateExplanationWriter.Write(payload);

    var prose = string.Join("\n\n", result.Sections.Select(s => $"{s.Heading}\n{s.Body}"));
    var verifiable = string.Join("\n\n",
        result.Sections.Where(s => !s.IsVerbatim).Select(s => s.Body));

    var verification = ExplanationVerifier.Verify(payload, verifiable);

    return Results.Ok(new
    {
        prose,
        verification,
        sections = result.Sections
    });
});

app.MapGet("/api/_debug/prompt/{id:int}", async (
    int id,
    IExplanationPayloadBuilder builder,
    CancellationToken ct) =>
{
    var payload = await builder.BuildAsync(ExplanationMode.Area, id, null, null, ct);
    if (payload is null) return Results.NotFound();

    return Results.Ok(new
    {
        factsInLedger = payload.Facts.Count,
        factsInPrompt = PromptBuilder.FilterFacts(payload).Count,
        systemInstruction = PromptBuilder.SystemInstruction(payload),
        userMessage = PromptBuilder.UserMessage(payload)
    });
});

app.MapGet("/api/_debug/compare/{a:int}/{b:int}", async (
    int a, int b, IExplanationPayloadBuilder builder, CancellationToken ct) =>
{
    var payload = await builder.BuildAsync(ExplanationMode.Compare, a, b, null, ct);
    return payload is null ? Results.NotFound() : Results.Ok(payload);
});

//app.MapGet("/api/_debug/ai/{id:int}", async (
//    int id,
//    IExplanationPayloadBuilder builder,
//    IAIExplanationService ai,
//    CancellationToken ct) =>
//{
//    var payload = await builder.BuildForAreaAsync(id, null, ct);
//    if (payload is null) return Results.NotFound();

//    var result = await ai.ExplainAsync(payload, ct);
//    return Results.Ok(result);
//});
// =============================================================================

app.MapControllers();

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
