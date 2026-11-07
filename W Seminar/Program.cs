using System.Text.Json;
using PokerSimPlatform.Core.Simulation;
using PokerSimPlatform.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();

// PokerSim-spezifische Services
builder.Services.Configure<PokerSimOptions>(builder.Configuration.GetSection(PokerSimOptions.SectionName));
var options = builder.Configuration.GetSection(PokerSimOptions.SectionName).Get<PokerSimOptions>() ?? new PokerSimOptions();
var dataPath = Path.Combine(builder.Environment.ContentRootPath, options.DataDirectory, options.ResultFileName);
var scenarioDataPath = Path.Combine(builder.Environment.ContentRootPath, options.DataDirectory, "scenario_results.json");

builder.Services.AddSingleton(sp => new ResultStore(dataPath, sp.GetRequiredService<ILogger<ResultStore>>()));
builder.Services.AddSingleton(new ScenarioResultStore(scenarioDataPath));
builder.Services.AddSingleton<EquityCache>();
builder.Services.AddSingleton<SimulationJobManager>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SimulationJobManager>());
builder.Services.AddHostedService<SeedHostedService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();
app.MapRazorPages();

// Fortschritt per Server-Sent Events (SSE) – ersetzt blockierendes OnPost.
app.MapGet("/api/simulations/{id}/events", async (string id, SimulationJobManager jobs, HttpContext ctx, CancellationToken ct) =>
{
    ctx.Response.Headers.ContentType = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-cache";
    var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    try
    {
        while (!ct.IsCancellationRequested)
        {
            var job = jobs.Get(id);
            if (job == null)
            {
                await ctx.Response.WriteAsync("event: error\ndata: {\"error\":\"Job nicht gefunden\"}\n\n", ct);
                break;
            }

            var snapshot = job.ToSnapshot();
            await ctx.Response.WriteAsync($"data: {JsonSerializer.Serialize(snapshot, json)}\n\n", ct);
            await ctx.Response.Body.FlushAsync(ct);

            if (snapshot.IsFinished) break;
            await Task.Delay(300, ct);
        }
    }
    catch (OperationCanceledException)
    {
        // Client hat die Verbindung geschlossen.
    }
});

app.MapPost("/api/simulations/{id}/cancel", (string id, SimulationJobManager jobs)
    => jobs.Cancel(id) ? Results.Ok() : Results.NotFound());

app.Run();
