namespace RealArtists.ShipHub.Mail.Models {
  using System;
  using System.Diagnostics.CodeAnalysis;

  public class PaymentSucceededOrganizationMailMessage : MailMessageBase {
    public double AmountPaid { get; set; }
    public string InvoicePdfUrl { get; set; }
    public PaymentMethodSummary PaymentMethodSummary { get; set; }
    public int PreviousMonthActiveUsersCount { get; set; }
    [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
    public string[] PreviousMonthActiveUsersSample { get; set; }
    public DateTimeOffset PreviousMonthStart { get; set; }
    public DateTimeOffset ServiceThroughDate { get; set; }
  }
}
