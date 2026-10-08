using System;
using System.Collections.Generic;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.ModelTests;

public class InstanceMutationDataElementChangesTests
{
    [Fact]
    public void IsEmpty_WithSpecifiedNullFalseZeroOrEmptyValue_ReturnsFalse()
    {
        InstanceMutationDataElementChanges[] changes =
        [
            new() { Locked = false },
            new() { IsRead = false },
            new() { Size = 0 },
            new() { ContentType = Change<string?>.Set(null) },
            new() { Filename = Change<string?>.Set(null) },
            new() { BlobStoragePath = Change<string?>.Set(null) },
            new() { Refs = Change<IReadOnlyList<Guid>?>.Set(null) },
            new() { Refs = Change<IReadOnlyList<Guid>?>.Set([]) },
            new() { References = Change<IReadOnlyList<Reference>?>.Set(null) },
            new() { References = Change<IReadOnlyList<Reference>?>.Set([]) },
            new() { Tags = Change<IReadOnlyList<string>?>.Set(null) },
            new() { Tags = Change<IReadOnlyList<string>?>.Set([]) },
            new() { Metadata = Change<IReadOnlyList<KeyValueEntry>?>.Set(null) },
            new() { Metadata = Change<IReadOnlyList<KeyValueEntry>?>.Set([]) },
            new() { UserDefinedMetadata = Change<IReadOnlyList<KeyValueEntry>?>.Set(null) },
            new() { UserDefinedMetadata = Change<IReadOnlyList<KeyValueEntry>?>.Set([]) },
            new() { DeleteStatus = Change<DeleteStatus?>.Set(null) },
            new() { FileScanResult = default(FileScanResult) },
        ];

        Assert.All(changes, change => Assert.False(change.IsEmpty));
    }

    [Fact]
    public void IsEmpty_WithoutSpecifiedChanges_ReturnsTrue()
    {
        InstanceMutationDataElementChanges changes = new();

        Assert.True(changes.IsEmpty);
    }
}
