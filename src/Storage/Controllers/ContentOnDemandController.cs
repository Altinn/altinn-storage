using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Clients;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Services;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Altinn.Platform.Storage.Controllers
{
    /// <summary>
    /// Implements endpoints on demand content generation
    /// </summary>
    [Route("storage/api/v1/ondemand/{org}/{app}/{instanceOwnerPartyId:int}/{instanceGuid:guid}/{dataGuid:guid}/{language}")]
    [ApiExplorerSettings(IgnoreApi = true)]
    [ApiController]
    public class ContentOnDemandController : Controller
    {
        private readonly IInstanceRepository _instanceRepository;
        private readonly IBlobRepository _blobRepository;
        private readonly IA2Repository _a2Repository;
        private readonly IApplicationRepository _applicationRepository;
        private readonly GeneralSettings _generalSettings;
        private readonly IA2OndemandFormattingService _a2OndemandFormattingService;
        private readonly IPdfGeneratorClient _pdfGeneratorClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="ContentOnDemandController"/> class
        /// </summary>
        /// <param name="instanceRepository">the instance repository handler</param>
        /// <param name="blobRepository">the blob repository handler</param>
        /// <param name="a2Repository">the a2 repository handler</param>
        /// <param name="applicationRepository">the application repository handler</param>
        /// <param name="settings">the general settings.</param>
        /// <param name="a2OndemandFormattingService">a2OndemandFormattingService</param>
        /// <param name="pdfGeneratorClient">pdfGeneratorClient</param>
        public ContentOnDemandController(
            IInstanceRepository instanceRepository,
            IBlobRepository blobRepository,
            IA2Repository a2Repository,
            IApplicationRepository applicationRepository,
            IOptions<GeneralSettings> settings,
            IA2OndemandFormattingService a2OndemandFormattingService,
            IPdfGeneratorClient pdfGeneratorClient)
        {
            _instanceRepository = instanceRepository;
            _blobRepository = blobRepository;
            _a2Repository = a2Repository;
            _applicationRepository = applicationRepository;
            _generalSettings = settings.Value;
            _a2OndemandFormattingService = a2OndemandFormattingService;
            _pdfGeneratorClient = pdfGeneratorClient;
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
        [HttpGet("signature")]
        public async Task<ActionResult> GetSignatureAsHtml([FromRoute] string org, [FromRoute] string app, [FromRoute] Guid instanceGuid, [FromRoute] Guid dataGuid, [FromRoute] string language)
        {
            (Instance instance, _) = await _instanceRepository.GetOne(instanceGuid, true);
            Application application = await _applicationRepository.FindOne(instance.AppId, instance.Org);
            DataElement signatureElement = instance.Data.First(d => d.DataType == "signature-data");

            List<SignatureView> view = await JsonSerializer.DeserializeAsync<List<SignatureView>>(
                await _blobRepository.ReadBlob(
                    $"{(_generalSettings.A2UseTtdAsServiceOwner ? "ttd" : org)}",
                    $"{org}/{app}/{instanceGuid}/data/{signatureElement.Id}",
                    application.StorageContainerNumber));

            return View("Signature", view);
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
        [HttpGet("payment")]
        public async Task<ActionResult> GetPaymentAsHtml([FromRoute] string org, [FromRoute] string app, [FromRoute] Guid instanceGuid, [FromRoute] Guid dataGuid, [FromRoute] string language)
        {
            (Instance instance, _) = await _instanceRepository.GetOne(instanceGuid, true);
            Application application = await _applicationRepository.FindOne(instance.AppId, instance.Org);
            DataElement paymentElement = instance.Data.First(d => d.DataType == "payment-data");

            PaymentView view = await JsonSerializer.DeserializeAsync<PaymentView>(
                await _blobRepository.ReadBlob(
                    $"{(_generalSettings.A2UseTtdAsServiceOwner ? "ttd" : org)}",
                    $"{org}/{app}/{instanceGuid}/data/{paymentElement.Id}",
                    application.StorageContainerNumber));

            return View("Payment", view);
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
        [HttpGet("formdatapdf")]
        public async Task<Stream> GetFormdataAsPdf([FromRoute] string org, [FromRoute] string app, [FromRoute] Guid instanceGuid, [FromRoute] Guid dataGuid, [FromRoute] string language)
        {
            (Instance instance, _) = await _instanceRepository.GetOne(instanceGuid, true);
            DataElement htmlElement = instance.Data.First(d => d.Id == dataGuid.ToString());
            string htmlFormId = htmlElement.Metadata.First(m => m.Key == "formid").Value;
            DataElement xmlElement = instance.Data.First(d => d.Metadata.First(m => m.Key == "formid").Value == htmlFormId && d.Id != htmlElement.Id);
            string visiblePagesString = xmlElement.Metadata.FirstOrDefault(m => m.Key == "A2VisiblePages")?.Value;
            List<int> visiblePages = !string.IsNullOrEmpty(visiblePagesString) ? visiblePagesString.Split(';').Select(int.Parse).ToList() : null;

            int lformid = int.Parse(xmlElement.Metadata.First(m => m.Key == "lformid").Value);
            PrintViewXslBEList xsls = [];
            int pageNumber = 1;
            foreach ((string xsl, bool isPortrait) in await _a2Repository.GetXsls(org, app, lformid, language, 3))
            {
                if (visiblePages == null || visiblePages.Contains(pageNumber))
                {
                    xsls.Add(new PrintViewXslBE() { PrintViewXsl = xsl, Id = $"{lformid}-{pageNumber}{language}", IsPortrait = isPortrait, PageNumber = pageNumber });
                }

                ++pageNumber;
            }

            xsls[^1].LastPage = true;

            if (xsls.Count > 1 && xsls.Exists(x => x.IsPortrait) && xsls.Exists(x => !x.IsPortrait))
            {
                // Mix of portrait and landscape, we must generate each page and merge them
                using (var mergedDoc = new PdfDocument())
                {
                    foreach (var xsl in xsls)
                    {
                        var pdfPages = await _pdfGeneratorClient.GeneratePdf(
                            $"{Request.Scheme}://{Request.Host}{Request.PathBase}{Request.Path}"
                                .Replace("formdatapdf", $"formdatahtml/{xsl.PageNumber}"),
                            xsl.IsPortrait);
                        using (var pageDoc = PdfReader.Open(pdfPages, PdfDocumentOpenMode.Import))
                        {
                            for (var i = 0; i < pageDoc.PageCount; i++)
                            {
                                pageDoc.Pages[i].Orientation = xsl.IsPortrait ? PdfSharp.PageOrientation.Portrait : PdfSharp.PageOrientation.Landscape;
                                mergedDoc.AddPage(pageDoc.Pages[i]);
                            }
                        }
                    }

                    MemoryStream mergedPdf = new MemoryStream();
                    mergedDoc.Save(mergedPdf);
                    return mergedPdf;
                }
            }
            else
            {
                // Generate all pages in a single operation
                return await _pdfGeneratorClient.GeneratePdf(
                    $"{Request.Scheme}://{Request.Host}{Request.PathBase}{Request.Path}".Replace("formdatapdf", "formdatahtml"),
                    xsls[0].IsPortrait);
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
        /// <param name="singlePageNr">optional filter for a single page number</param>
        /// <returns>The formatted content</returns>
        [HttpGet("formdatahtml/{singlepagenr?}")]
        public async Task<Stream> GetFormdataAsHtml([FromRoute]string org, [FromRoute] string app, [FromRoute] Guid instanceGuid, [FromRoute] Guid dataGuid, [FromRoute] string language, [FromRoute(Name = "singlepagenr")] int singlePageNr = -1)
        {
            return await GetFormdataAsHtmlInternal(org, app, instanceGuid, dataGuid, language, 3, singlePageNr);
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
        [HttpGet("formsummaryhtml")]
        public async Task<Stream> GetFormSummaryAsHtml([FromRoute] string org, [FromRoute] string app, [FromRoute] Guid instanceGuid, [FromRoute] Guid dataGuid, [FromRoute] string language)
        {
            return await GetFormdataAsHtmlInternal(org, app, instanceGuid, dataGuid, language, 2);
        }

        private async Task<Stream> GetFormdataAsHtmlInternal(string org, string app, Guid instanceGuid, Guid dataGuid, string language, int viewType, int singlePageNr = -1)
        {
            (Instance instance, _) = await _instanceRepository.GetOne(instanceGuid, true);
            Application application = await _applicationRepository.FindOne(instance.AppId, instance.Org);
            DataElement htmlElement = instance.Data.First(d => d.Id == dataGuid.ToString());
            string htmlFormId = htmlElement.Metadata.First(m => m.Key == "formid").Value;
            DataElement xmlElement = instance.Data.First(d => d.Metadata.First(m => m.Key == "formid").Value == htmlFormId && d.Id != htmlElement.Id);
            string visiblePagesString = xmlElement.Metadata.FirstOrDefault(m => m.Key == "A2VisiblePages")?.Value;
            List<int> visiblePages = !string.IsNullOrEmpty(visiblePagesString) ? visiblePagesString.Split(';').Select(int.Parse).ToList() : null;

            PrintViewXslBEList xsls = [];
            int lformid = int.Parse(xmlElement.Metadata.First(m => m.Key == "lformid").Value);
            int pageNumber = 1;
            foreach ((string xsl, bool isPortrait) in await _a2Repository.GetXsls(org, app, lformid, language, viewType))
            {
                if ((singlePageNr != -1 && singlePageNr == pageNumber) || (singlePageNr == -1 && (visiblePages == null || visiblePages.Contains(pageNumber))))
                {
                    xsls.Add(new PrintViewXslBE() { PrintViewXsl = xsl, Id = $"{lformid}-{pageNumber}{language}", IsPortrait = isPortrait, PageNumber = pageNumber });
                }

                ++pageNumber;
            }

            xsls[^1].LastPage = true;

            return _a2OndemandFormattingService.GetFormdataHtml(
                xsls,
                await _blobRepository.ReadBlob($"{(_generalSettings.A2UseTtdAsServiceOwner ? "ttd" : org)}", $"{org}/{app}/{instanceGuid}/data/{xmlElement.Id}", application.StorageContainerNumber),
                xmlElement.Created.ToString());
        }
    }

    /// <summary>
    /// View for signature data
    /// </summary>
    public class SignatureView
    {
        /// <summary>
        /// Gets or sets The unique identifier for Signature.
        /// </summary>
        public int SignatureID { get; set; }

        /// <summary>
        /// Gets or sets The party id for the person who has signed.
        /// </summary>
        public int SignedByUser { get; set; }

        /// <summary>
        /// Gets or sets The user ssn/org number for the signee.
        /// </summary>
        public string SignedByUserSSN { get; set; }

        /// <summary>
        /// Gets or sets The user name for the person who has signed.
        /// </summary>
        public string SignedByUserName { get; set; }

        /// <summary>
        /// Gets or sets The date and time at which the signature was created.
        /// </summary>
        public DateTime CreatedDateTime { get; set; }

        /// <summary>
        /// Gets or sets The signature stored in binary format.
        /// </summary>
        public byte[] Signature { get; set; }

        /// <summary>
        /// Gets or sets The text for that signature.
        /// </summary>
        public string SignatureText { get; set; }

        /// <summary>
        /// Gets or sets whether this signing is done for all the items in  the form
        /// 1=Group signing is set, 2=Group signing not set, 3=Group signing defined at each form level
        /// </summary>
        ////public int IsSigningAllRequired { get; set; }

        /// <summary>
        /// Gets or sets The authentication level attached with that signature.
        /// </summary>
        public int AuthenticationLevelID { get; set; }

        /// <summary>
        /// Gets or sets A Key that represents what level the user has when the entry was made
        /// </summary>
        public int AuthenticationMethod { get; set; }

        /// <summary>
        /// Gets or sets The name by which certificate was issued.
        /// </summary>
        public string CertificateIssuedByName { get; set; }

        /// <summary>
        /// Gets or sets The name for whom the signature was issued.
        /// </summary>
        public string CertificateIssuedForName { get; set; }

        /// <summary>
        /// Gets or sets Date and time from which the signature will be valid.
        /// </summary>
        public DateTime CertificateValidFrom { get; set; }

        /// <summary>
        /// Gets or sets Date and time till which the signature will be valid.
        /// </summary>
        public DateTime CertificateValidTo { get; set; }

        /// <summary>
        /// Gets or sets List of signed attachments.
        /// </summary>
        public List<int> SignedAttachmentList { get; set; }

        /// <summary>
        /// Gets or sets List of signed forms.
        /// </summary>
        public List<int> SignedFromList { get; set; }

        /// <summary>
        /// Gets or sets Step for which the Signature is added
        /// </summary>
        public int ProcessStepID { get; set; }

        /// <summary>
        /// List of attachment ids in a3 format
        /// </summary>
        public List<string> SignedAttachmentDataIds { get; set; }

        /// <summary>
        /// List of form ids in a3 format
        /// </summary>
        public List<string> SignedFormDataIds { get; set; }
    }

    /// <summary>
    /// Entity for holding information about a payment, reflects PaymentInfo DB table.
    /// </summary>
    public class PaymentView
    {
        /// <summary>
        /// Gets or sets PaymentID
        /// </summary>
        public int PaymentID { get; set; }

        /// <summary>
        /// Gets or sets reference to Payment Metadata ID
        /// </summary>
        public int PaymentMetadataID_FK { get; set; }

        /// <summary>
        /// Gets or sets Payment Sum
        /// </summary>
        public int PaymentSum { get; set; }

        /// <summary>
        /// Gets or sets Description for the payment
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Gets or sets the TransactionID
        /// </summary>
        public string TransactionId { get; set; }

        /// <summary>
        /// Gets or sets the OrderID
        /// </summary>
        public string OrderId { get; set; }

        /// <summary>
        /// Gets or sets reference to ReporteeElementID
        /// </summary>
        public int ReporteeElementId { get; set; }

        /// <summary>
        /// Gets or sets Created Date
        /// </summary>
        public DateTime CreatedDate { get; set; }

        /// <summary>
        /// Gets or sets Last Update Date
        /// </summary>
        public DateTime LastUpdatedDate { get; set; }

        /// <summary>
        /// Gets or sets the Status for the payment
        /// </summary>
        public int Status { get; set; }

        /// <summary>
        /// Gets or sets ClientIP
        /// </summary>
        public string ClientIP { get; set; }

        /// <summary>
        /// Gets or sets PartyID
        /// </summary>
        public int PartyId { get; set; }

        /// <summary>
        /// Gets or sets Reference
        /// </summary>
        public string Reference { get; set; }
    }
}
