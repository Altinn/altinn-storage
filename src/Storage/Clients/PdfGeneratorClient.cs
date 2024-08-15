using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Altinn.Platform.Storage.Clients;

/// <summary>
/// Implementation of the <see cref="IPdfGeneratorClient"/> interface using a HttpClient to send
/// requests to the PDF Generator service.
/// </summary>
public class PdfGeneratorClient: IPdfGeneratorClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<PdfGeneratorClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfGeneratorClient"/> class.
    /// </summary>
    /// <param name="httpClient">The HttpClient to use in communication with the PDF generator service.</param>
    /// <param name="generalSettings">Settings with endpoint uri</param>
    /// <param name="logger">The logger</param>
    public PdfGeneratorClient(
        HttpClient httpClient,
        IOptions<GeneralSettings> generalSettings,
        ILogger<PdfGeneratorClient> logger)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri(generalSettings.Value.PdfGeneratorEndpoint);
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<Stream> GeneratePdf(string url)
    {
        string requestContent = """{"url": "_url_"}""".Replace("_url_", url);
        var httpResponseMessage = await _httpClient.PostAsync(_httpClient.BaseAddress, new StringContent(requestContent, Encoding.UTF8, "application/json"));

        if (!httpResponseMessage.IsSuccessStatusCode)
        {
            var content = await httpResponseMessage.Content.ReadAsStringAsync();
            var ex = new Exception("Pdf generation failed");
            ex.Data.Add("responseContent", content);
            ex.Data.Add("responseStatusCode", httpResponseMessage.StatusCode.ToString());
            ex.Data.Add("responseReasonPhrase", httpResponseMessage.ReasonPhrase);

            _logger.LogError(
                "Pdf generation failed. Status code: {StatusCode}, response content: {Content}, reason: {Reason}",
                content,
                httpResponseMessage.StatusCode.ToString(),
                httpResponseMessage.ReasonPhrase);

            throw ex;
        }

        return await httpResponseMessage.Content.ReadAsStreamAsync();
    }
}
