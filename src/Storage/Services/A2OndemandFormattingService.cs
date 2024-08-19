using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml;
using System.Xml.Xsl;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Repository;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Altinn.Platform.Storage.Services
{
    /// <summary>
    /// A2OndemandFormatting
    /// The code below is based on copy/paste from legacy code
    /// </summary>
    public class A2OndemandFormattingService : IA2OndemandFormattingService
    {
        const string _css = "<style>div, h1, h2, h3, h4, h5, h6, span, html, body, table, tr, td { box-sizing: border-box !important;}\r\ntr { background-color: transparent !important; color: inherit !important;}\r\nbody {margin-left: 0px !important; margin-right: 0px !important; margin-top: 15px !important;}\r\nspan.xdTextBox, span.xdExpressionBox {-webkit-user-modify:read-write; }\r\nspan.xdTextBox:not([style*=\"DISPLAY: none\"]), span.xdExpressionBox:not([style*=\"DISPLAY: none\"]) { display: inline-block !important }\r\nspan.xdTextBox {text-overflow: clip !important;}\r\nspan.xdTextBox[style*=\"WIDTH: 100%\"], div.xdDTPicker { width: 99% !important }\r\nthead.xdTableHeader {background-color : inherit !important;}\r\n.xdRepeatingSection, .xdExpressionBox, .xdSection.xdRepeating { height: auto !important; }\r\n[style*=\"FONT-SIZE: xx-small\"] { font-size: 7.5pt !important }\r\n[style*=\"FONT-SIZE: x-small\"] { font-size: 10pt !important }\r\n[style*=\"FONT-SIZE: small\"] { font-size: 12pt !important }\r\n[style*=\"FONT-SIZE: medium\"] { font-size: 13.5pt !important }\r\n[style*=\"FONT-SIZE: large\"] { font-size: 18pt !important }\r\n[style*=\"FONT-SIZE: x-large\"] { font-size: 24pt !important }\r\n[style*=\"FONT-SIZE: xx-large\"] { font-size: 36pt !important }\r\ndiv.xdSection.xdRepeating[style*=\"WIDTH: 100%\"] { width: auto !important }\r\nspan.xdDTText, div.xdDTPicker { display: inline-block !important }\r\ninput[type='radio'] { -webkit-appearance: none; width: 12px; height: 12px; border: 1px solid darkgray; border-radius: 50%; outline: none; background-color: white; }\r\ninput[type='radio']:before { content: ''; display: block; width: 60%; height: 60%; margin-left: 25%; margin-top: 20%; border-radius: 50%; }\r\ninput[type='radio']:checked:before { background: black; }\r\n</style>";

        private readonly IA2Repository _a2Repository;
        private readonly ILogger<A2OndemandFormattingService> _logger;
        private readonly IMemoryCache _memoryCache;

        /// <summary>
        /// Initializes a new instance of the <see cref="A2OndemandFormattingService"/> class
        /// </summary>
        public A2OndemandFormattingService(IA2Repository a2Repository, ILogger<A2OndemandFormattingService> logger, IMemoryCache memoryCache)
        {
            _a2Repository = a2Repository;
            _logger = logger;
            _memoryCache = memoryCache;
        }

        /// <inheritdoc/>
        public Stream GetFormdataHtml(PrintViewXslBEList printXslList, Stream xmlData, string archiveStamp)
        {
            return new MemoryStream(Encoding.UTF8.GetBytes(GetFormdataHtmlInternal(xmlData, printXslList, archiveStamp)));
        }

        private string GetFormdataHtmlInternal(
            Stream formData,
            PrintViewXslBEList printViewXslBEList,
            string archiveStamp)
        {
            XmlDocument xmlDoc = new();
            xmlDoc.Load(formData);

            StringBuilder htmlString = new();

            foreach (PrintViewXslBE printViewXslBE in printViewXslBEList)
            {
                string htmlToTranslate = null;

                // Add formatting object to perform stringFormat of dates.
                A2FormServerXslFunction obj = new() { MainDomDocument = xmlDoc, A2Repository = _a2Repository, MemoryCache = _memoryCache };
                XsltArgumentList xslArg = new();
                xslArg.AddExtensionObject("http://schemas.microsoft.com/office/infopath/2003/xslt/formatting", obj);
                xslArg.AddExtensionObject("http://schemas.microsoft.com/office/infopath/2003/xslt/xDocument", obj);
                xslArg.AddExtensionObject("http://schemas.microsoft.com/office/infopath/2003/xslt/Math", obj);
                xslArg.AddExtensionObject("http://schemas.microsoft.com/office/infopath/2003/xslt/Util", obj);
                xslArg.AddExtensionObject("http://schemas.microsoft.com/office/infopath/2003/xslt/Date", obj);

                string cacheKey = $"xslt:{printViewXslBE.Id}";
                if (!_memoryCache.TryGetValue(cacheKey, out XslCompiledTransform xslCompiledTransform))
                {
                    xslCompiledTransform = GetXslCompiledTransformForPrint(printViewXslBE, xmlDoc, xslArg);

                    MemoryCacheEntryOptions cacheEntryOptions = new MemoryCacheEntryOptions()
                   .SetPriority(CacheItemPriority.Normal)
                   .SetAbsoluteExpiration(new TimeSpan(0, 0, 300));

                    _memoryCache.Set(cacheKey, xslCompiledTransform, cacheEntryOptions);
                }

                using (StringWriter htmlWriterOutput = new())
                {
                    xslCompiledTransform.Transform(xmlDoc, xslArg, htmlWriterOutput);
                    htmlToTranslate = htmlWriterOutput.ToString();
                }

                htmlToTranslate = htmlToTranslate.Replace("</head>", _css);

                // Add the archive time stamp and the list of attachments
                htmlToTranslate = SetArchiveTimeStampToHtml(archiveStamp, htmlToTranslate);

                htmlString.Append(htmlToTranslate);
            }

            return htmlString.ToString();
        }

        /// <summary>
        /// Adjusts the HTML with some CSS changes and embeds the CSS into the HTML file
        /// </summary>
        /// <param name="htmlMain">Main HTML file</param>
        /// <returns>Converted HTML file</returns>
        private static string SetHtmlAdjustments(string htmlMain)
        {
            // Doctype for HTML5
            htmlMain = htmlMain.Replace("<html", "<!DOCTYPE html><html");
            htmlMain = htmlMain.Replace("<META http-equiv=\"Content-Type\" content=\"text/html; charset=utf-16\">", "<META charset=\"utf-8\">");

            // Making the html uneditable
            htmlMain = htmlMain.Replace("contentEditable=\"true\"", "contentEditable=\"false\"");
            htmlMain = htmlMain.Replace("type=\"radio\"", "type=\"radio\" disabled=\"\"");
            htmlMain = htmlMain.Replace("type=\"checkbox\"", "type=\"checkbox\" disabled=\"\"");

            // Embedding the CSS to the HTML
            htmlMain = htmlMain.Replace("<style controlStyle", "<style type=\"text/css\">" + Css.PrintHtmlCss + "</style><style controlStyle");

            // Set height and width of controls
            htmlMain = htmlMain.Replace("WIDTH: 100%", "WIDTH: 98%");
            htmlMain = htmlMain.Replace("HEIGHT: auto\"></span>", "HEIGHT: 18px\"></span>");

            // Align the html content to left
            htmlMain = htmlMain.Replace("<div align=\"right\"", "<div");
            htmlMain = htmlMain.Replace("<tbody", "<tbody align=\"left\"");

            return htmlMain;
        }

        /// <summary>
        /// Sets the archive time stamp and the list of attachments in the main html string
        /// </summary>
        /// <param name="htmlHeaderText">Archive time stamp information</param>
        /// <param name="htmlMain">Main html string</param>
        /// <returns>Main html string after changes</returns>
        private static string SetArchiveTimeStampToHtml(string htmlHeaderText, string htmlMain)
        {
            // Make adjustments to the HTML
            htmlMain = SetHtmlAdjustments(htmlMain);

            string cssClassForPrintAll = string.Empty;
            if (!string.IsNullOrEmpty(htmlHeaderText))
            {
                // Appending html header text as html header and footer
                // Header and Footer tags for the archive time stamp
                string htmlHeader = "<header id='pageHeader'" + cssClassForPrintAll + ">" + htmlHeaderText + "</header>";
                string htmlFooter = "<footer id='pageFooter'" + cssClassForPrintAll + ">" + htmlHeaderText + "</footer>";

                // Adding the same archive time stamp as Header and footer for the HTML page
                htmlMain = htmlMain.Replace("</body>", htmlHeader + htmlFooter + "</body>");
            }

            return htmlMain;
        }

        /// <summary>
        /// Converts the printViewXSL to HTML with modifications
        /// </summary>
        /// <param name="printViewXslBE">print view XSL details</param>
        /// <param name="xmlDoc">Form data as XML document</param>
        /// <param name="xslArg">XSL argument list</param>
        /// <returns>Complied and transformed XSL</returns>
        private XslCompiledTransform GetXslCompiledTransformForPrint(
            PrintViewXslBE printViewXslBE,
            XmlDocument xmlDoc,
            XsltArgumentList xslArg)
        {
            // Remove the DatePicker button. It should not be visible in HTML and PDF.
            printViewXslBE.PrintViewXsl = printViewXslBE.PrintViewXsl.Replace(".xdDTButton{", ".xdDTButton{display:none;");

            // Picture button should not be visible in HTML and PDF. Adding display:none in the Picture button css class "xdPictureButton". 
            printViewXslBE.PrintViewXsl = printViewXslBE.PrintViewXsl.Replace(".xdPictureButton{", ".xdPictureButton{display:none;");

            // Rename get-DOM() to GetMainDOM() to support for filtering on values from main data source in xpath expressions.
            printViewXslBE.PrintViewXsl = printViewXslBE.PrintViewXsl.Replace("xdXDocument:get-DOM()", "xdXDocument:GetMainDOM()");
            printViewXslBE.PrintViewXsl = printViewXslBE.PrintViewXsl.Replace(".langFont {", ".langFont{display:none;");
            printViewXslBE.PrintViewXsl = printViewXslBE.PrintViewXsl.Replace(".optionalPlaceholder {", ".optionalPlaceholder{display:none;");

            PdfMofificationParametersBE pdfModParamBE = new();
            int extraFirstDivPadding = 0;
            string xsl = printViewXslBE.PrintViewXsl;

            try
            {
                XmlDocument xslDoc = new();
                xslDoc.LoadXml(xsl);

                XmlNode apos = xslDoc.CreateElement("xsl", "variable", "http://www.w3.org/1999/XSL/Transform");
                apos.InnerText = "'";
                XmlAttribute xmlAttribute = xslDoc.CreateAttribute("name");
                xmlAttribute.Value = "aposVariable";
                apos.Attributes.Append(xmlAttribute);
                xslDoc.DocumentElement.PrependChild(apos);

                XmlNode bodyStyleNode = xslDoc.SelectSingleNode("//body/@style");
                if (bodyStyleNode != null && bodyStyleNode.InnerXml.IndexOf("BACKGROUND-COLOR:") > -1)
                {
                    string color = bodyStyleNode.InnerXml.Substring(bodyStyleNode.InnerXml.IndexOf("BACKGROUND-COLOR: ") + 18, 7);
                    if (!color.Equals("#ffffff"))
                    {
                        pdfModParamBE.BackgroundColor = color;
                    }
                }

                // Forcing all views to be printed as centered on the page. This gives the best result with the new Winnovative webkit version.
                XmlNodeList firstDivs = xslDoc.SelectNodes("//body/div");
                if (firstDivs.Count == 0)
                {
                    firstDivs = xslDoc.SelectNodes("//body/*/div");
                }

                if (firstDivs != null)
                {
                    int count = 0;
                    foreach (XmlNode firstDiv in firstDivs)
                    {
                        count++;
                        if (firstDiv.Attributes != null)
                        {
                            // First-Non-null div for every page will be the main div of that page
                            if (count == firstDivs.Count)
                            {
                                if (firstDiv.Attributes["class"] != null)
                                {
                                    firstDiv.Attributes["class"].Value = "pageBreak";
                                }
                                else
                                {
                                    XmlAttribute classPrint = xslDoc.CreateAttribute("class");
                                    classPrint.Value = "pageBreak";
                                    firstDiv.Attributes.Append(classPrint);
                                }
                            }

                            if (firstDiv.Attributes["align"] != null)
                            {
                                firstDiv.Attributes["align"].Value = "center";
                            }
                            else
                            {
                                XmlAttribute align = xslDoc.CreateAttribute("align");
                                align.Value = "center";
                                firstDiv.Attributes.Append(align);
                            }

                            if (firstDiv.Attributes["style"] != null)
                            {
                                string style = firstDiv.Attributes["style"].Value;
                                if (style.IndexOf("PADDING-LEFT:") > -1)
                                {
                                    try
                                    {
                                        int start = -1;
                                        if (style.StartsWith("PADDING-LEFT:"))
                                        {
                                            start = style.IndexOf("PADDING-LEFT:") + 13;
                                        }
                                        else if (style.IndexOf(" PADDING-LEFT:") > -1)
                                        {
                                            start = style.IndexOf(" PADDING-LEFT:") + 14;
                                        }

                                        if (start > -1)
                                        {
                                            int length = 0;
                                            if (style.IndexOf(";", start) > -1)
                                            {
                                                length = style.IndexOf(";", start - 13) - start;
                                            }
                                            else
                                            {
                                                length = style.Length - start;
                                            }

                                            string widthString = style.Substring(start, length).Trim();
                                            if (widthString.Contains("px"))
                                            {
                                                int width = int.Parse(widthString.Replace("px", string.Empty));
                                                if (extraFirstDivPadding < width)
                                                {
                                                    extraFirstDivPadding = width;
                                                }
                                            }
                                        }
                                    }
                                    catch
                                    {
                                        // Ignore exception
                                    }
                                }
                            }
                        }
                    }
                }

                XmlNodeList tables = xslDoc.SelectNodes("//table");
                if (tables != null)
                {
                    foreach (XmlNode tnode in tables)
                    {
                        XmlAttribute tableclass = tnode.Attributes["class"];
                        if (tableclass != null && tableclass.Value.Contains("xdRepeatingTable"))
                        {
                            XmlNodeList tr = tnode.SelectNodes("tbody/tr");
                            if (tr != null)
                            {
                                foreach (XmlNode node in tr)
                                {
                                    XmlAttribute style = node.Attributes["style"];
                                    if (style != null)
                                    {
                                        if (!style.Value.Contains("page-break-inside: avoid"))
                                        {
                                            style.Value = string.Format("{0};page-break-inside: avoid", style.Value);
                                        }
                                    }
                                    else
                                    {
                                        XmlAttribute styleAtt = xslDoc.CreateAttribute("style");
                                        styleAtt.Value = "page-break-inside: avoid";
                                        node.Attributes.Append(styleAtt);
                                    }
                                }
                            }
                        }
                    }
                }

                XmlNodeList selectnodelist = xslDoc.SelectNodes("//select");
                if (selectnodelist != null)
                {
                    // Styling and other attributes to the xdTextBox, xdComboBox, etc
                    SelectNodeProcessing(xslDoc, selectnodelist);
                }

                XmlNodeList imageNodeList = xslDoc.SelectNodes("//img");
                if (imageNodeList != null)
                {
                    // Processing, embedding and converting the images to base64
                    ImageNodeProcessing(imageNodeList);
                }

                XmlNamespaceManager nsmgr = new(xslDoc.NameTable);
                if (!nsmgr.HasNamespace("xd"))
                {
                    nsmgr.AddNamespace("xd", "http://schemas.microsoft.com/office/infopath/2003");
                }

                if (!nsmgr.HasNamespace("xsl"))
                {
                    nsmgr.AddNamespace("xsl", "http://www.w3.org/1999/XSL/Transform");
                }

                XmlNodeList plainTextMultiLine =
                    xslDoc.SelectNodes("//span[@xd:datafmt='\"string\",\"plainMultiline\"'][@xd:xctname='PlainText']", nsmgr);
                if (plainTextMultiLine != null)
                {
                    foreach (XmlNode node in plainTextMultiLine)
                    {
                        if (node.Attributes != null)
                        {
                            XmlAttribute style = node.Attributes["style"];
                            if (style != null && style.Value.Contains("HEIGHT:"))
                            {
                                style.Value = style.Value.Insert(style.Value.Length, ";HEIGHT: auto");
                            }
                            else
                            {
                                XmlNode styleNode = node.SelectSingleNode(".//xsl:attribute[@name='style']", nsmgr);
                                if (styleNode != null && styleNode.InnerText.Contains("HEIGHT"))
                                {
                                    styleNode.InnerXml = styleNode.InnerXml.Insert(styleNode.InnerXml.Length, ";HEIGHT: auto");
                                }
                            }
                        }
                    }
                }

                XmlNodeList repeatingDiv = xslDoc.SelectNodes("//div[@xd:xctname='RepeatingSection']", nsmgr);
                if (repeatingDiv != null)
                {
                    foreach (XmlNode node in repeatingDiv)
                    {
                        XmlAttribute style = node.Attributes["style"];
                        if (style != null)
                        {
                            if (!style.Value.Contains("page-break-inside: avoid"))
                            {
                                style.Value = string.Format("{0};page-break-inside: avoid", style.Value);
                            }
                        }
                        else
                        {
                            XmlAttribute styleAtt = xslDoc.CreateAttribute("style");
                            styleAtt.Value = "page-break-inside: avoid";
                            node.Attributes.Append(styleAtt);
                        }
                    }
                }

                XmlNodeList repeatingTableHeader = xslDoc.SelectNodes("//tbody[@class='xdTableHeader']");
                if (repeatingTableHeader != null)
                {
                    foreach (XmlNode node in repeatingTableHeader)
                    {
                        string xml = node.OuterXml.Replace("<tbody", "<thead").Replace("<td", "<th").Replace("</tbody>", "</thead>").Replace("</td>", "</th>");
                        XmlDocumentFragment df = xslDoc.CreateDocumentFragment();
                        df.InnerXml = xml;
                        node.ParentNode.ReplaceChild(df, node);
                    }

                    XmlNode styleNode = xslDoc.SelectSingleNode("//style");
                    if (styleNode != null && !styleNode.InnerText.ToLower().Contains("thead {display: table-header-group;text-align: left;} th{ font-weight:normal;}"))
                    {
                        styleNode.InnerText += " thead {display: table-header-group;text-align: left;} th{ font-weight:normal;} ";
                    }
                }

                XmlNodeList stylenodes = xslDoc.SelectNodes("//@style");
                if (stylenodes != null)
                {
                    foreach (XmlNode node in stylenodes)
                    {
                        string style = node.InnerText;
                        if (style.IndexOf("WIDTH:") > -1)
                        {
                            try
                            {
                                int start = -1;
                                if (style.IndexOf("WIDTH:") == 0)
                                {
                                    start = style.IndexOf("WIDTH:") + 6;
                                }
                                else if (style.IndexOf(" WIDTH:") > -1)
                                {
                                    start = style.IndexOf(" WIDTH:") + 7;
                                }

                                if (start > -1)
                                {
                                    int length = 0;
                                    if (style.IndexOf(";", start) > -1)
                                    {
                                        length = style.IndexOf(";", start - 6) - start;
                                    }
                                    else
                                    {
                                        length = style.Length - start;
                                    }

                                    string widthString = style.Substring(start, length).Trim();
                                    if (widthString.Contains("px"))
                                    {
                                        int width = int.Parse(widthString.Replace("px", string.Empty));
                                        if (pdfModParamBE.HtmlViewerWidth < width)
                                        {
                                            pdfModParamBE.HtmlViewerWidth = width;
                                        }
                                    }
                                }
                            }
                            catch
                            {
                                // Ignore exception
                            }
                        }
                    }
                }

                pdfModParamBE.HtmlViewerWidth += extraFirstDivPadding;

                xsl = xslDoc.OuterXml;
                ////xsl = xsl.Replace("</head>", @"<link rel='stylesheet' type='text/css' href='" + folderPath + @"' /> <BASE HREF='" + imgFolder + @"'/>" + kernSetting + @"</head>");
                xsl = xsl.Replace("MIN-HEIGHT:", "HEIGHT:");
            }
            catch
            {
                // Ignore error and show unmodified html string
            }

            XslCompiledTransform xslCompiledTransform = new();
            using (StringReader stringReader = new(xsl))
            {
                xslCompiledTransform.Load(new XmlTextReader(stringReader));
            }

            // Make sure to do the first Transform() before putting into the cache because the first Transform() is expensive
            using (StringWriter htmlWriterOutput = new())
            {
                xslCompiledTransform.Transform(xmlDoc, xslArg, htmlWriterOutput);
                string dummy = htmlWriterOutput.ToString();
            }

            return xslCompiledTransform;
        }

        private void SelectNodeProcessing(XmlDocument xslDoc, XmlNodeList selectNodeList)
        {
            try
            {
                foreach (XmlNode selectNode in selectNodeList)
                {
                    XmlAttribute selectClass = selectNode.Attributes["class"];
                    XmlNode styleNode = null;
                    bool codelistWhenNode = false;
                    XmlNamespaceManager nsManager = new(xslDoc.NameTable);
                    nsManager.AddNamespace("xd", "http://schemas.microsoft.com/office/infopath/2003");
                    nsManager.AddNamespace("xsl", "http://www.w3.org/1999/XSL/Transform");

                    if (selectClass != null && selectClass.Value.Contains("xdComboBox"))
                    {
                        XmlAttribute selectStyle = selectNode.Attributes["style"];
                        styleNode = selectNode.SelectSingleNode("xsl:attribute[@name='style']", nsManager);

                        Dictionary<string, string> spanAttributes = [];
                        if (selectStyle != null)
                        {
                            spanAttributes.Add("style", selectStyle.Value);
                        }

                        spanAttributes.Add("class", "xdTextBox");
                        XmlNode spanTag = xslDoc.CreateElement("span");
                        foreach (KeyValuePair<string, string> attribute in spanAttributes)
                        {
                            XmlAttribute xmlAttribute = xslDoc.CreateAttribute(attribute.Key);
                            xmlAttribute.Value = attribute.Value;
                            spanTag.Attributes.Append(xmlAttribute);
                        }

                        XmlNodeList optionNodes = selectNode.SelectNodes("./option", nsManager);
                        if (optionNodes != null)
                        {
                            foreach (XmlNode optionNode in optionNodes)
                            {
                                XmlNode ifNode = optionNode.SelectSingleNode("./xsl:if", nsManager);
                                if (ifNode != null)
                                {
                                    ifNode.InnerXml = string.Empty;

                                    string displayValue = string.Empty;
                                    displayValue = optionNode.InnerText;

                                    Dictionary<string, string> xslVOAttributes = [];
                                    if (displayValue.Contains('\''))
                                    {
                                        xslVOAttributes.Add("select", "concat(" + "'" + displayValue.Replace("'", "',$aposVariable,'") + "')");
                                    }
                                    else
                                    {
                                        xslVOAttributes.Add("select", "'" + displayValue + "'");
                                    }

                                    XmlNode xslVONode = CreateXslNodeElement(xslDoc, xslVOAttributes, "value-of", "xsl", "http://www.w3.org/1999/XSL/Transform");
                                    ifNode.AppendChild(xslVONode);
                                    spanTag.AppendChild(ifNode);
                                }
                            }
                        }

                        XmlNodeList whenNodeList = selectNode.SelectNodes("//xsl:when[@test]", nsManager);
                        foreach (XmlNode node in whenNodeList)
                        {
                            if (node.Attributes["test"].Value == "function-available('xdXDocument:GetDOM')")
                            {
                                codelistWhenNode = true;
                            }
                        }

                        if (codelistWhenNode && !spanTag.HasChildNodes)
                        {
                            XmlNodeList chooseNodes = selectNode.SelectNodes("./xsl:choose", nsManager);
                            XmlNode chooseWhenNode = null;
                            foreach (XmlNode chooseNode in chooseNodes)
                            {
                                chooseWhenNode = chooseNode.SelectSingleNode("./xsl:when[@test]", nsManager);
                                if (chooseWhenNode != null && chooseWhenNode.Attributes["test"] != null && chooseWhenNode.Attributes["test"].Value == "function-available('xdXDocument:GetDOM')")
                                {
                                    XmlNode emptyOptionNode = chooseWhenNode.SelectSingleNode("./option", nsManager);
                                    if (emptyOptionNode != null)
                                    {
                                        chooseWhenNode.RemoveChild(emptyOptionNode);
                                    }

                                    XmlNode foreachNode = chooseWhenNode.SelectSingleNode("./xsl:for-each", nsManager);
                                    if (foreachNode != null)
                                    {
                                        XmlNode foreachOptionNode = foreachNode.SelectSingleNode("./option", nsManager);
                                        if (foreachOptionNode != null)
                                        {
                                            XmlNode ifTestNode = foreachOptionNode.SelectSingleNode("./xsl:if", nsManager);
                                            XmlNode optionValueNode = foreachOptionNode.SelectSingleNode("./xsl:value-of", nsManager);
                                            if (ifTestNode != null && optionValueNode != null)
                                            {
                                                ifTestNode.InnerXml = string.Empty;
                                                ifTestNode.AppendChild(optionValueNode);
                                                foreachNode.ReplaceChild(ifTestNode, foreachOptionNode);
                                            }
                                        }
                                    }
                                }

                                spanTag.AppendChild(chooseNode);
                            }
                        }

                        if (styleNode != null)
                        {
                            spanTag.PrependChild(styleNode);
                        }

                        XmlNode parentNode = selectNode.ParentNode;
                        if (parentNode != null)
                        {
                            parentNode.AppendChild(spanTag);
                            parentNode.RemoveChild(selectNode);
                        }

                        XmlNodeList optionNodeList = spanTag.SelectNodes(".//option", nsManager);
                        if (optionNodeList != null)
                        {
                            foreach (XmlNode optionNode in optionNodeList)
                            {
                                XmlNode newNode = xslDoc.CreateElement("span");
                                newNode.InnerXml = optionNode.InnerXml;
                                optionNode.ParentNode?.ReplaceChild(newNode, optionNode);
                            }
                        }
                    }
                }
            }
            catch
            {
                // Ignore exception and show unmodified DropDown.
            }
        }

        private static XmlNode CreateXslNodeElement(XmlDocument xDoc, Dictionary<string, string> attributes, string nodeName, string prefix, string nsUrl)
        {
            XmlNode node = xDoc.CreateElement(prefix, nodeName, nsUrl);
            foreach (KeyValuePair<string, string> attribute in attributes)
            {
                XmlAttribute xmlAttribute = xDoc.CreateAttribute(attribute.Key);
                xmlAttribute.Value = attribute.Value;
                node.Attributes.Append(xmlAttribute);
            }

            return node;
        }

        /// <summary>
        /// Converts the image in base64
        /// </summary>
        /// <param name="imageName">Name of the image</param>
        /// <returns>Path of the converted image</returns>
        private string ConvertImageToBase64HtmlEmbeddedImageString(string imageName)
        {
            byte[] imagebytes = _a2Repository.GetImage(imageName).Result;
            if (imagebytes == null)
            {
                return string.Empty;
            }

            using MemoryStream ms = new(imagebytes);
            return $"data:image/{GetImageFormatFromImageFileName(imageName)};base64,{Convert.ToBase64String(ms.ToArray())}";
        }

        /// <summary>
        /// Gets the image format from the image name
        /// </summary>
        /// <param name="imageFileName">Image file name</param>
        /// <returns>Image format</returns>
        private static string GetImageFormatFromImageFileName(string imageFileName)
        {
            string extention = imageFileName.Split('.').Last().ToLower();
            return extention switch
            {
                "bmp" or "gif" or "jpg" or "jpeg" or "png" or "tif" => extention,
                _ => "wmf",
            };
        }

        private void ImageNodeProcessing(XmlNodeList imageNodeList)
        {
            foreach (XmlNode imageNode in imageNodeList)
            {
                try
                {
                    if (imageNode.Attributes != null)
                    {
                        XmlAttribute src = imageNode.Attributes["src"];
                        if (src != null && !src.InnerText.StartsWith("res://infopath.exe"))
                        {
                            src.InnerText = ConvertImageToBase64HtmlEmbeddedImageString(src.InnerText);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error converting image to base64 string");
                }
            }
        }
    }

    /// <summary>
    /// Class description
    /// </summary>
    [DataContract(Namespace = "http://schemas.altinn.no/services/ServiceEngine/ServiceMetaData/2009/10")]
    public class PrintViewXslBE
    {
        #region Data contract members

        /// <summary>
        /// Gets or sets Identifier used to identify Logical Form in the Form Set Collection
        /// </summary>
        [DataMember]
        public string Id { get; set; }

        /// <summary>
        /// Gets or sets Identifier used to identify Logical Form Name in the Form Set Collection
        /// </summary>
        [DataMember]
        public string PrintViewXsl { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the PDF Page Orientation either Landscape or Portrait
        /// </summary>
        [DataMember]
        public bool IsPortrait { get; set; }

        /// <summary>
        /// Gets or sets print form data as a HTML string
        /// </summary>
        [DataMember]
        public string PrintViewHtml { get; set; }

        /// <summary>
        /// Gets or sets PDF modification parameters
        /// </summary>
        [DataMember]
        public PdfMofificationParametersBE PdfModificationParams { get; set; }

        #endregion
    }

    /// <summary>
    /// Collection of printViewXSLBE
    /// </summary>
    [CollectionDataContract(Namespace = "http://schemas.altinn.no/services/ServiceEngine/ServiceMetaData/2009/10")]
    public class PrintViewXslBEList : List<PrintViewXslBE>
    {
    }

    /// <summary>
    /// PdfMofificationParametersBE
    /// </summary>
    public class PdfMofificationParametersBE
    {
        /// <summary>
        /// Gets or sets Width of the html viewer
        /// </summary>
        [DataMember]
        public int HtmlViewerWidth { get; set; }

        /// <summary>
        /// Gets or sets Background color of the PDF
        /// </summary>
        [DataMember]
        public string BackgroundColor { get; set; }
    }

    /// <summary>
    /// Css
    /// </summary>
    public static class Css
    {
        /// <summary>
        /// PrintCss
        /// </summary>
        public static readonly string PrintHtmlCss = @"
            div, h1, h2, h3, h4, h5, h6, span, html, body, table, tr, td { box-sizing: border-box !important; }
            tr { background-color: transparent !important; color: inherit !important; height: auto; }
            h1, h2, h3 { text-transform: none !important; }
            th { text-align: left; }
            table { margin: auto; }
            body { margin: 0px !important; }
            span.xdTextBox, span.xdExpressionBox { -webkit-user-modify: read-only; }
            .xdRepeatingSection, .xdExpressionBox, .xdSection.xdRepeating { height: auto !important; }
            [style*=""FONT-SIZE: xx-small""] { font-size: 7.5pt !important; }
            [style*=""FONT-SIZE: x-small""] { font-size: 10pt !important; }
            [style*=""FONT-SIZE: small""] { font-size: 12pt !important; }
            [style*=""FONT-SIZE: medium""] { font-size: 13.5pt !important; }
            [style*=""FONT-SIZE: large""] { font-size: 18pt !important; }
            [style*=""FONT-SIZE: x-large""] { font-size: 24pt !important; }
            [style*=""FONT-SIZE: xx-large""] { font-size: 36pt !important; }
            div.xdSection.xdRepeating[style*=""WIDTH: 100%""] { width: auto !important; }
            span.xdDTText, div.xdDTPicker { display: inline-block !important; }
            span.xdExpressionBox:not([style*=""DISPLAY: none""]) { display: inline-block !important; }
            span.xdTextBox[style*=""WIDTH: 100%""], div.xdDTPicker { width: 98% !important; }
            thead.xdTableHeader { background-color: inherit !important; }
            td div font img { float: right; }
            div.attachmentInfo { text-align: left; margin-left: 310px; }
            @page { size: 210mm 297mm !important; margin: 5mm !important; }
            #pageHeader { -webkit-transform: rotate(-90deg); -webkit-transform-origin: right bottom; -moz-transform: rotate(-90deg); -moz-transform-origin: right bottom; -o-transform: rotate(-90deg); -o-transform-origin: right bottom; -ms-transform: rotate(-90deg); -ms-transform-origin: right bottom; position: fixed; top: 5px; right: 10px; color: red; border: 1px solid red; padding: 1px; }
            #pageFooter { -webkit-transform: rotate(90deg); -webkit-transform-origin: right bottom; -moz-transform: rotate(90deg); -moz-transform-origin: right bottom; -o-transform: rotate(90deg); -o-transform-origin: right bottom; -ms-transform: rotate(90deg); -ms-transform-origin: right bottom; position: fixed; bottom: 15px; left: 10px; color: red; border: 1px solid red; padding: 1px; margin-left: -215px; }
            footer.printAllStyle { margin-left: -165px !important; }
            .pageBreak { page-break-after: always; }

            @media print {
                div.attachmentInfo { text-align: left !important; margin-left: 0px !important; }
                footer.printAllStyle { margin-left: -165px !important; }
            }

            @-moz-document url-prefix() {
                #pageHeader { border: 1px ridge red !important; margin-right: 4.5px; }
                #pageFooter { border: 1px ridge red !important; margin-left: -235px; }
            }
            ";
    }
}
