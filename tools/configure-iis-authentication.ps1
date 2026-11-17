#Requires -Version 5.1
# Run elevated on the IIS server after enabling Anonymous and Windows Authentication on the application.
# Switches IIS Windows challenges off under the application's umbraco path, where Umbraco owns authentication,
# and keeps them for the embedded identity server everywhere else. See
# https://github.com/auroris/AurorisNet.Umbraco.IdentityServer.SSO/blob/main/docs/EMBEDDED-SERVER.md#iis-windows-authentication
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)][string]$Site,
    [string]$ApplicationPath = '/'
)
$ErrorActionPreference = 'Stop'
Add-Type -Path "$env:windir\System32\inetsrv\Microsoft.Web.Administration.dll"
$manager = New-Object Microsoft.Web.Administration.ServerManager
try {
    $siteEntry = $manager.Sites[$Site]
    if (!$siteEntry) { throw "IIS site not found: $Site" }
    $ApplicationPath = '/' + $ApplicationPath.Trim('/')
    if (!$siteEntry.Applications[$ApplicationPath]) { throw "IIS application not found: $Site$ApplicationPath" }
    $web = $manager.GetWebConfiguration($Site, $ApplicationPath)
    $source = @($web.GetSection('system.webServer/handlers').GetCollection() | Where-Object { $_['name'] -eq 'aspNetCore' })
    if ($source.Count -ne 1 -or $source[0]['modules'] -ne 'AspNetCoreModuleV2') {
        throw 'The application must have an ASP.NET Core Module V2 handler.'
    }
    $authentication = 'system.webServer/security/authentication/'
    foreach ($scheme in @('anonymousAuthentication', 'windowsAuthentication')) {
        if (!$web.GetSection($authentication + $scheme)['enabled']) {
            throw "Enable $scheme on the IIS application before configuring backoffice authentication."
        }
    }
    $location = $Site + $ApplicationPath.TrimEnd('/') + '/umbraco'
    if (!$PSCmdlet.ShouldProcess($location, 'Keep Umbraco authentication and suppress IIS Windows challenges')) { return }
    $config = $manager.GetApplicationHostConfiguration()
    $config.GetSection($authentication + 'windowsAuthentication', $location)['enabled'] = $false
    # An explicit child configuration path must retain the app's wildcard handler. Otherwise
    # IIS can select StaticFile and return 404 before ASP.NET Core sees the backoffice request.
    $handlers = $config.GetSection('system.webServer/handlers', $location).GetCollection()
    $target = @($handlers | Where-Object { $_['name'] -eq 'aspNetCore' })
    $mapping = if ($target.Count) { $target[0] } else { $handlers.CreateElement('add') }
    foreach ($attribute in @('name', 'path', 'verb', 'modules', 'resourceType', 'requireAccess', 'preCondition')) {
        $mapping[$attribute] = $source[0][$attribute]
    }
    if (!$target.Count) { $handlers.AddAt(0, $mapping) | Out-Null }
    $manager.CommitChanges()
    Write-Host "Configured $location. Windows SSO remains enabled outside the backoffice path."
} finally {
    $manager.Dispose()
}
