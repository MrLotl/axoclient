namespace AxoClient.UI.Dialogs;

public static class NamePrompt
{
    public static async Task<string?> AskAsync(AppServices app, string title, string suggestion, string confirmText,
        Func<string, string> clean, Func<string, string?> problemOf)
    {
        var box = Ui.Input(suggestion);
        var problem = Ui.Problem();

        bool Validate() => Ui.ShowProblem(problem, problemOf(clean(box.Text)), visible: box.Text.Length > 0);

        box.TextChanged += (_, _) => Validate();
        Validate();
        return await app.Dialogs.ShowFormAsync(title, Ui.Stack(Ui.Label("Name des Profils"), box, problem), confirmText, Validate)
            ? clean(box.Text)
            : null;
    }
}
