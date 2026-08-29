namespace TinyMenu.Core

module internal ConfigurationManager =
    open System
    open System.IO
    open System.Reflection
    open System.Text.Json
    open System.Text.Json.Serialization
    open System.Runtime.Serialization
    open Configuration

    let updateConfigWithCurrentPlatform (config:Config) =
        { config with AllowedEnvironments = config.Source.Environments |> List.where (fun x -> x.Platform = Platforms.currentPlatform) }

    let loadConfiguration configName =
        let currentDir = Directory.GetParent(Assembly.GetEntryAssembly().Location)
        let appConfigPath = Path.Combine(currentDir.FullName, configName)        
        let appConfigContents = File.ReadAllText(appConfigPath)

        let options = 
            JsonFSharpOptions.Default()
                .WithSkippableOptionFields(SkippableOptionFields.Always)
                .WithUnionInternalTag()
                .WithUnionTagName("type")
                .WithUnionNamedFields()
                .WithUnionTagCaseInsensitive(true)
                .WithUnionFieldNamingPolicy(JsonNamingPolicy.CamelCase)
                .ToJsonSerializerOptions()

        options.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase
        options.Converters.Add(JsonFSharpConverter())
        options.Converters.Add(JsonStringEnumConverter())

        let configFile = JsonSerializer.Deserialize<ConfigFile>(appConfigContents, options)

        let baseConfig = Config.DefaultWith(configFile)

        let platformAwareConfig = updateConfigWithCurrentPlatform baseConfig

        let selectedEnvironment = 
            match platformAwareConfig.AllowedEnvironments.Length with
            | 0 -> raise (ArgumentOutOfRangeException("Unable to select an environment for your current platform, none were found"))
            | 1 -> platformAwareConfig.AllowedEnvironments[0]
            | _ -> Prompts.show platformAwareConfig (platformAwareConfig.Source.App.Name) (platformAwareConfig.AllowedEnvironments)

        { platformAwareConfig with CurrentEnvironment = Some selectedEnvironment }