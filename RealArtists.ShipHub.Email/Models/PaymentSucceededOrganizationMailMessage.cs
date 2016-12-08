namespace RealArtists.ShipHub.Email.Models {
  using System;

  public class PaymentSucceededOrganizationMailMessage : MailMessageBase {
    public double AmountPaid { get; set; }
    public string LastCardDigits { get; set; }
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
    public byte[] InvoicePdfBytes { get; set; }
    public DateTimeOffset InvoiceDate { get; set; }
    public int PreviousMonthActiveUsersCount { get; set; }
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
    public string[] PreviousMonthActiveUsersSample { get; set; }
    public DateTimeOffset PreviousMonthStart { get; set; }
    public DateTimeOffset ServiceThroughDate { get; set; }
  }
}