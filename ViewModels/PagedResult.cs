namespace HardwareManagementSystem.ViewModels
{
    public class PagedResult<T>
    {
        public IEnumerable<T> Items { get; set; } = new List<T>();
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 10;
        public int TotalRecords { get; set; }
        public int TotalPages => (int)Math.Ceiling(TotalRecords / (double)PageSize);
        public string? SearchTerm { get; set; }
        public string? SortBy { get; set; }
        public string? SortDirection { get; set; }

        public int FirstItemIndex => TotalRecords == 0 ? 0 : (PageNumber - 1) * PageSize + 1;
        public int LastItemIndex => Math.Min(PageNumber * PageSize, TotalRecords);

        public bool HasPreviousPage => PageNumber > 1;
        public bool HasNextPage => PageNumber < TotalPages;

        public static readonly int[] AllowedPageSizes = [10, 25, 50, 100];

        public static int ValidatePageSize(int pageSize)
            => AllowedPageSizes.Contains(pageSize) ? pageSize : 10;

        public static int ValidatePageNumber(int pageNumber, int totalPages)
        {
            if (pageNumber < 1) return 1;
            if (totalPages > 0 && pageNumber > totalPages) return totalPages;
            return pageNumber;
        }
    }
}
