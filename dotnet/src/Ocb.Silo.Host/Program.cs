using Ocb.Grains.Channels;
using Orleans.Configuration;

var builder = WebApplication.CreateBuilder(args);

// ── Profile Selection ──────────────────────────────────────────
// "singlebox" — local development, in-memory clustering
// "cluster"  — production, PostgreSQL ADO.NET clustering (requires secrets)
// "test"     — CI, in-memory with ephemeral ports
// Resolved from config (allows WebApplicationFactory override) with env var fallback.
// ASP.NET Core's default builder adds environment variables to configuration,
// so `builder.Configuration["OCB_PROFILE"]` reads both config and env vars.
var profile = builder.Configuration["OCB_PROFILE"] ?? "singlebox";

builder.Host.UseOrleans(silo =>
{
    silo.Configure<ClusterOptions>(options =>
    {
        options.ClusterId = "avernet";
        options.ServiceId = "ocb";
    });

    switch (profile)
    {
        case "singlebox":
        case "test":
            silo.UseLocalhostClustering();
            silo.AddMemoryGrainStorage("orleans-storage");
            silo.AddMemoryGrainStorage("PubSubStore");
            silo.UseInMemoryReminderService();
            break;

        case "cluster":
            // PostgreSQL clustering — connection string injected via config
            // or resolved from ISecretResolver in production.
            // The connection string key is read from configuration;
            // secret resolution is delegated to production startup hooks.
            var pgConnStr = builder.Configuration.GetConnectionString("OrleansCluster")
                ?? builder.Configuration["Orleans:ClusterConnectionString"];
            if (string.IsNullOrWhiteSpace(pgConnStr))
                throw new InvalidOperationException(
                    "Orleans cluster connection string not configured.");

            silo.UseAdoNetClustering(options =>
            {
                options.Invariant = "Npgsql";
                options.ConnectionString = pgConnStr;
            });

            silo.AddAdoNetGrainStorage("orleans-storage", options =>
            {
                options.Invariant = "Npgsql";
                options.ConnectionString = pgConnStr;
            });

            // TODO: Orleans 10.x removed UseAdoNetReminderService.
            // Use AdoNet-backed reminder storage via AddAdoNetGrainStorage("Reminders", ...)
            // or configure the reminder table through the clustering provider.
            // For now, fall back to in-memory reminders in cluster mode.
            silo.UseInMemoryReminderService();
            break;
    }

    // Stream Provider — in-process SMS for in-cluster gRPC push
    silo.AddMemoryStreams("sms");

    // Endpoints (overridable via config)
    silo.ConfigureEndpoints(
        siloPort: int.TryParse(builder.Configuration["Orleans:SiloPort"], out var sp) ? sp : 11111,
        gatewayPort: int.TryParse(builder.Configuration["Orleans:GatewayPort"], out var gp) ? gp : 30000);

    // Incoming call filter — tenant-key validation
    silo.AddIncomingGrainCallFilter<TenantKeyGuardCallFilter>();
});

var app = builder.Build();
app.MapGet("/health/ready", () => Results.Ok(new { status = "ready", profile }));
app.Run();
