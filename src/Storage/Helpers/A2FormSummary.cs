using System;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Xsl;
using Altinn.Platform.Storage.Services;
using Microsoft.Extensions.Caching.Memory;

namespace Altinn.Platform.Storage.Helpers
{
    /// <summary>
    /// Port from a2 to support generation of a form summary
    /// </summary>
    public class A2FormSummary
    {
        /// <summary>
        /// Get form summary
        /// </summary>
        /// <param name="formData">The form data</param>
        /// <param name="xslBEList">Xsl list</param>
        /// <param name="memoryCache">Memory cache</param>
        /// <returns>The form summary in html format</returns>  
        public string GetFormSummary(string formData, PrintViewXslBEList xslBEList, IMemoryCache memoryCache)
        {
            StringBuilder htmlStr = new();
            XmlDocument xmlDoc = new();
            xmlDoc.LoadXml(formData);

            // For every xsl get the HTML string
            foreach (var printViewXslBE in xslBEList)
            {
                string cacheKey = $"xslt:{printViewXslBE.Id}";
                if (!memoryCache.TryGetValue(cacheKey, out XslCompiledTransform xslCompiledTransform))
                {
                    xslCompiledTransform = new();
                    XmlDocument xslDoc = new();
                    xslDoc.LoadXml(printViewXslBE.PrintViewXsl);

                    XmlNamespaceManager nsmgr = new(xslDoc.NameTable);
                    if (!nsmgr.HasNamespace("xd"))
                    {
                        nsmgr.AddNamespace("xd", "http://schemas.microsoft.com/office/infopath/2003");
                    }

                    if (!nsmgr.HasNamespace("xsl"))
                    {
                        nsmgr.AddNamespace("xsl", "http://www.w3.org/1999/XSL/Transform");
                    }

                    // Change textbox span to input
                    XmlNodeList textboxList = xslDoc.SelectNodes("//span[contains(@class,'xdTextBox')]");
                    if (textboxList != null)
                    {
                        foreach (XmlNode node in textboxList)
                        {
                            try
                            {
                                XmlNode valueOf = node.SelectSingleNode(".//xsl:value-of[contains(@select,'xdFormatting:formatString')]", nsmgr) ?? node.SelectSingleNode(".//xsl:value-of", nsmgr);

                                if (valueOf != null)
                                {
                                    XmlElement span = xslDoc.CreateElement("span");
                                    span.Attributes.Append(xslDoc.CreateAttribute("TempSpanAttribute1"));

                                    if (node.Attributes != null)
                                    {
                                        if (node.Attributes["class"] != null)
                                        {
                                            XmlAttribute classAttribute = node.Attributes["class"];
                                            span.Attributes.Append((XmlAttribute)classAttribute.CloneNode(true));
                                        }

                                        if (node.Attributes["style"] != null)
                                        {
                                            XmlAttribute style = (XmlAttribute)node.Attributes["style"].CloneNode(true);
                                            span.Attributes.Append(style);
                                        }

                                        XmlNodeList xmlNodeList = node.SelectNodes("xsl:attribute", nsmgr);
                                        if (xmlNodeList != null)
                                        {
                                            foreach (XmlNode innerNode in xmlNodeList)
                                            {
                                                span.AppendChild(innerNode.CloneNode(true));
                                            }
                                        }
                                    }

                                    span.Attributes.Append(xslDoc.CreateAttribute("TempSpanAttribute2"));
                                    span.AppendChild(valueOf);

                                    if (node.ParentNode != null)
                                    {
                                        node.ParentNode.ReplaceChild(span, node);
                                    }
                                }
                            }
                            catch
                            {
                                // No handling needed. Just show unmodified receipt.
                            }
                        }
                    }

                    printViewXslBE.PrintViewXsl = xslDoc.OuterXml;

                    using (StringReader stringReader = new StringReader(printViewXslBE.PrintViewXsl))
                    {
                        xslCompiledTransform.Load(new XmlTextReader(stringReader));
                    }

                    MemoryCacheEntryOptions cacheEntryOptions = new MemoryCacheEntryOptions()
                    .SetPriority(CacheItemPriority.Normal)
                    .SetAbsoluteExpiration(new TimeSpan(0, 0, 300));

                    memoryCache.Set(cacheKey, xslCompiledTransform, cacheEntryOptions);
                }

                using (StringWriter htmlWriterOutput = new())
                {
                    XsltArgumentList xslArg = new();

                    // Add formatting object to perform stringFormat of numbers.
                    // changed to use the formserverxsl functions of the pdfengine
                    A2FormServerXslFunction obj = new();
                    xslArg.AddExtensionObject("http://schemas.microsoft.com/office/infopath/2003/xslt/formatting", obj);
                    xslArg.AddExtensionObject("http://schemas.microsoft.com/office/infopath/2003/xslt/xDocument", obj);
                    xslArg.AddExtensionObject("http://schemas.microsoft.com/office/infopath/2003/xslt/Math", obj);
                    xslArg.AddExtensionObject("http://schemas.microsoft.com/office/infopath/2003/xslt/Util", obj);
                    xslArg.AddExtensionObject("http://schemas.microsoft.com/office/infopath/2003/xslt/Date", obj);

                    xslCompiledTransform.Transform(xmlDoc, xslArg, htmlWriterOutput);

                    htmlStr.Append(htmlWriterOutput);
                }
            }

            return htmlStr.ToString();
        }
    }
}
