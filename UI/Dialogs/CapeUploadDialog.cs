using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace AxoClient.UI.Dialogs;

public static class CapeUploadDialog
{
    public static async Task<(string Name, byte[] Png)?> AskAsync(AppServices app)
    {
        byte[]? png = null;
        string? fileProblem = null;
        var touched = false;

        var name = Ui.Input();
        name.MaxLength = 32;
        var fileText = new TextBlock
        {
            Text = "Keine Datei ausgewählt",
            Foreground = Ui.Resource<Brush>("MutedText"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 250
        };
        var preview = new Image { Width = 50, Height = 80, Stretch = Stretch.Uniform };
        var previewBox = new Border
        {
            Child = preview,
            Width = 70,
            Height = 96,
            CornerRadius = new CornerRadius(6),
            Background = Ui.Resource<Brush>("FieldBackground"),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 12),
            Visibility = Visibility.Collapsed
        };
        var problem = Ui.Problem();

        bool Validate() => Ui.ShowProblem(problem,
            name.Text.Trim().Length == 0 ? "Gib dem Umhang einen Namen."
            : fileProblem ?? (png == null ? "Wähle ein PNG-Bild aus." : null),
            visible: touched);

        var choose = Ui.Button("Datei wählen...", () =>
        {
            var dialog = new OpenFileDialog { Filter = "Umhang (*.png)|*.png", Title = "Umhang auswählen" };
            if (dialog.ShowDialog(Application.Current.MainWindow) != true)
                return;
            touched = true;
            fileText.Text = Path.GetFileName(dialog.FileName);
            try
            {
                var bytes = File.ReadAllBytes(dialog.FileName);
                fileProblem = ClientCapeService.UploadProblem(bytes);
                png = fileProblem == null ? bytes : null;
            }
            catch (Exception ex)
            {
                fileProblem = "Die Datei konnte nicht gelesen werden: " + ErrorReport.Short(ex);
                png = null;
            }
            preview.Source = png == null ? null : SkinRenderer.RenderCape(png);
            RenderOptions.SetBitmapScalingMode(preview, preview.Source?.Width > 20
                ? BitmapScalingMode.HighQuality
                : BitmapScalingMode.NearestNeighbor);
            Ui.Show(previewBox, png != null);
            if (name.Text.Trim().Length == 0)
                name.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
            Validate();
        });

        name.TextChanged += (_, _) => Validate();

        var form = Ui.Stack(
            Ui.Label("Name"), name,
            Ui.Label("Bild"), Ui.Row(choose, fileText),
            previewBox,
            Ui.Note($"PNG mit 64×32 Pixeln oder einem Vielfachen davon (bis {ClientCapeService.MaxUploadWidth}×" +
                    $"{ClientCapeService.MaxUploadWidth / 2}), höchstens {Formats.Size(ClientCapeService.MaxUploadBytes)}. " +
                    "Danach können alle AxoClient-Nutzer den Umhang auswählen.", 0, 11),
            problem);

        bool Confirm()
        {
            touched = true;
            return Validate();
        }

        return await app.Dialogs.ShowFormAsync("AxoClient-Umhang hochladen", form, "Hochladen", Confirm)
            ? (name.Text.Trim(), png!)
            : null;
    }
}
