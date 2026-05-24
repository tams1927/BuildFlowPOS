namespace HardwareManagementSystem.ViewModels
{
    public class PaginationMeta
    {
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
        public int TotalRecords { get; set; }
        public int TotalPages => (int)Math.Ceiling(TotalRecords / (double)PageSize);
        public int FirstItemIndex => TotalRecords == 0 ? 0 : (PageNumber - 1) * PageSize + 1;
        public int LastItemIndex => Math.Min(PageNumber * PageSize, TotalRecords);
        public string ActionName { get; set; } = "Index";
        public string ControllerName { get; set; } = string.Empty;
        public Dictionary<string, string?> ExtraParams { get; set; } = new();

        public static PaginationMeta From<T>(PagedResult<T> result, string controller, string action = "Index",
            Dictionary<string, string?>? extraParams = null)
        {
            return new PaginationMeta
            {
                PageNumber = result.PageNumber,
                PageSize = result.PageSize,
                TotalRecords = result.TotalRecords,
                ControllerName = controller,
                ActionName = action,
                ExtraParams = extraParams ?? new Dictionary<string, string?>()
            };
        }
    }
}
