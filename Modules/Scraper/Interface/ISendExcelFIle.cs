namespace JoraScraper.Modules.Scraper.Interface
{
    public interface ISendExcelFIle
    {
        Task<bool> SendRequest();
    }
}