namespace SystemPanel.ViewModels.Files
{
    public class FileItemViewModel
    {
        public string Name { get; set; }
        public long Size { get; set; }

        public string SizeDisplay =>
            Size < 1024 ? $"{Size} B" :
            Size < 1024 * 1024 ? $"{Size / 1024.0:F1} KB" :
            $"{Size / (1024.0 * 1024):F1} MB";
    }
}
