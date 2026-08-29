namespace TinyMenu.Core

module internal CommandInterpolation =
    open Configuration
    open System.Text.RegularExpressions
    open System.Collections.Generic
    open System

    type Interpolator(private config:Config) =
        let _interpolationSources = ["capture", "env"]
        let _interpolationRegex = Regex("{{([\w]+:{1}[\w]+)}}", RegexOptions.Compiled)        
        
        let applyCaptureToCommand (commandText:string) (requestedCapture:string) (captures:Map<string, obj>) : string =
            if captures.ContainsKey(requestedCapture) then
                commandText.Replace(sprintf "{{%s}}" requestedCapture, captures.[requestedCapture].ToString())
            else
                raise (ArgumentOutOfRangeException("requestedCapture", requestedCapture, sprintf "Unable to apply capture because it was not found"))

        member this.Interpolate(commandText:string, captures:Map<string, obj>) =
            let matches = _interpolationRegex.Matches(commandText)

            let rec applyCaptures (currentRequestedCaptures:string list) (currentCommandText:string) =
                let requested = currentRequestedCaptures.Head
                let appliedText = applyCaptureToCommand currentCommandText requested captures

                if currentRequestedCaptures.Tail.Length = 0 then
                    appliedText
                else
                    applyCaptures currentRequestedCaptures.Tail appliedText
        
            
            let requestedCaptures = seq {
                for currentMatch in matches do
                    yield currentMatch.Groups.[1].Value
            }

            applyCaptures (requestedCaptures |> List.ofSeq) commandText
