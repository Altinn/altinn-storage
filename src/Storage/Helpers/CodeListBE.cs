using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace Altinn.Platform.Storage.Helpers
{
    /// <summary>
    /// CodeListBE
    /// </summary>
    [Serializable]
    [DataContract(Name = "CodeList", Namespace = "http://schemas.altinn.no/services/ServiceEngine/ServiceMetaData/2009/10")]
    public class CodeListBE
    {
        #region Data contract members

        /// <summary>
        /// Gets or sets Identifier used to identify Code List
        /// </summary>
        [DataMember]
        public int CodeListID { get; set; }

        /// <summary>
        /// Gets or sets Identifier used to identify Name of Code List
        /// </summary>
        [DataMember]
        public string CodeListName { get; set; }

        /// <summary>
        /// Gets or sets Content details of Code List
        /// </summary>
        [DataMember]
        public CodeRowBEList CodeListRows { get; set; }

        /// <summary>
        /// Gets or sets Version of Code List
        /// </summary>
        [DataMember]
        public int CodeListVersion { get; set; }

        /// <summary>
        /// Gets or sets Language Type ID
        /// </summary>
        [DataMember]
        public int LanguageTypeID { get; set; }

        #endregion
    }

    /// <summary>
    /// Collection of CodeListBE
    /// </summary>
    [Serializable]
    [CollectionDataContract(Namespace = "http://schemas.altinn.no/services/ServiceEngine/ServiceMetaData/2009/10")]
    public class CodeListBEList : List<CodeListBE>
    {
    }

    /// <summary>
    /// CodeRowBE
    /// </summary>
    [Serializable]
    [DataContract(Name = "CodeListRow", Namespace = "http://schemas.altinn.no/services/ServiceEngine/ServiceMetaData/2009/10")]
    public class CodeRowBE
    {
        #region Data contract members

        /// <summary>
        /// Gets or sets Identifier used to Code
        /// </summary>
        [DataMember]
        public string Code { get; set; }

        /// <summary>
        /// Gets or sets Code Value1
        /// </summary>
        [DataMember]
        public string Value1 { get; set; }

        /// <summary>
        /// Gets or sets Code Value2
        /// </summary>
        [DataMember]
        public string Value2 { get; set; }

        /// <summary>
        /// Gets or sets Code Value3
        /// </summary>
        [DataMember]
        public string Value3 { get; set; }

        #endregion
    }

    /// <summary>
    /// Collection of CodeRowBE
    /// </summary>
    [Serializable]
    [CollectionDataContract(Namespace = "http://schemas.altinn.no/services/ServiceEngine/ServiceMetaData/2009/10")]
    public class CodeRowBEList : List<CodeRowBE>
    {
    }

    /// <summary>
    /// FilterMatchType
    /// </summary>
    [DataContract(Name = "FilterMatchType", Namespace = "http://schemas.altinn.no/serviceengine/formsengine/2009/10")]
    public enum FilterMatchType : int
    {
        /// <summary>
        /// The default with no filter
        /// </summary>
        [EnumMember]
        DEFAULT = 0,

        /// <summary>
        /// Exact match with the
        /// </summary>
        [EnumMember]
        EXACT = 1,

        /// <summary>
        /// The archive WEB-MVA receipts
        /// </summary>
        [EnumMember]
        STARTSWITH = 2,

        /// <summary>
        /// The PSAN form
        /// </summary>
        [EnumMember]
        ENDSWITH = 3,

        /// <summary>
        /// The PSAN form
        /// </summary>
        [EnumMember]
        CONTAINS = 4
    }
}
