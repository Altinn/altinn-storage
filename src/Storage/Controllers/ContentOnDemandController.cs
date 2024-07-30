using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Altinn.Platform.Storage.Authorization;
using Altinn.Platform.Storage.Clients;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Services;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Altinn.Platform.Storage.Controllers
{
    /// <summary>
    /// Implements endpoints on demand content generation
    /// </summary>
    [Route("storage/api/v1/ondemand/{org}/{app}/{instanceOwnerPartyId:int}/{instanceGuid:guid}/{dataGuid:guid}")]
    [ApiExplorerSettings(IgnoreApi = true)]
    [ApiController]
    public class ContentOnDemandController : ControllerBase
    {
        private readonly IInstanceRepository _instanceRepository;
        private readonly IInstanceEventRepository _instanceEventRepository;
        private readonly IDataRepository _dataRepository;
        private readonly IBlobRepository _blobRepository;
        private readonly IApplicationRepository _applicationRepository;
        private readonly IA2Repository _a2Repository;
        private readonly ITextRepository _textRepository;
        private readonly IPartiesWithInstancesClient _partiesWithInstancesClient;
        private readonly ILogger _logger;
        private readonly IAuthorization _authorizationService;
        private readonly IInstanceEventService _instanceEventService;
        private readonly string _storageBaseAndHost;
        private readonly GeneralSettings _generalSettings;
        private readonly AzureStorageConfiguration _azureStorageSettings;
        private readonly IRegisterService _registerService;
        private readonly IA2OndemandFormattingService _a2OndemandFormattingService;

        /// <summary>
        /// Initializes a new instance of the <see cref="ContentOnDemandController"/> class
        /// </summary>
        /// <param name="instanceRepository">the instance repository handler</param>
        /// <param name="instanceEventRepository">the instance events repository handler</param>
        /// <param name="dataRepository">the data element repository handler</param>
        /// <param name="blobRepository">the blob repository handler</param>
        /// <param name="applicationRepository">the application repository handler</param>
        /// <param name="a2Repository">the a2 repository handler</param>
        /// <param name="textRepository">the text repository handler</param>
        /// <param name="partiesWithInstancesClient">An implementation of <see cref="IPartiesWithInstancesClient"/> that can be used to send information to SBL.</param>
        /// <param name="logger">the logger</param>
        /// <param name="authorizationService">the authorization service.</param>
        /// <param name="instanceEventService">the instance event service.</param>
        /// <param name="registerService">the instance register service.</param>
        /// <param name="settings">the general settings.</param>
        /// <param name="azureStorageSettings">the azureStorage settings.</param>
        public ContentOnDemandController(
            IInstanceRepository instanceRepository,
            IInstanceEventRepository instanceEventRepository,
            IDataRepository dataRepository,
            IBlobRepository blobRepository,
            IApplicationRepository applicationRepository,
            IA2Repository a2Repository,
            ITextRepository textRepository,
            IPartiesWithInstancesClient partiesWithInstancesClient,
            ILogger<MigrationController> logger,
            IAuthorization authorizationService,
            IInstanceEventService instanceEventService,
            IRegisterService registerService,
            IOptions<GeneralSettings> settings,
            IOptions<AzureStorageConfiguration> azureStorageSettings,
            IA2OndemandFormattingService a2OndemandFormattingService)
        {
            _instanceRepository = instanceRepository;
            _instanceEventRepository = instanceEventRepository;
            _dataRepository = dataRepository;
            _blobRepository = blobRepository;
            _applicationRepository = applicationRepository;
            _a2Repository = a2Repository;
            _textRepository = textRepository;
            _partiesWithInstancesClient = partiesWithInstancesClient;
            _logger = logger;
            _storageBaseAndHost = $"{settings.Value.Hostname}/storage/api/v1/";
            _authorizationService = authorizationService;
            _instanceEventService = instanceEventService;
            _registerService = registerService;
            _generalSettings = settings.Value;
            _azureStorageSettings = azureStorageSettings.Value;
            _a2OndemandFormattingService = a2OndemandFormattingService;
        }

        /// <summary>
        /// Gets the formatted content
        /// </summary>
        /// <param name="org">org</param>
        /// <param name="app">app/param>
        /// <param name="instanceGuid">instanceGuid</param>
        /// <param name="dataGuid">dataGuid</param>
        /// <returns>The formatted content</returns>
        [AllowAnonymous]
        [HttpGet("signature")]
        public async Task<Stream> GetSignatureAsHtml([FromRoute] string org, [FromRoute] string app, [FromRoute] Guid instanceGuid, [FromRoute] Guid dataGuid)
        {
            // TODO Replace with proper formatting
            (Instance instance, _) = await _instanceRepository.GetOne(instanceGuid, true);
            DataElement signatureElement = instance.Data.First(d => d.DataType == "signature-data");

            // TODO remove hard coding of ttd below, replace "ttd" with org
            using (StreamReader reader = new(await _blobRepository.ReadBlob("ttd", $"{org}/{app}/{instanceGuid}/data/{signatureElement.Id}")))
            {
                string line = null;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (line.Contains("SignatureText"))
                    {
                        return new MemoryStream(Encoding.UTF8.GetBytes($"<html>Dette er generert dynamisk<div>{line.Split(':')[1].TrimStart().Replace("\"", null)}</div></html>"));
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Gets the formatted content
        /// </summary>
        /// <param name="org">org</param>
        /// <param name="app">app/param>
        /// <param name="instanceGuid">instanceGuid</param>
        /// <param name="dataGuid">dataGuid</param>
        /// <returns>The formatted content</returns>
        [AllowAnonymous]
        [HttpGet("formdata")]
        public async Task<Stream> GetFormdataAsHtml([FromRoute]string org, [FromRoute] string app, [FromRoute] Guid instanceGuid, [FromRoute] Guid dataGuid)
        {
            (Instance instance, _) = await _instanceRepository.GetOne(instanceGuid, true);
            DataElement htmlElement = instance.Data.First(d => d.Id == dataGuid.ToString());
            DataElement xmlElement = instance.Data.First(d => d.Metadata.First(m => m.Key == "formid").Value == htmlElement.Metadata.First(m => m.Key == "formid").Value && d.Id != htmlElement.Id);
            PrintViewXslBEList xsls = new PrintViewXslBEList();
            foreach (var xsl in await _a2Repository.GetXsls(
                org,
                app,
                int.Parse(xmlElement.Metadata.First(m => m.Key == "lformid").Value),
                //// TODO handle language
                "nb"))
            {
                xsls.Add(new PrintViewXslBE() { PrintViewXsl = xsl });
            }

            // TODO remove hard coding of ttd below
            return _a2OndemandFormattingService.GetHTML(
                xsls,
                //// await _blobRepository.ReadBlob(org, $"{org}/{app}/{instanceGuid}/data/{xmlElement.Id}"),
                await _blobRepository.ReadBlob("ttd", $"{org}/{app}/{instanceGuid}/data/{xmlElement.Id}"),
                xmlElement.Created.ToString(),
                1044);

            ////MemoryStream ms = new MemoryStream();
            ////HtmlConverter.ConvertToPdf(x, ms);
            ////PdfDocument pdf = PdfGenerator.GeneratePdf(new StreamReader(x).ReadToEnd(), PdfSharp.PageSize.A4);
            ////pdf.Save(@"c:\temp\hn.pdf");
            ////return ms;
        }
    }
}
