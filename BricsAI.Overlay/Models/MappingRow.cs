using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace BricsAI.Overlay.Models
{
    public class MappingRow : INotifyPropertyChanged
    {
        public const string NoneTarget = "None";

        /// <summary>Targets offered in each row's Target Layer drop-down. The AI's own suggestion is
        /// always added to a row's list if it is not one of these, so it can stay selected.</summary>
        public static readonly string[] StandardTargets =
            { NoneTarget, "Expo_Building", "Expo_Column", "Expo_View2", "Expo_Markings", "Expo_NES", "Expo_ImageMarkings" };

        private bool _isSelected;
        private string _targetLayer = "";
        private string _status = "pending";

        public event PropertyChangedEventHandler? PropertyChanged;
        /// <summary>Raised when the user (or Select All) changes the checkbox, so the header can refresh.</summary>
        public event EventHandler? SelectionChanged;

        public int Index { get; set; }
        public string SourceLayer { get; set; } = "";
        public string Confidence { get; set; } = "High";
        public string Reason { get; set; } = "";
        public string? SnapshotPath { get; set; }

        public List<string> TargetOptions { get; private set; } = new List<string>(StandardTargets);

        /// <summary>Target chosen in the drop-down. "None" means: do not map this layer.</summary>
        public string TargetLayer
        {
            get => _targetLayer;
            set
            {
                if (_targetLayer == value) return;
                _targetLayer = value;
                Raise();
                Raise(nameof(IsMappable));
                if (!IsMappable && _isSelected) IsSelected = false;
                else SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>False when the target is None — such a row cannot be included.</summary>
        public bool IsMappable => !string.Equals(_targetLayer, NoneTarget, StringComparison.OrdinalIgnoreCase);

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (value && !IsMappable) value = false;
                if (_isSelected == value) return;
                _isSelected = value;
                Status = value ? "included" : "excluded";
                Raise();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public string Status
        {
            get => _status;
            private set { if (_status != value) { _status = value; Raise(); } }
        }

        /// <summary>Sets the initial state without raising selection events.</summary>
        public void Init(string target, bool selected, string status)
        {
            if (!TargetOptions.Contains(target, StringComparer.OrdinalIgnoreCase))
                TargetOptions.Add(target);
            _targetLayer = target;
            _isSelected = selected && IsMappable;
            _status = status;
        }

        public bool IsLowConfidence => Confidence == "Low";
        public bool HasSnapshot => !string.IsNullOrEmpty(SnapshotPath) && File.Exists(SnapshotPath);

        public string SourceLayerDisplay => SourceLayer;
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

        private void Raise([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private static string Truncate(string s, int max) =>
            s.Length <= max ? s : s.Substring(0, max - 1) + "…";
    }
}
