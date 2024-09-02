using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
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
        if (DateTime.Now.Minute > 30)
        {
            var request = new PdfGeneratorRequest() { Url = url };
            request.Options = new() { Format = "A4", DisplayHeaderFooter = true };
            requestContent = JsonSerializer.Serialize(request);
        }

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

    /// <summary>
    /// This class is created to match the input required to generate a PDF by the PDF generator service.
    /// </summary>
    internal class PdfGeneratorRequest
    {
        /// <summary>
        /// The Url that the PDF generator will used to obtain the HTML needed to created the PDF.
        /// </summary>
        public string Url { get; set; } = string.Empty;

        /// <summary>
        /// PDF generator request options.
        /// </summary>
        public PdfGeneratorRequestOptions Options { get; set; } = new();
    }

    /// <summary>
    /// This class is created to match the PDF generator options used by the PDF generator.
    /// </summary>
    internal class PdfGeneratorRequestOptions
    {
        /// <summary>
        /// Indicate whether header and footer should be included.
        /// </summary>
        public bool DisplayHeaderFooter { get; set; } = false;

        /// <summary>
        /// Indicate wheter the background should be included.
        /// </summary>
        public bool PrintBackground { get; set; } = false;

        /// <summary>
        /// Defines the page size. Default is A4.
        /// </summary>
        public string Format { get; set; } = "A4";

        /// <summary>
        /// Whether to print in landscape orientation
        /// </summary>
        public bool Landscape { get; set; } = false;

        /// <summary>
        /// Defines the page margins. Default is "0.4in" on all sides.
        /// </summary>
        public MarginOptions Margin { get; set; } = null; //// new();
    }

    /// <summary>
    /// This class is created to match the PDF generator marking options.
    /// </summary>
    internal class MarginOptions
    {
        /// <summary>
        /// Top margin, accepts values labeled with units.
        /// </summary>
        public string Top { get; set; } = "0.75in";

        /// <summary>
        /// Left margin, accepts values labeled with units
        /// </summary>
        public string Left { get; set; } = "0.75in";

        /// <summary>
        /// Bottom margin, accepts values labeled with units
        /// </summary>
        public string Bottom { get; set; } = "0.75in";

        /// <summary>
        /// Right margin, accepts values labeled with units
        /// </summary>
        public string Right { get; set; } = "0.75in";
    }
}
