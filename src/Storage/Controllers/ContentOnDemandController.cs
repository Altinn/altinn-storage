using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Services;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace Altinn.Platform.Storage.Controllers
{
    /// <summary>
    /// Implements endpoints on demand content generation
    /// </summary>
    [Route("storage/api/v1/ondemand/{org}/{app}/{instanceOwnerPartyId:int}/{instanceGuid:guid}/{dataGuid:guid}/{language}")]
    [ApiExplorerSettings(IgnoreApi = true)]
    [ApiController]
    public class ContentOnDemandController : ControllerBase
    {
        private readonly IInstanceRepository _instanceRepository;
        private readonly IBlobRepository _blobRepository;
        private readonly IA2Repository _a2Repository;
        private readonly ILogger _logger;
        private readonly GeneralSettings _generalSettings;
        private readonly IA2OndemandFormattingService _a2OndemandFormattingService;

        /// <summary>
        /// Initializes a new instance of the <see cref="ContentOnDemandController"/> class
        /// </summary>
        /// <param name="instanceRepository">the instance repository handler</param>
        /// <param name="blobRepository">the blob repository handler</param>
        /// <param name="a2Repository">the a2 repository handler</param>
        /// <param name="logger">the logger</param>
        /// <param name="settings">the general settings.</param>
        /// <param name="a2OndemandFormattingService">a2OndemandFormattingService</param>
        public ContentOnDemandController(
            IInstanceRepository instanceRepository,
            IBlobRepository blobRepository,
            IA2Repository a2Repository,
            ILogger<ContentOnDemandController> logger,
            IOptions<GeneralSettings> settings,
            IA2OndemandFormattingService a2OndemandFormattingService)
        {
            _instanceRepository = instanceRepository;
            _blobRepository = blobRepository;
            _a2Repository = a2Repository;
            _logger = logger;
            _generalSettings = settings.Value;
            _a2OndemandFormattingService = a2OndemandFormattingService;
        }

        /// <summary>
        /// Gets the formatted content
        /// </summary>
        /// <param name="org">org</param>
        /// <param name="app">app</param>
        /// <param name="instanceGuid">instanceGuid</param>
        /// <param name="dataGuid">dataGuid</param>
        /// <param name="language">language</param>
        /// <returns>The formatted content</returns>
        [Authorize(Policy = AuthzConstants.POLICY_INSTANCE_READ)]
        [HttpGet("signature")]
        public async Task<Stream> GetSignatureAsHtml([FromRoute] string org, [FromRoute] string app, [FromRoute] Guid instanceGuid, [FromRoute] Guid dataGuid, [FromRoute] string language)
        {
            (Instance instance, _) = await _instanceRepository.GetOne(instanceGuid, true);
            DataElement signatureElement = instance.Data.First(d => d.DataType == "signature-data");

            using StreamReader reader = new(await _blobRepository.ReadBlob($"{(_generalSettings.A2UseTtdAsServiceOwner ? "ttd" : org)}", $"{org}/{app}/{instanceGuid}/data/{signatureElement.Id}"));

            // TODO Replace with proper formatting
            string line = null;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (line.Contains("SignatureText"))
                {
                    return new MemoryStream(Encoding.UTF8.GetBytes($"<html>Dette er generert dynamisk<br><br><div>{line.Split(':')[1].TrimStart().Replace("\"", null)}</div></html>"));
                }
            }

            return null;
        }

        /// <summary>
        /// Gets the formatted content
        /// </summary>
        /// <param name="org">org</param>
        /// <param name="app">app</param>
        /// <param name="instanceGuid">instanceGuid</param>
        /// <param name="dataGuid">dataGuid</param>
        /// <param name="language">language</param>
        /// <returns>The formatted content</returns>
        [Authorize(Policy = AuthzConstants.POLICY_INSTANCE_READ)]
        [HttpGet("payment")]
        public async Task<Stream> GetPaymentAsHtml([FromRoute] string org, [FromRoute] string app, [FromRoute] Guid instanceGuid, [FromRoute] Guid dataGuid, [FromRoute] string language)
        {
            // TODO Replace with proper formatting
            return new MemoryStream(Encoding.UTF8.GetBytes($"<html>Dette er generert dynamisk<br><br><div>Not yet implemented</div></html>"));
        }

        /// <summary>
        /// Gets the formatted content
        /// </summary>
        /// <param name="org">org</param>
        /// <param name="app">app</param>
        /// <param name="instanceGuid">instanceGuid</param>
        /// <param name="dataGuid">dataGuid</param>
        /// <param name="language">language</param>
        /// <returns>The formatted content</returns>
        [Authorize(Policy = AuthzConstants.POLICY_INSTANCE_READ)]
        [HttpGet("formdatapdf")]
        public async Task<Stream> GetFormdataAsPdf([FromRoute] string org, [FromRoute] string app, [FromRoute] Guid instanceGuid, [FromRoute] Guid dataGuid, [FromRoute] string language)
        {
            //// TODO: The playwright code below works in the dev environment. There are three issues:
            //// 1. Playwright is not supported out of the box on alpine linux
            //// 2. Rather then LaunchAsync we should use ConnectAsync to connect to a component running a browser
            //// 3. How to authorize
            try
            {
                using var playwright = await Playwright.CreateAsync();
                await using var browser = await playwright.Chromium.LaunchAsync();
                var page = await browser.NewPageAsync();
                await page.GotoAsync($"{Request.Scheme}://{Request.Host}{Request.PathBase}{Request.Path}{Request.QueryString}".Replace("formdatapdf", "formdatahtml"));
                return new MemoryStream(await page.PdfAsync());
            }
            catch (Exception)
            {
                return new MemoryStream(Encoding.UTF8.GetBytes(DummyPdf.Pdf));
            }
        }

        /// <summary>
        /// Gets the formatted content
        /// </summary>
        /// <param name="org">org</param>
        /// <param name="app">app</param>
        /// <param name="instanceGuid">instanceGuid</param>
        /// <param name="dataGuid">dataGuid</param>
        /// <param name="language">language</param>
        /// <returns>The formatted content</returns>
        [Authorize(Policy = AuthzConstants.POLICY_INSTANCE_READ)]
        [HttpGet("formdatahtml")]
        public async Task<Stream> GetFormdataAsHtml([FromRoute]string org, [FromRoute] string app, [FromRoute] Guid instanceGuid, [FromRoute] Guid dataGuid, [FromRoute] string language)
        {
            (Instance instance, _) = await _instanceRepository.GetOne(instanceGuid, true);
            DataElement htmlElement = instance.Data.First(d => d.Id == dataGuid.ToString());
            DataElement xmlElement = instance.Data.First(d => d.Metadata.First(m => m.Key == "formid").Value == htmlElement.Metadata.First(m => m.Key == "formid").Value && d.Id != htmlElement.Id);
            PrintViewXslBEList xsls = [];
            int lformid = int.Parse(xmlElement.Metadata.First(m => m.Key == "lformid").Value);
            int pageNumber = 0;
            foreach (var xsl in await _a2Repository.GetXsls(org, app, lformid, language))
            {
                xsls.Add(new PrintViewXslBE() { PrintViewXsl = xsl, Id = $"{lformid}-{pageNumber++}{language}" });
            }

            return _a2OndemandFormattingService.GetFormdataHtml(
                xsls,
                await _blobRepository.ReadBlob($"{(_generalSettings.A2UseTtdAsServiceOwner ? "ttd" : org)}", $"{org}/{app}/{instanceGuid}/data/{xmlElement.Id}"),
                xmlElement.Created.ToString());
        }
    }

    /// <summary>
    /// Dummy class to host dummy pdf content
    /// </summary>
    public static class DummyPdf
    {
        /// <summary>
        /// Dummy pdf content
        /// </summary>
        public const string Pdf =
$@"%PDF-1.7
1 0 obj  % entry point
<<
  /Type /Catalog
  /Pages 2 0 R
>>
endobj

2 0 obj
<<
  /Type /Pages
  /MediaBox [ 0 0 200 200 ]
  /Count 1
  /Kids [ 3 0 R ]
>>
endobj

3 0 obj
<<
  /Type /Page
  /Parent 2 0 R
  /Resources <<
    /Font <<
      /F1 4 0 R 
    >>
  >>
  /Contents 5 0 R
>>
endobj

4 0 obj
<<
  /Type /Font
  /Subtype /Type1
  /BaseFont /Times-Roman
>>
endobj

5 0 obj  % page content
<<
  /Length 44
>>
stream
BT
70 50 TD
/F1 12 Tf
(Under construction) Tj
ET
endstream
endobj

xref
0 6
0000000000 65535 f 
0000000010 00000 n 
0000000079 00000 n 
0000000173 00000 n 
0000000301 00000 n 
0000000380 00000 n 
trailer
<<
  /Size 6
  /Root 1 0 R
>>
startxref
492
%%EOF
";
    }
}
