namespace FO4RecordEditor.Models;

public enum ReferenceKind { Generic, Keyword, LeveledListEntry, Script, Master, ActorValue }

public sealed record Reference(
    string FromFormKey, string ToFormKey, string FieldPath, ReferenceKind Kind);
