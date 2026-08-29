namespace TinyMenu.Core

module internal CommandBuilders =
    open System
    open System.Collections.Generic
    open System.Threading
    open System.Threading.Tasks
    open System.Diagnostics
    open Configuration
    open CommandInterpolation
    open Platforms

    type private InvokableCommand = {
        Name: string
        Description: string
        CommandText: string option
        ShellCommandText: string list
        Options: string list
        NeedsUserInput: bool
        CaptureAs: string option
    }
    with
        static member Default() = { Name = ""; Description = ""; CommandText = None; ShellCommandText = []; Options = []; NeedsUserInput = false; CaptureAs = None }

    /// <summary>
    /// Represents the result of a command invocation, whether success or failure.
    /// </summary>
    type InvocationResult =
    | Success of message:string option * captures:Map<string,obj>
    | Failure of errorMessage:string * error:Exception option
    
    /// <summary>
    /// Represents a command that may be invoked along with previously captured values available for use within it.
    /// </summary>
    type IInvokable =
        /// <summary>
        /// Invokes the command.
        /// </summary>
        /// <param name="captures">A key/value collection of previously captured values by name.</param>
        /// <param name="cancellationToken">The token used for cancelling this command.</param>
        /// <returns>An awaitable task that produces the result of the command invocation.</returns>
        abstract Invoke: captures:Map<string, obj> * cancellationToken:CancellationToken -> Task<InvocationResult>

    type private UserInputCommand(private config:Config, private command:InvokableCommand, private interpolator:Interpolator) =
        inherit System.CommandLine.Command(command.Name, command.Description)

        interface IInvokable with
            member this.Invoke(captures:Map<string, obj>, cancellationToken:CancellationToken) : Task<InvocationResult> =
        
                if not command.NeedsUserInput then
                    raise (ArgumentException(sprintf "Unable to invoke command for user input, command has no request for that"))

                let interpolatedText = 
                    match command.CommandText with
                    | Some text -> interpolator.Interpolate(text, captures)
                    | None -> ""

                let input = 
                    if command.Options.IsEmpty then
                        Console.WriteLine(command.Description)
                        Console.ReadLine() :> obj
                    else
                        let availableOptions = Set(command.Options)
                        let options = 
                            match config.Source.Options with
                            | Some values -> 
                                values |> List.where (fun x -> availableOptions.Contains(x.Id))
                            | None ->
                                []

                        let selectedOption = Prompts.show config (command.Description) (options)

                        selectedOption.Value

                match command.CaptureAs with
                | Some capture -> 
                    let qualifiedCapture = sprintf "capture:%s" capture
                    let result = Success(Some(sprintf "Captured user input as '%s'" capture), captures.Add(qualifiedCapture, input))
                    Task.FromResult(result)
                | None -> 
                    Task.FromResult(Failure("No capture name was provided, user input was lost", None))

    type private InvokeProcessCommand(private config:Config, private command:InvokableCommand, private interpolator:Interpolator) =
        inherit System.CommandLine.Command(command.Name, command.Description)

        let _onLineRead = Event<string>()

        member _.OnLineRead = _onLineRead.Publish

        interface IInvokable with
            member this.Invoke(captures:Map<string, obj>, cancellationToken:CancellationToken) : Task<InvocationResult> =
            
                let interpolatedText = 
                    match command.CommandText with
                    | Some text -> interpolator.Interpolate(text, captures)
                    | None -> ""

                if command.NeedsUserInput then
                    Console.WriteLine(command.Description)

                    let input = Console.ReadLine()

                    match command.CaptureAs with
                    | Some capture -> 
                        let result = Success(None, (captures.Add(capture, input)))
                        Task.FromResult(result)
                    | None -> 
                        Task.FromResult(Failure("No capture name was provided, user input was lost", None))                
                else
                    let initialShellText = command.ShellCommandText.Head.Split(' ')
                    let hasShellArg = initialShellText.Length > 1

                    task {
                        let startInfo = ProcessStartInfo()

                        startInfo.FileName <- initialShellText.[0]
                        startInfo.CreateNoWindow <- true
                        startInfo.Arguments <- sprintf "%s %s" (if hasShellArg then initialShellText.[1] else "") interpolatedText
                        startInfo.RedirectStandardOutput <- true
                        startInfo.RedirectStandardError <- true
                        startInfo.UseShellExecute <- false

                        let commandProcess = new Process()

                        commandProcess.StartInfo <- startInfo

                        commandProcess.OutputDataReceived.Add(fun args -> _onLineRead.Trigger(args.Data))
                        commandProcess.ErrorDataReceived.Add(fun args -> _onLineRead.Trigger(args.Data))

                        commandProcess.Start() |> ignore

                        commandProcess.BeginOutputReadLine()
                        commandProcess.BeginErrorReadLine()

                        do! commandProcess.WaitForExitAsync(cancellationToken)

                        if commandProcess.ExitCode = 0 then
                            let result = Some(sprintf "Command '%s' executed successfully" command.Name)
                            return Success(result, captures)
                        else
                            return Failure(sprintf "Command '%s' failed with exit code %d" command.Name commandProcess.ExitCode, None)
                    }
    
    let private (|CurrentEnvShellInvokeText|_|) (config:Config) =
        match config.CurrentEnvironment with
        | None -> raise (InvalidOperationException("No current environment has been specified, unable to get the appropriate shell invoke text for it"))
        | Some env ->
            let uniqueShellTypeIds = Set(env.ShellTypes)
            let availableShellTypes = 
                config.ShellTypes 
                |> List.where (fun x -> uniqueShellTypeIds.Contains(x.Id))

            let shellTypes = availableShellTypes 
                                |> List.map (fun x -> 
                                    match x.InvokeShellArgument with
                                    | Some arg -> sprintf "%s %s" x.Executable arg
                                    | None -> ""
                                    )
                                |> List.choose (fun x -> if x.Length > 0 then Some x else None)

            if shellTypes.IsEmpty then
                None
            else
                Some shellTypes
        

    let private (|ShellInvokeText|_|) (config:Config) (appliesTo:AppliesTo) =
        match appliesTo with
        | AppliesToEnvironments environmentIds -> 
            let uniqueEnvironmentIds = Set(environmentIds)

            match config.CurrentEnvironment with
            | None -> raise (InvalidOperationException("Unable to get applicable environment shell text when no current environment has been determined"))
            | Some env ->
                if uniqueEnvironmentIds.Contains(env.Id) then
                    match config with
                    | CurrentEnvShellInvokeText shellTexts -> Some shellTexts
                    | _ -> None
                else 
                    None

        | AppliesToPlatforms platformIds ->
            let uniquePlatformIds = Set(platformIds)
                            
            if uniquePlatformIds.Contains(config.CurrentPlatform) then
                match config with
                | CurrentEnvShellInvokeText shellTexts -> Some shellTexts
                | _ -> None
            else
                None
        | AppliesToAll ->
            match config with
                | CurrentEnvShellInvokeText shellTexts -> Some shellTexts
                | _ -> None

    let rec private (|CommandText|_|) (config:Config) (invokable:InvokableCommand) (action:ActionExecution): InvokableCommand list option =
        match action with
        | UsesCommandText commandText -> 
            Some([{ invokable with CommandText = Some(commandText) }])
        | UsesCommand commandId -> 
            let command = config.Source.Commands |> List.tryFind (fun x -> x.Id = commandId)

            match command with
            | Some cmd ->                
                let appliesTo =
                    match cmd.AppliesTo with
                    | Some value -> value
                    | None -> AppliesToAll

                match appliesTo with
                | ShellInvokeText config shellText ->           
                    let description =
                        match cmd.Description with
                        | Some value -> value
                        | None -> ""

                    let invokable = { Name = cmd.Name; Description = description; CommandText = None; ShellCommandText = shellText; Options = []; NeedsUserInput = false; CaptureAs = None  }
                    
                    match cmd.Action with
                    | CommandText config invokable commands -> Some commands
                    | _ -> None
                | _ -> None
            | None -> None

        | UsesTool (toolId, args, captureAs) ->
            match config.Source.Tools with
            | None -> raise (InvalidOperationException(sprintf "Unable to use tool with ID '%s', no tool with that ID has been defined" toolId))
            | Some tools ->
                let tool = tools |> List.tryFind (fun x -> x.Id = toolId)
            
                match tool with
                | Some tool -> 
                    let appliesTo = if tool.AppliesTo.IsSome then tool.AppliesTo else Some AppliesToAll

                    match appliesTo with
                    | Some application ->
                        match application with
                        | ShellInvokeText config shellText ->
                            let toolCommand = sprintf "%s %s" tool.Command (String.Join(" ", args))
                            let toolDescription =
                                match tool.Description with
                                | Some description -> description
                                | None -> ""

                            let invokable = { Name = tool.Name; Description = toolDescription; CommandText = Some(toolCommand); ShellCommandText = shellText; Options = []; NeedsUserInput = false; CaptureAs = captureAs  }

                            Some [invokable]
                        | _ -> None
                    | None -> None
                | None -> None
        | UsesMultiPlatformCommands multiTargetCommands ->     
            let isSupported multiTargetCommand =
                match multiTargetCommand with
                | IsSupportedByPlatform -> 
                    match multiTargetCommand.Action with
                    | CommandText config invokable commands -> Some commands
                    | _ -> None
                | IsNotSupportedByPlatform -> None

            let supportedCommands = 
                multiTargetCommands 
                    |> List.choose isSupported
                    |> List.concat

            if supportedCommands.Length > 0 then
                Some(supportedCommands)
            else
                None
        | UsesSteps steps -> 
            let allCommands = 
                steps 
                    |> List.choose (fun step -> 
                        match step.Action with
                        | CommandText config invokable commands -> Some commands
                        | _ -> None
                    )
                    |> List.concat

            if allCommands.Length > 0 then
                Some allCommands
            else
                None
        | GetsUserInput (prompt, options, captureAs) ->
            match config with
            | CurrentEnvShellInvokeText shellText -> 
                let commandOptions =
                    match options with
                    | Some values -> values
                    | None -> []

                Some [{ Name = Guid.NewGuid().ToString(); Description = prompt; CommandText = None; ShellCommandText = shellText; Options = commandOptions; NeedsUserInput = true; CaptureAs = Some captureAs }]
            | _ -> None

    let private parseAction (config:Config) (action:ActionExecution) : InvokableCommand list =
        match action with
        | CommandText config (InvokableCommand.Default()) commandText -> commandText
        | _ -> raise (NotSupportedException(sprintf "Command text not found for action '%s'" (action.ToString())))

    /// <summary>
    /// Parses the menu definition into a series of invokable commands.
    /// </summary>
    /// <param name="config">The application's configuration to use when determining the commands and their order.</param>
    /// <param name="menu">The menu item that should be parsed into commands.</param>
    /// <returns>A series of invokable commands that comprise the menu operations.</returns>
    let parseMenu (config:Config) (menu:Menu) : IInvokable seq =
        if menu.Steps.Length = 0 then
            raise (ArgumentOutOfRangeException(sprintf "Unable to use menu '%s', no menu steps were found" menu.Name))
        else
            let interpolator = (Interpolator(config))

            seq {
                for step in menu.Steps do
                    for invokable in parseAction config step.Action do
                        if invokable.NeedsUserInput then
                            (UserInputCommand(config, invokable, interpolator))
                        else
                            (InvokeProcessCommand(config, invokable, interpolator))  
            }