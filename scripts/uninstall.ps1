param($installPath, $toolsPath, $package, $project)

$targetFileName = 'Arma3.DllExport.targets'

$projects = Get-DllExportMsBuildProjectsByFullName($project.FullName)

return $projects |  % {
    $currentProject = $_
    $currentProject.Xml.Imports | ? {
        $targetFileName -ieq [System.IO.Path]::GetFileName($_.Project)
    }  | % {  
        $currentProject.Xml.RemoveChild($_)
    }
}