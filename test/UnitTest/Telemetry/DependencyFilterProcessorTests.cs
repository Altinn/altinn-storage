using Altinn.Platform.Storage.Telemetry;
using Microsoft.AspNetCore.Http;
using Moq;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.Telemetry;

public class DependencyFilterProcessorTests
{
    /// <summary>
    /// Should trace all activities when disableTelemetryForMigration setting is false
    /// </summary>
    [Fact]
    public void ShouldMarkAsRecordedWhenNotDisableMigrationTelemetry()
    {
        // Arrange
        var dependencyFilterProcessor = new RequestFilterProcessor(new Storage.Configuration.GeneralSettings { DisableTelemetryForMigration = false });

        var activity = new System.Diagnostics.Activity("POST Migration");
        activity.ActivityTraceFlags = System.Diagnostics.ActivityTraceFlags.Recorded;

        // Act
        dependencyFilterProcessor.OnEnd(activity);

        // Assert
        Assert.Equal(System.Diagnostics.ActivityTraceFlags.Recorded, activity.ActivityTraceFlags);
    }

    /// <summary>
    /// Should trace all activities when disableTelemetryForMigration setting is false
    /// </summary>
    [Fact]
    public void ShouldMarkAsNoneWhenDisableMigrationTelemetry()
    {
        // Arrange
        Mock<IHttpContextAccessor> httpContextAccessor = new Mock<IHttpContextAccessor>(MockBehavior.Strict);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/storage/api/v1/migration";
        httpContextAccessor.SetupGet(accessor => accessor.HttpContext).Returns(httpContext);

        var dependencyFilterProcessor = new RequestFilterProcessor(new Storage.Configuration.GeneralSettings { DisableTelemetryForMigration = true }, httpContextAccessor.Object);

        var activity = new System.Diagnostics.Activity("Microsoft.AspNetCore.Hosting.HttpRequestIn");
        activity.ActivityTraceFlags = System.Diagnostics.ActivityTraceFlags.Recorded;

        // Act
        dependencyFilterProcessor.OnStart(activity);

        // Assert
        Assert.Equal(System.Diagnostics.ActivityTraceFlags.None, activity.ActivityTraceFlags);
    }

    /// <summary>
    /// Should trace all activities when disableTelemetryForMigration setting is false
    /// </summary>
    [Fact]
    public void ShouldMarkAsRecordenWhenOtherActivityAndNotDisableMigrationTelemetry()
    {
        // Arrange
        var dependencyFilterProcessor = new RequestFilterProcessor(new Storage.Configuration.GeneralSettings { DisableTelemetryForMigration = false });

        var activity = new System.Diagnostics.Activity("Postgres");
        activity.ActivityTraceFlags = System.Diagnostics.ActivityTraceFlags.Recorded;

        // Act
        dependencyFilterProcessor.OnEnd(activity);

        // Assert
        Assert.Equal(System.Diagnostics.ActivityTraceFlags.Recorded, activity.ActivityTraceFlags);
    }

    /// <summary>
    /// Should allow non-migration activities when disableTelemetryForMigration setting is true
    /// </summary>
    [Fact]
    public void ShouldMarkAsRecordenWhenOtherActivityAndDisableMigrationTelemetry()
    {
        // Arrange
        var dependencyFilterProcessor = new RequestFilterProcessor(new Storage.Configuration.GeneralSettings { DisableTelemetryForMigration = true });

        var activity = new System.Diagnostics.Activity("Postgres");
        activity.ActivityTraceFlags = System.Diagnostics.ActivityTraceFlags.Recorded;

        // Act
        dependencyFilterProcessor.OnEnd(activity);

        // Assert
        Assert.Equal(System.Diagnostics.ActivityTraceFlags.Recorded, activity.ActivityTraceFlags);
    }
}
