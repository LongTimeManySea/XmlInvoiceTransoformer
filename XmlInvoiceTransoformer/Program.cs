using System.Globalization;
using System.Xml.Linq;

namespace XmlInvoiceTransoformer
{
    class Program
    {
        private static readonly XNamespace DefaultNs = "urn:schemas-basda-org:2000: salesInvoice:xdr: 3. 01";
        private static readonly XNamespace OpNs = "urn:schemas-bossfed-co-uk: OP-Invoice-v1";

        static void Main(string[] args)
        {
            Console.WriteLine("== XML Invoice Transformer ==\n");

            string inputFilePath = Path.GetDirectoryName(Environment.CurrentDirectory);
            string outputFilePath = Path.Combine(inputFilePath, "output");

            if (!File.Exists(inputFilePath))
            {
                Console.WriteLine($"Error: Input file not found {inputFilePath}");
                return;
            }

            try
            {
                Console.WriteLine($"\nReading input file: {inputFilePath}");
                XDocument inputDoc = XDocument.Load(inputFilePath);

                Console.WriteLine("Transforming to blueprint format...");
                XDocument outputDoc = TransformToBlueprint(inputDoc);

                Console.WriteLine($"Saving output file: {outputFilePath}");
                outputDoc.Save(outputFilePath);

                Console.WriteLine("\n✓ Transformation completed successfully!");
                Console.WriteLine($"Output sace to: {Path.GetFullPath(outputFilePath)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nError during transformation: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.Message}");
            }
        }

        private static string GetDefaultOutputPath(string inputPath)
        {
            string directory = Path.GetDirectoryName(inputPath) ?? ".";
            string fileName = Path.GetFileNameWithoutExtension(inputPath);
            return Path.Combine(directory, $"{fileName}_Transformed.xml");
        }

        private static XDocument TransformToBlueprint(XDocument input)
        {
            var root = input.Root;
            if (root == null)
                throw new InvalidOperationException("Input XML has no root element");

            var data = new InvoiceData(root);

            var invoice = new XElement(DefaultNs + "Invoice",
                CreateInvoiceHead(data),
                CreateInvoiceReference(data),
                CreateAdditionalInvoiceReference(data),
                CreateAdditionalInvoiceDates(data),
                new XElement(DefaultNs + "InvoiceData", data.InvoiceDate),
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

        private static XElement CreateInvoiceHead(InvoiceData data)
        {
            return new XElement(DefaultNs + "InvoiceHead",
                new XElement(DefaultNs + "Schema",
                    new XElement(DefaultNs + "Version", "3.05")
                    ),
                    new XElement(DefaultNs + "Parameters",
                        new XElement(DefaultNs + "Language", "en-GB"),
                        new XElement(DefaultNs + "DecimalSeparator", ". "),
                        new XElement(DefaultNs + "Precision", "20.4")
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
                    new XElement(DefaultNs + "Checksum", GenerateCheckSum(data))
                );
        }

        private static string GenerateCheckSum(InvoiceData data)
        {
            int checksum = Math.Abs((data.InvoiceNumber + data.GrossTotal.ToString()).GetHashCode() % 100000);
            return checksum.ToString();
        }

        #endregion

        #region Reference Section

        private static XElement CreateInvoiceReference(InvoiceData data)
        {
            return new XElement(DefaultNs + "InvoiceReferences",
                new XElement(DefaultNs + "BuyersOrderNumber", data.CustomerOrderNumber),
                new XElement(DefaultNs + "SuppliersInvoiceNumber", data.InvoiceNumber),
                new XElement(DefaultNs + "DeliveryNoteNumber", data.DespatchNumber)
            );
        }

        private static XElement CreateAdditionalInvoiceReference(InvoiceData data)
        {
            return new XElement(OpNs + "AdditionalInvoiceReferences",
                new XElement(OpNs + "InvoiceReference",
                    new XAttribute("ReferenceType", "KWOS"),
                    new XElement(OpNs + "Reference", data.SalesOrderNumber)
                )
            );
        }

        private static XElement CreateAdditionalInvoiceDates(InvoiceData data)
        {
            return new XElement(OpNs + "AdditionalInvoiceDates",
                new XElement(OpNs + "InvoiceDateTime",
                    new XAttribute("DateTimeType", "ORD"),
                    new XAttribute("DateTimeDesc", "Order Date"),
                    data.OrderDate
                ),
                new XElement(OpNs + "InvoiceDateTime",
                    new XAttribute("DateTimeType", "DEL"),
                    new XAttribute("DateTimeDesc", "Delivery date")
                )
            );
        }

        #endregion

        #region Party Sections (Supplier, Buyer, InvoiceTo)

        private static XElement CreateSupplier(InvoiceData data)
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

        private static XElement CreateBuyer(InvoiceData data)
        {
            return new XElement(DefaultNs + "Buyer",
                new XElement(DefaultNs + "BuyerReferences",
                    new XElement(DefaultNs + "SuppliersCodeForBuyer", data.CustomerAccount)
                ),
                new XElement(DefaultNs + "Party", data.CustomerName),
                CreateAddressElement(data.InvoiceToAddress)
            );
        }

        private static XElement CreateInvoiceTo(InvoiceData data)
        {
            return new XElement(DefaultNs + "InvoiceTo",
                new XElement(DefaultNs + "Party", data.InvoiceToName)
            );
        }

        private static XElement CreateAddressElement(AddressInfo address)
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

        private static IEnumerable<XElement> CreateInvoiceLines(InvoiceData data)
        {
            int lineNumber = 1;
            foreach (var item in data.LineItems)
            {
                yield return CreateInvoiceLine(item, lineNumber++);
            }
        }

        private static XElement CreateInvoiceLine(LineItem item, int lineNumber)
        {
            return new XElement(DefaultNs + "InvoiceLine",
                new XElement(DefaultNs + "LineNumber", lineNumber),
                new XElement(DefaultNs + "InvoiceLineReferences",
                    new XElement(DefaultNs + "OrderLineNumber", item.ItemNumber),
                    new XElement(DefaultNs + "BuyersOrderLineReference", $"{item.ItemNumber} {item.ProductCode}")
                ),
                new XElement(OpNs + "AdditionalInvoiceReferences",
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

        private static XElement CreateSettlement(InvoiceData data)
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

        private static IEnumerable<XElement> CreateTaxSubTotals(InvoiceData data)
        {
            foreach (var vatGroup in data.VATDetails)
            {
                yield return new XElement(DefaultNs + "TaxSubTotal",
                    new XElement(DefaultNs + "TaxRate",
                        new XAttribute("Code", "S"),
                        vatGroup.Rate.ToString("F2")
                    ),
                    new XElement(DefaultNs + "NumberOfLineAtRate", data.LineItems.Count),
                    new XElement(DefaultNs + "TotalValueAtRate", vatGroup.PrincipleValue.ToString("F3")),
                    new XElement(DefaultNs + "TaxableValueAtRate", vatGroup.PrincipleValue.ToString("F1")),
                    new XElement(DefaultNs + "TaxAtRate", vatGroup.VATValue.ToString("F2")),
                    new XElement(DefaultNs + "NetPaymentAtRate", vatGroup.PrincipleValue.ToString("F3")),
                    new XElement(DefaultNs + "GrossPaymentAtRate", (vatGroup.PrincipleValue + vatGroup.VATValue).ToString("F3")),
                    new XElement(DefaultNs + "TaxCurrency",
                        new XElement(DefaultNs + "Currency",
                            new XAttribute("Code", data.CurrencyCode),
                            data.CurrencyCode
                        )
                    )
                );
            }
        }

        #endregion

        #region Invoice Total Section

        private static XElement CreateInvoiceTotal(InvoiceData data)
        {
            return new XElement(DefaultNs + "InvoiceTotal",
                new XElement(DefaultNs + "NumberOfLines", data.LineItems.Count),
                new XElement(DefaultNs + "NumberOfTaxRates", data.VATDetails.Count),
                new XElement(DefaultNs + "LinesValueTotal", data.NetTotal.ToString("F3")),
                new XElement(DefaultNs + "TaxableTotal", data.NetTotal.ToString("F2")),
                new XElement(DefaultNs + "TaxTotal", data.VATTotal.ToString("F2")),
                new XElement(DefaultNs + "NetPaymentTotal", data.GrossTotal.ToString("F2")),
                new XElement(DefaultNs + "GrossPaymentTotal", data.GrossTotal.ToString("F2"))
            );
        }

        #endregion
    }

    #region Data Models

    public class InvoiceData
    {
        public string CompanyName { get; set; } = string.Empty;
        public string VATRegistrationNo { get; set; } = string.Empty;
        public string CompanyRegistrationNo { get; set; } = string.Empty;
        public AddressInfo CompanyAddress { get; set; } = new AddressInfo();

        // Invoice Details
        public string InvoiceNumber { get; set; } = string.Empty;
        public string CustomerOrderNumber { get; set; } = string.Empty;
        public string YourReference { get; set; } = string.Empty;
        public string InvoiceDate { get; set; } = string.Empty;
        public string OrderDate { get; set; } = string.Empty;
        public string DespatchDate { get; set; } = string.Empty;
        public string DespatchNumber { get; set; } = string.Empty;
        public string SalesOrderNumber { get; set; } = string.Empty;

        // Customer Details
        public string CustomerAccount { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string InvoiceToName { get; set; } = string.Empty;
        public AddressInfo DeliverToAddress { get; set; } = new AddressInfo();
        public AddressInfo InvoiceToAddress { get; set; } = new AddressInfo();

        // Currency
        public string CurrencyCode { get; set; } = "GBP";
        public string CurrencyName { get; set; } = "Sterling";

        // Payment Terms
        public int PaymentDays { get; set; } = 30;
        public decimal EarlyPaymentDiscountPercent { get; set; } = 0;

        // Totals
        public decimal NetTotal { get; set; }
        public decimal VATTotal { get; set; }
        public decimal GrossTotal { get; set; }

        // Line Items and VAT
        public List<LineItem> LineItems { get; set; } = new List<LineItem>();
        public List<VATDetail> VATDetails { get; set; } = new List<VATDetail>();

        public InvoiceData(XElement root)
        {
            ParseCompanyDetails(root);
            ParseInvoiceDetails(root);
            ParseCustomerDetails(root);
            ParsePricingDetails(root);
            ParseDespatches(root);
            ParseVATDetails(root);
        }

        private void ParseCompanyDetails(XElement root)
        {
            var companyDetails = root.Element("CompanyDetails");
            if (companyDetails != null)
            {
                CompanyName = companyDetails.Attribute("Name")?.Value ?? string.Empty;
                VATRegistrationNo = companyDetails.Attribute("VATRegistrationNo")?.Value ?? string.Empty;
                CompanyRegistrationNo = companyDetails.Attribute("CoRegistrationNo")?.Value ?? string.Empty;
                CompanyAddress = ParseAddress(companyDetails.Element("Address"));
            }
        }

        private void ParseInvoiceDetails(XElement root)
        {
            var invoice = root.Element("Invoice");
            if (invoice != null)
            {
                InvoiceNumber = ExtractNumericPart(invoice.Attribute("Number")?.Value ?? string.Empty);
                CustomerOrderNumber = invoice.Attribute("OurReference")?.Value ?? string.Empty;
                YourReference = invoice.Attribute("YoueReference")?.Value ?? string.Empty;

                var dates = invoice.Element("Dates");
                if (dates != null)
                {
                    InvoiceDate = ConvertToIsoDate(dates.Element("InvoiceDate")?.Attribute("Date")?.Value);

                    var invoiceDateStr = dates.Element("InvoiceDate")?.Attribute("Date")?.Value;
                    var paymentDueDateStr = dates.Element("PaymenrDueDate")?.Attribute("Date")?.Value;
                    PaymentDays = CalculateDaysDifference(invoiceDateStr, paymentDueDateStr);
                }

                var pricingDetails = invoice.Element("PricingDetails");
                if (pricingDetails != null)
                {
                    var paymentTerms = pricingDetails.Element("PaymentTerms");
                    if (paymentTerms != null)
                    {
                        EarlyPaymentDiscountPercent = ParseDecimal(paymentTerms.Attribute("EarlyPercent")?.Value);
                    }
                }
            }
        }

        private void ParseCustomerDetails(XElement root)
        {
            var customerDetails = root.Element("Invoice")?.Element("CustomerDetails");
            if (customerDetails != null)
            {
                var customer = customerDetails.Element("Customer");
                if (customer != null)
                {
                    CustomerAccount = customer.Attribute("Account")?.Value ?? string.Empty;
                    CustomerName = customer.Attribute("Name")?.Value ?? string.Empty;
                }

                var deliverTo = customerDetails.Element("DeliverTo");
                if (deliverTo != null)
                {
                    DeliverToAddress = ParseAddress(deliverTo.Element("Addess"));
                    DeliverToAddress.ContactName = deliverTo.Attribute("Name")?.Value ?? string.Empty;
                }

                var invoiceTo = customerDetails.Element("InvoiceTo");
                if (invoiceTo != null)
                {
                    var invoiceToCustomer = invoiceTo.Element("Customer");
                    InvoiceToName = invoiceToCustomer?.Attribute("Name")?.Value ?? CustomerName;
                    InvoiceToAddress = ParseAddress(invoiceTo.Element("Address"));
                }
            }
        }

        private void ParsePricingDetails(XElement root)
        {
            var pricingDetails = root.Element("Invoice")?.Element("PricingDetails");
            if (pricingDetails != null)
            {
                var documentCurrency = pricingDetails.Element("DocumentCurrency");
                if (documentCurrency != null)
                {
                    CurrencyCode = documentCurrency.Attribute("DocumentCurrencyCode")?.Value ?? "GBP";
                    CurrencyName = MapCurrencyName(CurrencyCode);
                }

                NetTotal = ParseDecimal(pricingDetails.Element("Value")?.Attribute("DocumentValue")?.Value);
                VATTotal = ParseDecimal(pricingDetails.Element("VAT")?.Element("Value")?.Attribute("DocumentValue")?.Value);
                GrossTotal = NetTotal + VATTotal;
            }
        }

        private void ParseDespatches(XElement root)
        {
            var despatches = root.Element("Despatches");
            if (despatches == null) return;

            var despatch = despatches.Element("Despatch");
            if (despatch == null) return;

            var despatchDetails = despatch.Element("DepatchDetails");
            if (despatchDetails != null)
            {
                DespatchNumber = despatchDetails.Attribute("DespatchNumber")?.Value ?? string.Empty;
                DespatchDate = ConvertToIsoDate(despatchDetails.Element("Dates")?.Element("DespatchDate")?.Attribute("Date")?.Value);
            }

            var salesOrders = despatch.Element("SalesOrders");
            if (salesOrders != null)
            {
                foreach (var salesOrder in salesOrders.Elements("SalesOrder"))
                {
                    var orderDetails = salesOrder.Element("SalesOrderDetails");
                    if (orderDetails != null)
                    {
                        SalesOrderNumber = orderDetails.Attribute("SalesOrderNumber")?.Value ?? string.Empty;
                        OrderDate = ConvertToIsoDate(orderDetails.Element("Dates")?.Element("Document")?.Attribute("Date")?.Value);
                    }

                    var items = salesOrder.Element("Items");
                    if (items != null)
                    {
                        foreach (var item in items.Elements("Item"))
                        {
                            LineItems.Add(ParseLineItem(item));
                        }
                    }
                }
            }

            var charges = despatch.Element("Charges");
            if (charges != null)
            {
                foreach (var charge in charges.Elements("Charge"))
                {
                    var chargeItem = ParseChargeAsLineItem(charge);
                    if (chargeItem != null)
                    {
                        LineItems.Add(chargeItem);
                    }
                }
            }
        }

        private LineItem ParseLineItem(XElement item)
        {
            var product = item.Element("Product");
            var quantities = item.Element("Quantites")?.Element("OrderQuantity");
            var prices = item.Element("Prices")?.Element("UnitPrice");
            var lineValues = item.Element("LineValues")?.Element("NetLineValue");
            var vat = item.Element("VAT");

            return new LineItem()
            {
                ItemNumber = item.Attribute("ItemNumber")?.Value ?? "1",
                ProductCode = product?.Attribute("Code")?.Value ?? string.Empty,
                Description = product?.Attribute("Description1")?.Value ?? string.Empty,
                Quantity = ParseDecimal(quantities?.Attribute("Quantity")?.Value),
                UnitOfMeasure = quantities?.Attribute("UOM")?.Value ?? "EACH",
                UnitPrice = ParseDecimal(prices?.Attribute("DocumentPrice")?.Value),
                LineTotal = ParseDecimal(lineValues?.Attribute("DocumentValue")?.Value),
                VATCode = vat?.Attribute("Code")?.Value ?? "ASTD",
                VATRate = ParseDecimal(vat?.Attribute("Rate")?.Value),
                VATValue = ParseDecimal(vat?.Element("VATValue")?.Attribute("DocumentValue")?.Value)
            };
        }

        private LineItem? ParseChargeAsLineItem(XElement charge)
        {
            var chargeValue = ParseDecimal(charge.Element("ChargeValue")?.Attribute("DocumentValue")?.Value);
            if (chargeValue <= 0) return null;

            var chargeCode = charge.Element("ChargeCode");
            var vat = charge.Element("VAT");

            return new LineItem()
            {
                ItemNumber = "C1",
                ProductCode = chargeCode?.Attribute("Code")?.Value ?? "CHARGE",
                Description = chargeCode?.Attribute("Description")?.Value ?? "Charge",
                Quantity = 1,
                UnitOfMeasure = "EACH",
                UnitPrice = chargeValue,
                LineTotal = chargeValue,
                VATCode = vat?.Attribute("Code")?.Value ?? "ASTD",
                VATRate = ParseDecimal(vat?.Attribute("Rate")?.Value),
                VATValue = ParseDecimal(vat?.Element("VATValue")?.Attribute("DocumentValue")?.Value),
                IsCharge = true
            };
        }

        private void ParseVATDetails(XElement root)
        {
            var vatDetails = root.Element("VATDetails");
            if (vatDetails != null)
            {
                foreach (var vat in vatDetails.Elements("VAT"))
                {
                    vatDetails.Add(new VATDetail()
                    {
                        Code = vat.Attribute("Code")?.Value ?? string.Empty,
                        Description = vat.Attribute("Description").Value ?? string.Empty,
                        Rate = ParseDecimal(vat.Attribute("Rate")?.Value),
                        PrincipleValue = ParseDecimal(vat.Element("VATPrinciple")?.Attribute("DocumentValue")?.Value),
                        VATValue = ParseDecimal(vat.Element("VATValue")?.Attribute("DocumentValue")?.Value)
                    });
                }
            }
        }

        #region Helper Methods

        private AddressInfo ParseAddress(XElement? addressElement)
        {
            var address = new AddressInfo();
            if (addressElement == null) return address;

            var lines = new List<string>();
            for (int i = 1; i <= 6; i++)
            {
                var line = addressElement.Attribute($"Line{i}")?.Value;
                if (!string.IsNullOrWhiteSpace(line))
                {
                    lines.Add(line);
                }
            }

            address.Lines = lines;
            address.PostCode = addressElement.Attribute("PostCode")?.Value ?? string.Empty;
            address.CountryCode = addressElement.Attribute("CountryCode")?.Value ?? string.Empty;
            address.Country = addressElement.Attribute("Country")?.Value ?? string.Empty;

            return address;
        }

        private string ConvertToIsoDate(string? dateStr)
        {
            if (string.IsNullOrEmpty(dateStr)) return DateTime.Now.ToString("yyyy-MM-dd");

            if (DateTime.TryParseExact(dateStr, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                return date.ToString("yyyy-MM-dd");
            }

            return DateTime.Now.ToString("yyyy-MM-dd");
        }

        private int CalculateDaysDifference(string? startDateStr, string? endDateStr)
        {
            if (string.IsNullOrEmpty(startDateStr) || string.IsNullOrEmpty(endDateStr)) return 30;

            if (DateTime.TryParseExact(startDateStr, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var startDate) &&
                DateTime.TryParseExact(endDateStr, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var endDate))
            {
                return (int)(endDate - startDate).TotalDays;
            }

            return 30;
        }

        private string ExtractNumericPart(string value) =>
            new string(value.Where(char.IsDigit).ToArray());

        private decimal ParseDecimal(string? value)
        {
            if (string.IsNullOrEmpty(value)) return 0;
            return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : 0;
        }

        private string MapCurrencyName(string code)
        {
            return code switch
            {
                "GBP" => "Sterling",
                "EUR" => "Euro",
                "USD" => "US Dollar",
                _ => code
            };
        }

        #endregion
    }

    public class AddressInfo
    {
        public string ContactName { get; set; } = string.Empty;
        public List<string> Lines { get; set; } = new List<string>();
        public string PostCode { get; set; } = string.Empty;
        public string CountryCode { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
    }

    public class LineItem
    {
        public string ItemNumber { get; set; } = string.Empty;
        public string ProductCode { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public string UnitOfMeasure { get; set; } = string.Empty;
        public decimal UnitPrice { get; set; }
        public decimal LineTotal { get; set; }
        public string VATCode { get; set; } = string.Empty;
        public decimal VATRate { get; set; }
        public decimal VATValue { get; set; }
        public bool IsCharge { get; set; }
    }

    public class VATDetail
    {
        public string Code { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal Rate { get; set; }
        public decimal PrincipleValue { get; set; }
        public decimal VATValue { get; set; }
    }

    #endregion
}
