namespace MooRCON.Wpf.ViewModels;

public enum OutputKind
{
    Command,   // введённая команда
    Response,  // ответ сервера
    System     // системные сообщения клиента
}

public sealed class OutputEntry
{
    public string Text { get; }
    public OutputKind Kind { get; }

    public OutputEntry(string text, OutputKind kind)
    {
        Text = text;
        Kind = kind;
    }
}
