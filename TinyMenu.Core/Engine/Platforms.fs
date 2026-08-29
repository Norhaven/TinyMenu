namespace TinyMenu.Core

module internal Platforms =
    open System
    open Configuration
    
    let private getCurrentPlatform() =
        if OperatingSystem.IsWindows() then
            "windows"
        else if OperatingSystem.IsLinux() then
            "linux"
        else if OperatingSystem.IsMacOS() then
            "osx"
        else
            raise (NotSupportedException(sprintf "Your operating system is not currently supported"))

    let (|Windows|Linux|OSX|) (value:string) = 
        match value.ToLowerInvariant() with
        | "windows" -> Windows
        | "linux" -> Linux
        | "osx" -> OSX
        | _ -> raise (NotSupportedException(sprintf "Platform '%s' is unsupported" value))
    
    let (|IsSupportedByPlatform|IsNotSupportedByPlatform|) (targetedCommand:MultiTargetCommand) =
        match getCurrentPlatform() with
        | Windows when targetedCommand.Platform = EnvironmentPlatform.Windows -> IsSupportedByPlatform
        | Linux when targetedCommand.Platform = EnvironmentPlatform.Linux -> IsSupportedByPlatform
        | OSX when targetedCommand.Platform = EnvironmentPlatform.OSX -> IsSupportedByPlatform
        | _ -> IsNotSupportedByPlatform
                 
    let currentPlatform = 
        match getCurrentPlatform() with
        | Windows -> EnvironmentPlatform.Windows
        | Linux -> EnvironmentPlatform.Linux
        | OSX -> EnvironmentPlatform.OSX