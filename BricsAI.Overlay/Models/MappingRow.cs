using System.IO;
using System.Windows.Media.Imaging;

namespace BricsAI.Overlay.Models
{
    public class MappingRow
    {
        public int Index { get; set; }
        public string SourceLayer { get; set; } = "";
        public string TargetLayer { get; set; } = "";
        public string Confidence { get; set; } = "High";
        public string Reason { get; set; } = "";
        public string Status { get; set; } = "pending";
        public string? SnapshotPath { get; set; }

        public bool IsLowConfidence => Confidence == "Low";
        public bool HasSnapshot => !string.IsNullOrEmpty(SnapshotPath) && File.Exists(SnapshotPath);

        // Truncated versions for column display
        public string SourceLayerDisplay => Truncate(SourceLayer, 30);
        public string TargetLayerDisplay => Truncate(TargetLayer, 16);
        public string ReasonDisplay => Truncate(Reason, 36);

        // Lazily loaded image for the snapshot tooltip — null when no snapshot is available
        private BitmapImage? _snapshotImage;
        public BitmapImage? SnapshotImage
        {
            get
            {
                if (_snapshotImage != null) return _snapshotImage;
                if (!HasSnapshot) return null;
                try
                {
                    using var stream = File.OpenRead(SnapshotPath!);
                    var img = new BitmapImage();
                    img.BeginInit();
                    img.CacheOption = BitmapCacheOption.OnLoad;
                    img.StreamSource = stream;
                    img.EndInit();
                    img.Freeze();
                    _snapshotImage = img;
                    return _snapshotImage;
                }
                catch { return null; }
            }
        }

        private static string Truncate(string s, int max) =>
            s.Length <= max ? s : s.Substring(0, max - 1) + "…";
    }
}
