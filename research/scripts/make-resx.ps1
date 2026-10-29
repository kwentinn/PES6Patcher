# One-off generator used to create the initial Strings.resx (English) and Strings.fr.resx.
# Translators should edit / copy the .resx files directly.
param([string]$OutDir)

$strings = [ordered]@{
    # key = @(English, French)
    'Folder.Title'              = @('Game folder', 'Dossier du jeu')
    'Folder.Browse'             = @('Browse…', 'Parcourir…')
    'Folder.BrowseDescription'  = @('Folder containing PES6.exe', 'Dossier contenant PES6.exe')
    'Language.Label'            = @('Language:', 'Langue :')
    'Language.System'           = @('System ({0})', 'Système ({0})')
    'Display.Group'             = @('Display', 'Affichage')
    'Display.Resolution'        = @('Resolution:', 'Résolution :')
    'Display.ScreenResolution'  = @('Screen resolution', "Résolution de l'écran")
    'Display.FormatInfo'        = @('Aspect ratio {0:0.###}:1  —  menu layer {1} × 448 (original 512 × 448)', "Format {0:0.###}:1  —  repère des menus {1} × 448 (d'origine 512 × 448)")
    'Display.Borderless'        = @('Borderless window covering the screen (required above 1920 × 1080)', "Fenêtre sans bordure couvrant l'écran (nécessaire au-delà de 1920 × 1080)")
    'Gameplay.Group'            = @('Gameplay', 'Gameplay')
    'Gameplay.FreeDirection'    = @('Free analog stick direction', 'Directions libres au stick analogique')
    'Gameplay.Speed'            = @('Game speed:', 'Vitesse du jeu :')
    'Gameplay.SpeedHint'        = @('(1.00 = original speed)', "(1,00 = vitesse d'origine)")
    'Action.Apply'              = @('Apply', 'Appliquer')
    'Action.Restore'            = @('Restore original', "Restaurer l'original")
    'State.NotFound'            = @('PES6.exe not found in this folder.', 'PES6.exe introuvable dans ce dossier.')
    'State.Original'            = @('PES6.exe: original version, supported.', "PES6.exe : version d'origine, prise en charge.")
    'State.Patched'             = @('PES6.exe: already patched (the original is saved as PES6.exe.orig).', "PES6.exe : déjà patché (l'original est sauvegardé dans PES6.exe.orig).")
    'State.Unsupported'         = @('PES6.exe: unsupported version. This patch only works with one specific build of the exe.', "PES6.exe : version non prise en charge. Ce patch ne fonctionne qu'avec une version précise de l'exe.")
    'Log.Game'                  = @('Game: {0}', 'Jeu : {0}')
    'Log.NoKitserver'           = @('Warning: Kitserver not found (kitserver\kload.cfg). It is required.', 'Attention : Kitserver est introuvable (kitserver\kload.cfg). Il est nécessaire.')
    'Log.Error'                 = @('Error: {0}', 'Erreur : {0}')
    'Log.ExeBuilt'              = @('Patched exe built (2D half-width {0}, free directions: {1}).', 'Exe patché construit (demi-largeur 2D {0}, directions libres : {1}).')
    'Log.ExeInstalled'          = @('PES6.exe installed.', 'PES6.exe installé.')
    'Log.Kload'                 = @('kload.cfg: render {0} × {1}.', 'kload.cfg : rendu {0} × {1}.')
    'Log.KloadBorderless'       = @('kload.cfg: render {0} × {1}, borderless window.', 'kload.cfg : rendu {0} × {1}, fenêtre sans bordure.')
    'Log.Lodmixer'              = @('lodmixer.dat: 3D aspect ratio {0:0.#####}.', 'lodmixer.dat : format 3D {0:0.#####}.')
    'Log.LodmixerMissing'       = @("lodmixer.dat missing: run Kitserver's lodcfg.exe once, then apply again.", 'lodmixer.dat absent : lancer lodcfg.exe de Kitserver une fois, puis réappliquer.')
    'Log.Speeder'               = @('speeder.cfg: speed {0:0.0#}.', 'speeder.cfg : vitesse {0:0.0#}.')
    'Log.Settings'              = @('settings.dat: game resolution {0} × {1}.', 'settings.dat : résolution du jeu {0} × {1}.')
    'Log.SettingsMissing'       = @('settings.dat missing: run settings.exe once, then apply again.', 'settings.dat absent : lancer settings.exe une fois, puis réappliquer.')
    'Log.Done'                  = @('Done.', 'Terminé.')
    'Log.Backup'                = @('Backup: {0}.', 'Sauvegarde : {0}.')
    'Log.Restored'              = @('{0} restored.', '{0} restauré.')
    'Log.NothingToRestore'      = @('Nothing to restore.', 'Rien à restaurer.')
    'Log.RestoreDone'           = @('Restore complete.', 'Restauration terminée.')
    'Common.Yes'                = @('yes', 'oui')
    'Common.No'                 = @('no', 'non')
    'Error.GameRunning'         = @('Close PES6 and settings.exe first.', "Fermer PES6 et settings.exe d'abord.")
    'Error.NoKitserver'         = @('Kitserver not found: it is required for the resolution and the 3D aspect ratio.', 'Kitserver est introuvable : il est nécessaire pour la résolution et le format 3D.')
    'Error.NoOriginalExe'       = @('No copy of PES6.exe in the supported version was found.', "Aucune copie de PES6.exe dans la version supportée n'a été trouvée.")
    'Error.UnsupportedExe'      = @('PES6.exe does not match the supported version: {0}', 'PES6.exe ne correspond pas à la version supportée : {0}')
    'Error.HalfOutOfRange'      = @('2D half-width out of range.', 'Demi-largeur 2D hors limites.')
    'Error.BadLodmixer'         = @('Unexpected lodmixer.dat.', 'lodmixer.dat inattendu.')
    'Error.BadSettings'         = @('Unexpected settings.dat.', 'settings.dat inattendu.')
    'Check.Location'            = @('{0} (0x{1:X})', '{0} (0x{1:X})')
    'Check.Constants'           = @('free space for the constants', 'emplacement libre des constantes')
    'Check.Width2D'             = @('2D layer width', 'largeur 2D')
    'Check.Width3D'             = @('3D width', 'largeur 3D')
    'Check.CursorConstant'      = @('free space for the cursor', 'emplacement libre du curseur')
    'Check.ViewRect'            = @('2D view rectangle', 'rectangle de vue 2D')
    'Check.TurnThreshold'       = @('turn threshold', 'seuil de virage')
    'Check.Operand'             = @('operand not found', 'opérande introuvable')
    'Check.HookCave'            = @('free space: {0}', 'zone libre : {0}')
    'Check.HookEntry'           = @('entry point: {0}', "point d'entrée : {0}")
    'Hook.Clip'                 = @('list clipping', 'découpage des listes')
    'Hook.Cursor'               = @('player cursor', 'curseur du joueur')
    'Hook.FreeDirection'        = @('free directions', 'directions libres')
}

function Write-Resx([string]$path, [int]$index) {
    $settings = New-Object Xml.XmlWriterSettings
    $settings.Indent = $true
    $settings.Encoding = New-Object Text.UTF8Encoding($false)
    $w = [Xml.XmlWriter]::Create($path, $settings)
    $w.WriteStartElement('root')
    foreach ($h in @(@('resmimetype', 'text/microsoft-resx'), @('version', '2.0'),
                     @('reader', 'System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089'),
                     @('writer', 'System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089'))) {
        $w.WriteStartElement('resheader'); $w.WriteAttributeString('name', $h[0]); $w.WriteElementString('value', $h[1]); $w.WriteEndElement()
    }
    foreach ($key in $strings.Keys) {
        $w.WriteStartElement('data'); $w.WriteAttributeString('name', $key)
        $w.WriteAttributeString('xml', 'space', 'http://www.w3.org/XML/1998/namespace', 'preserve')
        $w.WriteElementString('value', $strings[$key][$index]); $w.WriteEndElement()
    }
    $w.WriteEndElement(); $w.Close()
}

New-Item -ItemType Directory -Force $OutDir | Out-Null
Write-Resx (Join-Path $OutDir 'Strings.resx') 0
Write-Resx (Join-Path $OutDir 'Strings.fr.resx') 1
"{0} textes ecrits dans {1}" -f $strings.Count, $OutDir
