$ErrorActionPreference = 'Stop'
$translations = Get-Content -Encoding UTF8 (Join-Path $PSScriptRoot 'translations.json') -Raw | ConvertFrom-Json
$output = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../src/Resources'))
[IO.Directory]::CreateDirectory($output) | Out-Null
foreach ($language in @('English','Korean')) {
    $name = if ($language -eq 'English') { 'Strings.resx' } else { 'Strings.ko.resx' }
    $settings = New-Object Xml.XmlWriterSettings
    $settings.Indent = $true; $settings.Encoding = New-Object Text.UTF8Encoding($false)
    $writer = [Xml.XmlWriter]::Create((Join-Path $output $name), $settings)
    try {
        $writer.WriteStartDocument(); $writer.WriteStartElement('root')
        foreach ($pair in @(@('resmimetype','text/microsoft-resx'), @('version','2.0'), @('reader','System.Resources.ResXResourceReader, System.Windows.Forms'), @('writer','System.Resources.ResXResourceWriter, System.Windows.Forms'))) {
            $writer.WriteStartElement('resheader'); $writer.WriteAttributeString('name', $pair[0]); $writer.WriteElementString('value', $pair[1]); $writer.WriteEndElement()
        }
        foreach ($entry in ($translations.PSObject.Properties | Sort-Object Name)) {
            $value = $entry.Value.$language
            if ([string]::IsNullOrWhiteSpace($value)) { throw "Missing $language translation: $($entry.Name)" }
            $writer.WriteStartElement('data'); $writer.WriteAttributeString('name', $entry.Name)
            $writer.WriteAttributeString('xml','space','http://www.w3.org/XML/1998/namespace','preserve')
            $writer.WriteElementString('value', $value); $writer.WriteEndElement()
        }
        $writer.WriteEndElement(); $writer.WriteEndDocument()
    } finally { $writer.Dispose() }
}
Write-Host "Generated $(@($translations.PSObject.Properties).Count) bilingual resources."
