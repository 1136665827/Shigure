namespace Shigure;

public sealed record RecognizedAuraInfo(
    string Name,
    int Value,
    int Time,
    string Row,
    int Index,
    string Hash,
    int? HashDistance,
    double? TemplateScore);
