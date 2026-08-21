using System.Collections.Generic;
using System.ComponentModel;
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
        public List<MappingRow>? MappingRows { get; set; }

        public bool HasMappingRows => MappingRows is { Count: > 0 };

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
