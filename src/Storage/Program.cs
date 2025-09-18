using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Altinn.Common.AccessToken;
using Altinn.Common.AccessToken.Configuration;
using Altinn.Common.AccessToken.Services;
using Altinn.Common.AccessTokenClient.Services;
using Altinn.Common.PEP.Authorization;
using Altinn.Common.PEP.Clients;
using Altinn.Common.PEP.Configuration;
using Altinn.Common.PEP.Implementation;
using Altinn.Common.PEP.Interfaces;
using Altinn.Platform.Storage.Authorization;
using Altinn.Platform.Storage.Clients;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Filters;
using Altinn.Platform.Storage.Health;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Messages;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Services;
using Altinn.Platform.Storage.Telemetry;
using Altinn.Platform.Storage.Wrappers;
using AltinnCore.Authentication.JwtCookie;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.Exporter;
using Azure.Security.KeyVault.Secrets;
using JasperFx.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Npgsql;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Wolverine;
using Wolverine.AzureServiceBus;
using Wolverine.Postgresql;
using Yuniql.AspNetCore;
using Yuniql.PostgreSql;

ILogger logger;

string vaultApplicationInsightsKey = "ApplicationInsights--InstrumentationKey";

string applicationInsightsConnectionString = string.Empty;

var builder = WebApplication.CreateBuilder(args);

ConfigureWebHostCreationLogging();

await SetConfigurationProviders(builder.Configuration, builder.Environment.IsDevelopment());

ConfigureApplicationLogging(builder.Logging);

ConfigureServices(builder.Services, builder.Configuration);

ConfigureWolverine(builder.Services, builder.Configuration);

logger.LogInformation("// Checking Azure Storage connection.");

var app = builder.Build();

Configure(builder.Configuration);

logger.LogInformation("// Running...");
app.Run();

void ConfigureApplicationLogging(ILoggingBuilder logging)
{
    logging.AddOpenTelemetry(builder =>
    {
        builder.IncludeFormattedMessage = true;
        builder.IncludeScopes = true;
    });
}

void ConfigureWebHostCreationLogging()
{
    var logFactory = LoggerFactory.Create(builder =>
    {
        builder
            .AddFilter("Altinn.Platform.Storage.Program", LogLevel.Debug)
            .AddConsole();
    });

    logger = logFactory.CreateLogger<Program>();
}

async Task SetConfigurationProviders(ConfigurationManager config, bool isDevelopment)
{
    string basePath = Directory.GetParent(Directory.GetCurrentDirectory()).FullName;

    config.SetBasePath(basePath);
    config.AddJsonFile(basePath + @"altinn-appsettings/altinn-dbsettings-secret.json", true, true);

    if (basePath == "/")
    {
        // In a pod/container where the app is located in an app folder on the root of the filesystem.
        string filePath = basePath + @"app/appsettings.json";
        config.AddJsonFile(filePath, false, true);
    }
    else
    {
        // Running on development machine.
        string filePath = Directory.GetCurrentDirectory() + @"/appsettings.json";
        config.AddJsonFile(filePath, false, true);
    }

    config.AddEnvironmentVariables();

    if (isDevelopment)
    {
        config.AddUserSecrets(Assembly.GetExecutingAssembly(), true);
    }

    await ConnectToKeyVaultAndSetApplicationInsights(config);

    config.AddCommandLine(args);
}

async Task ConnectToKeyVaultAndSetApplicationInsights(ConfigurationManager config)
{
    KeyVaultSettings keyVaultSettings = new();
    config.GetSection("kvSetting").Bind(keyVaultSettings);
    if (!string.IsNullOrEmpty(keyVaultSettings.SecretUri))
    {
        logger.LogInformation("Program // Set app insights connection string // App");

        DefaultAzureCredential azureCredentials = new();

        SecretClient client = new(new Uri(keyVaultSettings.SecretUri), azureCredentials);

        config.AddAzureKeyVault(new Uri(keyVaultSettings.SecretUri), azureCredentials);

        try
        {
            KeyVaultSecret keyVaultSecret = await client.GetSecretAsync(vaultApplicationInsightsKey);
            applicationInsightsConnectionString = string.Format("InstrumentationKey={0}", keyVaultSecret.Value);
        }
        catch (Exception vaultException)
        {
            logger.LogError(vaultException, $"Unable to read application insights key.");
        }
    }
}

void AddAzureMonitorTelemetryExporters(IServiceCollection services, string applicationInsightsConnectionString)
{
    services.Configure<OpenTelemetryLoggerOptions>(logging => logging.AddAzureMonitorLogExporter(o =>
    {
        o.ConnectionString = applicationInsightsConnectionString;
    }));
    services.ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddAzureMonitorMetricExporter(o =>
    {
        o.ConnectionString = applicationInsightsConnectionString;
    }));
    services.ConfigureOpenTelemetryTracerProvider(tracing => tracing
    .AddAzureMonitorTraceExporter(o =>
    {
        o.ConnectionString = applicationInsightsConnectionString;
    }));
}

void ConfigureServices(IServiceCollection services, IConfiguration config)
{
    logger.LogInformation("Program // ConfigureServices");
    GeneralSettings generalSettings = config.GetSection("GeneralSettings").Get<GeneralSettings>();

    var attributes = new List<KeyValuePair<string, object>>(2)
    {
        KeyValuePair.Create("service.name", (object)"platform-storage"),
    };

    services.AddOpenTelemetry()
        .ConfigureResource(resourceBuilder => resourceBuilder.AddAttributes(attributes))
        .WithMetrics(metrics =>
        {
            metrics.AddAspNetCoreInstrumentation();
            metrics.AddMeter(
                "Microsoft.AspNetCore.Hosting",
                "Microsoft.AspNetCore.Server.Kestrel",
                "System.Net.Http",
                Metrics.Meter.Name);
        })
        .WithTracing(tracing =>
        {
            if (builder.Environment.IsDevelopment())
            {
                tracing.SetSampler(new AlwaysOnSampler());
            }

            tracing.AddAspNetCoreInstrumentation();

            tracing.AddHttpClientInstrumentation();

            tracing.AddNpgsql();

            tracing.AddProcessor(new RequestFilterProcessor(generalSettings, new HttpContextAccessor()));
        });

    if (!string.IsNullOrEmpty(applicationInsightsConnectionString))
    {
        AddAzureMonitorTelemetryExporters(services, applicationInsightsConnectionString);
    }

    services.AddControllersWithViews().AddNewtonsoftJson();
    services.AddMemoryCache();
    services.AddHealthChecks().AddCheck<HealthCheck>("storage_health_check");

    services.AddHttpClient<AuthorizationApiClient>();
    services.AddHttpClient<IRegisterService, RegisterService>();

    services.AddAspNetCoreMetricsEnricher();

    services.Configure<AzureStorageConfiguration>(config.GetSection("AzureStorageConfiguration"));
    services.Configure<GeneralSettings>(config.GetSection("GeneralSettings"));
    services.Configure<KeyVaultSettings>(config.GetSection("kvSetting"));
    services.Configure<PepSettings>(config.GetSection("PepSettings"));
    services.Configure<PlatformSettings>(config.GetSection("PlatformSettings"));
    services.Configure<RegisterServiceSettings>(config.GetSection("RegisterServiceSettings"));
    services.Configure<QueueStorageSettings>(config.GetSection("QueueStorageSettings"));
    services.Configure<AccessTokenSettings>(config.GetSection("AccessTokenSettings"));
    services.Configure<PostgreSqlSettings>(config.GetSection("PostgreSqlSettings"));
    services.Configure<WolverineSettings>(config.GetSection("WolverineSettings"));

    services.AddSingleton<IAuthorizationHandler, AccessTokenHandler>();
    services.AddSingleton<IPublicSigningKeyProvider, PublicSigningKeyProvider>();

    services.AddAuthentication(JwtCookieDefaults.AuthenticationScheme)
        .AddJwtCookie(JwtCookieDefaults.AuthenticationScheme, options =>
        {
            options.JwtCookieName = generalSettings.RuntimeCookieName;
            options.MetadataAddress = generalSettings.OpenIdWellKnownEndpoint;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                ValidateIssuer = false,
                ValidateAudience = false,
                RequireExpirationTime = true,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };

            if (builder.Environment.IsDevelopment())
            {
                options.RequireHttpsMetadata = false;
            }
        });

    services.AddAuthorizationBuilder()
        .AddPolicy(AuthzConstants.POLICY_INSTANCE_READ, policy => policy.Requirements.Add(new AppAccessRequirement("read")))
        .AddPolicy(AuthzConstants.POLICY_INSTANCE_WRITE, policy => policy.Requirements.Add(new AppAccessRequirement("write")))
        .AddPolicy(AuthzConstants.POLICY_INSTANCE_DELETE, policy => policy.Requirements.Add(new AppAccessRequirement("delete")))
        .AddPolicy(AuthzConstants.POLICY_INSTANCE_COMPLETE, policy => policy.Requirements.Add(new AppAccessRequirement("complete")))
        .AddPolicy(AuthzConstants.POLICY_INSTANCE_SIGN, policy => policy.Requirements.Add(new AppAccessRequirement("sign")))
        .AddPolicy(AuthzConstants.POLICY_SCOPE_APPDEPLOY, policy => policy.Requirements.Add(new ScopeAccessRequirement("altinn:appdeploy")))
        .AddPolicy(AuthzConstants.POLICY_STUDIO_DESIGNER, policy => policy.Requirements.Add(new ClaimAccessRequirement("urn:altinn:app", "studio.designer")))
        .AddPolicy(AuthzConstants.POLICY_CORRESPONDENCE_SBLBRIDGE, policy => policy.Requirements.Add(new ScopeAccessRequirement("altinn:correspondence.sblbridge")))
        .AddPolicy("PlatformAccess", policy => policy.Requirements.Add(new AccessTokenRequirement()));

    services.AddSingleton<ClientIpCheckActionFilterAttribute>(container =>
    {
        return new ClientIpCheckActionFilterAttribute() { Safelist = generalSettings.MigrationIpWhiteList };
    });

    services.AddHttpContextAccessor();
    services.AddSingleton<IClaimsPrincipalProvider, ClaimsPrincipalProvider>();

    PostgreSqlSettings postgresSettings = config.GetSection("PostgreSqlSettings").Get<PostgreSqlSettings>();
    services.AddRepositoriesPostgreSQL(string.Format(postgresSettings.ConnectionString, postgresSettings.StorageDbPwd), postgresSettings.LogParameters);

    services.AddSingleton<IKeyVaultClientWrapper, KeyVaultClientWrapper>();
    services.AddSingleton<IPDP, PDPAppSI>();
    services.AddSingleton<IFileScanQueueClient, FileScanQueueClient>();

    services.AddTransient<IAuthorizationHandler, StorageAccessHandler>();
    services.AddTransient<IAuthorizationHandler, ScopeAccessHandler>();
    services.AddTransient<IAuthorizationHandler, ClaimAccessHandler>();
    services.AddTransient<IAuthorization, AuthorizationService>();
    services.AddSingleton<IAccessTokenGenerator, AccessTokenGenerator>();
    services.AddSingleton<ISigningCredentialsResolver, SigningCredentialsResolver>();
    services.AddTransient<IDataService, DataService>();
    services.AddTransient<ISigningService, SigningService>();
    services.AddTransient<IInstanceEventService, InstanceEventService>();
    services.AddSingleton<IApplicationService, ApplicationService>();
    services.AddSingleton<IA2OndemandFormattingService, A2OndemandFormattingService>();

    services.AddHttpClient<IPartiesWithInstancesClient, PartiesWithInstancesClient>();
    services.AddHttpClient<IOnDemandClient, OnDemandClient>();
    services.AddHttpClient<IPdfGeneratorClient, PdfGeneratorClient>();

    // Add Swagger support (Swashbuckle)
    services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new OpenApiInfo { Title = "Altinn Platform Storage", Version = "v1" });
        c.AddSecurityDefinition(JwtCookieDefaults.AuthenticationScheme, new OpenApiSecurityScheme
        {
            Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\". Remember to add \"Bearer\" to the input below before your token.",
            Name = "Authorization",
            In = ParameterLocation.Header,
            Type = SecuritySchemeType.ApiKey,
        });
        c.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference
                            {
                                Id = JwtCookieDefaults.AuthenticationScheme,
                                Type = ReferenceType.SecurityScheme
                            }
                        },
                        Array.Empty<string>()
                    }
                });
        try
        {
            c.IncludeXmlComments(GetXmlCommentsPathForControllers());

            // hardcoded since nuget restore does not export the xml file.
            c.IncludeXmlComments("Altinn.Platform.Storage.Interface.xml");
        }
        catch
        {
            // catch swashbuckle exception if it doesn't find the generated xml documentation file
        }
    });
    services.AddSwaggerGenNewtonsoftSupport();

    builder.Services.AddHostedService<OutboxService>();
}

void ConfigureWolverine(IServiceCollection services, IConfiguration config)
{
    WolverineSettings wolverineSettings = config.GetSection("WolverineSettings").Get<WolverineSettings>();

    services.AddWolverine(opts =>
    {
        if (!builder.Environment.IsDevelopment())
        {
            // Force wolverine to use bus for sending messages (not handle messages internal)
            opts.Policies.DisableConventionalLocalRouting();

            // Azure Service Bus transport
            opts.UseAzureServiceBus(wolverineSettings.ServiceBusConnectionString)

                // Use custom mapper to set deduplication id on outgoing messages
                .ConfigureSenders(s =>
                {
                    s.CustomizeOutgoingMessagesOfType<SyncInstanceToDialogportenCommand>((envelope, cmd) =>
                    {
                        if (wolverineSettings.EnableWolverineOutbox)
                        {
                            // Set a deterministic MessageId for SyncInstanceToDialogportenCommand messages, and
                            // deliver them at the end of a time bucket.
                            (envelope.Id, envelope.ScheduledTime) = DebounceHelper.TimeBucketId(cmd.InstanceId, 5.Seconds(), DateTimeOffset.UtcNow);
                        }
                        else
                        {
                            envelope.Id = Guid.Parse(cmd.InstanceId);
                        }
                    });
                })

                // Let Wolverine try to initialize any missing queues on the first usage at runtime
                .AutoProvision();

            // Publish CreateOrderCommand to ASB queue
            opts.PublishMessage<SyncInstanceToDialogportenCommand>()
                .ToAzureServiceBusQueue("altinn.dialogportenadapter.webapi")
                .ConfigureQueue(q =>
                {
                    // NOTE! This can ONLY be set at queue creation time
                    q.RequiresDuplicateDetection = true;

                    // 20 seconds is the minimum allowed by ASB duplicate detection according to
                    // https://learn.microsoft.com/en-us/azure/service-bus-messaging/duplicate-detection#duplicate-detection-window-size
                    q.DuplicateDetectionHistoryTimeWindow = TimeSpan.FromSeconds(20);
                });

            if (wolverineSettings.EnableWolverineOutbox)
            {
                // Outbox with Postgres
                opts.PersistMessagesWithPostgresql(wolverineSettings.PostgresConnectionString, schemaName: "wolverine");
                opts.Policies.UseDurableOutboxOnAllSendingEndpoints();
            }
        }
        else
        {
            // Out of the box, this uses a separate local queue
            // for each message based on the message type name
            opts.Policies.ConfigureConventionalLocalRouting()

                // Or you can customize the usage of queues
                // per message type
                .Named(type => type.Namespace)

                // Optionally configure the local queues
                .CustomizeQueues((type, listener) => { listener.Sequential(); });
        }
    });
}

static string GetXmlCommentsPathForControllers()
{
    // locate the xml file being generated by .NET
    string xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    string xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);

    return xmlPath;
}

void Configure(IConfiguration config)
{
    if (app.Environment.IsDevelopment() || app.Environment.IsStaging())
    {
        app.UseDeveloperExceptionPage();
    }
    else
    {
        app.UseExceptionHandler("/storage/api/v1/error");
    }

    ConsoleTraceService traceService = new() { IsDebugEnabled = true };
    string connectionString = string.Format(
        config.GetValue<string>("PostgreSqlSettings:AdminConnectionString"),
        config.GetValue<string>("PostgreSqlSettings:StorageDbAdminPwd"));
    app.UseYuniql(
        new PostgreSqlDataService(traceService),
        new PostgreSqlBulkImportService(traceService),
        traceService,
        new Yuniql.AspNetCore.Configuration
        {
            Workspace = Path.Combine(Environment.CurrentDirectory, config.GetValue<string>("PostgreSqlSettings:WorkspacePath")),
            ConnectionString = connectionString,
            IsAutoCreateDatabase = false,
            IsDebug = true
        });

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseRouting();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();
    app.MapHealthChecks("/health");
}
