using Microsoft.UI.Xaml;
using WinRT.Interop;

// Innerhalb von Nipp.App verdeckt der eigene Namespace Nipp.App.Windows das
// WinRT-Windows: aus Windows.Storage wuerde Nipp.App.Windows.Storage, und der
// Compiler meldete einen fehlenden Assemblyverweis. Dieselbe Falle wie
// Nipp.Core gegen Linphone.Core.
using StorageFile = global::Windows.Storage.StorageFile;
using FileOpenPicker = global::Windows.Storage.Pickers.FileOpenPicker;
using FileSavePicker = global::Windows.Storage.Pickers.FileSavePicker;
using PickerLocationId = global::Windows.Storage.Pickers.PickerLocationId;

namespace Nipp.App.Windows;

/// <summary>
/// Datei-Dialoge für WinUI 3.
///
/// <b>Warum es diesen Helfer gibt.</b> Ein Picker in WinUI 3 gehört zu einem
/// Fenster, und das muss ihm gesagt werden — die WinRT-Schnittstelle stammt aus
/// einer Zeit, in der eine Anwendung genau ein Fenster hatte. Ohne
/// <c>InitializeWithWindow</c> wirft <c>PickSingleFileAsync</c> nicht etwa eine
/// Ausnahme, sondern gar nichts sichtbar: der Dialog erscheint nie.
/// </summary>
internal static class FilePickers
{
    /// <summary>
    /// Fragt nach einem Ort zum Speichern. <c>null</c>, wenn abgebrochen wurde.
    /// </summary>
    public static async Task<StorageFile?> SaveAsync(
        Window window,
        string suggestedName,
        string typeLabel,
        string extension)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = suggestedName,
        };

        picker.FileTypeChoices.Add(typeLabel, [extension]);
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));

        return await picker.PickSaveFileAsync();
    }

    /// <summary>
    /// Fragt nach einer Datei zum Öffnen. <c>null</c>, wenn abgebrochen wurde.
    /// </summary>
    public static async Task<StorageFile?> OpenAsync(Window window, string extension)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };

        picker.FileTypeFilter.Add(extension);
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));

        return await picker.PickSingleFileAsync();
    }
}
