using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace XmlInvoiceTransformer
{
    /// <summary>
    /// Represents all extracted data from the input invoice XML
    /// </summary>
    public class InvoiceData
    {
        // Company Details
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
                YourReference = invoice.Attribute("YourReference")?.Value ?? string.Empty;

                var dates = invoice.Element("Dates");
                if (dates != null)
                {
                    InvoiceDate = ConvertToIsoDate(dates.Element("InvoiceDate")?.Attribute("Date")?.Value);

                    var invoiceDateStr = dates.Element("InvoiceDate")?.Attribute("Date")?.Value;
                    var paymentDueDateStr = dates.Element("PaymentDueDate")?.Attribute("Date")?.Value;
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
                    DeliverToAddress = ParseAddress(deliverTo.Element("Address"));
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

            var despatchDetails = despatch.Element("DespatchDetails");
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

            // Include charges as line items if needed
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
            var quantities = item.Element("Quantities")?.Element("OrderQuantity");
            var prices = item.Element("Prices")?.Element("UnitPrice");
            var lineValues = item.Element("LineValues")?.Element("NetLineValue");
            var vat = item.Element("VAT");

            return new LineItem
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

            return new LineItem
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
                    VATDetails.Add(new VATDetail
                    {
                        Code = vat.Attribute("Code")?.Value ?? string.Empty,
                        Description = vat.Attribute("Description")?.Value ?? string.Empty,
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
            address.PostCode = addressElement.Attribute("Postcode")?.Value ?? string.Empty;
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

        private string ExtractNumericPart(string value)
        {
            return new string(value.Where(char.IsDigit).ToArray());
        }

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
        public string UnitOfMeasure { get; set; } = "EACH";
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
}