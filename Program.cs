using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Waha40kMcp.Data;
using Waha40kMcp.Tools;

// ── Modus bestimmen ──────────────────────────────────────────────────────
// --http            → läuft als HTTP-Server (für Remote-Zugriff, z.B. vom Handy)
// (kein Flag)        → läuft als stdio-Prozess (für lokales Claude Desktop)
bool httpMode = args.Contains("--http");

// stdout SOFORT sperren — bevor irgendwas auf stdout schreibt (nur relevant für stdio-Modus,
// schadet im HTTP-Modus aber nicht)
var mcpStdout = Console.Out;
Console.SetOut(Console.Error);

WahapediaRepository repo;
try
{
    // Playwright Chromium installieren falls nicht vorhanden
    Microsoft.Playwright.Program.Main(["install", "chromium"]);

    // Wahapedia Daten laden
    repo = new WahapediaRepository();
    await repo.InitializeAsync();
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[FEHLER] Start fehlgeschlagen: {ex.Message}");
    Environment.Exit(1);
    return;
}

// stdout für MCP freigeben (stdio-Modus)
Console.SetOut(mcpStdout);

if (httpMode)
{
    // ── HTTP-Modus: für Remote-Zugriff (z.B. übers Handy, via NAS-Reverse-Proxy) ──
    var webBuilder = WebApplication.CreateBuilder(args);
    webBuilder.Logging.ClearProviders();
    webBuilder.Logging.AddConsole(); // im HTTP-Modus dürfen wir normal loggen

    // Auth-Token aus Umgebungsvariable lesen.
    // WICHTIG: setze WAHA40K_TOKEN als Umgebungsvariable auf deinem Server,
    // sonst startet der Server nicht im HTTP-Modus (Schutz gegen versehentlich offenen Zugriff).
    var authToken = Environment.GetEnvironmentVariable("WAHA40K_TOKEN");
    if (string.IsNullOrWhiteSpace(authToken))
    {
        Console.Error.WriteLine(
            "[FEHLER] WAHA40K_TOKEN ist nicht gesetzt. " +
            "Setze die Umgebungsvariable WAHA40K_TOKEN auf ein langes, zufälliges Geheimnis " +
            "bevor du den Server im --http Modus startest.");
        Environment.Exit(1);
    }

    var port = Environment.GetEnvironmentVariable("WAHA40K_PORT") ?? "5005";
    webBuilder.WebHost.UseUrls($"http://0.0.0.0:{port}");

    webBuilder.Services.AddSingleton(repo);
    webBuilder.Services.AddSingleton<MfmScraper>();
    webBuilder.Services.AddSingleton<IMfmScraper>(sp => sp.GetRequiredService<MfmScraper>());
    webBuilder.Services.AddSingleton<StrategyRepository>();

    webBuilder.Services
        .AddMcpServer()
        .WithHttpTransport()
        .WithTools<Waha40kTools>()
        .WithTools<CombatCalculator>()
        .WithTools<ArmyBuilderTools>()
        .WithTools<StrategyTools>();

    var app = webBuilder.Build();

    // Bearer-Token-Auth-Middleware: jede Anfrage muss "Authorization: Bearer <token>" enthalten.
    var expectedAuthBytes = Encoding.UTF8.GetBytes($"Bearer {authToken}");
    app.Use(async (context, next) =>
    {
        var headerBytes = Encoding.UTF8.GetBytes(context.Request.Headers.Authorization.ToString());

        // Konstante Laufzeit gegen Timing-Angriffe (CryptographicOperations.FixedTimeEquals
        // statt string ==, das früh abbricht sobald ein Zeichen abweicht).
        if (headerBytes.Length != expectedAuthBytes.Length ||
            !CryptographicOperations.FixedTimeEquals(headerBytes, expectedAuthBytes))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Unauthorized");
            return;
        }

        await next();
    });

    app.MapMcp();

    Console.Error.WriteLine($"[Waha40k] HTTP-Server läuft auf Port {port}. " +
                             "Stelle sicher, dass nur HTTPS (via Reverse Proxy) von außen erreichbar ist!");

    await app.RunAsync();
}
else
{
    // ── stdio-Modus: für lokales Claude Desktop (Standardverhalten, unverändert) ──
    var builder = Host.CreateApplicationBuilder(args);
    builder.Logging.ClearProviders();

    builder.Services.AddSingleton(repo);
    builder.Services.AddSingleton<MfmScraper>();
    builder.Services.AddSingleton<IMfmScraper>(sp => sp.GetRequiredService<MfmScraper>());
    builder.Services.AddSingleton<StrategyRepository>();

    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithTools<Waha40kTools>()
        .WithTools<CombatCalculator>()
        .WithTools<ArmyBuilderTools>()
        .WithTools<StrategyTools>();

    await builder.Build().RunAsync();
}
