using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Xml.XPath;
using System.Xml.Xsl;
using Microsoft.Extensions.Configuration;

namespace Altinn.Platform.Storage.Helpers
{
    /// <summary>
    /// A2 prit module
    /// </summary>
    public class A2Print
    {
        // TODO: change to standard a3 config
        private readonly IConfiguration _configuration;

        /// <summary>
        /// Default constructor
        /// </summary>
        /// <param name="configuration">Configuration</param>
        public A2Print(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        /// <summary>
        /// Get html
        /// </summary>
        /// <param name="printXslList">printXslList</param>
        /// <param name="xmlData">xmlData</param>
        /// <param name="archiveStamp">archiveStamp</param>
        /// <param name="languageID">languageID</param>
        /// <returns></returns>
        public Stream GetPrintHTML(PrintViewXslBEList printXslList, string xmlData, string archiveStamp, int languageID)
        {
            return new MemoryStream(Encoding.UTF8.GetBytes(GeneratePrint(xmlData, printXslList, languageID, archiveStamp)));
        }

        /// <summary>
        /// Generates the HTML print.
        /// </summary>
        /// <param name="formData">The form data.</param>
        /// <param name="printViewXslBEList">The information path view XSL be list.</param>
        /// <param name="languageID">Language ID</param>
        /// <returns>The print data as byte array (HTML or PDF)</returns>
        private string GeneratePrint(
            string formData,
            PrintViewXslBEList printViewXslBEList,
            int languageID,
            string archiveStamp)
        {
            string css = "<style>div, h1, h2, h3, h4, h5, h6, span, html, body, table, tr, td { box-sizing: border-box !important;}\r\ntr { background-color: transparent !important; color: inherit !important;}\r\nbody {margin-left: 0px !important; margin-right: 0px !important; margin-top: 15px !important;}\r\nspan.xdTextBox, span.xdExpressionBox {-webkit-user-modify:read-write; }\r\nspan.xdTextBox:not([style*=\"DISPLAY: none\"]), span.xdExpressionBox:not([style*=\"DISPLAY: none\"]) { display: inline-block !important }\r\nspan.xdTextBox {text-overflow: clip !important;}\r\nspan.xdTextBox[style*=\"WIDTH: 100%\"], div.xdDTPicker { width: 99% !important }\r\nthead.xdTableHeader {background-color : inherit !important;}\r\n.xdRepeatingSection, .xdExpressionBox, .xdSection.xdRepeating { height: auto !important; }\r\n[style*=\"FONT-SIZE: xx-small\"] { font-size: 7.5pt !important }\r\n[style*=\"FONT-SIZE: x-small\"] { font-size: 10pt !important }\r\n[style*=\"FONT-SIZE: small\"] { font-size: 12pt !important }\r\n[style*=\"FONT-SIZE: medium\"] { font-size: 13.5pt !important }\r\n[style*=\"FONT-SIZE: large\"] { font-size: 18pt !important }\r\n[style*=\"FONT-SIZE: x-large\"] { font-size: 24pt !important }\r\n[style*=\"FONT-SIZE: xx-large\"] { font-size: 36pt !important }\r\ndiv.xdSection.xdRepeating[style*=\"WIDTH: 100%\"] { width: auto !important }\r\nspan.xdDTText, div.xdDTPicker { display: inline-block !important }\r\ninput[type='radio'] { -webkit-appearance: none; width: 12px; height: 12px; border: 1px solid darkgray; border-radius: 50%; outline: none; background-color: white; }\r\ninput[type='radio']:before { content: ''; display: block; width: 60%; height: 60%; margin-left: 25%; margin-top: 20%; border-radius: 50%; }\r\ninput[type='radio']:checked:before { background: black; }\r\n</style>";
            XmlDocument xmlDoc = new XmlDocument();
            xmlDoc.LoadXml(formData);

            StringBuilder htmlString = new StringBuilder();

            foreach (PrintViewXslBE printViewXslBE in printViewXslBEList)
            {
                string htmlToTranslate = null;

                // Add formatting object to perform stringFormat of dates.
                FormServerXslFunction obj = new FormServerXslFunction { MainDomDocument = xmlDoc, Configuration = _configuration };
                XsltArgumentList xslArg = new XsltArgumentList();
                xslArg.AddExtensionObject("http://schemas.microsoft.com/office/infopath/2003/xslt/formatting", obj);
                xslArg.AddExtensionObject("http://schemas.microsoft.com/office/infopath/2003/xslt/xDocument", obj);
                xslArg.AddExtensionObject("http://schemas.microsoft.com/office/infopath/2003/xslt/Math", obj);
                xslArg.AddExtensionObject("http://schemas.microsoft.com/office/infopath/2003/xslt/Util", obj);
                xslArg.AddExtensionObject("http://schemas.microsoft.com/office/infopath/2003/xslt/Date", obj);

                XslCompiledTransform xslCompiledTransform = GetXslCompiledTransformForPrint(printViewXslBE, xmlDoc, xslArg, htmlToTranslate);

                if (htmlToTranslate == null)
                {
                    using (StringWriter htmlWriterOutput = new StringWriter())
                    {
                        xslCompiledTransform.Transform(xmlDoc, xslArg, htmlWriterOutput);
                        htmlToTranslate = htmlWriterOutput.ToString();
                    }
                }

                // Storing the generated html string and PDF modification parameters so that it could be used to generate the PDF
                //PdfMofificationParametersBE pdfModificationParams = AltinnCache.Get(AltinnCache.Id.PdfMofificationParameters, printViewXslBE.FormPageLocalizedId.ToString()) as PdfMofificationParametersBE;
                //printViewXslBE.printViewHtml = htmlToTranslate;
                //printViewXslBE.PdfModificationParams = pdfModificationParams;

                //htmlToTranslate = htmlToTranslate.Replace("</head>", css);

                // Add the archive time stamp and the list of attachments
                htmlToTranslate = SetArchiveTimeStampToHtml(archiveStamp, htmlToTranslate);

                htmlString.Append(htmlToTranslate);
            }

            //byte[] resultArray = null;

            //if (printType == PrintMethodType.PDF)
            //{
            //    using (PdfEngineProxy pdfEngine = new PdfEngineProxy())
            //    {
            //        resultArray = pdfEngine.GeneratePdfV2(
            //            printViewXslBEList,
            //            pdfSource,
            //            htmlHeaderText,
            //            htmlAttachmentInformation,
            //            languageID,
            //            createPdfa);
            //    }
            //}
            //else
            //{
            //resultArray = Encoding.UTF8.GetBytes(SetHtmlAttachmentsToHtml(htmlString.ToString(), htmlAttachmentInformation, languageID));
            //}

            //htmlString = SetHtmlAttachmentsToHtml

            return htmlString.ToString();
        }

        /// <summary>
        /// Adjusts the HTML with some CSS changes and embeds the CSS into the HTML file
        /// </summary>
        /// <param name="htmlMain">Main HTML file</param>
        /// <returns>Converted HTML file</returns>
        private string SetHtmlAdjustments(string htmlMain)
        {
            //Doctype for HTML5
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
        private string SetArchiveTimeStampToHtml(string htmlHeaderText, string htmlMain)
        {
            // Make adjustments to the HTML
            htmlMain = SetHtmlAdjustments(htmlMain);

            string cssClassForPrintAll = string.Empty;
            ////if (isPrintAllSelected)
            ////{
            ////    cssClassForPrintAll = "class='printAllStyle'";
            ////}

            if (!string.IsNullOrEmpty(htmlHeaderText))
            {
                // Appending html header text as html header and footer
                ////if (pdfSource == PDFType.ServiceArchive || pdfSource == PDFType.ReporteeArchive)
                {
                    // Replacing the semi colon with a space
                    ////string[] archiveTextElements = htmlHeaderText.Split(";".ToCharArray());
                    ////htmlHeaderText = archiveTextElements[0] + " " + archiveTextElements[1];

                    // Header and Footer tags for the archive time stamp
                    string htmlHeader = "<header id='pageHeader'" + cssClassForPrintAll + ">" + htmlHeaderText + "</header>";
                    string htmlFooter = "<footer id='pageFooter'" + cssClassForPrintAll + ">" + htmlHeaderText + "</footer>";

                    // Adding the same archive time stamp as Header and footer for the HTML page
                    htmlMain = htmlMain.Replace("</body>", htmlHeader + htmlFooter + "</body>");
                }
            }

            return htmlMain;
        }

        /// <summary>
        /// Converts the printViewXSL to HTML with modifications
        /// </summary>
        /// <param name="printViewXslBE">print view XSL details</param>
        /// <param name="xmlDoc">Form data as XML document</param>
        /// <param name="xslArg">XSL argument list</param>
        /// <param name="htmlToTranslate">HTML that needs to be translated</param>
        /// <returns>Complied and transformed XSL</returns>
        private XslCompiledTransform GetXslCompiledTransformForPrint(
            PrintViewXslBE printViewXslBE,
            XmlDocument xmlDoc,
            XsltArgumentList xslArg,
            string htmlToTranslate)
        {
            // Remove the DatePicker button. It should not be visible in HTML and PDF.
            printViewXslBE.PrintViewXsl = printViewXslBE.PrintViewXsl.Replace(".xdDTButton{", ".xdDTButton{display:none;");

            // Picture button should not be visible in HTML and PDF. Adding display:none in the Picture button css class "xdPictureButton". 
            printViewXslBE.PrintViewXsl = printViewXslBE.PrintViewXsl.Replace(".xdPictureButton{", ".xdPictureButton{display:none;");

            // Rename get-DOM() to GetMainDOM() to support for filtering on values from main data source in xpath expressions.
            printViewXslBE.PrintViewXsl = printViewXslBE.PrintViewXsl.Replace("xdXDocument:get-DOM()", "xdXDocument:GetMainDOM()");

            // Check UseAutomaticCustomization flag for new services
            //if (useAutomaticCustomization)
            {
                printViewXslBE.PrintViewXsl = printViewXslBE.PrintViewXsl.Replace(".langFont {", ".langFont{display:none;");
                printViewXslBE.PrintViewXsl = printViewXslBE.PrintViewXsl.Replace(".optionalPlaceholder {", ".optionalPlaceholder{display:none;");
            }

            PdfMofificationParametersBE pdfModParamBE = new PdfMofificationParametersBE();
            int extraFirstDivPadding = 0;
            string backColor = "#ffffff";

            string xsl = printViewXslBE.PrintViewXsl;

            try
            {
                XmlDocument xslDoc = new XmlDocument();
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
                        backColor = color;
                        pdfModParamBE.BackgroundColor = color;
                    }
                }

                //Forcing all views to be printed as centered on the page. This gives the best result with the new Winnovative webkit version.
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
                                        if (style.IndexOf("PADDING-LEFT:") == 0)
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
                                        //Ignore exception
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
                        if (tableclass != null)
                        {
                            if (tableclass.Value.Contains("xdRepeatingTable"))
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
                }

                // Check UseAutomaticCustomization flag for new services
                //if (useAutomaticCustomization)
                {
                    XmlNodeList selectnodelist = xslDoc.SelectNodes("//select");
                    if (selectnodelist != null)
                    {
                        // Styling and other attributes to the xdTextBox, xdComboBox, etc
                        SelectNodeProcessing(xslDoc, selectnodelist);
                    }
                }

                //if (printType == PrintMethodType.HTML)
                {
                    XmlNodeList imageNodeList = xslDoc.SelectNodes("//img");
                    if (imageNodeList != null)
                    {
                        // Processing, embedding and converting the images to base64
                        ImageNodeProcessing(xslDoc, imageNodeList);
                    }
                }

                XmlNamespaceManager nsmgr = new XmlNamespaceManager(xslDoc.NameTable);
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
                    if (styleNode != null)
                    {
                        if (!styleNode.InnerText.ToLower().Contains("thead {display: table-header-group;text-align: left;} th{ font-weight:normal;}"))
                        {
                            styleNode.InnerText += " thead {display: table-header-group;text-align: left;} th{ font-weight:normal;} ";
                        }
                    }
                }

                XmlNodeList stylenodes = xslDoc.SelectNodes("//@style");
                if (stylenodes != null)
                {
                    //int htmlwidth = 0;
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
                                //Ignore exception
                            }
                        }
                    }
                }

                pdfModParamBE.HtmlViewerWidth += extraFirstDivPadding;

                //AltinnCache.Insert(AltinnCache.Id.PdfMofificationParameters, printViewXslBE.FormPageLocalizedId.ToString(), pdfModParamBE);

                xsl = xslDoc.OuterXml;

                // If print type is PDF then the stylesheet path would be different and base reference for pdf images would be set
                //if (printType == PrintMethodType.PDF)
                //{
                //    string directory = Directory.GetDirectoryRoot(Directory.GetCurrentDirectory());
                //    string folderPath = directory + STYLESHEET_FOR_PDF;
                //    string imgFolder = AltinnConfiguration.FormEngine_PDF_ImageFolder;
                //    string kernSetting = AltinnConfiguration.FormEngine_PDF_EO_Use_Kerning ? @"<style>*{text-rendering: geometricPrecision !important; -webkit-font-feature-settings: ""kern"" !important }</style>" : null;
                //    xsl = xsl.Replace("</head>", @"<link rel='stylesheet' type='text/css' href='" + folderPath + @"' /> <BASE HREF='" + imgFolder + @"'/>" + kernSetting + @"</head>");
                //}

                xsl = xsl.Replace("MIN-HEIGHT:", "HEIGHT:");
            }
            catch
            {
                // Ignore error and show unmodified html string
            }

            XslCompiledTransform xslCompiledTransform = new XslCompiledTransform();
            using (StringReader stringReader = new StringReader(xsl))
            {
                xslCompiledTransform.Load(new XmlTextReader(stringReader));
            }

            // Make sure to do the first Transform() before putting into the cache because the first Transform() is expensive
            using (StringWriter htmlWriterOutput = new StringWriter())
            {
                //using (new PerformanceTracer("Transform"))
                {
                    xslCompiledTransform.Transform(xmlDoc, xslArg, htmlWriterOutput);
                    htmlToTranslate = htmlWriterOutput.ToString();
                }
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
                    XmlNamespaceManager nsManager = new XmlNamespaceManager(xslDoc.NameTable);
                    nsManager.AddNamespace("xd", "http://schemas.microsoft.com/office/infopath/2003");
                    nsManager.AddNamespace("xsl", "http://www.w3.org/1999/XSL/Transform");

                    if (selectClass != null)
                    {
                        if (selectClass.Value.Contains("xdComboBox"))
                        {
                            XmlAttribute selectStyle = selectNode.Attributes["style"];
                            styleNode = selectNode.SelectSingleNode("xsl:attribute[@name='style']", nsManager);

                            Dictionary<string, string> spanAttributes = new Dictionary<string, string>();
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

                                        Dictionary<string, string> xslVOAttributes = new Dictionary<string, string>();
                                        if (displayValue.Contains("'"))
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

                            if (codelistWhenNode)
                            {
                                if (!spanTag.HasChildNodes)
                                {
                                    XmlNodeList chooseNodes = selectNode.SelectNodes("./xsl:choose", nsManager);
                                    XmlNode chooseWhenNode = null;
                                    foreach (XmlNode chooseNode in chooseNodes)
                                    {
                                        chooseWhenNode = chooseNode.SelectSingleNode("./xsl:when[@test]", nsManager);
                                        if (chooseWhenNode != null && chooseWhenNode.Attributes["test"] != null)
                                        {
                                            if (chooseWhenNode.Attributes["test"].Value == "function-available('xdXDocument:GetDOM')")
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
                                        }

                                        spanTag.AppendChild(chooseNode);
                                    }
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
                                    if (optionNode.ParentNode != null)
                                    {
                                        optionNode.ParentNode.ReplaceChild(newNode, optionNode);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                //Ignore exception and show unmodified DropDown.
            }
        }

        private XmlNode CreateXslNodeElement(XmlDocument xDoc, Dictionary<string, string> attributes, string nodeName, string prefix, string nsUrl)
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
            using (MemoryStream ms = new MemoryStream())
            {
                using (Image img = Image.FromFile(_configuration["PdfImages"] + imageName))
                {
                    ImageFormat imgFormat = GetImageFormatFromImageFileName(imageName);
                    img.Save(ms, imgFormat);
                    return "data:image/" + imgFormat.ToString().ToLower() + ";base64," + System.Convert.ToBase64String(ms.ToArray());
                }
                ////return "";
            }
        }

        /// <summary>
        /// Gets the image format from the image name
        /// </summary>
        /// <param name="imageFileName">Image file name</param>
        /// <returns>Image format</returns>
        private static ImageFormat GetImageFormatFromImageFileName(string imageFileName)
        {
            switch (imageFileName.Substring(imageFileName.LastIndexOf(".") + 1))
            {
                case "bmp":
                    return ImageFormat.Bmp;
                case "gif":
                    return ImageFormat.Gif;
                case "jpg":
                case "jpeg":
                    return ImageFormat.Jpeg;
                case "png":
                    return ImageFormat.Png;
                case "tif":
                    return ImageFormat.Tiff;
                default:
                    return ImageFormat.Wmf;
            }
        }

        private void ImageNodeProcessing(XmlDocument xslDoc, XmlNodeList imageNodeList)
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
                    //AltinnLogger.LogError("Feil ved konvertering av bilde til base64 string", ex);
                }
            }
        }

    }

    /// <summary>
    /// FormServerXslFunction
    /// </summary>
    public class FormServerXslFunction
    {
        #region Fields

        /// <summary>
        /// Internal Map of replacement pattern values
        /// </summary>
        private Map[] map;

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
        /// Configuration
        /// </summary>
        public IConfiguration Configuration { get; set; }

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
            string str3;
            if (!string.IsNullOrEmpty(str) && !string.IsNullOrEmpty(str2))
            {
                if (DateCalculationHelper(str, str2, false, out str3))
                {
                    if (str.IndexOf('T') != -1)
                    {
                        return str3;
                    }

                    if (str.IndexOf(":", StringComparison.Ordinal) == -1)
                    {
                        return str3.Substring(0, str3.IndexOf('T'));
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
            return str3.Substring(startIndex, str3.Length - startIndex);
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
            XmlDocument xdoc = new XmlDocument();
            XmlElement elem = xdoc.CreateElement("a");
            xdoc.AppendChild(elem);

            while (nodeIterator.MoveNext())
            {
                XPathNavigator navigator = nodeIterator.Current;
                double currentValue;
                string str = navigator.Value;
                if (string.IsNullOrEmpty(str))
                {
                    continue;
                }

                if (double.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture.NumberFormat, out currentValue))
                {
                    sum += currentValue;
                    count++;
                }
                else
                {
                    elem.InnerText = double.NaN.ToString();
                }
            }

            if (!elem.InnerText.Equals(double.NaN.ToString()))
            {
                if (count > 0)
                {
                    elem.InnerText = (sum / count).ToString(CultureInfo.InvariantCulture.NumberFormat);
                }
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
            //string key = "pa:" + wsInputParams;
            //return AltinnCache.GetObjectFromCacheOrMethod(AltinnCache.Id.CodeListDOM, key, () =>
            //{
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
            StringBuilder xml = new StringBuilder();
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
                //ServiceMetaDataSI ws = new ServiceMetaDataSI();
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

            XmlDocument xdoc = new XmlDocument();
            xdoc.LoadXml(xml.ToString());
            return xdoc;
            ////});
        }

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

            CodeListBE filteredCodeList = new CodeListBE();
            CodeRowBEList codeRowListFinal = new CodeRowBEList();

            if (codeList != null && codeList.CodeListRows != null && codeList.CodeListRows.Count > 0)
            {
                if ((!string.IsNullOrEmpty(code) || !string.IsNullOrEmpty(value1Filter) || !string.IsNullOrEmpty(value2Filter)
                     || !string.IsNullOrEmpty(value3Filter)) && filterMatchType != FilterMatchType.DEFAULT)
                {
                    List<CodeRowBE> codeListRows =
                        codeList.CodeListRows.Where(
                            codeListRow =>
                            ((!string.IsNullOrEmpty(code) && codeListRow.Code.ToLower() == code.ToLower()) || string.IsNullOrEmpty(code))
                            && ((filterMatchType == FilterMatchType.EXACT
                                 && ((!string.IsNullOrEmpty(value1Filter) && codeListRow.Value1.ToLower() == value1Filter.ToLower())
                                     || string.IsNullOrEmpty(value1Filter))
                                 && ((!string.IsNullOrEmpty(value2Filter) && codeListRow.Value2.ToLower() == value2Filter.ToLower())
                                     || string.IsNullOrEmpty(value2Filter))
                                 && ((!string.IsNullOrEmpty(value3Filter) && codeListRow.Value3.ToLower() == value3Filter.ToLower())
                                     || string.IsNullOrEmpty(value3Filter)))
                                || (filterMatchType == FilterMatchType.STARTSWITH
                                    && ((!string.IsNullOrEmpty(value1Filter) && codeListRow.Value1.ToLower().StartsWith(value1Filter.ToLower()))
                                        || string.IsNullOrEmpty(value1Filter))
                                    && ((!string.IsNullOrEmpty(value2Filter) && codeListRow.Value2.ToLower().StartsWith(value2Filter.ToLower()))
                                        || string.IsNullOrEmpty(value2Filter))
                                    && ((!string.IsNullOrEmpty(value3Filter) && codeListRow.Value3.ToLower().StartsWith(value3Filter.ToLower()))
                                        || string.IsNullOrEmpty(value3Filter)))
                                || (filterMatchType == FilterMatchType.ENDSWITH
                                    && ((!string.IsNullOrEmpty(value1Filter) && codeListRow.Value1.ToLower().EndsWith(value1Filter.ToLower()))
                                        || string.IsNullOrEmpty(value1Filter))
                                    && ((!string.IsNullOrEmpty(value2Filter) && codeListRow.Value2.ToLower().EndsWith(value2Filter.ToLower()))
                                        || string.IsNullOrEmpty(value2Filter))
                                    && ((!string.IsNullOrEmpty(value3Filter) && codeListRow.Value3.ToLower().EndsWith(value3Filter.ToLower()))
                                        || string.IsNullOrEmpty(value3Filter)))
                                || (filterMatchType == FilterMatchType.CONTAINS
                                    && ((!string.IsNullOrEmpty(value1Filter) && codeListRow.Value1.ToLower().Contains(value1Filter.ToLower()))
                                        || string.IsNullOrEmpty(value1Filter))
                                    && ((!string.IsNullOrEmpty(value2Filter) && codeListRow.Value2.ToLower().Contains(value2Filter.ToLower()))
                                        || string.IsNullOrEmpty(value2Filter))
                                    && ((!string.IsNullOrEmpty(value3Filter) && codeListRow.Value3.ToLower().Contains(value3Filter.ToLower()))
                                        || string.IsNullOrEmpty(value3Filter))))).ToList();

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
            // Declare the CodeListBE to populate with CodeList details retrieved from the DB, and this BE is returned from this method
            CodeListBE codeList = new CodeListBE();
            ////codeList.CodeListID = Convert.ToInt32(reader["CodeListID_PK"]);
            codeList.CodeListName = codeListName;
            codeList.CodeListVersion = codeListVersion;
            codeList.LanguageTypeID = language;

            string codeListContent = GetCodeListXml(codeListName, language);

            if (!string.IsNullOrEmpty(codeListContent))
            {
                XmlDocument codeListXml = new XmlDocument();
                codeListXml.LoadXml(codeListContent);

                // get Collection of CodeList Row
                XmlNodeList codeRowCollection = codeListXml.SelectNodes("//CodeRow");

                if (codeRowCollection != null)
                {
                    codeList.CodeListRows = new CodeRowBEList();
                    foreach (XmlNode codeListRow in codeRowCollection)
                    {
                        CodeRowBE codeRow = new CodeRowBE();

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

            // Return the CodeListBE populated
            return codeList;
        }

        private string GetCodeListXml(string name, int lang)
        {
            string path = Configuration["CodelistXML.path"].ToString();

            string file = Path.Combine(path, name + "_" + lang + ".xml");

            if (File.Exists(file))
            {
                return File.ReadAllText(file);
            }
            else if (lang != 1044)
            {
                file = Path.Combine(path, name + "_1044.xml");

                if (File.Exists(file))
                {
                    return File.ReadAllText(file);
                }
            }

            return string.Empty;
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
            this.map = new[]
            {
                new Map('c', @"\p{_xmlC}"), new Map('C', @"\P{_xmlC}"), new Map('d', @"\p{_xmlD}"), new Map('D', @"\P{_xmlD}"),
                new Map('i', @"\p{_xmlI}"), new Map('I', @"\P{_xmlI}"), new Map('w', @"\p{_xmlW}"), new Map('W', @"\P{_xmlW}")
            };

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
            XmlDocument xdoc = new XmlDocument();
            XmlElement elem = xdoc.CreateElement("a");
            xdoc.AppendChild(elem);

            while (nodeIterator.MoveNext())
            {
                XPathNavigator navigator = nodeIterator.Current;
                double currentValue;
                string str = navigator.Value;
                if (string.IsNullOrEmpty(str))
                {
                    continue;
                }

                if (double.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture.NumberFormat, out currentValue))
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

            if (!elem.InnerText.Equals(double.NaN.ToString()))
            {
                if (count > 0)
                {
                    elem.InnerText = maxValue.ToString(CultureInfo.InvariantCulture.NumberFormat);
                }
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
            XmlDocument xdoc = new XmlDocument();
            XmlElement elem = xdoc.CreateElement("a");
            xdoc.AppendChild(elem);

            while (nodeIterator.MoveNext())
            {
                XPathNavigator navigator = nodeIterator.Current;
                double currentValue;
                string str = navigator.Value;
                if (string.IsNullOrEmpty(str))
                {
                    continue;
                }

                if (double.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture.NumberFormat, out currentValue))
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

            if (!elem.InnerText.Equals(double.NaN.ToString()))
            {
                if (count > 0)
                {
                    elem.InnerText = minValue.ToString(CultureInfo.InvariantCulture.NumberFormat);
                }
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
        public string FormatString(string str, string type, string format)
        {
            try
            {
                if (string.IsNullOrEmpty(str))
                {
                    return str;
                }

                format = format.TrimEnd(";".ToCharArray());
                string[] formatArray = format.Split(";".ToCharArray());
                Hashtable formatParams = new Hashtable();

                for (int i = 0; i < formatArray.Length; i++)
                {
                    if (formatArray[i].IndexOf(":") > -1)
                    {
                        string key = formatArray[i].Substring(0, formatArray[i].IndexOf(":"));
                        string value = formatArray[i].Substring(formatArray[i].IndexOf(":") + 1);
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
                                int locale = int.Parse(formatParams["locale"].ToString());
                                dateStr = date.ToString(formatString, new CultureInfo(locale));
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
                                numinfo.NumberDecimalDigits = str.IndexOf(".") > 0 ? str.Substring(str.IndexOf(".") + 1).Length : 0;
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
            StringBuilder builder = new StringBuilder();
            builder.Append("^");
            char[] charArray = pattern.ToCharArray();
            int length = pattern.Length;
            int startIndex = 0;
            int index = 0;
            while (true)
            {
                if (index < (length - 2))
                {
                    if (charArray[index] == '\\')
                    {
                        if (charArray[index + 1] != '\\')
                        {
                            char ch = charArray[index + 1];
                            int num4 = 0;
                        Label_002A:
                            if (num4 < this.map.Length)
                            {
                                if (this.map[num4].Match != ch)
                                {
                                    num4++;
                                    goto Label_002A;
                                }

                                if (startIndex < index)
                                {
                                    builder.Append(charArray, startIndex, index - startIndex);
                                }

                                builder.Append(this.map[num4].Replacement);
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

                    builder.Append("$");
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
            if (argument is double)
            {
                double num = (double)argument;
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
            DateTime time;
            output = null;
            if (DateTime.TryParse(absolute, CultureInfo.InvariantCulture, DateTimeStyles.None, out time))
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
        private string EscapeXmlEntities(string str)
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
                this.Match = m;
                this.Replacement = r;
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
        public int FormPageLocalizedId { get; set; }

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
