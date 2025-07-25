using System;
using System.Collections.Generic;
using System.ComponentModel;

using Newtonsoft.Json;

namespace Altinn.Platform.Storage.Interface.Models
{
    /// <summary>
    /// Represents metadata about a type of data element that the application will require when stepping through the process of
    /// an application while completing an instance.
    /// </summary>
    [JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
    public class DataType
    {
        /// <summary>
        /// Gets or sets the data type id.
        /// It must be unique within the scope of an application. Logical name of the schema of which data elements should be validated against.
        /// Should be in lower case and can only contain letters, dash and numbers. No space or slashes are allowed.
        /// Examples are: main, subschema-x, cv, attachment
        /// </summary>
        [JsonProperty(PropertyName = "id")]
        public string Id { get; set; }

        /// <summary>
        /// Gets or sets a description of the data type with language description.
        /// </summary>
        [JsonProperty(PropertyName = "description")]
        public LanguageString Description { get; set; }

        /// <summary>
        /// Gets or sets a list of allowed content types (Mime types). If null or empty all content types are allowed.
        /// </summary>
        [JsonProperty(PropertyName = "allowedContentTypes")]
        public List<string> AllowedContentTypes { get; set; }

        /// <summary>
        /// Gets or sets a list of allowed contributers.
        /// Value should be preceded by an approved key.
        /// If null or empty no contributer restrictions are set.
        /// </summary>
        [Obsolete("Use AllowedContributors instead. This property will be removed in a future version.")]
        [JsonProperty(PropertyName = "allowedContributers")]
        public List<string> AllowedContributers { get; set; }

        /// <summary>
        /// Gets or sets a list of allowed contributors.
        /// Value should be preceded by an approved key.
        /// If null or empty then no contributor restrictions are set.
        /// </summary>
        [JsonProperty(PropertyName = "allowedContributors")]
        public List<string> AllowedContributors { get; set; }
        
        /// <summary>
        /// When this is set, it overrides what action is required to read the blob of data elements of this data type.
        /// </summary>
        [JsonProperty(PropertyName = "actionRequiredToRead")]
        public string ActionRequiredToRead { get; set; }
        
        /// <summary>
        /// When this is set, it overrides what action is required to write to the blob of data elements of this data type.
        /// </summary>
        [JsonProperty(PropertyName = "actionRequiredToWrite")]
        public string ActionRequiredToWrite { get; set; }

        /// <summary>
        /// Gets or sets an object with information about how the application logic will handle the data element.
        /// </summary>
        [JsonProperty(PropertyName = "appLogic")]
        public ApplicationLogic AppLogic { get; set; }

        /// <summary>
        /// Gets or sets a reference to the process element id of the task where this data element should be updated.
        /// </summary>
        [JsonProperty(PropertyName = "taskId")]
        public string TaskId { get; set; }

        /// <summary>
        /// Gets or sets the maximum allowed size of the file in mega bytes. If missing there is no limit on file size.
        /// </summary>
        [JsonProperty(PropertyName = "maxSize")]
        public int? MaxSize { get; set; }

        /// <summary>
        /// Gets or sets the maximum number of allowed elements of this type on the same application instance. Default is 1.
        /// </summary>
        /// <remarks>
        /// Zero or below indicate unbounded maximum number of elements.
        /// </remarks>
        [JsonProperty(PropertyName = "maxCount")]
        [DefaultValue(1)]
        public int MaxCount { get; set; }

        /// <summary>
        /// Gets or sets the minimum number of required elements of this type on the same application instance. Default is 1.
        /// </summary>
        /// <remarks>
        /// Zero or below indicate that the element type is optional.
        /// </remarks>
        [JsonProperty(PropertyName = "minCount")]
        [DefaultValue(1)]
        public int MinCount { get; set; }

        /// <summary>
        /// Gets or sets the grouping for this data type. Can be a a string ("Photos") or a text resource key ("scheme.grouping") if the grouping name should support multiple languages.
        /// </summary>
        /// <remarks>
        /// Leaving field empty means that this data element should not have it's own grouping and will be grouped with other binary attachments that do not have defined a grouping.
        /// </remarks>
        [JsonProperty(PropertyName = "grouping")]
        public string Grouping { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the element should trigger PDF generation
        /// </summary>
        [JsonProperty(PropertyName = "enablePdfCreation")]
        public bool EnablePdfCreation { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether file uploaded to this data type should be scanned for malware. Default value is <c>false</c>.
        /// </summary>
        [JsonProperty(PropertyName = "enableFileScan")]
        public bool EnableFileScan { get; set; }

        /// <summary>
        /// Gets or sets a value indicating wheter a file scan status of pending should trigger a validation error or not. Default is <c>false</c>.
        /// </summary>
        [JsonProperty(PropertyName = "validationErrorOnPendingFileScan")]
        public bool ValidationErrorOnPendingFileScan { get; set; }

        /// <summary>
        /// Gets or sets a list of enabled file analysers this data type should be analysed against to extract metadata about the file.
        /// This metadata can in turn either be used to validate against or simply to extract metadata to add to the datamodel.
        /// The id's provided should match the id's registered with IFileAnalyser implementations registered in the application.
        /// </summary>
        [JsonProperty(PropertyName = "enabledFileAnalysers")]
        public List<string> EnabledFileAnalysers { get; set; } = new List<string>();

        /// <summary>
        /// Gets or sets a list of enabled file validators this data type should be validated against.
        /// </summary>
        [JsonProperty(PropertyName = "enabledFileValidators")]
        public List<string> EnabledFileValidators { get; set; } = new List<string>();
        
        /// <summary>
        /// Gets or sets a list of allowed keys for user defined metadata.
        /// If null or empty, all user defined metadata keys are allowed.
        /// </summary>
        [JsonProperty(PropertyName = "allowedKeysForUserDefinedMetadata")]
        public List<string> AllowedKeysForUserDefinedMetadata { get; set; }
    }
}
