using System;
using System.Collections.Generic;
using System.Linq;

using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;

using Xunit;

namespace Altinn.Platform.Storage.UnitTest.HelperTests
{
    public class DataElementHelperTests
    {
        [Fact]
        public void CreateDataElement_GeneratedFromIdsProvided_DataElementReferencesPopulated()
        {
            // AAct
            var actual = DataElementHelper.CreateDataElement("dataType", null, new Instance { AppId = "ttd/app-test", Id = $"1337/{Guid.NewGuid()}" }, DateTime.UtcNow, "application/json", "file-name.json", 1234, "12345", new List<Guid> { Guid.NewGuid(), Guid.NewGuid() });

            // Assert
            Assert.NotEmpty(actual.References);
            Assert.Equal(RelationType.GeneratedFrom, actual.References.First().Relation);
            Assert.Equal(ReferenceType.DataElement, actual.References.First().ValueType);
        }

        [Fact]
        public void CreateDataElement_NoGeneratedFromIdsProvided_DataElementReferencesIsNull()
        {
            // Act
            var actual = DataElementHelper.CreateDataElement("dataType", null, new Instance { AppId = "ttd/app-test", Id = $"1337/{Guid.NewGuid()}" }, DateTime.UtcNow, "application/json", "file-name.json", 1234, "12345", null);

            // Assert
            Assert.Null(actual.References);
        }
    }
}
