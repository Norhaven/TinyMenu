namespace TinyMenu.Core

module internal Configuration = 
    
    type EnvironmentPlatform =
    | Windows = 0
    | Linux = 1
    | OSX = 2

    type IIdentifiable =
        abstract member Id: string
        abstract member Name: string

    type ExecutionType = {        
        Id: string      
        Name: string   
    }
    
    type ShellType = {
        Id: string
        Name: string
        Executable: string
        InvokeShellArgument: string option        
    }
    
    type AppliesTo =
    | AppliesToEnvironments of environments: string list
    | AppliesToPlatforms of platforms: string list
    | AppliesToAll
    
    type Tool = {
       Id: string
       Name: string
       Description: string option
       Command: string
       AppliesTo: AppliesTo option
    }
    
    type Variable = {
        Name: string
        Value: obj
    }
    
    type Environment = {
        Id: string
        Name: string
        Platform: EnvironmentPlatform
        Variables: Variable list
        ShellTypes: string list
    } 
    with
        interface IIdentifiable with
            member this.Id = this.Id
            member this.Name = this.Name    

    type ActionExecution =
    | UsesCommandText of commandText:string
    | UsesCommand of commandId: string
    | UsesTool of toolId: string * withArgs: string list * captureAs: string option
    | UsesMultiPlatformCommands of commands: MultiTargetCommand list
    | UsesSteps of steps: Step list
    | GetsUserInput of prompt: string * options: string list option * captureAs: string
    and 
        Step = {
            Action: ActionExecution
            AppliesTo: AppliesTo option
        }
        and      
        MultiTargetCommand = {
            Platform: EnvironmentPlatform
            Environment: string option
            Action: ActionExecution
        }
        
    type SelectionOption = {
        Id: string
        Name: string
        Value: obj
    }
    with
        interface IIdentifiable with
            member this.Id = this.Id
            member this.Name = this.Name
    
    type Command = {
        Id: string
        Name: string
        Description: string option
        AppliesTo: AppliesTo option
        Action: ActionExecution
    }
    
    type Menu = {
        FullOption: string
        ShortOption: string
        Name: string
        Description: string
        AppliesTo: AppliesTo
        Steps: Step list
    }
    
    type App = {
        Name: string
        Description: string
        SelectionColor: string
        DefaultColor: string
        SelectionScreenHeader: string
        ShouldLog: bool
    } 
    
    type ConfigFile = {
        ShellTypes: ShellType list
        Tools: Tool list option
        Environments: Environment list
        Commands: Command list
        Options: SelectionOption list option
        Menus: Menu list
        App: App
    }
    
    type Config = {
        AppConfigPath: string
        AllowedEnvironments: Environment list
        CurrentEnvironment: Environment option
        CurrentPlatform: string
        ShellTypes: ShellType list
        Source: ConfigFile
    }
    with
        static member DefaultWith(source:ConfigFile) =
            { AppConfigPath = ""; AllowedEnvironments = []; CurrentEnvironment = None; CurrentPlatform = ""; ShellTypes = source.ShellTypes; Source = source }

        
        



