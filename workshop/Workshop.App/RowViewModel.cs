using System.ComponentModel;
using System.Runtime.CompilerServices;
using Workshop.Core;

namespace Workshop.App;

public sealed class RowViewModel : INotifyPropertyChanged
{
    public CorpusRow Source { get; }
    public string Id => Source.Record.Id;
    public string Key => Source.Record.Key;
    public string Section => Source.Section;
    public string Original => Source.Record.Value;
    public string OriginalDisplay => TextDocument.Display(Original);
    public string FrenchDisplay => TextDocument.Display(Source.French);
    public string LegacyDisplay => TextDocument.Display(Source.Legacy);
    public string Location => $"{Source.File} • строка {Source.Record.LineIndex + 1} • вхождение {Source.Record.Occurrence + 1}";
    public string ShortText => OriginalDisplay.Replace('\n', ' ');
    private string target = "", note = "", sourceStamp;
    private bool reviewed;
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<RowViewModel>? Edited;

    public RowViewModel(CorpusRow source, TranslationEdit? edit)
    {
        Source = source;
        sourceStamp = edit?.SourceHash ?? source.Document.SourceHash;
        if (edit is not null) { target = edit.Target; note = edit.Note; reviewed = edit.Status == "Проверено"; }
    }

    public string Target
    {
        get => target;
        set
        {
            if (value == target) return;
            target = value; reviewed = false; sourceStamp = Source.Document.SourceHash;
            Change();
        }
    }
    public string DraftText { get => TextDocument.Display(target); set => Target = TextDocument.ToEngine(value); }
    public string Note { get => note; set { if (note != value) { note = value; Change(); } } }
    public bool Reviewed { get => reviewed; set { if (reviewed != value) { reviewed = value; if (value) sourceStamp = Source.Document.SourceHash; Change(); } } }
    public bool HasTranslation => target.Length > 0;
    public bool HasSavedContent => HasTranslation || note.Length > 0 || reviewed;
    public string Status => !HasTranslation ? "Не переведено" : HasErrors ? "Ошибка" : reviewed ? "Проверено" : "Черновик";
    public int SourceLength => Original.Length;
    public int TargetLength => target.Length;
    public bool HasErrors => Diagnostics.Any(x => x.Severity == "Ошибка");
    public List<Diagnostic> Diagnostics
    {
        get
        {
            var diagnostics = TranslationValidator.Validate(Id, Original, target);
            if (sourceStamp != Source.Document.SourceHash && HasTranslation)
                diagnostics.Add(new(Id, "Ошибка", "Исходный файл изменился после перевода. Сверьте текст и подтвердите проверку."));
            return diagnostics;
        }
    }
    public TranslationEdit ToEdit() => new() { Target = target, Note = note, Status = reviewed ? "Проверено" : "Черновик", SourceHash = sourceStamp };

    private void Change()
    {
        foreach (string property in new[] { nameof(Target), nameof(DraftText), nameof(Note), nameof(Reviewed), nameof(Status), nameof(HasTranslation), nameof(TargetLength), nameof(Diagnostics), nameof(HasErrors) }) Notify(property);
        Edited?.Invoke(this);
    }
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
