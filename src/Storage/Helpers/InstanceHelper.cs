using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;

namespace Altinn.Platform.Storage.Helpers
{
    /// <summary>
    /// A helper clas to process instance objects
    /// </summary>
    public static class InstanceHelper
    {
        /// <summary>
        /// Converts to a simpler instance object that includes some application metadata
        /// </summary>
        public static MessageBoxInstance ConvertToMessageBoxInstance(Instance instance)
        {
            InstanceStatus status = instance.Status ?? new InstanceStatus();
            DateTime? visibleAfter = instance.VisibleAfter;

            string instanceGuid = instance.Id.Contains('/') ? instance.Id.Split('/')[1] : instance.Id;

            DateTime createdDateTime = visibleAfter != null && visibleAfter > instance.Created ? (DateTime)visibleAfter : instance.Created.Value;

            MessageBoxInstance messageBoxInstance = new()
            {
                CreatedDateTime = createdDateTime,
                DueDateTime = instance.DueBefore,
                Id = instanceGuid,
                InstanceOwnerId = instance.InstanceOwner.PartyId,
                LastChangedBy = FindLastChanged(instance).LastChangedBy,
                Org = instance.Org,
                AppName = instance.AppId.Split('/')[1],
                ProcessCurrentTask = GetSBLStatusForCurrentTask(instance),
                AllowNewCopy = false,
                DeletedDateTime = status.SoftDeleted,
                ArchivedDateTime = status.Archived,
                DeleteStatus = status.SoftDeleted.HasValue ? DeleteStatusType.SoftDeleted : DeleteStatusType.Default,
                ReadStatus = status.ReadStatus,
                DataValues = instance.DataValues
            };

            if (instance.PresentationTexts is not null)
            {
                messageBoxInstance.PresentationText = string.Join(", ", instance.PresentationTexts.Select(pt => pt.Value).ToArray());
            }

            if (instance.Status?.Substatus != null)
            {
                messageBoxInstance.Substatus = new Substatus
                {
                    Label = instance.Status.Substatus.Label,
                    Description = instance.Status.Substatus.Description
                };
            }

            return messageBoxInstance;
        }

        /// <summary>
        /// Returns app id
        /// </summary>
        public static string GetAppId(MessageBoxInstance instance)
        {
            return instance.Org.ToLower() + "/" + instance.AppName;
        }

        /// <summary>
        /// Converts list of instance events to a list of simplified sbl instance events
        /// </summary>
        /// <param name="instanceEvents">List of instance events to convert.</param>
        public static List<SblInstanceEvent> ConvertToSBLInstanceEvent(List<InstanceEvent> instanceEvents)
        {
            List<SblInstanceEvent> simpleEvents = [];
            foreach (InstanceEvent instanceEvent in instanceEvents)
            {
                simpleEvents.Add(
                    new SblInstanceEvent
                    {
                        Id = instanceEvent.Id,
                        User = instanceEvent.User,
                        CreatedDateTime = instanceEvent.Created,
                        EventType = instanceEvent.EventType,
                        AdditionalInfo = instanceEvent.AdditionalInfo,
                        RelatedUser = instanceEvent.RelatedUser
                    });
            }

            return simpleEvents;
        }

        /// <summary>
        /// Gets the equivalent sbl status for a given instance status
        /// </summary>
        /// <param name="instance">the instance</param>
        /// <returns>status</returns>
        public static string GetSBLStatusForCurrentTask(Instance instance)
        {
            if (instance.Process != null)
            {
                string taskType;
                if (instance.Process.Ended != null && instance.Status?.Archived == null)
                {
                    return "Submit";
                }
                else if (instance.Process.Ended != null && instance.Status?.Archived != null)
                {
                    return "Archived";
                }
                else if ((taskType = instance.Process?.CurrentTask?.AltinnTaskType) != null)
                {
                    if (taskType.Equals("confirmation", StringComparison.OrdinalIgnoreCase))
                    {
                        return "Confirmation";
                    }
                    else if (taskType.Equals("feedback", StringComparison.OrdinalIgnoreCase))
                    {
                        return "Feedback";
                    }
                    else if (taskType.Equals("signing", StringComparison.OrdinalIgnoreCase))
                    {
                        return "Signing";
                    }
                }

                return "FormFilling";
            }
            else
            {
                return "default";
            }
        }

        /// <summary>
        /// Finds last changed by for an instance and its listed data elements
        /// </summary>
        /// <param name="instance">The instance</param>
        /// <returns>Last changed by</returns>
        public static (string LastChangedBy, DateTime? LastChanged) FindLastChanged(Instance instance)
        {
            string lastChangedBy = instance.LastChangedBy;
            DateTime? lastChanged = instance.LastChanged;
            if (instance.Data == null || instance.Data.Count == 0)
            {
                return (lastChangedBy, lastChanged);
            }

            List<DataElement> newerDataElements = instance.Data.FindAll(dataElement =>
                dataElement.LastChanged != null
                && dataElement.LastChangedBy != null
                && dataElement.LastChanged > instance.LastChanged);

            if (newerDataElements.Count == 0)
            {
                return (lastChangedBy, lastChanged);
            }

            lastChanged = (DateTime)instance.LastChanged;
            newerDataElements.ForEach((DataElement dataElement) =>
            {
                if (dataElement.LastChanged > lastChanged)
                {
                    lastChangedBy = dataElement.LastChangedBy;
                    lastChanged = (DateTime)dataElement.LastChanged;
                }
            });

            return (lastChangedBy, lastChanged);
        }

        /// <summary>
        /// Replaces central texts in instance with values from text resource.
        /// </summary>
        /// <param name="instances">list of instances</param>
        /// <param name="textResources">list of text resources</param>
        /// <param name="language">the language</param>
        /// <returns>list of instances with updated texts</returns>
        public static List<MessageBoxInstance> ReplaceTextKeys(List<MessageBoxInstance> instances, List<TextResource> textResources, string language)
        {
            foreach (MessageBoxInstance instance in instances)
            {
                string id = $"{instance.Org}-{instance.AppName}-{language}";
                string appTitle =
                    textResources.Find(t => t.Id.Equals(id))?.Resources.Where(r => r.Id.Equals("appName")).Select(r => r.Value).FirstOrDefault() ??
                    textResources.Find(t => t.Id.Equals(id))?.Resources.Where(r => r.Id.Equals("ServiceName")).Select(r => r.Value).FirstOrDefault();
                instance.Title = appTitle ?? instance.AppName;

                if (!string.IsNullOrWhiteSpace(instance.PresentationText))
                {
                    // Appending presentation text to title to avoid needing changes in SBL.
                    instance.Title += $", {instance.PresentationText}";
                }

                if (instance.Substatus?.Label != null)
                {
                    instance.Substatus.Label = textResources.Find(t => t.Id.Equals(id))?.Resources.Where(r => r.Id.Equals(instance.Substatus.Label)).Select(r => r.Value).FirstOrDefault() ?? instance.Substatus.Label;
                }

                if (instance.Substatus?.Description != null)
                {
                    instance.Substatus.Description = textResources.Find(t => t.Id.Equals(id))?.Resources.Where(r => r.Id.Equals(instance.Substatus.Description)).Select(r => r.Value).FirstOrDefault() ?? instance.Substatus.Description;
                }
            }

            return instances;
        }

        /// <summary>
        /// Goes through the hide settings for each app's message box configuration and removes instances that satisfy the hide conditions
        /// </summary>
        /// <param name="applications">The list of applications</param>
        /// <param name="instances">The list of applications to process.</param>
        public static void RemoveHiddenInstances(Dictionary<string, Application> applications, List<Instance> instances)
        {
            List<Instance> instancesToRemove = [];

            foreach (Instance instance in instances)
            {
                Application app = applications[instance.AppId];
                HideSettings hideSettings = app.MessageBoxConfig?.HideSettings;

                if (hideSettings == null)
                {
                    continue;
                }

                if (hideSettings.HideAlways)
                {
                    instancesToRemove.Add(instance);
                }
                else if (HideOnCurrentTask(hideSettings, instance.Process?.CurrentTask))
                {
                    instancesToRemove.Add(instance);
                }
            }

            instances.RemoveAll(instancesToRemove.Contains);
        }

        private static bool HideOnCurrentTask(HideSettings hideSettings, ProcessElementInfo currentTask)
        {
            if (hideSettings.HideOnTask == null || currentTask == null)
            {
                return false;
            }

            return hideSettings.HideOnTask.Contains(currentTask.ElementId);
        }

        /// <summary>
        /// Parsing instanceOwnerIdentifier string and find the number type and value.
        /// </summary>
        /// <param name="instanceOwnerIdentifier">The list of applications</param>
        public static (string InstanceOwnerIdType, string InstanceOwnerIdValue) GetIdentifierFromInstanceOwnerIdentifier(string instanceOwnerIdentifier)
        {
            if (string.IsNullOrEmpty(instanceOwnerIdentifier))
            {
                return (string.Empty, string.Empty);
            }

            string[] parts = instanceOwnerIdentifier.Replace(" ", string.Empty).ToLower().Split(':');
            if (parts.Length != 2)
            {
                return (string.Empty, string.Empty);
            }

            string partyType = parts[0];
            string partyNumber = parts[1];

            if (Enum.TryParse<PartyType>(partyType, true, out _))
            {
                return (partyType, partyNumber);
            }

            return (string.Empty, string.Empty);
        }

        /// <summary>
        /// Checks if an instance is prevented from deletion based on the application's PreventInstanceDeletionForDays setting.
        /// </summary>
        /// <param name="instanceStatus">The status of the instance to check</param>
        /// <param name="application">The application the instance belongs to</param>
        public static bool IsPreventedFromDeletion(InstanceStatus instanceStatus, Application application)
        {
            if (!instanceStatus.Archived.HasValue || !application.PreventInstanceDeletionForDays.HasValue)
            {
                return false;
            }

            DateTime archivedDateTime = instanceStatus.Archived.Value;
            DateTime dueDate = archivedDateTime.AddDays(application.PreventInstanceDeletionForDays.Value);

            return DateTime.UtcNow < dueDate;
        }
    }

    /// <summary>
    /// A helper class to validate instance owner ID with regular expression
    /// </summary>
    public static partial class InstanceOwnerIdRegExHelper
    {
        [GeneratedRegex(@"^\d{11}$", RegexOptions.None, matchTimeoutMilliseconds: 500)]
        public static partial Regex ElevenDigitRegex();

        [GeneratedRegex(@"^\d{9}$", RegexOptions.None, matchTimeoutMilliseconds: 500)]
        public static partial Regex NineDigitRegex();
    }
}
