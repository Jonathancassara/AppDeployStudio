# AppDeploy Studio

Windows desktop application for preparing PowerShell App Deployment Toolkit packages for SCCM and Intune. Created by [Jonathan Cassara](https://github.com/Jonathancassara).

## Get started

1. Download the portable ZIP from [Releases](https://github.com/Jonathancassara/AppDeployStudio/releases/latest) and extract it.
2. Open `AppDeployStudio.exe`. Choose English, French, Polish or Ukrainian from the language selector.
3. Enter the application details and configure actions for MSI or EXE installers. The export includes the configured `Invoke-AppDeployToolkit.ps1`, its launcher, the package files and the deployment commands.

For preview and export, the app uses PSAppDeployToolkit V4 4.1.8. It downloads the official ZIP on first use and caches it under `%LOCALAPPDATA%\AppDeployStudio\Templates\4.1.8`. For offline use, save the official `PSAppDeployToolkit_Template_v4.zip` beside the executable. The app verifies the archive’s SHA-256 checksum. Other template versions can be downloaded from the Templates tab. [Official PSAppDeployToolkit releases](https://github.com/PSAppDeployToolkit/PSAppDeployToolkit/releases)

## Build on Windows

With .NET Framework 4.8 and PowerShell available, run `.\Build.ps1` from the repository folder. It compiles `AppDeployStudio.exe` using the Windows .NET Framework compiler. The build requires no package restore or template archive.

## License

You may use AppDeploy Studio at work, modify it and share it free of charge. Keep the license and the original author credit, and document your changes when distributing a modified copy. Selling the original or a modified version requires written permission. [AppDeploy Studio License](LICENSE)

PSAppDeployToolkit is obtained separately and retains its own LGPL 3.0 terms. [PSAppDeployToolkit license](https://github.com/PSAppDeployToolkit/PSAppDeployToolkit/blob/4.1.8/COPYING.Lesser)

## En français

Téléchargez le ZIP dans [Releases](https://github.com/Jonathancassara/AppDeployStudio/releases/latest), extrayez-le, puis lancez `AppDeployStudio.exe`. L’export prépare les fichiers de déploiement sans lancer l’installation. La template V4 4.1.8 est téléchargée au premier aperçu ou export. Pour un usage hors ligne, placez son ZIP officiel à côté de l’exécutable.

La licence autorise l’utilisation, y compris en entreprise, la modification et le partage gratuit. Une redistribution doit conserver le crédit **Jonathan Cassara**, inclure la licence et documenter les modifications apportées. La vente nécessite une autorisation écrite. [Texte de la licence](LICENSE)
