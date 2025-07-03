using System;
using Altinn.Platform.Storage.Helpers;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.HelperTests
{
    /// <summary>
    /// This is a test class for InstanceHelper and is intended
    /// to contain all InstanceHelper Unit Tests
    /// </summary>
    public class MessageBoxQueryModelTests
    {
        [Fact]
        public void CloneWithEmptyInstanceOwnerPartyIdList_WhenAllPropertiesSet_ClonesSuccessfully()
        {
            // Arrange
            var model = new MessageBoxQueryModel()
            {
                InstanceOwnerPartyIdList = [100, 101],
                AppId = "some_app_id",
                IncludeActive = true,
                IncludeArchived = false,
                IncludeDeleted = true,
                FromLastChanged = DateTime.Parse("2021-04-07T05:16:47.7911165Z"),
                ToLastChanged = DateTime.Parse("2021-04-10T05:16:47.7911165Z"),
                FromCreated = null,
                ToCreated = null,
                SearchString = "app",
                ArchiveReference = "123",
                Language = "en",
                FilterMigrated = true
            };

            // Act
            var clone = model.CloneWithEmptyInstanceOwnerPartyIdList();

            // Assert
            Assert.Empty(clone.InstanceOwnerPartyIdList);
            Assert.Equal(model.AppId, clone.AppId);
            Assert.Equal(model.IncludeActive, clone.IncludeActive);
            Assert.Equal(model.IncludeArchived, clone.IncludeArchived);
            Assert.Equal(model.FromLastChanged, clone.FromLastChanged);
            Assert.Equal(model.ToLastChanged, clone.ToLastChanged);
            Assert.Equal(model.FromCreated, clone.FromCreated);
            Assert.Equal(model.ToCreated, clone.ToCreated);
            Assert.Equal(model.SearchString, clone.SearchString);
            Assert.Equal(model.ArchiveReference, clone.ArchiveReference);
            Assert.Equal(model.Language, clone.Language);
            Assert.Equal(model.FilterMigrated, clone.FilterMigrated);
        }

        [Fact]
        public void CloneWithEmptyInstanceOwnerPartyIdList_WhenOnlyEmptyInstanceOwnerPartyIdListSet_ClonesSuccessfully()
        {
            // Arrange
            var model = new MessageBoxQueryModel()
            {
                InstanceOwnerPartyIdList = []
            };

            // Act
            var clone = model.CloneWithEmptyInstanceOwnerPartyIdList();

            // Assert
            Assert.Empty(clone.InstanceOwnerPartyIdList);
        }
    }
}
