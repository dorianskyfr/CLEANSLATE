using System.Diagnostics;
using System.Security.Principal;

namespace CleanSlate.App.Infrastructure;

/// <summary>
/// Aide à l'élévation UAC « à la demande ». L'application démarre sans privilèges ;
/// quand l'utilisateur souhaite nettoyer des cibles système, on relance le
/// processus en tant qu'administrateur (verbe "runas").
/// </summary>
public static class ElevationHelper
{
    /// <summary>Vrai si le processus courant s'exécute avec des droits administrateur.</summary>
    public static bool IsRunningAsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Relance l'application en tant qu'administrateur, puis ferme l'instance
    /// courante. Retourne false si l'utilisateur refuse l'UAC.
    /// </summary>
    public static bool RestartAsAdministrator()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
            return false;

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = true, // requis pour le verbe "runas"
            Verb = "runas",
        };

        try
        {
            Process.Start(startInfo);
            return true;
        }
        catch
        {
            // L'utilisateur a refusé l'invite UAC : on reste en mode non élevé.
            return false;
        }
    }
}
