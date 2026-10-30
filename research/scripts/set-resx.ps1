# Add, replace or remove texts in Strings.resx (English) and Strings.fr.resx.
# Usage: set-resx.ps1 -Dir src/PES6Patcher.Core/Resources -Set @{ 'Key' = @('English', 'Français') } -Remove 'Old.Key'
param([string]$Dir, [hashtable]$Set = @{}, [string[]]$Remove = @())

foreach ($file in @(@('Strings.resx', 0), @('Strings.fr.resx', 1))) {
    $path = Join-Path $Dir $file[0]
    $xml = New-Object Xml.XmlDocument; $xml.PreserveWhitespace = $false; $xml.Load($path)
    foreach ($key in $Remove) {
        $node = $xml.root.SelectSingleNode("data[@name='$key']"); if ($node) { [void]$xml.root.RemoveChild($node) }
    }
    foreach ($key in $Set.Keys) {
        $text = $Set[$key][$file[1]]
        $node = $xml.root.SelectSingleNode("data[@name='$key']")
        if (-not $node) {
            $node = $xml.CreateElement('data'); $node.SetAttribute('name', $key)
            $node.SetAttribute('space', 'http://www.w3.org/XML/1998/namespace', 'preserve')
            [void]$node.AppendChild($xml.CreateElement('value')); [void]$xml.root.AppendChild($node)
        }
        $node.SelectSingleNode('value').InnerText = $text
    }
    $settings = New-Object Xml.XmlWriterSettings; $settings.Indent = $true; $settings.Encoding = New-Object Text.UTF8Encoding($false)
    $w = [Xml.XmlWriter]::Create($path, $settings); $xml.Save($w); $w.Close()
}
