using HardwareManagementSystem.Models;

namespace HardwareManagementSystem.ViewModels
{
    // ── Customer Statement of Account ─────────────────────────────────

    public class CustomerSOAViewModel
    {
        public Customer Customer { get; set; } = null!;
        public DateTime DateFrom { get; set; }
        public DateTime DateTo { get; set; }
        public decimal BeginningBalance { get; set; }
        public List<CustomerLedger> Transactions { get; set; } = new();
        public decimal ClosingBalance { get; set; }
        public decimal TotalDebits { get; set; }
        public decimal TotalCredits { get; set; }
    }

    // ── Customer Aging ────────────────────────────────────────────────

    public class CustomerAgingViewModel
    {
        public DateTime AsOfDate { get; set; }
        public List<CustomerAgingRow> Rows { get; set; } = new();

        public decimal TotalCurrent   => Rows.Sum(r => r.Current);
        public decimal TotalDays1_30  => Rows.Sum(r => r.Days1_30);
        public decimal TotalDays31_60 => Rows.Sum(r => r.Days31_60);
        public decimal TotalDays61_90 => Rows.Sum(r => r.Days61_90);
        public decimal TotalOver90    => Rows.Sum(r => r.Over90);
        public decimal TotalBalance   => Rows.Sum(r => r.TotalOutstanding);
    }

    public class CustomerAgingRow
    {
        public int    CustomerId        { get; set; }
        public string CustomerName      { get; set; } = string.Empty;
        public string? ContactNumber    { get; set; }
        public decimal CreditLimit      { get; set; }
        /// <summary>Charges within 0–30 days that are unpaid.</summary>
        public decimal Current          { get; set; }
        public decimal Days1_30         { get; set; }
        public decimal Days31_60        { get; set; }
        public decimal Days61_90        { get; set; }
        public decimal Over90           { get; set; }
        public decimal TotalOutstanding { get; set; }
    }

    // ── Supplier Statement ────────────────────────────────────────────

    public class SupplierStatementViewModel
    {
        public Supplier Supplier { get; set; } = null!;
        public DateTime DateFrom { get; set; }
        public DateTime DateTo { get; set; }
        public List<SupplierStatementLine> Lines { get; set; } = new();
        public decimal TotalPurchases      { get; set; }
        public decimal TotalPayments       { get; set; }
        public decimal OpeningBalance      { get; set; }
        public decimal ClosingBalance      { get; set; }
    }

    public class SupplierStatementLine
    {
        public DateTime Date           { get; set; }
        public string   ReferenceNo    { get; set; } = string.Empty;
        public string   Type           { get; set; } = string.Empty;  // Purchase / Payment
        public string   Description    { get; set; } = string.Empty;
        public decimal  Debit          { get; set; }
        public decimal  Credit         { get; set; }
        public decimal  RunningBalance { get; set; }
    }

    // ── Supplier Aging ────────────────────────────────────────────────

    public class SupplierAgingViewModel
    {
        public DateTime AsOfDate { get; set; }
        public List<SupplierAgingRow> Rows { get; set; } = new();

        public decimal TotalCurrent   => Rows.Sum(r => r.Current);
        public decimal TotalDays1_30  => Rows.Sum(r => r.Days1_30);
        public decimal TotalDays31_60 => Rows.Sum(r => r.Days31_60);
        public decimal TotalDays61_90 => Rows.Sum(r => r.Days61_90);
        public decimal TotalOver90    => Rows.Sum(r => r.Over90);
        public decimal TotalBalance   => Rows.Sum(r => r.TotalOutstanding);
    }

    public class SupplierAgingRow
    {
        public int    SupplierId        { get; set; }
        public string SupplierName      { get; set; } = string.Empty;
        public string? ContactNumber    { get; set; }
        public decimal Current          { get; set; }
        public decimal Days1_30         { get; set; }
        public decimal Days31_60        { get; set; }
        public decimal Days61_90        { get; set; }
        public decimal Over90           { get; set; }
        public decimal TotalOutstanding { get; set; }
    }
}
