using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Xml.XPath;
using Altinn.Platform.Storage.Repository;
using Microsoft.Extensions.Caching.Memory;

namespace Altinn.Platform.Storage.Helpers
{
    /// <summary>
    /// Port from a2 to support xslt transformations. Accessed from A2OndemandFormattingService.GetFormdataHtml
    /// </summary>
    public class A2FormServerXslFunction
    {
        #region Fields

        /// <summary>
        /// Internal Map of replacement pattern values
        /// </summary>
        private Map[] _map;
        private readonly Dictionary<string, CodeListBE> _codelists = [];

        #endregion

        #region Public Methods and Operators

        /// <summary>
        /// Gets or sets the main DOM string.
        /// </summary>
        /// <value>
        /// The main DOM string.
        /// </value>
        public XmlDocument MainDomDocument { get; set; }

        /// <summary>
        /// A2Repository
        /// </summary>
        public IA2Repository A2Repository { get; set; }

        /// <summary>
        /// Memory cache
        /// </summary>
        public IMemoryCache MemoryCache { get; set; }

        /// <summary>
        /// Gets the main DOM.
        /// </summary>
        /// <returns>Main DOM of XML document</returns>
        public XmlDocument GetMainDOM()
        {
            return MainDomDocument;
        }

        /// <summary>
        /// Adds days.
        /// </summary>
        /// <param name="date">
        /// Date
        /// </param>
        /// <param name="days">
        /// days to be added
        /// </param>
        /// <returns>
        /// Error message if any
        /// </returns>
        public string AddDays(object date, object days)
        {
            string str = DateCalculationArgumentHelper(date);
            string str2 = DateCalculationArgumentHelper(days);
            if (!string.IsNullOrEmpty(str) && !string.IsNullOrEmpty(str2))
            {
                if (DateCalculationHelper(str, str2, false, out string str3))
                {
                    if (str.IndexOf('T') != -1)
                    {
                        return str3;
                    }

                    if (str.IndexOf(':', StringComparison.Ordinal) == -1)
                    {
                        return str3[..str3.IndexOf('T')];
                    }

                    return "#ERR?";
                }

                return "#ERR?";
            }

            return string.Empty;
        }

        /// <summary>
        /// Adds seconds.
        /// </summary>
        /// <param name="date">
        /// Date
        /// </param>
        /// <param name="seconds">
        /// seconds to be added
        /// </param>
        /// <returns>
        /// Error message if any
        /// </returns>
        public string AddSeconds(object date, object seconds)
        {
            string str = DateCalculationArgumentHelper(date);
            string str2 = DateCalculationArgumentHelper(seconds);
            string str3;
            if (!string.IsNullOrEmpty(str) && !string.IsNullOrEmpty(str2))
            {
                if (!DateCalculationHelper(str, str2, true, out str3))
                {
                    return "#ERR?";
                }

                if (str.IndexOf('T') != -1)
                {
                    return str3;
                }
            }
            else
            {
                return string.Empty;
            }

            if (str.IndexOf('-') != -1)
            {
                return str3;
            }

            if (str.IndexOf('/') != -1)
            {
                return str3;
            }

            int startIndex = str3.IndexOf('T') + 1;
            return str3[startIndex..];
        }

        /// <summary>
        /// Calculates average.
        /// </summary>
        /// <param name="nodeIterator">
        /// The node iterator.
        /// </param>
        /// <returns>
        /// Average of the nodes
        /// </returns>
        public XPathNodeIterator Avg(XPathNodeIterator nodeIterator)
        {
            double sum = 0.0;
            int count = 0;
            XmlDocument xdoc = new();
            XmlElement elem = xdoc.CreateElement("a");
            xdoc.AppendChild(elem);

            while (nodeIterator.MoveNext())
            {
                XPathNavigator navigator = nodeIterator.Current;
                string str = navigator.Value;
                if (string.IsNullOrEmpty(str))
                {
                    continue;
                }

                if (double.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture.NumberFormat, out double currentValue))
                {
                    sum += currentValue;
                    count++;
                }
                else
                {
                    elem.InnerText = double.NaN.ToString();
                }
            }

            if (!elem.InnerText.Equals(double.NaN.ToString()) && count > 0)
            {
                elem.InnerText = (sum / count).ToString(CultureInfo.InvariantCulture.NumberFormat);
            }

            return xdoc.CreateNavigator().Select("a");
        }

        /// <summary>
        /// Gets the specified CodeList XmlDocument.
        /// </summary>
        /// <param name="wsInputParams">
        /// The WS input parameters. This is a semi-colon separated list.
        /// </param>
        /// <returns>
        /// DOM of XML document
        /// </returns>
        public XmlDocument GetDOM(string wsInputParams)
        {
            string cacheKey = $"pa:{wsInputParams}";
            if (!MemoryCache.TryGetValue(cacheKey, out XmlDocument xdoc))
            {
                const string XMLStart =
                    @"<dfs:myFields xmlns:my=""http://schemas.microsoft.com/office/infopath/2003/myXSD/2010-03-20T09:04:26"" xmlns:tns=""http://www.altinn.no/services/ServiceEngine/ServiceMetaData/2009/10"" xmlns:q1=""http://schemas.altinn.no/services/ServiceEngine/ServiceMetaData/2009/10"" xmlns:dfs=""http://schemas.microsoft.com/office/infopath/2003/dataFormSolution"" xmlns:xd=""http://schemas.microsoft.com/office/infopath/2003""><dfs:queryFields><tns:GetCodeList><tns:codeListName/><tns:codeListVersion/><tns:languageID/></tns:GetCodeList></dfs:queryFields><dfs:dataFields><tns:GetCodeListResponse><tns:GetCodeListResult><q1:CodeListID/><q1:CodeListName/><q1:CodeListRows>";
                const string XMLEnd =
                    @"</q1:CodeListRows><q1:CodeListVersion/><q1:LanguageTypeID/></tns:GetCodeListResult></tns:GetCodeListResponse></dfs:dataFields><xd:SchemaInfo LocalName=""myFields"" NamespaceURI=""http://schemas.microsoft.com/office/infopath/2003/dataFormSolution""><xd:Namespaces><xd:Namespace LocalName=""myFields"" NamespaceURI=""http://schemas.microsoft.com/office/infopath/2003/dataFormSolution""/></xd:Namespaces><xd:RequiredAnys/></xd:SchemaInfo></dfs:myFields>";
                const string CoderowXml =
                    @"<q1:CodeListRow><q1:Code>{0}</q1:Code><q1:Value1>{1}</q1:Value1><q1:Value2>{2}</q1:Value2><q1:Value3>{3}</q1:Value3></q1:CodeListRow>";
                const string XMLStartFiltered =
                    @"<dfs:myFields xmlns:my=""http://schemas.microsoft.com/office/infopath/2003/myXSD/2010-03-20T09:04:26"" xmlns:tns=""http://www.altinn.no/services/ServiceEngine/ServiceMetaData/2009/10"" xmlns:q1=""http://schemas.altinn.no/services/ServiceEngine/ServiceMetaData/2009/10"" xmlns:dfs=""http://schemas.microsoft.com/office/infopath/2003/dataFormSolution"" xmlns:xd=""http://schemas.microsoft.com/office/infopath/2003"">
                                                <dfs:queryFields><tns:GetFilteredCodeList><tns:codeListName/><tns:codeListVersion/><tns:languageID/>
                                                <tns:code/><tns:value1Filter/><tns:value2Filter/><tns:value3Filter/><tns:filterMatchType/></tns:GetFilteredCodeList>
                                                </dfs:queryFields><dfs:dataFields><tns:GetFilteredCodeListResponse><tns:GetFilteredCodeListResult><q1:CodeListID/><q1:CodeListName/><q1:CodeListRows>";
                const string XMLEndFiltered =
                    @"</q1:CodeListRows><q1:CodeListVersion/><q1:LanguageTypeID/></tns:GetFilteredCodeListResult></tns:GetFilteredCodeListResponse></dfs:dataFields><xd:SchemaInfo LocalName=""myFields"" NamespaceURI=""http://schemas.microsoft.com/office/infopath/2003/dataFormSolution""><xd:Namespaces><xd:Namespace LocalName=""myFields"" NamespaceURI=""http://schemas.microsoft.com/office/infopath/2003/dataFormSolution""/></xd:Namespaces><xd:RequiredAnys/></xd:SchemaInfo></dfs:myFields>";
                const string CoderowXmlFiltered =
                    @"<q1:CodeListRow><q1:Code>{0}</q1:Code><q1:Value1>{1}</q1:Value1><q1:Value2>{2}</q1:Value2><q1:Value3>{3}</q1:Value3></q1:CodeListRow>";
                StringBuilder xml = new();
                string[] wsParams = wsInputParams.Split(";".ToCharArray());
                if (wsParams.Length > 3)
                {
                    xml.Append(XMLStartFiltered);
                }
                else
                {
                    xml.Append(XMLStart);
                }

                try
                {
                    CodeListBE codelist;
                    if (wsParams.Length > 3)
                    {
                        codelist = GetFilteredCodeList(wsParams[0], int.Parse(wsParams[1]), int.Parse(wsParams[2]), wsParams[3], wsParams[4], wsParams[5], wsParams[6], (FilterMatchType)Enum.Parse(typeof(FilterMatchType), wsParams[7]));
                        foreach (CodeRowBE row in codelist.CodeListRows)
                        {
                            xml.AppendFormat(
                                CoderowXmlFiltered,
                                EscapeXmlEntities(row.Code),
                                EscapeXmlEntities(row.Value1),
                                EscapeXmlEntities(row.Value2),
                                EscapeXmlEntities(row.Value3));
                        }
                    }
                    else
                    {
                        codelist = GetCodeList(wsParams[0], int.Parse(wsParams[1]), int.Parse(wsParams[2]));
                        if (codelist != null && codelist.CodeListRows != null)
                        {
                            foreach (CodeRowBE row in codelist.CodeListRows)
                            {
                                xml.AppendFormat(
                                    CoderowXml,
                                    EscapeXmlEntities(row.Code),
                                    EscapeXmlEntities(row.Value1),
                                    EscapeXmlEntities(row.Value2),
                                    EscapeXmlEntities(row.Value3));
                            }
                        }
                    }
                }
                catch
                {
                    // If ws call fails, return an empty xml
                }

                if (wsParams.Length > 3)
                {
                    xml.Append(XMLEndFiltered);
                }
                else
                {
                    xml.Append(XMLEnd);
                }

                xdoc = new();
                xdoc.LoadXml(xml.ToString());

                MemoryCacheEntryOptions cacheEntryOptions = new MemoryCacheEntryOptions()
               .SetPriority(CacheItemPriority.Normal)
               .SetAbsoluteExpiration(new TimeSpan(0, 0, 300));

                MemoryCache.Set(cacheKey, xdoc, cacheEntryOptions);
            }

            return xdoc;
        }

        [SuppressMessage("Microsoft.StyleCop.CSharp.MaintainabilityRules", "SA1408:ConditionalExpressionsMustDeclarePrecedence", Justification = "Old code")]
        private CodeListBE GetFilteredCodeList(
            string codeListName,
            int codeListVersion,
            int languageID,
            string code,
            string value1Filter,
            string value2Filter,
            string value3Filter,
            FilterMatchType filterMatchType)
        {
            CodeListBE codeList = GetCodeListInternal(codeListName, codeListVersion, languageID);

            CodeListBE filteredCodeList = new();
            CodeRowBEList codeRowListFinal = [];

            if (codeList != null && codeList.CodeListRows != null && codeList.CodeListRows.Count > 0)
            {
                if ((!string.IsNullOrEmpty(code) || !string.IsNullOrEmpty(value1Filter) || !string.IsNullOrEmpty(value2Filter)
                     || !string.IsNullOrEmpty(value3Filter)) && filterMatchType != FilterMatchType.DEFAULT)
                {
                    List<CodeRowBE> codeListRows =
                        codeList.CodeListRows.Where(
                            codeListRow =>
                            (!string.IsNullOrEmpty(code) && codeListRow.Code.ToLower() == code.ToLower() || string.IsNullOrEmpty(code))
                            && (filterMatchType == FilterMatchType.EXACT
                                 && (!string.IsNullOrEmpty(value1Filter) && codeListRow.Value1.ToLower() == value1Filter.ToLower()
                                     || string.IsNullOrEmpty(value1Filter))
                                 && (!string.IsNullOrEmpty(value2Filter) && codeListRow.Value2.ToLower() == value2Filter.ToLower()
                                     || string.IsNullOrEmpty(value2Filter))
                                 && (!string.IsNullOrEmpty(value3Filter) && codeListRow.Value3.ToLower() == value3Filter.ToLower()
                                     || string.IsNullOrEmpty(value3Filter))
                                || filterMatchType == FilterMatchType.STARTSWITH
                                    && (!string.IsNullOrEmpty(value1Filter) && codeListRow.Value1.ToLower().StartsWith(value1Filter.ToLower())
                                        || string.IsNullOrEmpty(value1Filter))
                                    && (!string.IsNullOrEmpty(value2Filter) && codeListRow.Value2.ToLower().StartsWith(value2Filter.ToLower())
                                        || string.IsNullOrEmpty(value2Filter))
                                    && (!string.IsNullOrEmpty(value3Filter) && codeListRow.Value3.ToLower().StartsWith(value3Filter.ToLower())
                                        || string.IsNullOrEmpty(value3Filter))
                                || filterMatchType == FilterMatchType.ENDSWITH
                                    && (!string.IsNullOrEmpty(value1Filter) && codeListRow.Value1.ToLower().EndsWith(value1Filter.ToLower())
                                        || string.IsNullOrEmpty(value1Filter))
                                    && (!string.IsNullOrEmpty(value2Filter) && codeListRow.Value2.ToLower().EndsWith(value2Filter.ToLower())
                                        || string.IsNullOrEmpty(value2Filter))
                                    && (!string.IsNullOrEmpty(value3Filter) && codeListRow.Value3.ToLower().EndsWith(value3Filter.ToLower())
                                        || string.IsNullOrEmpty(value3Filter))
                                || filterMatchType == FilterMatchType.CONTAINS
                                    && (!string.IsNullOrEmpty(value1Filter) && codeListRow.Value1.ToLower().Contains(value1Filter.ToLower())
                                        || string.IsNullOrEmpty(value1Filter))
                                    && (!string.IsNullOrEmpty(value2Filter) && codeListRow.Value2.ToLower().Contains(value2Filter.ToLower())
                                        || string.IsNullOrEmpty(value2Filter))
                                    && (!string.IsNullOrEmpty(value3Filter) && codeListRow.Value3.ToLower().Contains(value3Filter.ToLower())
                                        || string.IsNullOrEmpty(value3Filter)))).ToList();

                    foreach (CodeRowBE codeListRow in codeListRows)
                    {
                        codeRowListFinal.Add(codeListRow);
                    }

                    filteredCodeList.CodeListRows = codeRowListFinal;
                    filteredCodeList.CodeListID = codeList.CodeListID;
                    filteredCodeList.CodeListName = codeList.CodeListName;
                    filteredCodeList.CodeListVersion = codeList.CodeListVersion;
                    filteredCodeList.LanguageTypeID = codeList.LanguageTypeID;
                }
                else
                {
                    filteredCodeList = codeList;
                }
            }
            else
            {
                filteredCodeList = codeList;
            }

            return filteredCodeList;
        }

        private CodeListBE GetCodeList(string codeListName, int codeListVersion, int languageID)
        {
            return GetFilteredCodeList(codeListName, codeListVersion, languageID, null, null, null, null, FilterMatchType.EXACT);
        }

        private CodeListBE GetCodeListInternal(string codeListName, int codeListVersion, int language)
        {
            if (!_codelists.ContainsKey(codeListName))
            {
                // Declare the CodeListBE to populate with CodeList details retrieved from the DB, and this BE is returned from this method
                CodeListBE codeList = new()
                {
                    CodeListName = codeListName,
                    CodeListVersion = codeListVersion,
                    LanguageTypeID = language
                };

                string codeListContent = GetCodeListXml(codeListName, language);

                if (!string.IsNullOrEmpty(codeListContent))
                {
                    XmlDocument codeListXml = new();
                    codeListXml.LoadXml(codeListContent);

                    // get Collection of CodeList Row
                    XmlNodeList codeRowCollection = codeListXml.SelectNodes("//CodeRow");

                    if (codeRowCollection != null)
                    {
                        codeList.CodeListRows = [];
                        foreach (XmlNode codeListRow in codeRowCollection)
                        {
                            CodeRowBE codeRow = new();

                            // This should not happen but in case if values are not provided in TUL                                    
                            if (codeListRow.SelectSingleNode("Code") != null)
                            {
                                codeRow.Code = codeListRow.SelectSingleNode("Code").InnerText;
                            }

                            if (codeListRow.SelectSingleNode("Value1") != null)
                            {
                                codeRow.Value1 = codeListRow.SelectSingleNode("Value1").InnerText;
                            }

                            if (codeListRow.SelectSingleNode("Value2") != null)
                            {
                                codeRow.Value2 = codeListRow.SelectSingleNode("Value2").InnerText;
                            }

                            if (codeListRow.SelectSingleNode("Value3") != null)
                            {
                                codeRow.Value3 = codeListRow.SelectSingleNode("Value3").InnerText;
                            }

                            codeList.CodeListRows.Add(codeRow);
                        }
                    }
                }

                _codelists[codeListName] = codeList;
            }

            // Return the CodeListBE populated
            return _codelists[codeListName];
        }

        private string GetCodeListXml(string name, int lang)
        {
            string language = lang switch
            {
                1044 => "nb",
                2068 => "nn",
                1033 => "en",
                _ => "nb",
            };
            return A2Repository.GetCodelist(name, language).Result;
        }

        /// <summary>
        /// Matches the specified value to the input pattern.
        /// </summary>
        /// <param name="value">
        /// The value.
        /// </param>
        /// <param name="pattern">
        /// The pattern.
        /// </param>
        /// <returns>
        /// True if matched
        /// </returns>
        public bool Match(string value, string pattern)
        {
            _map =
            [
                new Map('c', @"\p{_xmlC}"), new Map('C', @"\P{_xmlC}"), new Map('d', @"\p{_xmlD}"), new Map('D', @"\P{_xmlD}"),
                new Map('i', @"\p{_xmlI}"), new Map('I', @"\P{_xmlI}"), new Map('w', @"\p{_xmlW}"), new Map('W', @"\P{_xmlW}")
            ];

            bool isMatch = Regex.IsMatch(value, Preprocess(pattern), RegexOptions.None);
            return isMatch;
        }

        /// <summary>
        /// Gets Maximum of the nodes.
        /// </summary>
        /// <param name="nodeIterator">
        /// The node iterator.
        /// </param>
        /// <returns>
        /// Maximum node value
        /// </returns>
        public XPathNodeIterator Max(XPathNodeIterator nodeIterator)
        {
            double maxValue = 0.0;
            int count = 0;
            XmlDocument xdoc = new();
            XmlElement elem = xdoc.CreateElement("a");
            xdoc.AppendChild(elem);

            while (nodeIterator.MoveNext())
            {
                XPathNavigator navigator = nodeIterator.Current;
                string str = navigator.Value;
                if (string.IsNullOrEmpty(str))
                {
                    continue;
                }

                if (double.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture.NumberFormat, out double currentValue))
                {
                    if (currentValue > maxValue)
                    {
                        maxValue = currentValue;
                    }

                    if (count == 0)
                    {
                        maxValue = currentValue;
                    }
                }
                else
                {
                    elem.InnerText = double.NaN.ToString();
                }

                count++;
            }

            if (!elem.InnerText.Equals(double.NaN.ToString()) && count > 0)
            {
                elem.InnerText = maxValue.ToString(CultureInfo.InvariantCulture.NumberFormat);
            }

            return xdoc.CreateNavigator().Select("a");
        }

        /// <summary>
        /// Gets Minimum of the nodes.
        /// </summary>
        /// <param name="nodeIterator">
        /// The node iterator.
        /// </param>
        /// <returns>
        /// Minimum value node
        /// </returns>
        public XPathNodeIterator Min(XPathNodeIterator nodeIterator)
        {
            double minValue = 0.0;
            int count = 0;
            XmlDocument xdoc = new();
            XmlElement elem = xdoc.CreateElement("a");
            xdoc.AppendChild(elem);

            while (nodeIterator.MoveNext())
            {
                XPathNavigator navigator = nodeIterator.Current;
                string str = navigator.Value;
                if (string.IsNullOrEmpty(str))
                {
                    continue;
                }

                if (double.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture.NumberFormat, out double currentValue))
                {
                    if (currentValue < minValue)
                    {
                        minValue = currentValue;
                    }

                    if (count == 0)
                    {
                        minValue = currentValue;
                    }
                }
                else
                {
                    elem.InnerText = double.NaN.ToString();
                }

                count++;
            }

            if (!elem.InnerText.Equals(double.NaN.ToString()) && count > 0)
            {
                elem.InnerText = minValue.ToString(CultureInfo.InvariantCulture.NumberFormat);
            }

            return xdoc.CreateNavigator().Select("a");
        }

        /// <summary>
        /// Gets current date and time
        /// </summary>
        /// <returns>
        /// Current date time
        /// </returns>
        public string Now()
        {
            return DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Returns the node-set with any blank elements replaced with zero (0)
        /// </summary>
        /// <param name="nodes">
        /// The nodes.
        /// </param>
        /// <returns>
        /// Node-set with blank elements
        /// </returns>
        public XPathNodeIterator Nz(XPathNodeIterator nodes)
        {
            while (nodes.MoveNext())
            {
                if (nodes.Current.Value.Length == 0)
                {
                    nodes.Current.SetValue("0");
                }
            }

            return nodes;
        }

        /// <summary>
        /// Gets today's date.
        /// </summary>
        /// <returns>
        /// Today's date
        /// </returns>
        public string Today()
        {
            return DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Formats the input string according to the type and format input parameters
        /// </summary>
        /// <param name="str">
        /// The string to be formatted
        /// </param>
        /// <param name="type">
        /// The type of the string
        /// </param>
        /// <param name="format">
        /// The format
        /// </param>
        /// <returns>
        /// Formatted string
        /// </returns>
        [SuppressMessage("StyleCop.CSharp.NamingRules", "SA1300:Element should begin with upper-case letter", Justification = "Must match infopath callback name")]
        public string formatString(string str, string type, string format)
        {
            try
            {
                if (string.IsNullOrEmpty(str))
                {
                    return str;
                }

                format = format.TrimEnd(";".ToCharArray());
                string[] formatArray = format.Split(";".ToCharArray());
                Hashtable formatParams = [];

                for (int i = 0; i < formatArray.Length; i++)
                {
                    if (formatArray[i].IndexOf(':') > -1)
                    {
                        string key = formatArray[i][..formatArray[i].IndexOf(':')];
                        string value = formatArray[i][(formatArray[i].IndexOf(':') + 1)..];
                        formatParams[key] = value;
                    }
                }

                // Add one case for each type to be converted.
                switch (type)
                {
                    case "string":
                        if (format.Equals("plainMultiline"))
                        {
                            str = str.Replace("\n", "<br/>");
                        }

                        break;
                    case "date":
                    case "datetime":
                        DateTime date = DateTime.Parse(str);
                        string dateStr = string.Empty;
                        string timeStr = string.Empty;

                        string formatString = formatParams["dateFormat"].ToString();
                        switch (formatString)
                        {
                            case "Short Date":
                                dateStr = date.ToShortDateString();
                                break;
                            case "Long Date":
                                dateStr = date.ToLongDateString();
                                break;
                            case "Year Month":
                                dateStr = DateTimeFormatInfo.CurrentInfo.MonthNames[date.Month - 1] + " " + date.Year;
                                break;
                            case "none":
                                if (formatParams.ContainsKey("noSeconds") && formatParams["noSeconds"].Equals("1"))
                                {
                                    timeStr = date.ToShortTimeString();
                                }
                                else
                                {
                                    timeStr = date.ToLongTimeString();
                                }

                                break;
                            default:
                                dateStr = date.ToString(formatString, CultureInfo.InvariantCulture);
                                break;
                        }

                        if (formatParams.ContainsKey("timeFormat"))
                        {
                            formatString = formatParams["timeFormat"].ToString();
                            switch (formatString)
                            {
                                case "none":
                                    break;
                                default:
                                    int locale = int.Parse(formatParams["locale"].ToString());
                                    if (formatParams.ContainsKey("noSeconds") && formatParams["noSeconds"].Equals("1"))
                                    {
                                        formatString = formatString.Replace(":ss", string.Empty);
                                    }

                                    timeStr = date.ToString(formatString, new CultureInfo(locale));
                                    break;
                            }
                        }

                        str = (dateStr + " " + timeStr).Trim();
                        break;

                    case "currency":
                    case "number":
                    case "percentage":
                        NumberFormatInfo numinfo = new CultureInfo(Thread.CurrentThread.CurrentCulture.LCID).NumberFormat;
                        if (formatParams.ContainsKey("currencyLocale"))
                        {
                            numinfo = new CultureInfo(int.Parse(formatParams["currencyLocale"].ToString())).NumberFormat;
                        }

                        if (formatParams.ContainsKey("grouping") && formatParams["grouping"].Equals("0"))
                        {
                            numinfo.CurrencyGroupSeparator = string.Empty;
                            numinfo.NumberGroupSeparator = string.Empty;
                        }
                        else
                        {
                            numinfo.CurrencyGroupSeparator = " ";
                            numinfo.NumberGroupSeparator = " ";
                        }

                        decimal val = decimal.Parse(str, new CultureInfo(1033).NumberFormat);

                        if (formatParams.ContainsKey("numDigits"))
                        {
                            if (formatParams["numDigits"].Equals("auto"))
                            {
                                numinfo.NumberDecimalDigits = str.IndexOf('.') > 0 ? str[(str.IndexOf('.') + 1)..].Length : 0;
                            }
                            else
                            {
                                int numDigits = int.Parse(formatParams["numDigits"].ToString());
                                if (numDigits > -1)
                                {
                                    numinfo.NumberDecimalDigits = numDigits;
                                }
                            }
                        }

                        if (formatParams.ContainsKey("negativeOrder"))
                        {
                            if (type.Equals("currency"))
                            {
                                numinfo.CurrencyNegativePattern = int.Parse(formatParams["negativeOrder"].ToString());
                                if (formatParams.ContainsKey("positiveOrder"))
                                {
                                    numinfo.CurrencyPositivePattern = int.Parse(formatParams["positiveOrder"].ToString());
                                }
                            }
                            else
                            {
                                numinfo.NumberNegativePattern = int.Parse(formatParams["negativeOrder"].ToString());
                            }
                        }

                        if (type.Equals("number") || type.Equals("percentage"))
                        {
                            str = string.Format(val.ToString("N", numinfo));
                        }
                        else if (type.Equals("currency"))
                        {
                            str = string.Format(val.ToString("C", numinfo));
                        }

                        break;
                }
            }
            catch
            {
                // ignore error, unformated string is returned
            }

            return str;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Preprocesses the specified pattern to match .NET regular expression pattern.
        /// </summary>
        /// <param name="pattern">
        /// The pattern.
        /// </param>
        /// <returns>
        /// Pre process steps
        /// </returns>
        internal string Preprocess(string pattern)
        {
            StringBuilder builder = new();
            builder.Append('^');
            char[] charArray = pattern.ToCharArray();
            int length = pattern.Length;
            int startIndex = 0;
            int index = 0;
            while (true)
            {
                if (index < length - 2)
                {
                    if (charArray[index] == '\\')
                    {
                        if (charArray[index + 1] != '\\')
                        {
                            char ch = charArray[index + 1];
                            int num4 = 0;
                        Label_002A:
                            if (num4 < _map.Length)
                            {
                                if (_map[num4].Match != ch)
                                {
                                    num4++;
                                    goto Label_002A;
                                }

                                if (startIndex < index)
                                {
                                    builder.Append(charArray, startIndex, index - startIndex);
                                }

                                builder.Append(_map[num4].Replacement);
                                index++;
                                startIndex = index + 1;
                            }
                        }
                        else
                        {
                            index++;
                        }
                    }
                }
                else
                {
                    if (startIndex < length)
                    {
                        builder.Append(charArray, startIndex, length - startIndex);
                    }

                    builder.Append('$');
                    return builder.ToString();
                }

                index++;
            }
        }

        /// <summary>
        /// Date calculation argument helper.
        /// </summary>
        /// <param name="argument">
        /// The argument.
        /// </param>
        /// <returns>
        /// Calculated date
        /// </returns>
        private static string DateCalculationArgumentHelper(object argument)
        {
            if (argument is double num)
            {
                return num.ToString(NumberFormatInfo.CurrentInfo);
            }

            return argument as string;
        }

        /// <summary>
        /// Date calculation helper.
        /// </summary>
        /// <param name="absolute">
        /// The absolute.
        /// </param>
        /// <param name="increment">
        /// The increment.
        /// </param>
        /// <param name="seconds">
        /// if set to <c>true</c> [seconds].
        /// </param>
        /// <param name="output">
        /// The output.
        /// </param>
        /// <returns>
        /// True if date calculated
        /// </returns>
        private static bool DateCalculationHelper(string absolute, string increment, bool seconds, out string output)
        {
            int num;
            output = null;
            if (DateTime.TryParse(absolute, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime time))
            {
                if (increment.Length != 0)
                {
                    if (!int.TryParse(increment, NumberStyles.Integer, CultureInfo.InvariantCulture, out num))
                    {
                        return false;
                    }
                }
                else
                {
                    num = 0;
                }
            }
            else
            {
                return false;
            }

            time = !seconds ? time.AddDays(num) : time.AddSeconds(num);

            output = XmlConvert.ToString(time, XmlDateTimeSerializationMode.Unspecified);
            return true;
        }

        /// <summary>
        /// Escapes the XML entities.
        /// </summary>
        /// <param name="str">The string to be escaped.</param>
        /// <returns>XML entities</returns>
        private static string EscapeXmlEntities(string str)
        {
            if (string.IsNullOrEmpty(str))
            {
                return str;
            }

            return str.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&apos;");
        }

        #endregion

        /// <summary>
        /// Map Structure
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct Map
        {
            /// <summary>
            /// Char Match
            /// </summary>
            internal char Match;

            /// <summary>
            /// Replacement string
            /// </summary>
            internal string Replacement;

            /// <summary>
            /// Initializes a new instance of the <see cref="Map"/> struct
            /// </summary>
            /// <param name="m">
            /// Match data
            /// </param>
            /// <param name="r">
            /// Replacement data
            /// </param>
            internal Map(char m, string r)
            {
                Match = m;
                Replacement = r;
            }
        }
    }

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
