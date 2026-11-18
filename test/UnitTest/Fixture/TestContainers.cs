using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Azure.Core.Pipeline;
using Azure.Storage.Blobs;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Testcontainers.PostgreSql;

namespace Altinn.Platform.Storage.UnitTest.Fixture;

/// <summary>
/// The PostgreSQL and Azurite containers the tests run against. They are started on first use, shared
/// by the whole test run and removed by Testcontainers when the run ends, so every run starts from an
/// empty database that the migrations in the working tree are applied to.
/// </summary>
/// <remarks>
/// <para>
/// Azurite serves HTTPS with a certificate created for the run, and accepts bearer tokens
/// (<c>--oauth basic</c>), so that user delegation keys can be requested from it as from Azure. Clients
/// trust the certificate through <see cref="AzuriteClientOptions"/> or
/// <see cref="CreateAzuriteHttpClient"/>, and get tokens from <see cref="AzuriteTokenCredential"/>.
/// </para>
/// <para>
/// The images are pinned by digest; Renovate updates them through a custom manager in renovate.json.
/// </para>
/// </remarks>
public static class TestContainers
{
    /// <summary>
    /// The account name of Azurite's well-known development account.
    /// </summary>
    public const string AzuriteAccountName = "devstoreaccount1";

    /// <summary>
    /// The account key of Azurite's well-known development account.
    /// </summary>
    public const string AzuriteAccountKey =
        "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";

    private const string _postgresImage =
        "postgres:16@sha256:f9a7b9ebf6f56201f949db745e47bc423c0f0f8294bafe991cc10bf74b029657";
    private const string _azuriteImage =
        "mcr.microsoft.com/azure-storage/azurite:3.37.0@sha256:830430c1da1a2d537e08f3e6764dd1f5ae00cf0346bcaf625b968ec3f0971fd5";
    private const string _postgresAdminUser = "platform_storage_admin";
    private const string _postgresPassword = "Password";
    private const ushort _azuriteBlobPort = 10000;

    private static readonly Lazy<Endpoints> _endpoints = new(Start);

    /// <summary>
    /// Gets the Azurite blob endpoint of the development account.
    /// </summary>
    public static string AzuriteBlobEndpoint => _endpoints.Value.AzuriteBlobEndpoint;

    /// <summary>
    /// Gets client options that trust Azurite's certificate.
    /// </summary>
    public static BlobClientOptions AzuriteClientOptions => _endpoints.Value.AzuriteClientOptions;

    /// <summary>
    /// Gets the settings that point the configuration in appsettings.unittest.json at the containers.
    /// </summary>
    public static IReadOnlyDictionary<string, string?> Settings => _endpoints.Value.Settings;

    /// <summary>
    /// Creates an HTTP client that trusts Azurite's certificate, for requests without the Azure SDK.
    /// </summary>
    public static HttpClient CreateAzuriteHttpClient()
    {
        return new HttpClient(CreateAzuriteHandler(_endpoints.Value.AzuriteCertificateThumbprint));
    }

    private static Endpoints Start()
    {
        using RSA azuriteKey = RSA.Create(2048);
        CertificateRequest certificateRequest = new(
            "CN=azurite",
            azuriteKey,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );
        SubjectAlternativeNameBuilder subjectAlternativeNames = new();
        subjectAlternativeNames.AddIpAddress(IPAddress.Loopback);
        subjectAlternativeNames.AddDnsName("localhost");
        certificateRequest.CertificateExtensions.Add(subjectAlternativeNames.Build());
        using X509Certificate2 azuriteCertificate = certificateRequest.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(7)
        );

        PostgreSqlContainer postgres = new PostgreSqlBuilder(_postgresImage)
            .WithUsername(_postgresAdminUser)
            .WithPassword(_postgresPassword)
            .WithDatabase("postgres")
            .WithResourceMapping(
                ReadEmbeddedResource("postgres_init.sql"),
                "/docker-entrypoint-initdb.d/postgres_init.sql"
            )
            .Build();

        IContainer azurite = new ContainerBuilder(_azuriteImage)
            .WithResourceMapping(
                Encoding.ASCII.GetBytes(azuriteCertificate.ExportCertificatePem()),
                "/certs/azurite.pem"
            )
            .WithResourceMapping(
                Encoding.ASCII.GetBytes(azuriteKey.ExportPkcs8PrivateKeyPem()),
                "/certs/azurite-key.pem"
            )
            .WithCommand(
                "azurite-blob",
                "--blobHost",
                "0.0.0.0",
                "--blobPort",
                $"{_azuriteBlobPort}",
                "--skipApiVersionCheck",
                "--cert",
                "/certs/azurite.pem",
                "--key",
                "/certs/azurite-key.pem",
                "--oauth",
                "basic"
            )
            .WithPortBinding(_azuriteBlobPort, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("successfully listens"))
            .Build();

        Task.WhenAll(postgres.StartAsync(), azurite.StartAsync()).GetAwaiter().GetResult();

        string postgresServer =
            $"Host={postgres.Hostname};Port={postgres.GetMappedPublicPort(PostgreSqlBuilder.PostgreSqlPort)}";
        string azuriteBlobEndpoint =
            $"https://{azurite.Hostname}:{azurite.GetMappedPublicPort(_azuriteBlobPort)}/{AzuriteAccountName}";

        string thumbprint = azuriteCertificate.Thumbprint;
        return new Endpoints(
            azuriteBlobEndpoint,
            thumbprint,
            new BlobClientOptions
            {
                Transport = new HttpClientTransport(CreateAzuriteHandler(thumbprint)),
            },
            new Dictionary<string, string?>
            {
                ["PostgreSQLSettings:AdminConnectionString"] =
                    $"{postgresServer};Username={_postgresAdminUser};Password={{0}};Database=storagedb;GSS Encryption Mode=Disable",
                ["PostgreSQLSettings:ConnectionString"] =
                    $"{postgresServer};Username=platform_storage;Password={{0}};Database=storagedb;GSS Encryption Mode=Disable",
                ["WolverineSettings:PostgresConnectionString"] =
                    $"{postgresServer};Username=platform_storage;Password={_postgresPassword};Database=storagedb;GSS Encryption Mode=Disable",
                ["AzureStorageConfiguration:BlobEndPoint"] = azuriteBlobEndpoint,
            }
        );
    }

    private static HttpClientHandler CreateAzuriteHandler(string certificateThumbprint)
    {
        return new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
                certificate?.Thumbprint == certificateThumbprint,
        };
    }

    private static byte[] ReadEmbeddedResource(string name)
    {
        using Stream stream =
            Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded resource '{name}' not found");
        using MemoryStream copy = new();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private sealed record Endpoints(
        string AzuriteBlobEndpoint,
        string AzuriteCertificateThumbprint,
        BlobClientOptions AzuriteClientOptions,
        IReadOnlyDictionary<string, string?> Settings
    );
}
