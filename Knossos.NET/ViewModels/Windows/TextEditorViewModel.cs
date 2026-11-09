using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Knossos.NET.Classes;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Knossos.NET.ViewModels
{
    public partial class TextEditorViewModel : ViewModelBase
    {
        private readonly string path;
        private readonly Action? close;
        private TextFileDocument? document;
        private bool allowDiscard;
        [ObservableProperty] private string text = string.Empty;
        [ObservableProperty] private string error = string.Empty;
        [ObservableProperty] private bool isLoading;
        [ObservableProperty] private bool isSaving;
        [ObservableProperty] private bool isDiscardPromptOpen;
        public string Title => "Edit " + Path.GetFileName(path);
        public string FilePath => path;
        public string EncodingLabel => document?.EncodingLabel ?? "Loading encoding…";
        public bool IsDirty => document != null && Text != document.Text;
        public bool CanEdit => !IsLoading && !IsSaving && document != null;
        public bool CanSave => CanEdit && IsDirty;
        public TextEditorViewModel() : this(string.Empty) { }
        public TextEditorViewModel(string path, Action? close = null) { this.path = path; this.close = close; }
        partial void OnTextChanged(string value) { OnPropertyChanged(nameof(IsDirty)); OnPropertyChanged(nameof(CanSave)); SaveCommand.NotifyCanExecuteChanged(); }
        private void NotifyState()
        {
            OnPropertyChanged(nameof(EncodingLabel)); OnPropertyChanged(nameof(IsDirty));
            OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(CanSave)); SaveCommand.NotifyCanExecuteChanged();
        }
        public async Task LoadAsync()
        {
            IsLoading = true; Error = string.Empty; NotifyState();
            try { document = await TextFileDocument.OpenAsync(path); Text = document.Text; }
            catch (Exception ex) { Error = ex.Message; }
            finally { IsLoading = false; NotifyState(); }
        }
        [RelayCommand(CanExecute = nameof(CanSave))]
        private async Task SaveAsync()
        {
            if (document == null) return;
            IsSaving = true; Error = string.Empty; NotifyState();
            try { await document.SaveAsync(Text); }
            catch (Exception ex) { Error = ex.Message; }
            finally { IsSaving = false; NotifyState(); }
        }
        public bool RequestClose()
        {
            if (IsSaving) return false;
            if (IsDirty && !allowDiscard) { IsDiscardPromptOpen = true; return false; }
            return true;
        }
        [RelayCommand] private void DiscardAndClose() { allowDiscard = true; close?.Invoke(); }
        [RelayCommand] private void KeepEditing() => IsDiscardPromptOpen = false;
    }
}
