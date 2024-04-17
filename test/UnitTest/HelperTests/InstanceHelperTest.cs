using System;
using System.Collections.Generic;
using System.Linq;

using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.UnitTest.Extensions;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest
{
    /// <summary>
    /// This is a test class for InstanceHelper and is intended
    /// to contain all InstanceHelper Unit Tests
    /// </summary>
    public class InstanceHelperTest
    {
        /// <summary>
        /// Scenario: Converting list containing a single instance whith id {instanceOwner}/{instanceGuid}
        /// Expected: The instance is converted to a message box instance
        /// Success: MessageBoxInstance Id equals {instanceGuid}
        /// </summary>
        [Fact]
        public void ConvertToMessageBoxInstance_TC01()
        {
            // Arrange
            string instanceOwner = "instanceOwner";
            string instanceGuid = Guid.NewGuid().ToString();
            Instance instance = TestData.Instance_1_1.Clone();
            instance.Id = $"{instanceOwner}/{instanceGuid}";

            // Act
            MessageBoxInstance actual = InstanceHelper.ConvertToMessageBoxInstance(instance);

            // Assert
            Assert.Equal(instanceGuid, actual.Id);
            Assert.Equal(2, actual.DataValues.Count);
        }

        /// <summary>
        /// Scenario: Convert instance
        /// Expected: The LastChangedBy in MessageBoxInstance comes from the instance itself
        /// Success: MessageBoxInstance LastChangedBy equals {lastChangedBy}
        /// </summary>
        [Fact]
        public void ConvertToMessageBoxInstance_TC02()
        {
            // Arrange
            string lastChangedBy = "20000000";
            Instance instance = TestData.Instance_1_1.Clone();

            // Act
            MessageBoxInstance actual = InstanceHelper.ConvertToMessageBoxInstance(instance);

            // Assert
            Assert.Equal(lastChangedBy, actual.LastChangedBy);
        }

        /// <summary>
        /// Scenario: Converting list containing a single instance with data element, where LastChanged for data element is older than LastChanged for instance.
        /// Expected: The LastChangedBy in MessageBoxInstance comes from data element
        /// Success: MessageBoxInstance LastChangedBy equals {lastChangedBy}
        /// </summary>
        [Fact]
        public void ConvertToMessageBoxSingleInstance_TC03()
        {
            // Arrange
            string lastChangedBy = TestData.UserId_1;
            Instance instance = TestData.Instance_1_1.Clone();
            instance.Data = new List<DataElement>()
            {
                new DataElement()
                {
                    LastChanged = Convert.ToDateTime("2019-08-21T19:19:22.2135489Z"),
                    LastChangedBy = lastChangedBy
                }
            };

            // Act
            MessageBoxInstance actual = InstanceHelper.ConvertToMessageBoxInstance(instance);
            string actualLastChangedBy = actual.LastChangedBy;

            // Assert
            Assert.Equal(lastChangedBy, actualLastChangedBy);
        }

        /// <summary>
        /// Scenario: Getting sbl status for a given instance when the current task is Task_1
        /// Expected: The SBL status "FormFilling" is returned
        /// Success: SBL status is as expected
        /// </summary>
        [Fact]
        public void GetSBLStatusForCurrentTask_data_IsConvertedToFormFilling()
        {
            Instance instance = TestData.Instance_1_Status_1;
            string sblStatus = InstanceHelper.GetSBLStatusForCurrentTask(instance);
            Assert.Equal("FormFilling", sblStatus);
        }

        /// <summary>
        /// Scenario: Getting sbl status for a given instance when the current task is null
        /// and the process.ended is not null and the status.archived is null
        /// Expected: The SBL status "Submit" is returned
        /// Success: SBL status is as expected
        /// </summary>
        [Fact]
        public void GetSBLStatusForCurrentTask_EndedNotArchived_IsConvertedToSubmit()
        {
            Instance instance = TestData.Instance_1_Status_2;
            string sblStatus = InstanceHelper.GetSBLStatusForCurrentTask(instance);
            Assert.Equal("Submit", sblStatus);
        }

        /// <summary>
        /// Scenario: Getting sbl status for a given instance when the current task is null
        /// and the process.ended is not null and the status.archived is not null
        /// Expected: The SBL status "Archived" is returned
        /// Success: SBL status is as expected
        /// </summary>
        [Fact]
        public void GetSBLStatusForCurrentTask_EndedAndArchived_IsConvertedToArchived()
        {
            Instance instance = TestData.Instance_1_Status_3;
            string sblStatus = InstanceHelper.GetSBLStatusForCurrentTask(instance);
            Assert.Equal("Archived", sblStatus);
        }

        /// <summary>
        /// Scenario: Getting sbl status for a given instance when the process is null
        /// and the process.ended is null and the status.archived is null
        /// Expected: The SBL status "default" is returned
        /// Success: SBL status is as expected
        /// </summary>
        [Fact]
        public void GetSBLStatusForCurrentTask_MissingProcessState_IsConvertedToDefault()
        {
            Instance instance = TestData.Instance_1_Status_4;
            string sblStatus = InstanceHelper.GetSBLStatusForCurrentTask(instance);
            Assert.Equal("default", sblStatus);
        }

        /// <summary>
        /// Scenario: Getting sbl status for an instance in a confirmation step
        /// Expected: The SBL status "Confirmation" is returned
        /// Success: SBL status is as expected
        /// </summary>
        [Fact]
        public void GetSBLStatusForCurrentTask_Confirmation()
        {
            Instance instance = new Instance
            {
                Process = new ProcessState
                {
                    Started = DateTime.Parse("2021-01-18T16:38:28.3776631Z"),
                    StartEvent = "StartEvent_1",
                    CurrentTask = new ProcessElementInfo
                    {
                        Flow = 3,
                        Started = DateTime.Parse("2021-01-18T16:41:24.6560293Z"),
                        ElementId = "Task_2",
                        Name = "Bekreft skjemadata",
                        AltinnTaskType = "confirmation"
                    }
                }
            };

            string sblStatus = InstanceHelper.GetSBLStatusForCurrentTask(instance);
            Assert.Equal("Confirmation", sblStatus);
        }

        /// <summary>
        /// Scenario: Getting sbl status for an instance in a confirmation step
        /// Expected: The SBL status "Confirmation" is returned
        /// Success: SBL status is as expected
        /// </summary>
        [Fact]
        public void GetSBLStatusForCurrentTask_Feedback()
        {
            Instance instance = new Instance
            {
                Process = new ProcessState
                {
                    Started = DateTime.Parse("2021-01-18T16:38:28.3776631Z"),
                    StartEvent = "StartEvent_1",
                    CurrentTask = new ProcessElementInfo
                    {
                        Flow = 3,
                        Started = DateTime.Parse("2021-01-18T16:41:24.6560293Z"),
                        ElementId = "Task_2",
                        Name = "Venter på tilbakemelding",
                        AltinnTaskType = "feedback"
                    }
                }
            };

            string sblStatus = InstanceHelper.GetSBLStatusForCurrentTask(instance);
            Assert.Equal("Feedback", sblStatus);
        }

        /// <summary>
        /// Scenario: Getting sbl status for an instance in a signing step
        /// Expected: The SBL status "Signing" is returned
        /// Success: SBL status is as expected
        /// </summary>
        [Fact]
        public void GetSBLStatusForCurrentTask_Signing()
        {
            Instance instance = new Instance
            {
                Process = new ProcessState
                {
                    Started = DateTime.Parse("2021-01-18T16:38:28.3776631Z"),
                    StartEvent = "StartEvent_1",
                    CurrentTask = new ProcessElementInfo
                    {
                        Flow = 3,
                        Started = DateTime.Parse("2021-01-18T16:41:24.6560293Z"),
                        ElementId = "Task_2",
                        Name = "Signering",
                        AltinnTaskType = "signing"
                    }
                }
            };

            string sblStatus = InstanceHelper.GetSBLStatusForCurrentTask(instance);
            Assert.Equal("Signing", sblStatus);
        }

        /// <summary>
        /// Scenario: Find last changed by from the instance and date elements
        /// Expected: lastChangedBy is an user id is from the instance without dateelements
        /// Success: lastChangedBy equals {expectedlastChangedBy} and lastchanged equals {expectedlastChanged}
        /// </summary>
        [Fact]
        public void FindLastChangedBy_TC01()
        {
            // Arrange
            Instance instance = TestData.Instance_2_2;
            string expectedlastChangedBy = "20000000";
            DateTime expectedlastChanged = Convert.ToDateTime("2019-08-20T19:19:22.2135489Z");

            // Act
            (string lastChangedBy, DateTime? lastChanged) = InstanceHelper.FindLastChanged(instance);

            // Assert
            Assert.Equal(expectedlastChangedBy, lastChangedBy);
            Assert.Equal(expectedlastChanged, lastChanged);
        }

        /// <summary>
        /// Scenario: Find last changed by from the instance and date elements
        /// Expected: lastChangedBy is an user id from one dataelement
        /// Success: lastChangedBy equals {expectedlastChangedBy} and lastchanged equals {expectedlastChanged}
        /// </summary>
        [Fact]
        public void FindLastChangedBy_TC02()
        {
            // Arrange
            Instance instance = TestData.Instance_1_2;
            string expectedlastChangedBy = "20000001";
            DateTime expectedlastChanged = Convert.ToDateTime("2019-09-20T21:19:22.2135489Z");

            // Act
            (string lastChangedBy, DateTime? lastChanged) = InstanceHelper.FindLastChanged(instance);

            // Assert
            Assert.Equal(expectedlastChangedBy, lastChangedBy);
            Assert.Equal(expectedlastChanged, lastChanged);
        }

        /// <summary>
        /// Scenario: Find last changed by from the instance and date elements
        /// Expected: lastChangedBy is an user id from the dataelements list that has the latest lastChanged datetime
        /// Success: lastChangedBy equals {expectedlastChangedBy} and lastchanged equals {expectedlastChanged}
        /// </summary>
        [Fact]
        public void FindLastChangedBy_TC03()
        {
            // Arrange
            Instance instance = TestData.Instance_2_1;
            string expectedlastChangedBy = "20000001";
            DateTime expectedlastChanged = Convert.ToDateTime("2019-10-20T21:19:22.2135489Z").ToUniversalTime();

            // Act
            (string lastChangedBy, DateTime? lastChanged) = InstanceHelper.FindLastChanged(instance);

            // Assert
            Assert.Equal(expectedlastChangedBy, lastChangedBy);
            Assert.Equal(expectedlastChanged, lastChanged);
        }

        /// <summary>
        /// Call Remove instances, list stays the same as no conditions are met.
        /// </summary>
        [Fact]
        public void RemoveHiddenInstances_NoHideConditionsMet()
        {
            // Arrange
            Dictionary<string, Application> apps = new();

            apps.Add("ttd/no-hideSettings", new Application { Id = "ttd/no-hideSettings" });
            apps.Add("ttd/hide-task-1", new Application
            {
                Id = "ttd/hide-task-1",
                MessageBoxConfig = new()
                {
                    HideSettings = new()
                    {
                        HideOnTask = new()
                        {
                            "Task_1"
                        }
                    }
                }
            });

            Instance i1 = new Instance
            {
                AppId = "ttd/no-hideSettings"
            };

            Instance i2 = new Instance
            {
                AppId = "ttd/hide-task-1",
                Process = new ProcessState
                {
                    CurrentTask = new ProcessElementInfo
                    {
                        ElementId = "Task_2"
                    }
                }
            };

            List<Instance> instances = new() { i1, i2 };

            // Act
            InstanceHelper.RemoveHiddenInstances(apps, instances);

            // Assert
            Assert.Equal(2, instances.Count);
        }

        /// <summary>
        /// Call Remove instances, single instance is removed as it is on a task in the hide list.
        /// </summary>
        [Fact]
        public void RemoveHiddenInstances_TaskConditionMet_OneInstanceRemoved()
        {
            // Arrange
            Dictionary<string, Application> apps = new();

            apps.Add("ttd/no-hideSettings", new Application { Id = "ttd/no-hideSettings" });
            apps.Add("ttd/hide-task-1", new Application
            {
                Id = "ttd/hide-task-1",
                MessageBoxConfig = new()
                {
                    HideSettings = new()
                    {
                        HideOnTask = new()
                        {
                            "Task_1"
                        }
                    }
                }
            });

            Instance i1 = new Instance
            {
                AppId = "ttd/no-hideSettings"
            };

            Instance i2 = new Instance
            {
                AppId = "ttd/hide-task-1",
                Process = new ProcessState
                {
                    CurrentTask = new ProcessElementInfo
                    {
                        ElementId = "Task_1"
                    }
                }
            };

            List<Instance> instances = new() { i1, i2 };

            // Act
            InstanceHelper.RemoveHiddenInstances(apps, instances);

            // Assert
            Assert.Single(instances);
        }

        /// <summary>
        /// Call Remove instances, all instances removed as the app is marked with hideAlways.
        /// </summary>
        [Fact]
        public void RemoveHiddenInstances_AlwaysConditionMet_AllInstancsRemoved()
        {
            // Arrange
            Dictionary<string, Application> apps = new();
            apps.Add(
                "ttd/hideAlwayshideSettings",
                new Application
                {
                    Id = "ttd/hideAlwayshideSettings",
                    MessageBoxConfig = new MessageBoxConfig()
                    {
                        HideSettings = new()
                        {
                            HideAlways = true
                        }
                    }
                });

            Instance i1 = new Instance
            {
                AppId = "ttd/hideAlwayshideSettings"
            };

            Instance i2 = new Instance
            {
                AppId = "ttd/hideAlwayshideSettings"
            };

            Instance i3 = new Instance
            {
                AppId = "ttd/hideAlwayshideSettings"
            };

            List<Instance> instances = new() { i1, i2, i3 };

            // Act
            InstanceHelper.RemoveHiddenInstances(apps, instances);

            // Assert
            Assert.Empty(instances);
        }

        /// <summary>
        /// Replaces text keys, appName key is available, should be used as Title
        /// </summary>
        [Fact]
        public void ReplaceTextKeys_AppNameAvailable_AppNameKeyUsedAsTitle()
        {
            // Arrange
            List<MessageBoxInstance> instances = new();
            instances.Add(new MessageBoxInstance
            {
                Org = "ttd",
                AppName = "test-app"
            });

            List<TextResource> textResources = new();
            List<TextResourceElement> textResource = new();
            textResource.Add(new TextResourceElement
            {
                Value = "ValueFromAppNameKey",
                Id = "appName",
            });
            textResource.Add(new TextResourceElement
            {
                Value = "ValueFromServiceNameKey",
                Id = "ServiceName",
            });

            textResources.Add(new TextResource
            {
                Id = "ttd-test-app-nb",
                Resources = textResource
            });

            // Act
            instances = InstanceHelper.ReplaceTextKeys(instances, textResources, "nb");

            // Assert
            Assert.Equal("ValueFromAppNameKey", instances[0].Title);
        }

        /// <summary>
        /// Replaces text keys, appName key is not available, should default to ServiceName
        /// </summary>
        [Fact]
        public void ReplaceTextKeys_AppNameNotAvailable_ServiceNameKeyUsedAsTitle()
        {
            // Arrange
            List<MessageBoxInstance> instances = new();
            instances.Add(new MessageBoxInstance
            {
                Org = "ttd",
                AppName = "test-app"
            });

            List<TextResource> textResources = new();
            List<TextResourceElement> textResource = new();
            textResource.Add(new TextResourceElement
            {
                Value = "ValueFromServiceNameKey",
                Id = "ServiceName",
            });

            textResources.Add(new TextResource
            {
                Id = "ttd-test-app-nb",
                Resources = textResource
            });

            // Act
            instances = InstanceHelper.ReplaceTextKeys(instances, textResources, "nb");

            // Assert
            Assert.Equal("ValueFromServiceNameKey", instances[0].Title);
        }

        [Fact]
        public void ConvertToSBLInstanceEvent_SingleEvent_AllPropertiesMapped()
        {
            // Arrange
            List<InstanceEvent> input = new()
            {
                new InstanceEvent
                {
                    Id = Guid.Parse("64f6d272-3700-4616-beea-931361d10fc8"),
                    User = new()
                    {
                        UserId = 1337
                    },
                    EventType = "test.event",
                    Created = DateTime.Now
                }
            };

            // Act
            var actual = InstanceHelper.ConvertToSBLInstanceEvent(input)[0];

            // Assert
            Assert.NotNull(actual);
            Assert.Equal("64f6d272-3700-4616-beea-931361d10fc8", actual.Id.ToString());
            Assert.Equal(1337, actual.User.UserId);
            Assert.Equal("test.event", actual.EventType);
        }

        [Theory]
        [InlineData(null, "", "")]
        [InlineData("", "", "")]
        [InlineData("person12345", "", "")]
        [InlineData("invalid:12345", "", "")]
        [InlineData("PERSON:12345", "person", "12345")]
        [InlineData("organisation:  12345  ", "organisation", "12345")]
        [InlineData("  person:12345", "person", "12345")]
        [InlineData("Person:12345", "person", "12345")]
        [InlineData("organisation: 123 45", "organisation", "12345")]
        [InlineData("organisation:12345", "organisation", "12345")]
        [InlineData("organisation:67890", "organisation", "67890")]
        [InlineData(" organisation : 456 78", "organisation", "45678")]
        public void GetIdentifierFromInstanceOwnerIdentifier_ValidInput_ReturnsCorrectTuple(string instanceOwnerIdentifier, string expectedType, string expectedValue)
        {
            // Act
            var result = InstanceHelper.GetIdentifierFromInstanceOwnerIdentifier(instanceOwnerIdentifier);

            // Assert
            Assert.Equal((expectedType, expectedValue), result);
        }

        [Theory]
        [InlineData("person", "123456789", "123456789", null)]
        [InlineData("organisation", "123456789", null, "123456789")]
        [InlineData("organisation", null, null, null)]
        [InlineData("person", null, null, null)]
        [InlineData("invalid_type", "value_not_returned", null, null)]
        public void SeparatePersonAndOrgNo_ReturnsCorrectValues(string instanceOwnerIdType, string instanceOwnerIdValue, string expectedPersonNo, string expectedOrgNo)
        {
            // Arrange & Act
            var result = InstanceHelper.SeparatePersonAndOrgNo(instanceOwnerIdType, instanceOwnerIdValue);

            // Assert
            Assert.Equal(expectedPersonNo, result.PersonNo);
            Assert.Equal(expectedOrgNo, result.OrgNo);
        }
    }
}
