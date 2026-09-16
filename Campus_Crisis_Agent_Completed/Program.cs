using CampusCrisisAgent.Services;
using CampusCrisisAgent.Models;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/campus-crisis-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

try
{
    Log.Information("Starting Campus Crisis Agent");

    builder.Host.UseSerilog();

    builder.Services.AddControllersWithViews();

    // Register configuration options
    builder.Services.Configure<CorrelationSettings>(
        builder.Configuration.GetSection(CorrelationSettings.SectionName));
    builder.Services.Configure<DecisionSettings>(
        builder.Configuration.GetSection(DecisionSettings.SectionName));

    builder.Services.AddSingleton<IncidentStateService>();
    builder.Services.AddSingleton<CorrelationService>();
    builder.Services.AddSingleton<AssessmentService>();
    builder.Services.AddSingleton<DecisionService>();
    builder.Services.AddSingleton<ReportProcessor>();

// ---------------------------------------------------------------------------
// GROQ PLUG-IN POINT
// GroqLlmClient is implemented and will be used if GROQ_API_KEY is configured
// Set GROQ_API_KEY in appsettings.json or as an environment variable
// ---------------------------------------------------------------------------
builder.Services.AddSingleton<ILlmClient, GroqLlmClient>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Dashboard/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Dashboard}/{action=Index}/{id?}");

Log.Information("Campus Crisis Agent started successfully");
app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
