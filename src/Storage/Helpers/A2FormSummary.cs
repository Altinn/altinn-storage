////using System.Collections.Generic;
////using System.IO;
////using System.Text;
////using System.Xml.Xsl;
////using System.Xml;

////namespace Altinn.Platform.Storage.Helpers
////{
////    public class A2FormSummary
////    {
////        /// <summary>
////        /// Private method to get the HTML string
////        /// </summary>
////        /// <param name="formSetElementBEList">
////        /// The FormSetElementBEList
////        /// </param>
////        /// <param name="languageId">
////        /// The language ID
////        /// </param>
////        /// <param name="viewType">
////        /// The view type
////        /// </param>
////        /// <param name="reporteeElementId">
////        /// The Reportee Element ID
////        /// </param>
////        /// <param name="pdfType">
////        /// The PDF Type
////        /// </param>
////        /// <returns>
////        /// Summary of HTML form List
////        /// </returns>
////        private SummaryHTMLBEList GetHTMLString(
////            FormSetElementBEList formSetElementBEList, int languageId, ViewType viewType, int reporteeElementId, Altinn.Common.SBL.Enums.ServiceEngine.PDFType pdfType)
////        {
////            SummaryHTMLBE summaryHTMLBE = null;
////            SummaryHTMLBEList summaryHTMLBEList = new SummaryHTMLBEList();
////            Dictionary<string, InfoPathViewXslBEList> xslCache = new Dictionary<string, InfoPathViewXslBEList>();

////            // For every From in the list the FormData is retrieved
////            foreach (FormSetElementBE formSetElementBE in formSetElementBEList)
////            {
////                ArchiveReportingServiceDetailsBE reportingServiceDetails = GetReporteeArchiveSI().GetReporteeArchiveServiceDetails(reporteeElementId);

////                if (reportingServiceDetails != null && reportingServiceDetails.EncryptBinaryFiles && !GetReporteeArchiveSI().IsEncryptedWithApplet(reporteeElementId))
////                {
////                    string[] presentationFieldValue = formSetElementBE.ItemName.Split('|');
////                    if (presentationFieldValue.Length == 2)
////                    {
////                        formSetElementBE.ItemName = presentationFieldValue[0] + GetReporteeArchiveSI().DecryptPresentationFieldValue(presentationFieldValue[1], reporteeElementId);
////                    }
////                }

////                summaryHTMLBE = new SummaryHTMLBE();
////                if (formSetElementBE.ItemType == ItemTypeEnum.MainForm || formSetElementBE.ItemType == ItemTypeEnum.SubForm)
////                {
////                    // Get the InfoPath View Xsl List for the FormId passed
////                    InfoPathViewXslBEList infoPathViewXslList;

////                    // Get the form Data corresponding to the formId retrieved
////                    if (xslCache.ContainsKey(formSetElementBE.DataFormatID))
////                    {
////                        infoPathViewXslList = xslCache[formSetElementBE.DataFormatID];
////                    }
////                    else
////                    {
////                        infoPathViewXslList = pdfType == PDFType.ServiceArchive ? this.GetInfoPathViewXslFromSOA(formSetElementBE.ItemID, languageId, viewType) : this.GetInfoPathViewXsl(formSetElementBE.ItemID, languageId, viewType);
////                        xslCache[formSetElementBE.DataFormatID] = infoPathViewXslList;
////                    }

////                    if (infoPathViewXslList.Count == 0)
////                    {
////                        continue;
////                    }

////                    StringBuilder htmlStr = new StringBuilder();
////                    XmlDocument xmlDoc = new XmlDocument();
////                    xmlDoc.LoadXml(pdfType == PDFType.ServiceArchive ? this.GetServiceOwnerArchiveSI().GetFormData(formSetElementBE.ItemID) : this.GetReporteeArchiveSI().GetFormData(formSetElementBE.ItemID));

////                    // For every InfoPathViewXsl get the HTML string
////                    foreach (InfoPathViewXslBE infopathViewXsl in infoPathViewXslList)
////                    {
////                        // XmlWriter xmlWriterOutput;
////                        // string html = null;
////                        XslCompiledTransform xslCompiledTransform = AltinnCache.Get(AltinnCache.Id.Xslt, infopathViewXsl.FormPageLocalizedId.ToString()) as XslCompiledTransform;
////                        if (xslCompiledTransform == null)
////                        {
////                            xslCompiledTransform = new XslCompiledTransform();

////                            string xsl = infopathViewXsl.InfoPathViewXsl;
////                            XmlDocument xslDoc = new XmlDocument();
////                            xslDoc.LoadXml(xsl);

////                            XmlNamespaceManager nsmgr = new XmlNamespaceManager(xslDoc.NameTable);
////                            if (!nsmgr.HasNamespace("xd"))
////                            {
////                                nsmgr.AddNamespace("xd", "http://schemas.microsoft.com/office/infopath/2003");
////                            }

////                            if (!nsmgr.HasNamespace("xsl"))
////                            {
////                                nsmgr.AddNamespace("xsl", "http://www.w3.org/1999/XSL/Transform");
////                            }

////                            //Change textbox span to input
////                            XmlNodeList textboxList = xslDoc.SelectNodes("//span[contains(@class,'xdTextBox')]");
////                            if (textboxList != null)
////                            {
////                                foreach (XmlNode node in textboxList)
////                                {
////                                    try
////                                    {
////                                        XmlNode valueOf = node.SelectSingleNode(".//xsl:value-of[contains(@select,'xdFormatting:formatString')]", nsmgr) ?? node.SelectSingleNode(".//xsl:value-of", nsmgr);

////                                        if (valueOf != null)
////                                        {
////                                            XmlElement span = xslDoc.CreateElement("span");
////                                            span.Attributes.Append(xslDoc.CreateAttribute("TempSpanAttribute1"));

////                                            if (node.Attributes != null)
////                                            {
////                                                if (node.Attributes["class"] != null)
////                                                {
////                                                    XmlAttribute classAttribute = node.Attributes["class"];
////                                                    span.Attributes.Append((XmlAttribute)classAttribute.CloneNode(true));
////                                                }

////                                                if (node.Attributes["style"] != null)
////                                                {
////                                                    XmlAttribute style = (XmlAttribute)node.Attributes["style"].CloneNode(true);
////                                                    span.Attributes.Append(style);
////                                                }

////                                                XmlNodeList xmlNodeList = node.SelectNodes("xsl:attribute", nsmgr);
////                                                if (xmlNodeList != null)
////                                                {
////                                                    foreach (XmlNode innerNode in xmlNodeList)
////                                                    {
////                                                        span.AppendChild(innerNode.CloneNode(true));
////                                                    }
////                                                }
////                                            }

////                                            span.Attributes.Append(xslDoc.CreateAttribute("TempSpanAttribute2"));
////                                            span.AppendChild(valueOf);

////                                            if (node.ParentNode != null)
////                                            {
////                                                node.ParentNode.ReplaceChild(span, node);
////                                            }
////                                        }
////                                    }
////                                    catch
////                                    {
////                                        //No handling needed. Just show unmodified receipt.
////                                    }
////                                }
////                            }

////                            xsl = xslDoc.OuterXml;

////                            using (StringReader stringReader = new StringReader(xsl))
////                            {
////                                xslCompiledTransform.Load(new XmlTextReader(stringReader));
////                            }

////                            AltinnCache.Insert(AltinnCache.Id.Xslt, infopathViewXsl.FormPageLocalizedId.ToString(), xslCompiledTransform);
////                        }

////                        using (StringWriter htmlWriterOutput = new StringWriter())
////                        {
////                            XsltArgumentList xslArg = new XsltArgumentList();

////                            // Add formatting object to perform stringFormat of numbers.
////                            // changed to use the formserverxsl functions of the pdfengine
////                            FormServerXslFunctions obj = new FormServerXslFunctions();
////                            xslArg.AddExtensionObject("http://schemas.microsoft.com/office/infopath/2003/xslt/formatting", obj);
////                            xslArg.AddExtensionObject("http://schemas.microsoft.com/office/infopath/2003/xslt/xDocument", obj);
////                            xslArg.AddExtensionObject("http://schemas.microsoft.com/office/infopath/2003/xslt/Math", obj);
////                            xslArg.AddExtensionObject("http://schemas.microsoft.com/office/infopath/2003/xslt/Util", obj);
////                            xslArg.AddExtensionObject("http://schemas.microsoft.com/office/infopath/2003/xslt/Date", obj);

////                            using (new PerformanceTracer("Transform"))
////                            {
////                                xslCompiledTransform.Transform(xmlDoc, xslArg, htmlWriterOutput);
////                            }

////                            htmlStr.Append(htmlWriterOutput);
////                        }
////                    }

////                    // Assign the FormSetElements to SummaryHTMLBE
////                    summaryHTMLBE.FormID = formSetElementBE.ItemID;
////                    summaryHTMLBE.FormName = formSetElementBE.ItemName;
////                    summaryHTMLBE.FormType = formSetElementBE.ItemType;
////                    summaryHTMLBE.FormHTML = htmlStr.ToString();

////                    // Add the SummaryHTMLBE to the SummaryHTMLList
////                    summaryHTMLBEList.Add(summaryHTMLBE);
////                }
////            }

////            return summaryHTMLBEList;
////        }
////    }
////}
