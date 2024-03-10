# Define the root path of the Git repository
$rootPath = "."

# Function to delete bin and obj folders
function DeleteBinAndObjFolders {
    param (
        [string]$path
    )

    # Find all bin and obj folders under the specified path
    $folders = Get-ChildItem -Path $path -Recurse -Directory | Where-Object { $_.Name -eq "bin" -or $_.Name -eq "obj" }

    # Delete each folder found
    foreach ($folder in $folders) {
        Write-Host "Deleting $($folder.FullName)"
        Remove-Item -Recurse -Force $folder.FullName
    }
}

# Call the function with the root path of the Git repository
DeleteBinAndObjFolders -path $rootPath
