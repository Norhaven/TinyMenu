namespace TinyMenu.Core

open System.Threading
open System.Threading.Tasks

type IApplication =
    abstract member ExecuteAsync: args:string array * cancellationToken: CancellationToken -> Task<int>

module CLI =
    open System
    open System.CommandLine
    open System.Collections.Generic
    open Configuration
    open CommandInterpolation
    open CommandBuilders
    open Platforms

    type internal App(private config:Config, private rootCommand:RootCommand) =
        do
            let app = config.Source.App

            Console.WriteLine(app.Name)
            Console.WriteLine(app.Description)
            Console.WriteLine()

        static let createCommandFor (config:Config) (menu:Menu) (cancellationToken:CancellationToken) : CommandLine.Command =
        
            let invokableCommands = CommandBuilders.parseMenu config menu |> List.ofSeq

            let action = fun (result:ParseResult) (cancellationToken:CancellationToken) ->                    
                
                let rec invokeNextMenuCommand (invokables:IInvokable list) (captures:Map<string, obj>) : Task =
                    task {                      
                        if cancellationToken.IsCancellationRequested then
                            return! Task.FromCanceled(cancellationToken)
                        elif invokables.IsEmpty then
                            return! Task.CompletedTask
                        else
                            let invokable = invokables.Head

                            let! result = invokable.Invoke(captures, cancellationToken)

                            match result with
                            | Success(message, currentCaptures) -> 
                                if message.IsSome && config.Source.App.ShouldLog then
                                    printfn "%s" message.Value

                                return! invokeNextMenuCommand (invokables.Tail) currentCaptures
                            | Failure (errorMessage, error) -> 
                                printfn "%s" errorMessage

                                match error with
                                | Some ex -> 
                                    printfn "%s" ex.Message
                                    return! Task.FromException(ex)
                                | None -> 
                                    return! Task.CompletedTask
                    } :> Task

                invokeNextMenuCommand invokableCommands (Map<string, obj>(Seq.empty))

            let command = CommandLine.Command(menu.FullOption, menu.Description)

            command.Aliases.Add(menu.ShortOption)

            command.SetAction(action)

            command

        static member LoadFrom(configName:string, cancellationToken:CancellationToken) : App =
            let config = ConfigurationManager.loadConfiguration configName

            let selectedEnvironment = 
                match config.AllowedEnvironments with
                | allowed when allowed.Length = 0 ->
                    raise (ArgumentOutOfRangeException("Unable to select an environment for your current platform, none were found"))
                | allowed when allowed.Length = 1 ->
                    allowed[0]
                | _ ->
                    Prompts.show config (config.Source.App.Name) (config.AllowedEnvironments)

            let fullConfig = { config with CurrentEnvironment = Some selectedEnvironment }
            let root = (RootCommand(fullConfig.Source.App.Description))

            for menu in fullConfig.Source.Menus do
                let applicableTo = match menu.AppliesTo with
                                    | AppliesToEnvironments envs -> HashSet<string>(envs).Contains(selectedEnvironment.Id)
                                    | AppliesToPlatforms platforms -> HashSet<string>(platforms).Contains(config.CurrentPlatform)
                                    | AppliesToAll -> true

                if applicableTo then
                    let command = createCommandFor fullConfig menu cancellationToken
                    root.Add(command);                   

            App(fullConfig, root)

        interface IApplication with
            member this.ExecuteAsync(args:string array, cancellationToken:CancellationToken) : Task<int> =
                rootCommand.Parse(args).InvokeAsync(cancellationToken=cancellationToken)
        
            
    let public loadFrom(configName:string, cancellationToken:CancellationToken) : IApplication =
        App.LoadFrom(configName, cancellationToken) :> IApplication