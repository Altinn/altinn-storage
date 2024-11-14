using System;
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
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Services;
using Altinn.Platform.Storage.Wrappers;
using Altinn.Platform.Telemetry;

using AltinnCore.Authentication.JwtCookie;

using Azure.Identity;
using Azure.Security.KeyVault.Secrets;

using Microsoft.ApplicationInsights.AspNetCore.Extensions;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.WindowsServer.TelemetryChannel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Yuniql.AspNetCore;
using Yuniql.PostgreSql;

ILogger logger;

string vaultApplicationInsightsKey = "ApplicationInsights--InstrumentationKey";

string applicationInsightsConnectionString = string.Empty;

var builder = WebApplication.CreateBuilder(args);

ConfigureSetupLogging();

await SetConfigurationProviders(builder.Configuration, builder.Environment.IsDevelopment());

ConfigureLogging(builder.Logging);

ConfigureServices(builder.Services, builder.Configuration);

logger.LogInformation("// Checking Azure Storage connection.");

var app = builder.Build();

Configure(builder.Configuration);

logger.LogInformation("// Running...");
app.Run();

void ConfigureSetupLogging()
{
    var logFactory = LoggerFactory.Create(builder =>
    {
        builder
            .AddFilter("Altinn.Platform.Register.Program", LogLevel.Debug)
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

void ConfigureLogging(ILoggingBuilder logging)
{
    // The default ASP.NET Core project templates call CreateDefaultBuilder, which adds the following logging providers:
    // Console, Debug, EventSource
    // https://docs.microsoft.com/en-us/aspnet/core/fundamentals/logging/?view=aspnetcore-3.1

    // Clear log providers
    logging.ClearProviders();

    // Setup up application insight if ApplicationInsightsConnectionString is available
    if (!string.IsNullOrEmpty(applicationInsightsConnectionString))
    {
        // Add application insights https://docs.microsoft.com/en-us/azure/azure-monitor/app/ilogger
        logging.AddApplicationInsights(
                  configureTelemetryConfiguration: (config) => config.ConnectionString = applicationInsightsConnectionString,
                  configureApplicationInsightsLoggerOptions: (options) => { });

        // Optional: Apply filters to control what logs are sent to Application Insights.
        // The following configures LogLevel Information or above to be sent to
        // Application Insights for all categories.
        logging.AddFilter<Microsoft.Extensions.Logging.ApplicationInsights.ApplicationInsightsLoggerProvider>(string.Empty, LogLevel.Warning);

        // Adding the filter below to ensure logs of all severity from Program.cs
        // is sent to ApplicationInsights.
        logging.AddFilter<Microsoft.Extensions.Logging.ApplicationInsights.ApplicationInsightsLoggerProvider>(typeof(Program).FullName, LogLevel.Trace);
    }
    else
    {
        // If not application insight is available log to console
        logging.AddFilter("Microsoft", LogLevel.Warning);
        logging.AddFilter("System", LogLevel.Warning);
    }

    logging.AddConsole();
}

void ConfigureServices(IServiceCollection services, IConfiguration config)
{
    logger.LogInformation("Program // ConfigureServices");

    services.AddControllersWithViews().AddNewtonsoftJson();
    services.AddMemoryCache();
    services.AddHealthChecks().AddCheck<HealthCheck>("storage_health_check");

    services.AddHttpClient<AuthorizationApiClient>();
    services.AddHttpClient<IRegisterService, RegisterService>();

    services.Configure<AzureStorageConfiguration>(config.GetSection("AzureStorageConfiguration"));
    services.Configure<GeneralSettings>(config.GetSection("GeneralSettings"));
    services.Configure<KeyVaultSettings>(config.GetSection("kvSetting"));
    services.Configure<PepSettings>(config.GetSection("PepSettings"));
    services.Configure<PlatformSettings>(config.GetSection("PlatformSettings"));
    services.Configure<RegisterServiceSettings>(config.GetSection("RegisterServiceSettings"));
    services.Configure<QueueStorageSettings>(config.GetSection("QueueStorageSettings"));
    services.Configure<AccessTokenSettings>(config.GetSection("AccessTokenSettings"));
    services.Configure<PostgreSqlSettings>(config.GetSection("PostgreSqlSettings"));

    services.AddSingleton<IAuthorizationHandler, AccessTokenHandler>();
    services.AddSingleton<IPublicSigningKeyProvider, PublicSigningKeyProvider>();

    GeneralSettings generalSettings = config.GetSection("GeneralSettings").Get<GeneralSettings>();

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
        .AddPolicy(AuthzConstants.POLICY_PLATFORM_ACCESS, policy => policy.Requirements.Add(new AccessTokenRequirement()));

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
    services.AddTransient<IInstanceService, InstanceService>();
    services.AddTransient<IInstanceEventService, InstanceEventService>();
    services.AddSingleton<IApplicationService, ApplicationService>();
    services.AddSingleton<IA2OndemandFormattingService, A2OndemandFormattingService>();

    services.AddHttpClient<IPartiesWithInstancesClient, PartiesWithInstancesClient>();
    services.AddHttpClient<IOnDemandClient, OnDemandClient>();
    services.AddHttpClient<IPdfGeneratorClient, PdfGeneratorClient>();

    if (!string.IsNullOrEmpty(applicationInsightsConnectionString))
    {
        services.AddSingleton(typeof(ITelemetryChannel), new ServerTelemetryChannel() { StorageFolder = "/tmp/logtelemetry" });
        services.AddApplicationInsightsTelemetry(new ApplicationInsightsServiceOptions
        {
            ConnectionString = applicationInsightsConnectionString,
            EnablePerformanceCounterCollectionModule = false
        });

        services.AddApplicationInsightsTelemetryProcessor<HealthTelemetryFilter>();
        services.AddApplicationInsightsTelemetryProcessor<IdentityTelemetryFilter>();
        services.AddSingleton<ITelemetryInitializer, CustomTelemetryInitializer>();
    }

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
