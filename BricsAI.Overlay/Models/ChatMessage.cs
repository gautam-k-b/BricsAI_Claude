using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace BricsAI.Overlay.Models
{
    public class ChatMessage : INotifyPropertyChanged
    {
        private bool _isThinking;

        private string _content = string.Empty;

        public string Role { get; set; } = string.Empty; // "User" or "Assistant"

        public string Content
        {
            get => _content;
            set
            {
                if (_content != value)
                {
                    _content = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsUser => Role == "User";
        public string DisplayName => IsUser ? "User:" : "BricsAI:";

        /// <summary>
        /// True for a mapping-review message. Widens the bubble and enables the structured
        /// row grid instead of plain text rendering.
        /// </summary>
        public bool IsTableContent { get; set; }

        /// <summary>
        /// Structured mapping rows shown in the interactive table below the header text.
        /// Null for non-table messages.
        /// </summary>
        private List<MappingRow>? _mappingRows;
        public List<MappingRow>? MappingRows
        {
            get => _mappingRows;
            set
            {
                _mappingRows = value;
                if (value != null)
                    foreach (var row in value) row.SelectionChanged += (_, _) => RefreshSelectionState();
                OnPropertyChanged();
                RefreshSelectionState();
            }
        }

        public bool HasMappingRows => MappingRows is { Count: > 0 };

        private bool _isActive = true;
        /// <summary>True only for the newest mapping table; older tables become read-only.</summary>
        public bool IsActive
        {
            get => _isActive;
            set { if (_isActive != value) { _isActive = value; OnPropertyChanged(); } }
        }

        private bool _suppressSelectAll;

        /// <summary>Header "select all" checkbox: true = all mappable rows selected, null = some, false = none.</summary>
        public bool? AllSelected
        {
            get
            {
                var mappable = MappingRows?.Where(r => r.IsMappable).ToList();
                if (mappable == null || mappable.Count == 0) return false;
                int sel = mappable.Count(r => r.IsSelected);
                return sel == 0 ? false : sel == mappable.Count ? true : (bool?)null;
            }
            set
            {
                if (MappingRows == null) return;
                _suppressSelectAll = true;
                foreach (var row in MappingRows) row.IsSelected = value == true;
                _suppressSelectAll = false;
                RefreshSelectionState();
            }
        }

        public string SelectionSummary =>
            MappingRows == null ? "" : $"{MappingRows.Count(r => r.IsSelected)} of {MappingRows.Count} selected";

        private void RefreshSelectionState()
        {
            if (_suppressSelectAll) return;
            OnPropertyChanged(nameof(AllSelected));
            OnPropertyChanged(nameof(SelectionSummary));
        }

        public bool IsThinking
        {
            get => _isThinking;
            set
            {
                if (_isThinking != value)
                {
                    _isThinking = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
