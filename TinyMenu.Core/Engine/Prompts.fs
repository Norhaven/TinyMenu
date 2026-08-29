namespace TinyMenu.Core

module internal Prompts =
    open Configuration
    open System
    
    type QueryableContainer<'T when 'T :> IIdentifiable> = {
        Name: string
        SelectionColor: ConsoleColor
        DefaultColor: ConsoleColor
        Items: 'T list
    }

    let rec private showSelectionOptions<'T when 'T :> IIdentifiable> 
        (container:QueryableContainer<'T>) (currentIndex: int) : 'T =

        Console.SetCursorPosition(0, 1)

        let items = container.Items

        if (items.Length = 0) then
            raise (ArgumentOutOfRangeException(sprintf "Unable to show selections options for '%s' when no options exist" container.Name))
        else
            for i in 0..items.Length - 1 do
                let currentItem = items.[i]

                if i = currentIndex then
                    Console.ForegroundColor <- container.SelectionColor
                    Console.WriteLine(sprintf "* %s" currentItem.Name)
                else
                    Console.ForegroundColor <- container.DefaultColor
                    Console.WriteLine(sprintf "  %s" currentItem.Name)

            Console.ResetColor()

            let move = Console.ReadKey()

            match move.Key with
            | ConsoleKey.UpArrow when currentIndex > 0 -> showSelectionOptions container (currentIndex - 1)
            | ConsoleKey.DownArrow when currentIndex < container.Items.Length - 1 -> showSelectionOptions container (currentIndex + 1)
            | ConsoleKey.Enter -> container.Items.[currentIndex] 
            | _ -> showSelectionOptions container currentIndex

    let show<'T when 'T :> IIdentifiable> (config:Config) (name:string) (items:'T list) : 'T =
        let app = config.Source.App
        let selectionSuccess, selectionColor = Enum.TryParse<ConsoleColor>(app.SelectionColor)
        let defaultSuccess, defaultColor = Enum.TryParse<ConsoleColor>(app.DefaultColor)

        if (not selectionSuccess) then
            raise (InvalidOperationException(sprintf "The selection color '%s' is not a valid color!" app.SelectionColor))
        
        if (not defaultSuccess) then
            raise (InvalidOperationException(sprintf "The default color '%s' is not a valid color!" app.DefaultColor))

        Console.ForegroundColor <- defaultColor
        Console.Clear()
        Console.WriteLine(app.SelectionScreenHeader)

        let container = { Name = name; SelectionColor = selectionColor; DefaultColor = defaultColor; Items = items }
        
        let selectedOption = showSelectionOptions container 0

        Console.WriteLine()

        selectedOption

