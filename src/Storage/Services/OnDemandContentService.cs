#nullable disable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Clients;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Altinn.Platform.Storage.Services;

/// <inheritdoc/>
public class OnDemandContentService : IOnDemandContentService
{
    private const string _primaryFontFamily = "segoe wp";
    private const string _fallbackFontFamily = "Arial";
    private const int _fontSize = 9;

    // The watermark numbers are in XUnit points (1/72 inch)
    private const int _watermarkWidth = 15;
    private const int _watermarkHeigth = 155;
    private const int _watermarkMarginW = 18;
    private const int _watermarkMarginH = 5;
    private const int _watermarkPosition = _watermarkHeigth + _watermarkMarginH;

    private readonly IInstanceRepository _instanceRepository;
    private readonly IBlobRepository _blobRepository;
    private readonly IA2Repository _a2Repository;
    private readonly IApplicationRepository _applicationRepository;
    private readonly GeneralSettings _generalSettings;
    private readonly IA2OndemandFormattingService _a2OndemandFormattingService;
    private readonly IPdfGeneratorClient _pdfGeneratorClient;
    private readonly ICompositeViewEngine _viewEngine;
    private readonly ITempDataProvider _tempDataProvider;
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="OnDemandContentService"/> class
    /// </summary>
    /// <param name="instanceRepository">the instance repository handler</param>
    /// <param name="blobRepository">the blob repository handler</param>
    /// <param name="a2Repository">the a2 repository handler</param>
    /// <param name="applicationRepository">the application repository handler</param>
    /// <param name="settings">the general settings.</param>
    /// <param name="a2OndemandFormattingService">a2OndemandFormattingService</param>
    /// <param name="pdfGeneratorClient">pdfGeneratorClient</param>
    /// <param name="viewEngine">the view engine used to render the signature and payment views</param>
    /// <param name="tempDataProvider">tempDataProvider</param>
    /// <param name="serviceProvider">used to back the view context when there is no request in flight</param>
    public OnDemandContentService(
        IInstanceRepository instanceRepository,
        IBlobRepository blobRepository,
        IA2Repository a2Repository,
        IApplicationRepository applicationRepository,
        IOptions<GeneralSettings> settings,
        IA2OndemandFormattingService a2OndemandFormattingService,
        IPdfGeneratorClient pdfGeneratorClient,
        ICompositeViewEngine viewEngine,
        ITempDataProvider tempDataProvider,
        IServiceProvider serviceProvider
    )
    {
        _instanceRepository = instanceRepository;
        _blobRepository = blobRepository;
        _a2Repository = a2Repository;
        _applicationRepository = applicationRepository;
        _generalSettings = settings.Value;
        _a2OndemandFormattingService = a2OndemandFormattingService;
        _pdfGeneratorClient = pdfGeneratorClient;
        _viewEngine = viewEngine;
        _tempDataProvider = tempDataProvider;
        _serviceProvider = serviceProvider;
    }

    static OnDemandContentService()
    {
        // Use font resolver and font from the PdfSharp demo code to avoid installing fonts
        // on the alpine image.
        GlobalFontSettings.FontResolver = new SegoeWpFontResolver();
    }

    /// <inheritdoc/>
    public Task<Stream> GetContent(
        string kind,
        string app,
        Guid instanceGuid,
        Guid dataGuid,
        string language,
        CancellationToken cancellationToken
    ) =>
        kind switch
        {
            "signature" => GetSignatureAsHtml(instanceGuid, cancellationToken),
            "payment" => GetPaymentAsHtml(instanceGuid, cancellationToken),
            "formdatapdf" => GetFormdataAsPdf(
                app,
                instanceGuid,
                dataGuid,
                language,
                cancellationToken
            ),
            "formdatahtml" => GetFormdataAsHtml(
                app,
                instanceGuid,
                dataGuid,
                language,
                cancellationToken
            ),
            "formsummaryhtml" => GetFormSummaryAsHtml(
                app,
                instanceGuid,
                dataGuid,
                language,
                cancellationToken
            ),
            _ => Task.FromResult<Stream>(null),
        };

    /// <inheritdoc/>
    public async Task<Stream> GetSignatureAsHtml(
        Guid instanceGuid,
        CancellationToken cancellationToken
    )
    {
        InstanceInternal instance = await _instanceRepository.GetOne(
            instanceGuid,
            true,
            cancellationToken
        );
        if (instance is null)
        {
            return null;
        }

        Application application = await _applicationRepository.FindOne(
            instance.AppId,
            instance.Org,
            cancellationToken
        );
        DataElementInternal signatureElement = instance.Data.First(d =>
            d.DataType == "signature-data"
        );
        DataElementHelper.EnsureExpectedBlobStoragePath(
            signatureElement,
            instanceGuid,
            instance.AppId
        );

        List<SignatureView> view = await JsonSerializer.DeserializeAsync<List<SignatureView>>(
            await _blobRepository.ReadBlob(
                $"{(_generalSettings.A2UseTtdAsServiceOwner ? "ttd" : instance.Org)}",
                signatureElement.BlobStoragePath,
                application.StorageAccountNumber,
                cancellationToken
            ),
            (JsonSerializerOptions)null,
            cancellationToken
        );

        return await RenderView("Signature", view);
    }

    /// <inheritdoc/>
    public async Task<Stream> GetPaymentAsHtml(
        Guid instanceGuid,
        CancellationToken cancellationToken
    )
    {
        InstanceInternal instance = await _instanceRepository.GetOne(
            instanceGuid,
            true,
            cancellationToken
        );
        if (instance is null)
        {
            return null;
        }

        Application application = await _applicationRepository.FindOne(
            instance.AppId,
            instance.Org,
            cancellationToken
        );
        DataElementInternal paymentElement = instance.Data.First(d => d.DataType == "payment-data");
        DataElementHelper.EnsureExpectedBlobStoragePath(
            paymentElement,
            instanceGuid,
            instance.AppId
        );

        PaymentView view = await JsonSerializer.DeserializeAsync<PaymentView>(
            await _blobRepository.ReadBlob(
                $"{(_generalSettings.A2UseTtdAsServiceOwner ? "ttd" : instance.Org)}",
                paymentElement.BlobStoragePath,
                application.StorageAccountNumber,
                cancellationToken
            ),
            (JsonSerializerOptions)null,
            cancellationToken
        );

        return await RenderView("Payment", view);
    }

    /// <inheritdoc/>
    public async Task<Stream> GetFormdataAsPdf(
        string app,
        Guid instanceGuid,
        Guid dataGuid,
        string language,
        CancellationToken cancellationToken
    )
    {
        InstanceInternal instance = await _instanceRepository.GetOne(
            instanceGuid,
            true,
            cancellationToken
        );
        if (instance is null)
        {
            return null;
        }

        DataElementInternal htmlElement = instance.Data.First(d => d.Id == dataGuid);
        string htmlFormId = htmlElement.Metadata.First(m => m.Key == "formid").Value;
        DataElementInternal xmlElement = instance.Data.First(d =>
            d.Metadata?.First(m => m.Key == "formid").Value == htmlFormId && d.Id != htmlElement.Id
        );
        string visiblePagesString = xmlElement
            .Metadata.FirstOrDefault(m => m.Key == "A2VisiblePages")
            ?.Value;
        List<int> visiblePages = !string.IsNullOrEmpty(visiblePagesString)
            ? visiblePagesString.Split(';').Select(int.Parse).ToList()
            : null;

        int lformid = int.Parse(xmlElement.Metadata.First(m => m.Key == "lformid").Value);
        PrintViewXslBEList printViews = [];
        int pageNumber = 1;
        foreach (
            (string view, bool isPortrait) in await _a2Repository.GetXsls(
                instance.Org,
                app,
                lformid,
                language,
                3
            )
        )
        {
            if (visiblePages == null || visiblePages.Contains(pageNumber))
            {
                printViews.Add(
                    new PrintViewXslBE()
                    {
                        PrintViewXsl = view,
                        Id = $"{lformid}-{pageNumber}{language}",
                        IsPortrait = isPortrait,
                        PageNumber = pageNumber,
                    }
                );
            }

            ++pageNumber;
        }

        printViews[^1].LastPage = true;

        using var mergedDoc = new PdfDocument();
        foreach (var view in printViews)
        {
            (string html, PrintViewXslBEList updatedViews) = await GetFormdataAsHtmlString(
                app,
                instanceGuid,
                dataGuid,
                language,
                3,
                cancellationToken,
                view.PageNumber
            );
            if (html is null)
            {
                return null;
            }

            var pdfPages = await _pdfGeneratorClient.GeneratePdf(
                html,
                view.IsPortrait,
                GetScale(updatedViews[0])
            );
            using var pageDoc = PdfReader.Open(pdfPages, PdfDocumentOpenMode.Import);
            for (var i = 0; i < pageDoc.PageCount; i++)
            {
                pageDoc.Pages[i].Orientation = view.IsPortrait
                    ? PdfSharp.PageOrientation.Portrait
                    : PdfSharp.PageOrientation.Landscape;
                mergedDoc.AddPage(pageDoc.Pages[i]);
            }
        }

        MemoryStream pdfStream = new();
        mergedDoc.Save(pdfStream);

        DateTime created;
        if (instance.DataValues.TryGetValue("A2ArchRefTs", out string a2ArchRefTs))
        {
            created = DateTime.ParseExact(
                a2ArchRefTs,
                "dd-MM-yyyy HH:mm:ss",
                CultureInfo.InvariantCulture
            );
        }
        else
        {
            created = ((DateTime)instance.Created).ToLocalTime();
        }

        string timestampFormat =
            language == "en" ? "MM/dd/yyyy hh:mm:ss tt" : "dd.MM.yyyy HH:mm:ss";
        string watermark =
            created.ToString(timestampFormat, CultureInfo.InvariantCulture)
            + $" AR{instance.DataValues["A2ArchRef"]}";

        using var finalPdfDocument = PdfReader.Open(pdfStream, PdfDocumentOpenMode.Modify);
        AddWaterMarksAndPageNumber(finalPdfDocument, watermark);
        MemoryStream finalPdfStream = new();
        finalPdfDocument.Save(finalPdfStream);
        return finalPdfStream;
    }

    /// <inheritdoc/>
    public async Task<Stream> GetFormdataAsHtml(
        string app,
        Guid instanceGuid,
        Guid dataGuid,
        string language,
        CancellationToken cancellationToken,
        int singlePageNr = -1
    )
    {
        (Stream html, _) = await GetFormdataAsHtmlStream(
            app,
            instanceGuid,
            dataGuid,
            language,
            3,
            cancellationToken,
            singlePageNr
        );

        return html;
    }

    /// <inheritdoc/>
    public async Task<Stream> GetFormSummaryAsHtml(
        string app,
        Guid instanceGuid,
        Guid dataGuid,
        string language,
        CancellationToken cancellationToken
    )
    {
        (Stream html, _) = await GetFormdataAsHtmlStream(
            app,
            instanceGuid,
            dataGuid,
            language,
            2,
            cancellationToken
        );

        return html;
    }

    private async Task<Stream> RenderView<TModel>(string viewName, TModel model)
    {
        ViewEngineResult viewResult = _viewEngine.GetView(
            executingFilePath: null,
            viewPath: $"/Views/ContentOnDemand/{viewName}.cshtml",
            isMainPage: true
        );
        if (!viewResult.Success)
        {
            throw new InvalidOperationException($"Unable to locate the on demand view {viewName}.");
        }

        HttpContext httpContext = new DefaultHttpContext { RequestServices = _serviceProvider };
        ActionContext actionContext = new(httpContext, new RouteData(), new ActionDescriptor());

        await using StringWriter writer = new();
        ViewContext viewContext = new(
            actionContext,
            viewResult.View,
            new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
            {
                Model = model,
            },
            new TempDataDictionary(httpContext, _tempDataProvider),
            writer,
            new HtmlHelperOptions()
        );

        await viewResult.View.RenderAsync(viewContext);

        return new MemoryStream(Encoding.UTF8.GetBytes(writer.ToString()));
    }

    private async Task<(Stream Html, PrintViewXslBEList Views)> GetFormdataAsHtmlStream(
        string app,
        Guid instanceGuid,
        Guid dataGuid,
        string language,
        int viewType,
        CancellationToken cancellationToken,
        int singlePageNr = -1
    )
    {
        (string html, PrintViewXslBEList views) = await GetFormdataAsHtmlString(
            app,
            instanceGuid,
            dataGuid,
            language,
            viewType,
            cancellationToken,
            singlePageNr
        );
        if (html is null)
        {
            return (null, null);
        }

        return (new MemoryStream(Encoding.UTF8.GetBytes(html)), views);
    }

    private async Task<(string Html, PrintViewXslBEList Views)> GetFormdataAsHtmlString(
        string app,
        Guid instanceGuid,
        Guid dataGuid,
        string language,
        int viewType,
        CancellationToken cancellationToken,
        int singlePageNr = -1
    )
    {
        InstanceInternal instance = await _instanceRepository.GetOne(
            instanceGuid,
            true,
            cancellationToken
        );
        if (instance is null)
        {
            return (null, null);
        }

        Application application = await _applicationRepository.FindOne(
            instance.AppId,
            instance.Org
        );
        DataElementInternal htmlElement = instance.Data.First(d => d.Id == dataGuid);
        string htmlFormId = htmlElement.Metadata.First(m => m.Key == "formid").Value;
        DataElementInternal xmlElement = instance.Data.First(d =>
            d.Metadata?.First(m => m.Key == "formid").Value == htmlFormId && d.Id != htmlElement.Id
        );
        DataElementHelper.EnsureExpectedBlobStoragePath(xmlElement, instanceGuid, instance.AppId);
        string visiblePagesString = xmlElement
            .Metadata.FirstOrDefault(m => m.Key == "A2VisiblePages")
            ?.Value;
        List<int> visiblePages = !string.IsNullOrEmpty(visiblePagesString)
            ? visiblePagesString.Split(';').Select(int.Parse).ToList()
            : null;

        PrintViewXslBEList views = [];
        int lformid = int.Parse(xmlElement.Metadata.First(m => m.Key == "lformid").Value);
        int pageNumber = 1;
        foreach (
            (string view, bool isPortrait) in await _a2Repository.GetXsls(
                instance.Org,
                app,
                lformid,
                language,
                viewType
            )
        )
        {
            if (
                (singlePageNr != -1 && singlePageNr == pageNumber)
                || (
                    singlePageNr == -1
                    && (visiblePages == null || visiblePages.Contains(pageNumber))
                )
            )
            {
                views.Add(
                    new PrintViewXslBE()
                    {
                        PrintViewXsl = view,
                        Id = $"{lformid}-{pageNumber}{language}",
                        IsPortrait = isPortrait,
                        PageNumber = pageNumber,
                    }
                );
            }

            ++pageNumber;
        }

        views[^1].LastPage = true;

        Stream blob = await _blobRepository.ReadBlob(
            $"{(_generalSettings.A2UseTtdAsServiceOwner ? "ttd" : instance.Org)}",
            xmlElement.BlobStoragePath,
            application.StorageAccountNumber,
            cancellationToken
        );

        return (_a2OndemandFormattingService.GetFormdataHtml(views, blob), views);
    }

    private static float GetScale(PrintViewXslBE infoPathViewXslBE)
    {
        float margin = _watermarkMarginW + _watermarkWidth;

        // Set A4 width in XUnit points (1/72 inch)
        float pageWidth = infoPathViewXslBE.IsPortrait ? 595.92f : 842.88f;

        if (infoPathViewXslBE.PdfModificationParams != null)
        {
            // Set html width in XUnit points (1/72 inch). HtmlViewerWidth is in pixels (1/96 inch)
            float htmlWidth = infoPathViewXslBE.PdfModificationParams.HtmlViewerWidth * 72 / 96.0f;
            return (pageWidth - (2 * margin)) / htmlWidth;
        }

        return 1;
    }

    private static void AddWaterMarksAndPageNumber(PdfDocument document, string watermark)
    {
        for (var idx = 0; idx < document.Pages.Count; idx++)
        {
            var page = document.Pages[idx];

            // Get an XGraphics object for drawing beneath the existing content.
            using XGraphics gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);

            var state = gfx.Save();
            DrawWatermark(
                gfx,
                page.Width.Point - _watermarkMarginW,
                _watermarkPosition,
                -90,
                watermark
            );
            gfx.Restore(state);
            state = gfx.Save();
            DrawWatermark(
                gfx,
                _watermarkMarginW,
                page.Height.Point - _watermarkPosition,
                90,
                watermark
            );
            gfx.Restore(state);
            gfx.DrawString(
                (idx + 1).ToString(),
                GetFont(),
                XBrushes.Black,
                new XPoint(page.Width.Point - 23, page.Height.Point - 5)
            );
        }
    }

    private static void DrawWatermark(
        XGraphics gfx,
        double x,
        double y,
        double angle,
        string watermark
    )
    {
        XRect rect = new(x, y, _watermarkHeigth, _watermarkWidth);
        XBrush brush = XBrushes.Red;
        XStringFormat format = new()
        {
            Alignment = XStringAlignment.Center,
            LineAlignment = XLineAlignment.Center,
        };
        gfx.RotateAtTransform(angle, new XPoint(x, y));
        gfx.DrawRectangle(XPens.Red, rect);
        gfx.DrawString(watermark, GetFont(), brush, rect, format);
    }

    private static XFont GetFont()
    {
        try
        {
            return new(_primaryFontFamily, _fontSize);
        }
        catch (Exception)
        {
            return new(_fallbackFontFamily, _fontSize);
        }
    }
}
