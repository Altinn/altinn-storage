using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Models.Metrics;
using Altinn.Platform.Storage.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Altinn.Platform.Storage.Controllers;

/// <summary>
/// Metrics controller for handling methods for instances
/// </summary>
[Route("storage/api/v1/metrics")]
[ApiController]
public class MetricsController(IMetricsService metricsService, ILogger<MetricsController> logger)
    : ControllerBase
{
    /// <summary>
    /// Endpoint for triggering generation of daily instance metrics
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation that returns an <see cref="ActionResult"/>.</returns>
    [HttpGet("instances")]
    [ApiExplorerSettings(IgnoreApi = true)]
    [Authorize(Policy = "PlatformAccess")]
    public async Task<ActionResult> GetDailyInstanceStatistics(CancellationToken cancellationToken)
    {
        try
        {
            var data = await metricsService.GetDailyInstanceMetrics(cancellationToken);
            return await BuildParquetResponseAsync(data, cancellationToken);
        }
        catch (Exception e) when (e is DataException)
        {
            logger.LogError(e, "Appid format is invalid");
            return StatusCode(
                500,
                "Unable to get daily instance statistics, AppId format is invalid"
            );
        }
        catch (Exception e) when (e is InvalidOperationException)
        {
            logger.LogError(e, "CDN failure");
            return StatusCode(500, "Unable to get daily instance statistics, CDN failure");
        }
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
        Response.Headers["X-Generated-At"] = response.GeneratedAt.ToString("O");

        return File(response.FileStream, "application/octet-stream", response.FileName);
    }
}
