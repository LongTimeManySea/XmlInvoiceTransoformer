using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace XmlInvoiceTransformer
{
    /// <summary>
    /// Transforms SalesInvoicePrint XML format to BASDA Commercial Invoice format
    /// </summary>
    public class InvoiceTransformer
    {
        private static readonly XNamespace DefaultNs = "urn:schemas-basda-org:2000: salesInvoice: xdr: 3. 01";
        private static readonly XNamespace OpNs = "urn:schemas-bossfed-co-uk: OP-Invoice-v1";

        public XDocument Transform(XDocument input)
        {
            var root = input.Root
                ?? throw new InvalidOperationException("Input XML has no root element");

            var data = new InvoiceData(root);

            var invoice = new XElement(DefaultNs + "Invoice",
                CreateInvoiceHead(data),
                CreateInvoiceReferences(data),
                CreateAdditionalInvoiceReferences(data),
                CreateAdditionalInvoiceDates(data),
                new XElement(DefaultNs + "InvoiceDate", data.InvoiceDate),
                CreateSupplier(data),
                CreateBuyer(data),
                CreateInvoiceTo(data),
                CreateInvoiceLines(data),
                CreateSettlement(data),
                CreateTaxSubTotals(data),
                CreateInvoiceTotal(data)
            );

            return new XDocument(new XDeclaration("1.0", "utf-8", null), invoice);
        }

        #region Invoice Head Section

        private XElement CreateInvoiceHead(InvoiceData data)
        {
            return new XElement(DefaultNs + "InvoiceHead",
                new XElement(DefaultNs + "Schema",
                    new XElement(DefaultNs + "Version", "3.05")
                ),
                new XElement(DefaultNs + "Parameters",
                    new XElement(DefaultNs + "Language", "en-GB"),
                    new XElement(DefaultNs + "DecimalSeparator", ". "),
                    new XElement(DefaultNs + "Precision", "20. 4")
                ),
                new XElement(DefaultNs + "InvoiceType",
                    new XAttribute("Code", "INV"),
                    "Commercial Invoice"
                ),
                new XElement(DefaultNs + "InvoiceCurrency",
                    new XElement(DefaultNs + "Currency",
                        new XAttribute("Code", data.CurrencyCode),
                        data.CurrencyName
                    )
                ),
                new XElement(DefaultNs + "Checksum", GenerateChecksum(data))
            );
        }

        private static string GenerateChecksum(InvoiceData data)
        {
            int checksum = Math.Abs((data.InvoiceNumber + data.GrossTotal.ToString()).GetHashCode() % 100000);
            return checksum.ToString();
        }

        #endregion

        #region References Section

        private XElement CreateInvoiceReferences(InvoiceData data)
        {
            return new XElement(DefaultNs + "InvoiceReferences",
                new XElement(DefaultNs + "BuyersOrderNumber", data.CustomerOrderNumber),
                new XElement(DefaultNs + "SuppliersInvoiceNumber", data.InvoiceNumber),
                new XElement(DefaultNs + "DeliveryNoteNumber", data.DespatchNumber)
            );
        }

        private XElement CreateAdditionalInvoiceReferences(InvoiceData data)
        {
            return new XElement(OpNs + "AdditionalInvoiceReferences",
                new XElement(OpNs + "InvoiceReference",
                    new XAttribute("ReferenceType", "KWOS"),
                    new XElement(OpNs + "Reference", data.SalesOrderNumber)
                )
            );
        }

        private XElement CreateAdditionalInvoiceDates(InvoiceData data)
        {
            return new XElement(OpNs + "AdditionalInvoiceDates",
                new XElement(OpNs + "InvoiceDateTime",
                    new XAttribute("DateTimeType", "ORD"),
                    new XAttribute("DateTimeDesc", "Order Date"),
                    data.OrderDate
                ),
                new XElement(OpNs + "InvoiceDateTime",
                    new XAttribute("DateTimeType", "DEL"),
                    new XAttribute("DateTimeDesc", "Delivery date"),
                    data.DespatchDate
                )
            );
        }

        #endregion

        #region Party Sections

        private XElement CreateSupplier(InvoiceData data)
        {
            return new XElement(DefaultNs + "Supplier",
                new XElement(DefaultNs + "SupplierReferences",
                    new XElement(DefaultNs + "TaxNumber", data.VATRegistrationNo),
                    new XElement(DefaultNs + "GLN", data.CompanyRegistrationNo)
                ),
                new XElement(DefaultNs + "Party", data.CompanyName),
                CreateAddressElement(data.CompanyAddress)
            );
        }

        private XElement CreateBuyer(InvoiceData data)
        {
            return new XElement(DefaultNs + "Buyer",
                new XElement(DefaultNs + "BuyerReferences",
                    new XElement(DefaultNs + "SuppliersCodeForBuyer", data.CustomerAccount)
                ),
                new XElement(DefaultNs + "Party", data.CustomerName),
                CreateAddressElement(data.InvoiceToAddress)
            );
        }

        private XElement CreateInvoiceTo(InvoiceData data)
        {
            return new XElement(DefaultNs + "InvoiceTo",
                new XElement(DefaultNs + "Party", data.InvoiceToName)
            );
        }

        private XElement CreateAddressElement(AddressInfo address)
        {
            var addressElement = new XElement(DefaultNs + "Address");

            foreach (var line in address.Lines.Where(l => !string.IsNullOrWhiteSpace(l)))
            {
                addressElement.Add(new XElement(DefaultNs + "AddressLine", line));
            }

            if (!string.IsNullOrWhiteSpace(address.PostCode))
            {
                addressElement.Add(new XElement(DefaultNs + "PostCode", address.PostCode));
            }

            return addressElement;
        }

        #endregion

        #region Invoice Lines Section

        private IEnumerable<XElement> CreateInvoiceLines(InvoiceData data)
        {
            int lineNumber = 1;
            foreach (var item in data.LineItems)
            {
                yield return CreateInvoiceLine(item, lineNumber++);
            }
        }

        private XElement CreateInvoiceLine(LineItem item, int lineNumber)
        {
            return new XElement(DefaultNs + "InvoiceLine",
                new XElement(DefaultNs + "LineNumber", lineNumber),
                new XElement(DefaultNs + "InvoiceLineReferences",
                    new XElement(DefaultNs + "OrderLineNumber", item.ItemNumber),
                    new XElement(DefaultNs + "BuyersOrderLineReference", $"{item.ItemNumber} {item.ProductCode}")
                ),
                new XElement(OpNs + "AdditionalInvoiceLineReferences",
                    new XElement(OpNs + "InvoiceLineReference",
                        new XAttribute("ReferenceType", "SETFLG"),
                        new XAttribute("ReferenceDesc", "Settlement Discount Flag"),
                        new XElement(OpNs + "Reference", "Y")
                    )
                ),
                new XElement(DefaultNs + "Product",
                    new XElement(DefaultNs + "SuppliersProductCode", item.ProductCode),
                    new XElement(DefaultNs + "Description", item.Description)
                ),
                new XElement(DefaultNs + "Quantity",
                    new XElement(DefaultNs + "Packsize", "1"),
                    new XElement(DefaultNs + "Amount", item.Quantity.ToString("F0"))
                ),
                new XElement(DefaultNs + "Price",
                    new XElement(DefaultNs + "UnitPrice", item.UnitPrice.ToString("F3"))
                ),
                new XElement(DefaultNs + "LineTax",
                    new XElement(DefaultNs + "TaxRate",
                        new XAttribute("Code", "S"),
                        item.VATRate.ToString("F2")
                    )
                ),
                new XElement(DefaultNs + "LineTotal", item.LineTotal.ToString("F3"))
            );
        }

        #endregion

        #region Settlement Section

        private XElement CreateSettlement(InvoiceData data)
        {
            return new XElement(DefaultNs + "Settlement",
                new XElement(DefaultNs + "SettlementTerms",
                    new XElement(DefaultNs + "DaysFromInvoice", data.PaymentDays)
                ),
                new XElement(DefaultNs + "SettlementDiscount",
                    new XElement(DefaultNs + "PercentDiscount",
                        new XElement(DefaultNs + "Percentage", data.EarlyPaymentDiscountPercent.ToString("F2"))
                    ),
                    new XElement(DefaultNs + "AmountDiscount",
                        new XElement(DefaultNs + "Amount", "0.00")
                    )
                )
            );
        }

        #endregion

        #region Tax Summary Section

        private IEnumerable<XElement> CreateTaxSubTotals(InvoiceData data)
        {
            foreach (var vatGroup in data.VATDetails)
            {
                yield return new XElement(DefaultNs + "TaxSubTotal",
                    new XElement(DefaultNs + "TaxRate",
                        new XAttribute("Code", "S"),
                        vatGroup.Rate.ToString("F2")
                    ),
                    new XElement(DefaultNs + "NumberOfLinesAtRate", data.LineItems.Count),
                    new XElement(DefaultNs + "TotalValueAtRate", vatGroup.PrincipleValue.ToString("F3")),
                    new XElement(DefaultNs + "TaxableValueAtRate", vatGroup.PrincipleValue.ToString("F1")),
                    new XElement(DefaultNs + "TaxAtRate", vatGroup.VATValue.ToString("F2")),
                    new XElement(DefaultNs + "NetPaymentAtRate", vatGroup.PrincipleValue.ToString("F3")),
                    new XElement(DefaultNs + "GrossPaymentAtRate", (vatGroup.PrincipleValue + vatGroup.VATValue).ToString("F3")),
                    new XElement(DefaultNs + "TaxCurrency",
                        new XElement(DefaultNs + "Currency",
                            new XAttribute("Code", data.CurrencyCode),
                            data.CurrencyName
                        )
                    )
                );
            }
        }

        #endregion

        #region Invoice Total Section

        private XElement CreateInvoiceTotal(InvoiceData data)
        {
            return new XElement(DefaultNs + "InvoiceTotal",
                new XElement(DefaultNs + "NumberOfLines", data.LineItems.Count),
                new XElement(DefaultNs + "NumberOfTaxRates", data.VATDetails.Count),
                new XElement(DefaultNs + "LineValueTotal", data.NetTotal.ToString("F3")),
                new XElement(DefaultNs + "TaxableTotal", data.NetTotal.ToString("F2")),
                new XElement(DefaultNs + "TaxTotal", data.VATTotal.ToString("F2")),
                new XElement(DefaultNs + "NetPaymentTotal", data.GrossTotal.ToString("F2")),
                new XElement(DefaultNs + "GrossPaymentTotal", data.GrossTotal.ToString("F2"))
            );
        }

        #endregion
    }
}