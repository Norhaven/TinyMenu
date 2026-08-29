open TinyMenu.Core
open System.Threading
open System.Threading.Tasks

let run (args:string array) =
    task {
        let cancellationSource = new CancellationTokenSource()

        try
            let app = CLI.loadFrom("TinyMenu.Example.config.json", cancellationSource.Token)

            return! app.ExecuteAsync(args, cancellationSource.Token)
        with 
        | ex ->
            printfn "An error occurred, aborting application start: %s" ex.Message
            cancellationSource.Dispose()
            return! Task.FromResult(-1)
    }

[<EntryPoint>]
let main args =
    let asyncProgram = run args

    asyncProgram
    |> Async.AwaitTask<int>
    |> Async.RunSynchronously


