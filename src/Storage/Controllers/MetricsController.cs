using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Models.Metrics;
using Altinn.Platform.Storage.Services;
using Microsoft.AspNetCore.Mvc;

namespace Altinn.Platform.Storage.Controllers;

/// <summary>
/// Metrics controller for handling methods for instances
/// </summary>
[Route("storage/api/v1/metrics")]
[ApiController]
public class MetricsController(IMetricsService metricsService) : ControllerBase
{
    /// <summary>
    /// Endpoint for triggering generation of daily instance metrics
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation that returns an <see cref="ActionResult"/>.</returns>
    [HttpGet("instances")]
    [ApiExplorerSettings(IgnoreApi = true)]
    [Produces("application/octet-stream")]
    public async Task<ActionResult> GetDailyInstanceStatistics(CancellationToken cancellationToken)
    {
        var data = await metricsService.GetDailyInstanceMetrics(cancellationToken);
        return await BuildParquetResponseAsync(data, cancellationToken);
    }

    private async Task<ActionResult> BuildParquetResponseAsync<T>(
        DailyMetrics<T> data,
        CancellationToken ct
    )
    {
        MetricsSummary response = await metricsService.GetParquetFile(data, ct);
        HttpContext.Response.RegisterForDispose(response.FileStream);

        Response.Headers["X-File-Hash"] = response.FileHash;
        Response.Headers["X-File-Size"] = response.FileSizeBytes.ToString();
        Response.Headers["X-Total-FileTransfer-Count"] = response.TotalFileTransferCount.ToString();
        Response.Headers["X-Generated-At"] = response.GeneratedAt.ToString("O");

        return File(response.FileStream, "application/octet-stream", response.FileName);
    }
}
