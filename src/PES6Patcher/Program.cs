namespace PES6Patcher;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        LanguagePreference.Apply(LanguagePreference.Load());

        // Changing the language rebuilds the window (no restart: that would ask for administrator rights again).
        MainForm.State? state = null;
        do
        {
            var form = new MainForm(state);
            Application.Run(form);
            state = form.RebuildWith;
        } while (state != null);
    }
}
