using Microsoft.EntityFrameworkCore;
using MetaHub.Api.Endpoints;
using MetaHub.Infrastructure;
using MetaHub.Ingest;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("MetaHub")
    ?? Environment.GetEnvironmentVariable("METAHUB_CONNECTION")
    ?? "Host=localhost;Port=5432;Database=metahub;Username=metahub;Password=metahub";

builder.Services.AddMetaHubInfrastructure(connectionString);
builder.Services.AddAnimeIngest();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Apply EF migrations on startup unless explicitly disabled (handy for the docker-compose setup).
if (app.Configuration.GetValue("MetaHub:AutoMigrate", true))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MetaHubDbContext>();
    db.Database.Migrate();
}

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapMetaHubEndpoints();

app.Run();

/// <summary>Exposed so integration tests can reference the API host.</summary>
public partial class Program;
